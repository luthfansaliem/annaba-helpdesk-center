using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Application.Auth;

public sealed record InvitationLookup(string State, string? Email, string[]? Roles);
public sealed record AcceptInvitationRequest(string? Token, string? Name, string? Password);

/// <summary>INV-003. Creation/resend/revoke belong to the admin slice; this is the public acceptance side.</summary>
public sealed class InvitationAcceptService(IAppDb db, IPasswordHasher hasher, ITokenService tokens, IRateLimiter limiter, IClock clock,
    SettingsProvider settings, PasswordPolicy policy, AuditWriter audit)
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    private async Task Throttle(ClientInfo client, int limit, CancellationToken ct)
    {
        var hit = await limiter.HitAsync($"invite:ip:{client.Ip ?? "unknown"}", limit, Window, ct);
        if (!hit.Allowed) throw DomainException.RateLimited(hit.RetryAfter);
    }

    private static string StateOf(Invitation i, DateTimeOffset now) => i.Status switch
    {
        InvitationStatus.Accepted => "used",
        InvitationStatus.Revoked => "revoked",
        _ => i.ExpiresAt <= now ? "expired" : "valid",
    };

    /// <summary>Tells the UI which screen to show. Unknown tokens reveal nothing beyond "invalid" (tokens are 256-bit, so existence ≠ guessable).</summary>
    public async Task<InvitationLookup> LookupAsync(string? token, ClientInfo client, CancellationToken ct = default)
    {
        await Throttle(client, 30, ct);
        if (string.IsNullOrEmpty(token) || token.Length > 200) return new("invalid", null, null);
        var hash = tokens.Hash(token);
        var inv = await db.Invitations.AsNoTracking().Include(i => i.Roles).FirstOrDefaultAsync(i => i.TokenHash == hash, ct);
        if (inv is null) return new("invalid", null, null);
        var state = StateOf(inv, clock.UtcNow);
        if (state != "valid") return new(state, null, null);
        var roleIds = inv.Roles.Select(r => r.RoleId).ToList();
        var names = await db.Roles.AsNoTracking().Where(r => roleIds.Contains(r.Id) && r.ArchivedAt == null).Select(r => r.Name).ToListAsync(ct);
        return new("valid", inv.Email, [.. names.Order()]);
    }

    public async Task AcceptAsync(AcceptInvitationRequest req, ClientInfo client, CancellationToken ct = default)
    {
        await Throttle(client, 10, ct);
        var unavailable = DomainException.Expired("This invitation is no longer valid.");
        if (string.IsNullOrEmpty(req.Token) || req.Token.Length > 200) throw unavailable;
        var hash = tokens.Hash(req.Token);
        var now = clock.UtcNow;
        var inv = await db.Invitations.AsNoTracking().Include(i => i.Roles).FirstOrDefaultAsync(i => i.TokenHash == hash, ct);
        if (inv is null || StateOf(inv, now) != "valid") throw unavailable;

        var name = (req.Name ?? "").Trim();
        if (name.Length < 2 || name.Length > 200) throw DomainException.Validation("Please correct the highlighted fields.", new Dictionary<string, string> { ["name"] = "Enter your full name." });
        var s = await settings.GetForAsync(inv.TenantId, ct); // anonymous: the inviting organisation sets the password rules
        await policy.ValidateAsync(req.Password, inv.Email, name, s, ct); // before consuming the token
        var passwordHash = hasher.Hash(req.Password!);

        await db.InTransactionAsync(async () =>
        {
            var accepted = await db.Invitations.Where(i => i.TokenHash == hash && i.Status == InvitationStatus.Pending && i.ExpiresAt > now)
                .ExecuteUpdateAsync(u => u.SetProperty(i => i.Status, InvitationStatus.Accepted).SetProperty(i => i.AcceptedAt, now), ct);
            if (accepted == 0) throw unavailable; // lost a race: exactly one acceptance wins

            var wanted = inv.Roles.Select(x => x.RoleId).ToList();
            var roleIds = await db.Roles.Where(r => wanted.Contains(r.Id) && r.ArchivedAt == null).Select(r => r.Id).ToListAsync(ct);
            if (roleIds.Count == 0) roleIds = [(await db.Roles.FirstAsync(r => r.TenantId == inv.TenantId && r.Key == SystemRoles.User, ct)).Id];

            var user = await db.Users.Include(u => u.Credential).Include(u => u.Profile).Include(u => u.UserRoles).FirstOrDefaultAsync(u => u.Email == inv.Email, ct);
            if (user is not null && user.Status != UserStatus.Pending) throw unavailable; // an account already exists: never take it over
            if (user is null)
            {
                user = new User { Email = inv.Email, TenantId = inv.TenantId, CreatedAt = now };
                user.Profile = new UserProfile();
                db.Users.Add(user);
            }
            user.Name = name; user.Status = UserStatus.Active; user.EmailVerifiedAt = now; user.UpdatedAt = now;
            if (user.Credential is null) user.Credential = new UserCredential { PasswordHash = passwordHash, PasswordChangedAt = now };
            else { user.Credential.PasswordHash = passwordHash; user.Credential.PasswordChangedAt = now; }
            user.UserRoles.Clear();
            foreach (var rid in roleIds) user.UserRoles.Add(new UserRole { RoleId = rid, AssignedBy = inv.InvitedBy, AssignedAt = now });

            audit.Record("invitation.accept", "invitation", inv.Id.ToString(), Outcome.Success, client, user.Id, user.Email);
            await db.SaveChangesAsync(ct);
            await db.Invitations.Where(i => i.TokenHash == hash).ExecuteUpdateAsync(u => u.SetProperty(i => i.AcceptedUserId, user.Id), ct);
            return 0;
        }, ct);
    }
}
