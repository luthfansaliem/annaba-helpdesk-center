using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;

namespace AnnabaDigital.Application.Users;

public sealed record RequestableRole(Guid Id, string Key, string Name, string Description, int PermissionCount);
public sealed record CreateAccessRequest(Guid? RoleId, string? Reason);
public sealed record DecideAccessRequest(string? Note);
public sealed record AccessRequestItem(Guid Id, Guid RequesterId, string RequesterName, string RequesterEmail, RoleRef Role, string Reason, string Status,
    DateTimeOffset CreatedAt, string? DecidedByName, DateTimeOffset? DecidedAt, string? DecisionNote);

/// <summary>
/// USR-007. Anyone signed in can ask for a role they don't hold (never the protected Super Admin role). Someone who may assign roles decides:
/// approving goes through the normal role-assignment path (re-authentication, hierarchy rule, anti-escalation, audit), and nobody decides their own request.
/// </summary>
public sealed class AccessRequestService(IAppDb db, IClock clock, UserAdminService users, AuditWriter audit, ApprovalService approvals)
{
    public const int ReasonMin = 10, ReasonMax = 500;

    public async Task<List<RequestableRole>> RequestableRolesAsync(SessionPrincipal actor, CancellationToken ct = default)
    {
        var held = db.UserRoles.Where(r => r.UserId == actor.UserId).Select(r => r.RoleId);
        return await db.Roles.AsNoTracking().Where(r => r.ArchivedAt == null && !r.IsProtected && !held.Contains(r.Id)).OrderBy(r => r.Name)
            .Select(r => new RequestableRole(r.Id, r.Key, r.Name, r.Description, r.Permissions.Count)).ToListAsync(ct);
    }

