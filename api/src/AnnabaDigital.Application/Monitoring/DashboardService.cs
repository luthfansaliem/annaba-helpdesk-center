using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Application.Users;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Application.Monitoring;

/// <summary>
/// DSH-001…005. Every number comes from the database, and every widget is included only if the caller holds the permission that
/// protects the underlying data (a Security Auditor never receives invitation data, a plain user never receives organisation data).
/// </summary>
public sealed class DashboardService(IAppDb db, IClock clock, AuthorizationService authz, UserAdminService users, MonitoringService monitoring, SettingsProvider settings)
{
    private static readonly string[] RoleChangeActions = ["role.create", "role.update", "role.duplicate", "role.archive", "matrix.save", "user.assign_role"];

    public async Task<AdminDashboard> AdminAsync(SessionPrincipal actor, CancellationToken ct = default)
    {
        var p = await authz.GetEffectivePermissionsAsync(actor.UserId, ct); var now = clock.UtcNow; var visible = new List<string>();
        bool Has(string perm) => p.Contains(perm);

        int? total = null, active = null, susp = null, pendInv = null, roles = null, sess = null;
        int[]? growth = null; StatusCount[]? status = null, byRole = null; UserListItem[]? recentReg = null;
        if (Has(Permissions.UsersRead))
        {
            var st = await users.StatsAsync(actor, ct); total = st.Total; active = st.Active; susp = st.Suspended; growth = st.GrowthLast12Months;
            status = st.ByStatus.Where(s => s.Count > 0).ToArray(); byRole = st.ByRole; recentReg = st.RecentRegistrations; visible.AddRange(["kpi.users", "chart.growth", "chart.status", "chart.roles", "panel.registrations"]);
        }
        if (Has(Permissions.InvitationsRead)) { pendInv = await db.Invitations.CountAsync(i => i.Status == InvitationStatus.Pending && i.ExpiresAt > now, ct); visible.Add("kpi.invitations"); }
        if (Has(Permissions.RolesRead)) { roles = await db.Roles.CountAsync(r => r.ArchivedAt == null, ct); visible.Add("kpi.roles"); }
        if (Has(Permissions.SessionsReadAll)) { sess = await db.Sessions.CountAsync(s => s.RevokedAt == null && s.IdleExpiresAt > now && s.AbsoluteExpiresAt > now, ct); visible.Add("kpi.sessions"); }

        LoginActivity? login = null; SecurityEventItem[]? recentSec = null;
        if (Has(Permissions.SecurityRead))
        {
            const int Days = 14; var start = now.UtcDateTime.Date.AddDays(-(Days - 1)); var startTs = new DateTimeOffset(start, TimeSpan.Zero);
            // counted per day in the database, not by loading every sign-in event of the last two weeks
            var rows = await db.AuditEvents.AsNoTracking().Where(a => a.Ts >= startTs && (a.Action == "auth.login.success" || a.Action == "auth.login.failure"))
                .GroupBy(a => new { a.Ts.UtcDateTime.Year, a.Ts.UtcDateTime.Month, a.Ts.UtcDateTime.Day, a.Action }).Select(g => new { g.Key.Year, g.Key.Month, g.Key.Day, g.Key.Action, N = g.Count() }).ToListAsync(ct);
            var days = Enumerable.Range(0, Days).Select(i => start.AddDays(i)).ToArray();
            int Count(string action, DateTime d) => rows.Where(r => r.Action == action && r.Year == d.Year && r.Month == d.Month && r.Day == d.Day).Sum(r => r.N);
            login = new LoginActivity([.. days.Select(d => d.ToString("yyyy-MM-dd"))], [.. days.Select(d => Count("auth.login.success", d))], [.. days.Select(d => Count("auth.login.failure", d))]);
            recentSec = [.. (await monitoring.SecurityAsync(new SecurityQuery(null, null, 1, 5), ct)).Data]; visible.AddRange(["chart.logins", "panel.security"]);
        }

        RecentLogin[]? logins = null; RecentChange[]? changes = null;
        if (Has(Permissions.AuditRead))
        {
            logins = [.. (await db.AuditEvents.AsNoTracking().Where(a => a.Action == "auth.login.success").OrderByDescending(a => a.Ts).Take(5).ToListAsync(ct)).Select(a => new RecentLogin(a.Ts, a.ActorLabel, a.Outcome.ToString().ToLowerInvariant()))];
            changes = [.. (await db.AuditEvents.AsNoTracking().Where(a => RoleChangeActions.Contains(a.Action) && a.Outcome == Outcome.Success).OrderByDescending(a => a.Ts).Take(5).ToListAsync(ct)).Select(a => new RecentChange(a.Ts, a.Action, a.TargetId ?? a.TargetType, a.ActorLabel))];
            visible.AddRange(["panel.logins", "panel.role_changes"]);
        }

        PendingInvite[]? pending = null;
        if (Has(Permissions.InvitationsRead))
        {
            var inv = await db.Invitations.AsNoTracking().Include(i => i.Roles).Where(i => i.Status == InvitationStatus.Pending && i.ExpiresAt > now).OrderByDescending(i => i.CreatedAt).Take(5).ToListAsync(ct);
            var rids = inv.SelectMany(i => i.Roles.Select(r => r.RoleId)).Distinct().ToList();
            var names = await db.Roles.AsNoTracking().Where(r => rids.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.Name, ct);
            pending = [.. inv.Select(i => new PendingInvite(i.Id, i.Email, [.. i.Roles.Select(r => names.GetValueOrDefault(r.RoleId, "—")).Order()], i.ExpiresAt))]; visible.Add("panel.invitations");
        }

        var quick = new List<string>();
        if (Has(Permissions.UsersCreate) && Has(Permissions.UsersAssignRole)) quick.Add("add_user");
        if (Has(Permissions.InvitationsManage)) quick.Add("invite_user");
        if (Has(Permissions.PermissionsRead)) quick.Add("review_access_matrix");
        if (Has(Permissions.AuditRead)) quick.Add("open_audit_log");
        if (Has(Permissions.SecurityRead)) quick.Add("review_security_events");
        if (Has(Permissions.SettingsManage)) quick.Add("system_settings");

        return new AdminDashboard(new DashKpis(total, active, susp, pendInv, roles, sess), growth, status, byRole, login, recentReg, logins, changes, recentSec, pending, [.. quick], [.. visible]);
    }

