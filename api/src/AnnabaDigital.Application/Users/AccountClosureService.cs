using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Application.Email;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;

namespace AnnabaDigital.Application.Users;

public sealed record ClosureView(Guid Id, string Status, string Reason, DateTimeOffset RequestedAt, DateTimeOffset? ExecuteAfter, string? DecisionNote);
public sealed record ClosureItem(Guid Id, Guid UserId, string UserName, string UserEmail, string Status, string Reason, DateTimeOffset RequestedAt,
    string? DecidedByName, DateTimeOffset? DecidedAt, string? DecisionNote, DateTimeOffset? ExecuteAfter);
public sealed record RequestClosure(string? Reason);
public sealed record DecideClosure(string? Note);

/// <summary>
/// A person asks to close their account (fresh password check). Someone who may suspend accounts approves; a waiting period follows during which the person
/// can still cancel; then the scheduler closes it: the account is deactivated and its personal data erased (name, e-mail, profile, picture, credentials, MFA,
/// linked sign-ins, sessions, roles). The row itself stays so history keeps pointing at "Closed account"; audit entries are append-only and are kept for the
/// organisation's retention period. The last active Super Admin can't be closed.
/// </summary>
public sealed class AccountClosureService(IAppDb db, IClock clock, UserAdminService users, ApprovalService approvals, AuditWriter audit, SettingsProvider settings,
    IEmailQueue email, IAppLinks links, ITenantContext tenant)
{
    public const int WaitDays = 14;
    public const int ReasonMax = 500;
    private static readonly ClosureStatus[] Open = [ClosureStatus.Pending, ClosureStatus.Approved];

    // ------------------------------------------------------------ the person
    public async Task<ClosureView?> MineAsync(SessionPrincipal who, CancellationToken ct = default)
    {
        var r = await db.AccountClosureRequests.AsNoTracking().Where(x => x.UserId == who.UserId).OrderByDescending(x => x.RequestedAt).FirstOrDefaultAsync(ct);
        return r is null ? null : new ClosureView(r.Id, r.Status.ToString().ToLowerInvariant(), r.Reason, r.RequestedAt, r.ExecuteAfter, r.DecisionNote);
    }

    public async Task<ClosureView> RequestAsync(RequestClosure req, SessionPrincipal who, ClientInfo client, CancellationToken ct = default)
    {
        Guards.RequireRecentReauthForCritical(who, await settings.GetAsync(ct), clock); // an irreversible request: a fresh password check
        var reason = (req.Reason ?? "").Trim();
        if (reason.Length > ReasonMax) throw DomainException.Validation("The reason is too long.", new Dictionary<string, string> { ["reason"] = $"Use at most {ReasonMax} characters." });
        var me = await db.Users.AsNoTracking().FirstAsync(u => u.Id == who.UserId, ct);
        if (await users.IsLastActiveSuperAdminAsync(who.UserId, ct)) throw DomainException.Invariant("You are the last active Super Admin. Make someone else a Super Admin before closing your account.");
        if (await db.AccountClosureRequests.AnyAsync(x => x.UserId == who.UserId && Open.Contains(x.Status), ct)) throw DomainException.Conflict("You already have a closure request in progress.");
        var r = new AccountClosureRequest { UserId = who.UserId, Reason = reason, RequestedAt = clock.UtcNow };
        db.AccountClosureRequests.Add(r);
        audit.Record("account.closure.request", "user", who.UserId.ToString(), Outcome.Success, client, who.UserId, who.Email, new Dictionary<string, string> { ["requestId"] = r.Id.ToString() });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { throw DomainException.Conflict("You already have a closure request in progress."); } // lost a race
        await approvals.NotifyClosureRequestedAsync(me.Name, who.UserId, ct);
        return new ClosureView(r.Id, "pending", r.Reason, r.RequestedAt, null, null);
    }

    public async Task CancelAsync(SessionPrincipal who, ClientInfo client, CancellationToken ct = default)
    {
        var r = await db.AccountClosureRequests.FirstOrDefaultAsync(x => x.UserId == who.UserId && Open.Contains(x.Status), ct) ?? throw DomainException.Conflict("There is no closure request to cancel.");
        r.Status = ClosureStatus.Cancelled; r.DecidedAt = clock.UtcNow;
        audit.Record("account.closure.cancel", "user", who.UserId.ToString(), Outcome.Success, client, who.UserId, who.Email, new Dictionary<string, string> { ["requestId"] = r.Id.ToString() });
        await db.SaveChangesAsync(ct);
    }

    // ------------------------------------------------------------ the administrators
    public async Task<PagedResult<ClosureItem>> ListAsync(string? status, int page, int pageSize, CancellationToken ct = default)
    {
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100);
        var q = db.AccountClosureRequests.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(status) && !status.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            if (!Enum.TryParse<ClosureStatus>(status, true, out var st)) throw DomainException.Validation("Unknown status.", new Dictionary<string, string> { ["status"] = "Use pending, approved, rejected, cancelled or completed." });
            q = q.Where(x => x.Status == st);
        }
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(x => x.RequestedAt).ThenByDescending(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new { x.Id, x.UserId, Name = x.User!.Name, x.User.Email, x.Status, x.Reason, x.RequestedAt, x.DecidedBy, x.DecidedAt, x.DecisionNote, x.ExecuteAfter }).ToListAsync(ct);
        var deciders = rows.Where(r => r.DecidedBy != null).Select(r => r.DecidedBy!.Value).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => deciders.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Name, ct);
        return new PagedResult<ClosureItem>([.. rows.Select(r => new ClosureItem(r.Id, r.UserId, r.Name, r.Email, r.Status.ToString().ToLowerInvariant(), r.Reason, r.RequestedAt,
            r.DecidedBy is { } d ? names.GetValueOrDefault(d) : null, r.DecidedAt, r.DecisionNote, r.ExecuteAfter))], page, pageSize, total);
    }

    public async Task DecideAsync(Guid id, bool approve, DecideClosure? body, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var note = body?.Note?.Trim();
        if (note is { Length: > 500 }) throw DomainException.Validation("The note is too long.", new Dictionary<string, string> { ["note"] = "Use at most 500 characters." });
        var r = await db.AccountClosureRequests.AsNoTracking().Include(x => x.User).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Closure request");
        if (r.Status != ClosureStatus.Pending) throw DomainException.Conflict("This request was already decided.");
        var action = approve ? "account.closure.approve" : "account.closure.reject";
        if (r.UserId == actor.UserId)
        {
            audit.Record(action, "user", r.UserId.ToString(), Outcome.Denied, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["reason"] = "own request" });
            await db.SaveChangesAsync(ct);
            throw DomainException.DeniedBecause("You can't decide your own request. Ask another administrator.");
        }
        await users.EnsureManageableAsync(r.UserId, actor, action, client, ct); // hierarchy: not someone who holds permissions you don't
        if (approve && await users.IsLastActiveSuperAdminAsync(r.UserId, ct)) throw DomainException.Invariant("This is the last active Super Admin. Assign another Super Admin first.");

        var now = clock.UtcNow; var to = approve ? ClosureStatus.Approved : ClosureStatus.Rejected; DateTimeOffset? when = approve ? now.AddDays(WaitDays) : null;
        var rows = await db.AccountClosureRequests.Where(x => x.Id == id && x.Status == ClosureStatus.Pending)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.Status, to).SetProperty(x => x.DecidedBy, actor.UserId).SetProperty(x => x.DecidedAt, now).SetProperty(x => x.DecisionNote, note).SetProperty(x => x.ExecuteAfter, when), ct);
        if (rows == 0) throw DomainException.Conflict("This request was already decided.");
        audit.Record(action, "user", r.UserId.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["requestId"] = id.ToString(), ["executeAfter"] = when?.ToString("O") ?? "" });
        await db.SaveChangesAsync(ct);
        email.Enqueue(approve ? EmailTemplates.ClosureApproved(r.User!.Email, r.User.Name, when!.Value, $"{links.Origin.TrimEnd('/')}/profile?tab=privacy") : EmailTemplates.ClosureRejected(r.User!.Email, r.User.Name, note));
    }

    // ------------------------------------------------------------ the scheduler
    /// <summary>Closes every approved request whose waiting period is over, each as its own organisation. Safe with several instances: the request is claimed inside the closing transaction.</summary>
    public async Task<int> RunDueAsync(CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var due = await db.AccountClosureRequests.IgnoreQueryFilters().AsNoTracking().Where(x => x.Status == ClosureStatus.Approved && x.ExecuteAfter != null && x.ExecuteAfter <= now)
            .Select(x => new { x.Id, x.TenantId, x.UserId }).ToListAsync(ct);
        var closed = 0;
        foreach (var d in due)
        {
            using (tenant.Switch(d.TenantId))
            {
                db.ResetTracking();
                if (await CloseAsync(d.Id, d.UserId, now, ct)) closed++;
            }
        }
        return closed;
    }

    private async Task<bool> CloseAsync(Guid requestId, Guid userId, DateTimeOffset now, CancellationToken ct)
    {
        var u = await db.Users.Include(x => x.Profile).FirstOrDefaultAsync(x => x.Id == userId, ct);
        if (u is null) return false;
        var client = new ClientInfo(null, null, null);
        if (await users.IsLastActiveSuperAdminAsync(userId, ct))
        {
            // they became the last Super Admin during the waiting period: don't leave the organisation without one
            await db.AccountClosureRequests.Where(x => x.Id == requestId && x.Status == ClosureStatus.Approved)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, ClosureStatus.Cancelled).SetProperty(x => x.DecidedAt, now).SetProperty(x => x.DecisionNote, "Not closed: this became the last active Super Admin."), ct);
            audit.Record("account.closure.skipped", "user", userId.ToString(), Outcome.Failure, client, null, "system (account closure)", new Dictionary<string, string> { ["reason"] = "last_super_admin" });
            await db.SaveChangesAsync(ct);
            return false;
        }
        var originalEmail = u.Email; var originalName = u.Name; var claimed = false;
        await db.InTransactionAsync(async () =>
        {
            var rows = await db.AccountClosureRequests.Where(x => x.Id == requestId && x.Status == ClosureStatus.Approved)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, ClosureStatus.Completed).SetProperty(x => x.CompletedAt, now), ct);
            if (rows == 0) return 0; // another instance has it
            claimed = true;
            await db.Sessions.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
            await db.MfaRecoveryCodes.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
            await db.MfaFactors.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
            await db.UserIdentities.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
            await db.EmailVerificationTokens.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
            await db.PasswordResetTokens.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
            await db.Avatars.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
            await db.Credentials.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
            // the invitation that created this account (and any other for the same address) still carries the e-mail address and a free-text message
            var closedEmail = $"closed-{u.Id:N}@closed.invalid";
            await db.Invitations.Where(x => x.AcceptedUserId == userId || x.Email == originalEmail).ExecuteUpdateAsync(s => s.SetProperty(x => x.Email, closedEmail).SetProperty(x => x.Message, (string?)null), ct);
            await db.UserRoles.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
            u.Status = UserStatus.Deactivated; u.Name = "Closed account"; u.Email = closedEmail; u.UpdatedAt = now; u.Version++;
            if (u.Profile is { } p) { p.Phone = null; p.Department = null; p.JobTitle = null; p.AvatarId = null; }
            audit.Record("account.close", "user", userId.ToString(), Outcome.Success, client, null, "system (account closure)", new Dictionary<string, string> { ["requestId"] = requestId.ToString() });
            await db.SaveChangesAsync(ct);
            return 0;
        }, ct);
        if (claimed) email.Enqueue(EmailTemplates.AccountClosed(originalEmail, originalName));
        return claimed;
    }
}