    public async Task<AccessRequestItem> CreateAsync(CreateAccessRequest req, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var fields = new Dictionary<string, string>();
        var reason = (req.Reason ?? "").Trim();
        if (req.RoleId is null) fields["roleId"] = "Choose a role.";
        if (reason.Length < ReasonMin || reason.Length > ReasonMax) fields["reason"] = $"Explain why you need it ({ReasonMin}–{ReasonMax} characters).";
        if (fields.Count > 0) throw DomainException.Validation("Please correct the highlighted fields.", fields);
        var role = await db.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.Id == req.RoleId && r.ArchivedAt == null && !r.IsProtected, ct)
            ?? throw DomainException.Validation("That role can't be requested.", new Dictionary<string, string> { ["roleId"] = "Choose one of the available roles." });
        if (await db.UserRoles.AnyAsync(r => r.UserId == actor.UserId && r.RoleId == role.Id, ct)) throw DomainException.Conflict("You already have this role.");
        if (await db.AccessRequests.AnyAsync(r => r.RequesterId == actor.UserId && r.RoleId == role.Id && r.Status == AccessRequestStatus.Pending, ct)) throw DomainException.Conflict("You already have a pending request for this role.");
        var ar = new AccessRequest { RequesterId = actor.UserId, RoleId = role.Id, Reason = reason, CreatedAt = clock.UtcNow };
        db.AccessRequests.Add(ar);
        audit.Record("access.request", "role", role.Key, Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["requestId"] = ar.Id.ToString() });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { throw DomainException.Conflict("You already have a pending request for this role."); } // lost a race with another submit
        var item = (await ItemsAsync(db.AccessRequests.Where(r => r.Id == ar.Id), ct)).Single();
        await approvals.NotifyRequestSubmittedAsync(item, actor.UserId, ct);
        return item;
    }

    public async Task<List<AccessRequestItem>> MineAsync(SessionPrincipal actor, CancellationToken ct = default)
        => await ItemsAsync(db.AccessRequests.Where(r => r.RequesterId == actor.UserId), ct);

    public async Task CancelAsync(Guid id, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var ar = await db.AccessRequests.Include(r => r.Role).FirstOrDefaultAsync(r => r.Id == id && r.RequesterId == actor.UserId, ct) ?? throw DomainException.NotFound("Access request");
        if (ar.Status != AccessRequestStatus.Pending) throw DomainException.Conflict("This request was already decided.");
        ar.Status = AccessRequestStatus.Cancelled; ar.DecidedAt = clock.UtcNow;
        audit.Record("access.request.cancel", "role", ar.Role!.Key, Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["requestId"] = id.ToString() });
        await db.SaveChangesAsync(ct);
    }

    public async Task<PagedResult<AccessRequestItem>> ListAsync(string? status, int page, int pageSize, CancellationToken ct = default)
    {
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100);
        var q = db.AccessRequests.AsQueryable();
        if (!string.IsNullOrWhiteSpace(status) && !status.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            if (!Enum.TryParse<AccessRequestStatus>(status, true, out var st)) throw DomainException.Validation("Unknown status.", new Dictionary<string, string> { ["status"] = "Use pending, approved, rejected or cancelled." });
            q = q.Where(r => r.Status == st);
        }
        var total = await q.CountAsync(ct);
        return new PagedResult<AccessRequestItem>(await ItemsAsync(q, ct, page, pageSize), page, pageSize, total);
    }

    public async Task DecideAsync(Guid id, bool approve, DecideAccessRequest? body, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var note = body?.Note?.Trim();
        if (note is { Length: > 500 }) throw DomainException.Validation("The note is too long.", new Dictionary<string, string> { ["note"] = "Use at most 500 characters." });
        var ar = await db.AccessRequests.AsNoTracking().Include(r => r.Role).Include(r => r.Requester).FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw DomainException.NotFound("Access request");
        if (ar.Status != AccessRequestStatus.Pending) throw DomainException.Conflict("This request was already decided.");
        if (ar.RequesterId == actor.UserId)
        {
            audit.Record(approve ? "access.request.approve" : "access.request.reject", "role", ar.Role!.Key, Outcome.Denied, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["reason"] = "own request" });
            await db.SaveChangesAsync(ct);
            throw DomainException.DeniedBecause("You can't decide your own request. Ask another administrator.");
        }
        if (approve)
        {
            if (ar.Requester!.Status != UserStatus.Active) throw DomainException.Conflict("The requester's account isn't active.");
            var current = await db.UserRoles.Where(r => r.UserId == ar.RequesterId).Select(r => r.RoleId).ToListAsync(ct);
            await users.SetRolesAsync(ar.RequesterId, [.. current.Append(ar.RoleId).Distinct()], actor, client, ct); // re-auth, hierarchy, anti-escalation, audit
        }
        var now = clock.UtcNow; var to = approve ? AccessRequestStatus.Approved : AccessRequestStatus.Rejected;
        var rows = await db.AccessRequests.Where(r => r.Id == id && r.Status == AccessRequestStatus.Pending)
            .ExecuteUpdateAsync(u => u.SetProperty(r => r.Status, to).SetProperty(r => r.DecidedBy, actor.UserId).SetProperty(r => r.DecidedAt, now).SetProperty(r => r.DecisionNote, note), ct);
        if (rows == 0) throw DomainException.Conflict("This request was already decided.");
        audit.Record(approve ? "access.request.approve" : "access.request.reject", "role", ar.Role!.Key, Outcome.Success, client, actor.UserId, actor.Email,
            new Dictionary<string, string> { ["requestId"] = id.ToString(), ["requester"] = ar.Requester!.Email });
        await db.SaveChangesAsync(ct);
        approvals.NotifyRequestDecided(ar.Requester!.Email, ar.Requester.Name, ar.Role!.Name, approve, note);
    }

    private async Task<List<AccessRequestItem>> ItemsAsync(IQueryable<AccessRequest> q, CancellationToken ct, int page = 1, int pageSize = 200)
    {
        var rows = await q.AsNoTracking().OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(r => new { r.Id, r.RequesterId, Name = r.Requester!.Name, r.Requester.Email, RoleId = r.Role!.Id, r.Role.Key, RoleName = r.Role.Name, r.Reason, r.Status, r.CreatedAt, r.DecidedBy, r.DecidedAt, r.DecisionNote }).ToListAsync(ct);
        var deciders = rows.Where(r => r.DecidedBy != null).Select(r => r.DecidedBy!.Value).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => deciders.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Name, ct);
        return [.. rows.Select(r => new AccessRequestItem(r.Id, r.RequesterId, r.Name, r.Email, new RoleRef(r.RoleId, r.Key, r.RoleName), r.Reason, r.Status.ToString().ToLowerInvariant(),
            r.CreatedAt, r.DecidedBy is { } d ? names.GetValueOrDefault(d) : null, r.DecidedAt, r.DecisionNote))];
    }
}
