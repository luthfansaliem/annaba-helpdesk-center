using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Application.Email;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;

namespace AnnabaDigital.Application.Users;

/// <summary>INV-001/002: create, list, resend, revoke. Tokens are 256-bit, hash-only at rest, single-use, expiring; resend rotates the token.</summary>
public sealed class InvitationAdminService(IAppDb db, IClock clock, ITokenService tokens, SettingsProvider settings, AuthorizationService authz, AuditWriter audit, IEmailQueue email, IAppLinks links)
{
    /// <summary>An invitee has no profile yet, so the invitation is written in the language of the person who sends it.</summary>
    private async Task<string> InviterLanguageAsync(SessionPrincipal actor, CancellationToken ct)
        => await db.Profiles.AsNoTracking().Where(p => p.UserId == actor.UserId).Select(p => p.Language).FirstOrDefaultAsync(ct) ?? "en";

    private static string Link(IAppLinks l, string token) => $"{l.Origin.TrimEnd('/')}/invite?token={Uri.EscapeDataString(token)}";
    private static readonly string[] Statuses = ["pending", "accepted", "expired", "revoked"];

    private string StatusOf(Invitation i) => i.Status switch
    {
        InvitationStatus.Accepted => "accepted", InvitationStatus.Revoked => "revoked",
        _ => i.ExpiresAt <= clock.UtcNow ? "expired" : "pending",
    };

