using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Application.Auth;

/// <summary>What the API knows about the caller after authentication. Permissions are NOT embedded in the cookie — they are resolved per request.</summary>
public sealed record SessionPrincipal(Guid SessionId, Guid UserId, string Email, string Name, bool Locked, bool MfaPending, DateTimeOffset? ReauthAt, bool MfaEnrollmentRequired = false, bool Remember = false, Guid TenantId = default, bool IsPlatform = false, Guid? ActingTenantId = null, string? ActingTenantName = null);

public static class RevokeReason
{
    public const string User = "user";
    public const string Admin = "admin";
    public const string PasswordReset = "password_reset";
    public const string Suspended = "suspended";
    public const string TooManyUnlockFailures = "unlock_failures";
    public const string Rotated = "rotated";
}

public sealed class SessionService(IAppDb db, ITokenService tokens, IClock clock, SettingsProvider settings, TenantDirectory tenants)
{
    private static readonly TimeSpan TouchInterval = TimeSpan.FromSeconds(60);
    /// <summary>Support mode ends by itself after this long; the platform administrator enters again if they still need it.</summary>
    public static readonly TimeSpan SupportMax = TimeSpan.FromHours(2);

    public sealed record Created(string RawToken, Session Session);

    public async Task<Created> CreateAsync(User user, bool remember, bool mfaRequired, ClientInfo client, CancellationToken ct = default)
    {
        var s = await settings.GetForAsync(user.TenantId, ct);
        var now = clock.UtcNow;
        var (device, browser) = DeviceInfo.Describe(client.UserAgent);
        var absolute = remember ? TimeSpan.FromDays(s.RememberAbsoluteDays) : TimeSpan.FromHours(s.SessionAbsoluteHours);
        var idle = remember ? TimeSpan.FromDays(s.RememberIdleDays) : TimeSpan.FromMinutes(s.SessionIdleMinutes);
        var raw = tokens.NewToken();
        var session = new Session
        {
            TokenHash = tokens.Hash(raw), UserId = user.Id, TenantId = user.TenantId, CreatedAt = now, LastActiveAt = now, IdleExpiresAt = now + idle, AbsoluteExpiresAt = now + absolute,
            Remember = remember, MfaRequired = mfaRequired, Ip = client.Ip, UserAgent = client.UserAgent?.Length > 300 ? client.UserAgent[..300] : client.UserAgent,
            DeviceLabel = device, BrowserLabel = browser,
        };
        db.Sessions.Add(session);
        return new Created(raw, session);
    }

    /// <summary>Resolves a raw cookie token to a usable principal or null (revoked / expired / user not active).</summary>
    public async Task<SessionPrincipal?> ValidateAsync(string? rawToken, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(rawToken) || rawToken.Length > 200) return null;
        var hash = tokens.Hash(rawToken);
        var session = await db.Sessions.Include(x => x.User).FirstOrDefaultAsync(x => x.TokenHash == hash, ct);
        if (session?.User is null) return null;
        var now = clock.UtcNow;
        if (session.RevokedAt is not null || now >= session.IdleExpiresAt || now >= session.AbsoluteExpiresAt) return null;
        if (session.User.Status != UserStatus.Active) return null;
        var tenant = await tenants.GetAsync(session.User.TenantId, ct);
        if (tenant is null || tenant.Status != TenantStatus.Active) return null; // a suspended organisation locks everyone out at once

        // Support mode only holds while the person is still in the platform organisation, the target is an active other organisation, and the time box has not run out.
        Guid? acting = null; string? actingName = null;
        if (session.ActingTenantId is { } at)
        {
            var target = await tenants.GetAsync(at, ct);
            if (tenant.IsPlatform && target is { IsPlatform: false, Status: TenantStatus.Active } && session.ActingSince is { } since && now - since < SupportMax) { acting = at; actingName = target.Name; }
            else { session.ActingTenantId = null; session.ActingSince = null; await db.SaveChangesAsync(ct); }
        }

        var s = await settings.GetForAsync(session.User.TenantId, ct);
        if (now - session.LastActiveAt >= TouchInterval)
        {
            var idle = session.Remember ? TimeSpan.FromDays(s.RememberIdleDays) : TimeSpan.FromMinutes(s.SessionIdleMinutes);
            session.LastActiveAt = now;
            session.IdleExpiresAt = now + idle > session.AbsoluteExpiresAt ? session.AbsoluteExpiresAt : now + idle;
            await db.SaveChangesAsync(ct);
        }
        // Organisation policy "required": a user without a confirmed factor may only reach the enrollment endpoints.
        var enrollmentRequired = s.MfaPolicy == "required" && !session.MfaRequired && !await db.MfaFactors.AnyAsync(f => f.UserId == session.UserId && f.ConfirmedAt != null, ct);
        return new SessionPrincipal(session.Id, session.UserId, session.User.Email, session.User.Name, session.LockedAt is not null, session.MfaRequired && session.MfaVerifiedAt is null,
            session.ReauthAt, enrollmentRequired, session.Remember, session.User.TenantId, tenant.IsPlatform, acting, actingName);
    }

    public async Task<bool> RevokeAsync(Guid sessionId, string reason, CancellationToken ct = default)
    {
        var s = await db.Sessions.FirstOrDefaultAsync(x => x.Id == sessionId && x.RevokedAt == null, ct);
        if (s is null) return false;
        s.RevokedAt = clock.UtcNow; s.RevokedReason = reason;
        return true;
    }

    public async Task<int> RevokeAllForUserAsync(Guid userId, string reason, Guid? exceptSessionId = null, CancellationToken ct = default)
    {
        var list = await db.Sessions.Where(x => x.UserId == userId && x.RevokedAt == null && x.Id != exceptSessionId).ToListAsync(ct);
        foreach (var s in list) { s.RevokedAt = clock.UtcNow; s.RevokedReason = reason; }
        return list.Count;
    }
}

public sealed class AuthorizationService
{
    private readonly IAppDb db;
    private readonly Dictionary<Guid, IReadOnlySet<string>> _memo = new();

    // Remembered for the rest of THIS request only (the service is scoped), and forgotten the moment anything that decides permissions is saved, so a role change is
    // reflected immediately. Never shared between requests: authorization is not cached across them.
    public AuthorizationService(IAppDb db) { this.db = db; db.PermissionDataChanged += () => _memo.Clear(); }

    /// <summary>Effective permissions = union of permissions of the user's non-archived roles. Zero when the user is not Active.</summary>
    public async Task<IReadOnlySet<string>> GetEffectivePermissionsAsync(Guid userId, CancellationToken ct = default)
    {
        if (_memo.TryGetValue(userId, out var known)) return known;
        // A person's permissions come from their own role assignments, wherever the request is currently scoped (e.g. the platform acting inside a new tenant).
        var keys = await (from u in db.Users.IgnoreQueryFilters().AsNoTracking()
                          where u.Id == userId && u.Status == UserStatus.Active
                          from ur in db.UserRoles.Where(x => x.UserId == u.Id)
                          join r in db.Roles.IgnoreQueryFilters() on ur.RoleId equals r.Id
                          where r.ArchivedAt == null
                          join rp in db.RolePermissions on r.Id equals rp.RoleId
                          select rp.PermissionKey).Distinct().ToListAsync(ct);
        // Defence in depth: ignore anything not in the code-defined catalog.
        var set = keys.Where(k => Permissions.All.Contains(k)).ToHashSet();
        _memo[userId] = set; return set;
    }
}
