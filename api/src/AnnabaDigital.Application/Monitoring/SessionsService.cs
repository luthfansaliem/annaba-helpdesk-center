using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Application.Users;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;

namespace AnnabaDigital.Application.Monitoring;

/// <summary>SES-001. Users manage their own sessions; <c>sessions.read_all</c> lists everyone's (never tokens). Admin-side revocation lives in UserAdminService.</summary>
public sealed class SessionsService(IAppDb db, IClock clock, SessionService sessions, AuditWriter audit)
{
    private IQueryable<Session> Live(IQueryable<Session> q) { var now = clock.UtcNow; return q.Where(s => s.RevokedAt == null && s.IdleExpiresAt > now && s.AbsoluteExpiresAt > now); }
    private static DateTimeOffset Expiry(Session s) => s.IdleExpiresAt < s.AbsoluteExpiresAt ? s.IdleExpiresAt : s.AbsoluteExpiresAt;

    public async Task<IReadOnlyList<MySession>> MineAsync(SessionPrincipal who, CancellationToken ct = default)
    {
        var rows = await Live(db.Sessions.AsNoTracking().Where(s => s.UserId == who.UserId)).OrderByDescending(s => s.LastActiveAt).ToListAsync(ct);
        return [.. rows.Select(s => new MySession(s.Id, s.Id == who.SessionId, s.DeviceLabel, s.BrowserLabel, s.Ip, s.CreatedAt, s.LastActiveAt, Expiry(s), s.LockedAt != null, s.Remember))];
    }

    /// <summary>Returns true when the revoked session was the caller's own (the endpoint then clears the cookie).</summary>
    public async Task<bool> RevokeOneAsync(SessionPrincipal who, Guid sessionId, ClientInfo client, CancellationToken ct = default)
    {
        var s = await db.Sessions.FirstOrDefaultAsync(x => x.Id == sessionId && x.UserId == who.UserId && x.RevokedAt == null, ct) ?? throw DomainException.NotFound("Session"); // other people's ids look identical to unknown ones
        s.RevokedAt = clock.UtcNow; s.RevokedReason = RevokeReason.User;
        audit.Record("session.revoke", "session", s.Id.ToString(), Outcome.Success, client, who.UserId, who.Email, new Dictionary<string, string> { ["self"] = "true", ["current"] = (s.Id == who.SessionId).ToString() });
        await db.SaveChangesAsync(ct);
        return s.Id == who.SessionId;
    }

    public async Task<int> RevokeOthersAsync(SessionPrincipal who, ClientInfo client, CancellationToken ct = default)
    {
        var n = await sessions.RevokeAllForUserAsync(who.UserId, RevokeReason.User, who.SessionId, ct);
        audit.Record("session.revoke_others", "user", who.UserId.ToString(), Outcome.Success, client, who.UserId, who.Email, new Dictionary<string, string> { ["count"] = n.ToString() });
        await db.SaveChangesAsync(ct);
        return n;
    }

    public async Task<int> SignOutEverywhereAsync(SessionPrincipal who, ClientInfo client, CancellationToken ct = default)
    {
        var n = await sessions.RevokeAllForUserAsync(who.UserId, RevokeReason.User, null, ct);
        audit.Record("session.revoke_all", "user", who.UserId.ToString(), Outcome.Success, client, who.UserId, who.Email, new Dictionary<string, string> { ["count"] = n.ToString() });
        await db.SaveChangesAsync(ct);
        return n;
    }

    public async Task<PagedResult<AllSessionItem>> AllAsync(SessionQuery q, CancellationToken ct = default)
    {
        var page = Math.Max(1, q.Page); var size = Math.Clamp(q.PageSize, 1, 100);
        var query = Live(db.Sessions.AsNoTracking()); if (q.UserId is { } uid) query = query.Where(s => s.UserId == uid);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(s => s.LastActiveAt).ThenBy(s => s.Id).Skip((page - 1) * size).Take(size)
            .Select(s => new { s.Id, s.UserId, s.User!.Name, s.User.Email, s.DeviceLabel, s.BrowserLabel, s.Ip, s.CreatedAt, s.LastActiveAt, s.IdleExpiresAt, s.AbsoluteExpiresAt, Locked = s.LockedAt != null }).ToListAsync(ct);
        return new PagedResult<AllSessionItem>([.. rows.Select(r => new AllSessionItem(r.Id, r.UserId, r.Name, r.Email, r.DeviceLabel, r.BrowserLabel, r.Ip, r.CreatedAt, r.LastActiveAt, r.IdleExpiresAt < r.AbsoluteExpiresAt ? r.IdleExpiresAt : r.AbsoluteExpiresAt, r.Locked))], page, size, total);
    }
}