    public async Task<IReadOnlyList<InvitationItem>> ListAsync(string? status, CancellationToken ct = default)
    {
        var st = status?.Trim().ToLowerInvariant();
        if (st is not null && st != "" && st != "all" && !Statuses.Contains(st)) throw DomainException.Validation("Unknown status filter.", new Dictionary<string, string> { ["status"] = "Use pending, accepted, expired or revoked." });
        var rows = await db.Invitations.AsNoTracking().Include(i => i.Roles).OrderByDescending(i => i.CreatedAt).Take(500).ToListAsync(ct);
        var roleIds = rows.SelectMany(r => r.Roles.Select(x => x.RoleId)).Distinct().ToList();
        var roles = await db.Roles.AsNoTracking().Where(r => roleIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, ct);
        var inviterIds = rows.Select(r => r.InvitedBy).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => inviterIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Name, ct);
        return rows.Select(i => new InvitationItem(i.Id, i.Email, StatusOf(i),
                i.Roles.Where(r => roles.ContainsKey(r.RoleId)).Select(r => new RoleRef(r.RoleId, roles[r.RoleId].Key, roles[r.RoleId].Name)).ToArray(),
                names.GetValueOrDefault(i.InvitedBy), i.CreatedAt, i.ExpiresAt, i.AcceptedAt))
            .Where(i => st is null or "" or "all" || i.Status == st).ToList();
    }

    /// <summary>Roles an actor may attach to an invitation/user = roles whose permissions they fully hold (anti-escalation).</summary>
    public async Task<List<Role>> ResolveGrantableRolesAsync(Guid[] roleIds, SessionPrincipal actor, string action, ClientInfo client, CancellationToken ct)
    {
        var ids = roleIds.Distinct().ToList();
        if (ids.Count == 0 || ids.Count > 20) throw DomainException.Validation("Select between 1 and 20 roles.", new Dictionary<string, string> { ["roleIds"] = "Select at least one role." });
        var roles = await db.Roles.Include(r => r.Permissions).Where(r => ids.Contains(r.Id)).ToListAsync(ct);
        if (roles.Count != ids.Count || roles.Any(r => r.ArchivedAt is not null)) throw DomainException.Validation("One of the selected roles does not exist.", new Dictionary<string, string> { ["roleIds"] = "Unknown or archived role." });
        var mine = await authz.GetEffectivePermissionsAsync(actor.UserId, ct);
        var forbidden = roles.FirstOrDefault(r => !Guards.CanGrant(mine, r.Permissions.Select(p => p.PermissionKey)));
        if (forbidden is not null)
        {
            audit.Record(action, "role", forbidden.Key, Outcome.Denied, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["reason"] = "role exceeds actor privileges" });
            await db.SaveChangesAsync(ct);
            throw DomainException.DeniedBecause($"You can't grant the role “{forbidden.Name}” because it includes permissions you don't hold.");
        }
        return roles;
    }

    public async Task<InvitationItem> CreateAsync(CreateInvitationRequest req, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var addr = AuthService.NormalizeEmail(req.Email);
        if (addr.Length == 0 || addr.Length > 254 || !System.Text.RegularExpressions.Regex.IsMatch(addr, @"^[^\s@]+@[^\s@]+\.[^\s@]+$"))
            throw DomainException.Validation("Enter a valid email address.", new Dictionary<string, string> { ["email"] = "Enter a valid email address." });
        if (req.Message is { Length: > 500 }) throw DomainException.Validation("Message is too long.", new Dictionary<string, string> { ["message"] = "Use at most 500 characters." });
        var roles = await ResolveGrantableRolesAsync(req.RoleIds ?? [], actor, "invitation.create", client, ct);
        var s = await settings.GetAsync(ct);

        string raw; Invitation inv;
        try
        {
            (inv, raw) = await db.InTransactionAsync(async () =>
            {
                if (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == addr, ct)) throw DomainException.Conflict("A user with this email already exists.");
                if (await db.Invitations.AnyAsync(i => i.Email == addr && i.Status == InvitationStatus.Pending && i.ExpiresAt > clock.UtcNow, ct)) throw DomainException.Conflict("A pending invitation already exists for this email.");
                // an expired-but-still-"Pending" row would violate the partial unique index; retire it first
                var stale = await db.Invitations.Where(i => i.Email == addr && i.Status == InvitationStatus.Pending).ToListAsync(ct);
                foreach (var o in stale) { o.Status = InvitationStatus.Revoked; o.RevokedAt = clock.UtcNow; }
                await db.SaveChangesAsync(ct);

                var token = tokens.NewToken(); var now = clock.UtcNow;
                var i = new Invitation { Email = addr, TokenHash = tokens.Hash(token), InvitedBy = actor.UserId, Message = req.Message?.Trim(), Status = InvitationStatus.Pending, CreatedAt = now, ExpiresAt = now.AddDays(s.InvitationExpiryDays) };
                foreach (var r in roles) i.Roles.Add(new InvitationRole { RoleId = r.Id });
                db.Invitations.Add(i);
                audit.Record("invitation.create", "invitation", i.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["email"] = addr, ["roles"] = string.Join(",", roles.Select(r => r.Key)) });
                await db.SaveChangesAsync(ct);
                return (i, token);
            }, ct);
        }
        catch (DomainException e) when (e.Code == ErrorCodes.Conflict)
        {
            db.ResetTracking();
            audit.Record("invitation.create", "invitation", null, Outcome.Failure, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["email"] = addr, ["reason"] = e.Code });
            await db.SaveChangesAsync(ct);
            throw;
        }
        email.Enqueue(EmailTemplates.Invitation(addr, actor.Name, Link(links, raw), s.InvitationExpiryDays) with { Lang = await InviterLanguageAsync(actor, ct) });
        return (await ListAsync(null, ct)).First(x => x.Id == inv.Id);
    }

    /// <summary>Rotates the token (old link stops working), extends expiry and re-activates. Shared with "resend" from the users page.</summary>
    public async Task<string> RotateAsync(Invitation inv, SecuritySettings s, CancellationToken ct)
    {
        var raw = tokens.NewToken(); var now = clock.UtcNow;
        inv.TokenHash = tokens.Hash(raw); inv.Status = InvitationStatus.Pending; inv.RevokedAt = null; inv.CreatedAt = now; inv.ExpiresAt = now.AddDays(s.InvitationExpiryDays);
        await Task.CompletedTask;
        return raw;
    }

    public async Task<InvitationItem> ResendAsync(Guid id, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var s = await settings.GetAsync(ct);
        var inv = await db.Invitations.Include(i => i.Roles).FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw DomainException.NotFound("Invitation");
        if (inv.Status == InvitationStatus.Accepted) throw DomainException.Invariant("This invitation was already accepted.");
        await ResolveGrantableRolesAsync([.. inv.Roles.Select(r => r.RoleId)], actor, "invitation.resend", client, ct);
        if (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == inv.Email && u.Status != UserStatus.Pending, ct)) throw DomainException.Conflict("A user with this email already exists.");
        if (await db.Invitations.AnyAsync(i => i.Email == inv.Email && i.Id != inv.Id && i.Status == InvitationStatus.Pending && i.ExpiresAt > clock.UtcNow, ct)) throw DomainException.Conflict("Another pending invitation exists for this email.");

        var raw = await RotateAsync(inv, s, ct);
        audit.Record("invitation.resend", "invitation", inv.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["email"] = inv.Email });
        await db.SaveChangesAsync(ct);
        email.Enqueue(EmailTemplates.Invitation(inv.Email, actor.Name, Link(links, raw), s.InvitationExpiryDays) with { Lang = await InviterLanguageAsync(actor, ct) });
        return (await ListAsync(null, ct)).First(x => x.Id == inv.Id);
    }

    public async Task RevokeAsync(Guid id, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var inv = await db.Invitations.FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw DomainException.NotFound("Invitation");
        if (inv.Status != InvitationStatus.Pending) throw DomainException.Invariant("Only pending invitations can be revoked.");
        var rows = await db.Invitations.Where(i => i.Id == id && i.Status == InvitationStatus.Pending).ExecuteUpdateAsync(u => u.SetProperty(i => i.Status, InvitationStatus.Revoked).SetProperty(i => i.RevokedAt, clock.UtcNow), ct);
        if (rows == 0) throw DomainException.Invariant("Only pending invitations can be revoked."); // accepted concurrently
        audit.Record("invitation.revoke", "invitation", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["email"] = inv.Email });
        await db.SaveChangesAsync(ct);
    }
}