    public async Task<UserDashboard> MeAsync(SessionPrincipal who, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var u = await db.Users.AsNoTracking().Include(x => x.Profile).Include(x => x.Credential).Include(x => x.UserRoles).ThenInclude(r => r.Role).Include(x => x.MfaFactors).AsSplitQuery().FirstAsync(x => x.Id == who.UserId, ct);
        var perms = await authz.GetEffectivePermissionsAsync(u.Id, ct); var s = await settings.GetAsync(ct);
        var mfa = u.MfaFactors.Any(f => f.ConfirmedAt != null);
        var live = await db.Sessions.CountAsync(x => x.UserId == u.Id && x.RevokedAt == null && x.IdleExpiresAt > now && x.AbsoluteExpiresAt > now, ct);
        var recent = await db.AuditEvents.AsNoTracking().Where(a => a.ActorId == u.Id).OrderByDescending(a => a.Ts).Take(8).ToListAsync(ct);
        var links = new List<string> { "/profile", "/sessions", "/settings" };
        if (perms.Contains(Permissions.UsersRead)) links.Add("/users/all"); if (perms.Contains(Permissions.AuditRead)) links.Add("/audit");
        return new UserDashboard(u.Name, u.Email, u.Status.ToString().ToLowerInvariant(), u.EmailVerifiedAt != null, u.LastLoginAt,
            [.. u.UserRoles.Select(r => new RoleRef(r.Role!.Id, r.Role.Key, r.Role.Name)).OrderBy(r => r.Name)], perms.Count, [.. perms.Order()], live, mfa, s.MfaPolicy,
            u.Credential?.PasswordChangedAt, Completion(u, avatar: u.Profile?.AvatarId != null, mfa), [.. recent.Select(a => new MyActivity(a.Ts, a.Action, a.Outcome.ToString().ToLowerInvariant()))], [.. links]);
    }

    /// <summary>Profile completion = share of seven checks that are satisfied; the list tells the user what is left.</summary>
    public static ProfileCompletion Completion(User u, bool avatar, bool mfa)
    {
        var checks = new (string Name, bool Ok)[] { ("name", u.Name.Trim().Length >= 2), ("phone", !string.IsNullOrWhiteSpace(u.Profile?.Phone)), ("department", !string.IsNullOrWhiteSpace(u.Profile?.Department)),
            ("jobTitle", !string.IsNullOrWhiteSpace(u.Profile?.JobTitle)), ("avatar", avatar), ("emailVerified", u.EmailVerifiedAt != null), ("mfa", mfa) };
        return new ProfileCompletion((int)Math.Round(100.0 * checks.Count(c => c.Ok) / checks.Length), [.. checks.Where(c => !c.Ok).Select(c => c.Name)]);
    }
}
