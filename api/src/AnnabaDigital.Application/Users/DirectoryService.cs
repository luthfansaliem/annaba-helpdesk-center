using System.Globalization;
using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Application.Users;

public sealed record GroupMappingDto(Guid Id, string Group, Guid RoleId, string RoleKey, string RoleName, int People, DateTimeOffset CreatedAt);
public sealed record SaveGroupMappingRequest(string? Group, Guid? RoleId);
public sealed record DirectorySyncRequest(string? Csv, bool DryRun = true, bool SuspendMissing = false);
public sealed record DirectorySyncRow(int Line, string Email, string Action, string? Detail);
public sealed record DirectorySyncResult(bool DryRun, int Rows, int Updated, int Suspended, int Unchanged, int NotFound, int Failed, IReadOnlyList<DirectorySyncRow> Details);
public sealed record DirectoryOverview(bool SsoGroupsClaim, int Mappings, int ScimGroups, int ScimUsers, DateTimeOffset? LastScimAt, DateTimeOffset? LastSyncAt);

/// <summary>
/// Keeps a person's directory-managed roles in step with their groups (INT-003). Used at SSO sign-in (the token's groups claim) and when SCIM changes a group.
/// Only roles that appear in a mapping are touched, and Super Admin is never one of them, so no directory change can lock the organisation out.
/// </summary>
public sealed class DirectoryRoleSync(IAppDb db, IClock clock, AuditWriter audit)
{
    public async Task<(int Added, int Removed)> SyncAsync(Guid userId, IEnumerable<string> groups, string source, ClientInfo client, CancellationToken ct = default)
    {
        var mappings = await db.DirectoryGroupMappings.AsNoTracking().ToListAsync(ct);
        if (mappings.Count == 0) return (0, 0);
        var have = groups.Select(g => g.Trim().ToLowerInvariant()).Where(g => g.Length > 0).ToHashSet();
        var managed = mappings.Select(m => m.RoleId).ToHashSet();
        var wanted = mappings.Where(m => have.Contains(m.Group.ToLowerInvariant())).Select(m => m.RoleId).ToHashSet();
        var current = await db.UserRoles.Where(r => r.UserId == userId).ToListAsync(ct);
        var add = wanted.Where(r => current.All(c => c.RoleId != r)).ToList();
        var remove = current.Where(c => managed.Contains(c.RoleId) && !wanted.Contains(c.RoleId)).ToList();
        if (add.Count == 0 && remove.Count == 0) return (0, 0);
        var now = clock.UtcNow;
        foreach (var r in add) db.UserRoles.Add(new UserRole { UserId = userId, RoleId = r, AssignedBy = null, AssignedAt = now });
        foreach (var r in remove) db.UserRoles.Remove(r);
        var keys = await db.Roles.AsNoTracking().Where(r => add.Contains(r.Id) || remove.Select(x => x.RoleId).Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.Key, ct);
        audit.Record("directory.roles_synced", "user", userId.ToString(), Outcome.Success, client, null, $"directory ({source})",
            new Dictionary<string, string> { ["added"] = string.Join(",", add.Select(a => keys.GetValueOrDefault(a))), ["removed"] = string.Join(",", remove.Select(r => keys.GetValueOrDefault(r.RoleId))), ["source"] = source });
        audit.Security("privilege.change", Severity.Medium, $"Directory {source} changed roles: +{add.Count} −{remove.Count}", userId);
        return (add.Count, remove.Count);
    }

    /// <summary>The groups a person is in through SCIM, by name.</summary>
    public async Task<List<string>> ScimGroupsOfAsync(Guid userId, CancellationToken ct) => await db.ScimGroups.AsNoTracking().Where(g => g.MemberIds.Contains(userId)).Select(g => g.DisplayName).ToListAsync(ct);
}

/// <summary>
/// INT-003 and ADM-004 (sync). Group-to-role mappings, and a CSV directory sync that updates what HR keeps (employee ID, department, job title, manager, site)
/// and suspends people marked inactive, or missing from the file when asked. People are never created here (they arrive by invitation or SCIM), Super Admins
/// are never suspended by a sync, and nobody suspends themselves. Needs <c>directory.manage</c>; the same sync is on the public API with the
/// <c>directory.write</c> scope for a nightly job.
/// </summary>
public sealed class DirectoryService(IAppDb db, IClock clock, AuditWriter audit, InvitationAdminService invitations, SessionService sessions, SettingsProvider settings, IOidcClient oidc)
{
    private static DomainException Invalid(Dictionary<string, string> e) => DomainException.Validation("Please correct the highlighted fields.", e);

    public async Task<DirectoryOverview> OverviewAsync(CancellationToken ct = default)
    {
        var last = await db.AuditEvents.AsNoTracking().Where(a => a.Action == "directory.scim").OrderByDescending(a => a.Ts).Select(a => (DateTimeOffset?)a.Ts).FirstOrDefaultAsync(ct);
        var sync = await db.AuditEvents.AsNoTracking().Where(a => a.Action == "directory.sync").OrderByDescending(a => a.Ts).Select(a => (DateTimeOffset?)a.Ts).FirstOrDefaultAsync(ct);
        return new DirectoryOverview(oidc.Enabled, await db.DirectoryGroupMappings.CountAsync(ct), await db.ScimGroups.CountAsync(ct),
            await db.Users.CountAsync(u => u.Profile != null && u.Profile.ExternalId != null, ct), last, sync);
    }

    public async Task<IReadOnlyList<GroupMappingDto>> MappingsAsync(CancellationToken ct = default)
    {
        var rows = await (from m in db.DirectoryGroupMappings.AsNoTracking() join r in db.Roles.AsNoTracking() on m.RoleId equals r.Id orderby m.Group select new { m, r.Key, r.Name }).ToListAsync(ct);
        var roleIds = rows.Select(x => x.m.RoleId).Distinct().ToList();
        var counts = await db.UserRoles.AsNoTracking().Where(ur => roleIds.Contains(ur.RoleId)).GroupBy(ur => ur.RoleId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
        return [.. rows.Select(x => new GroupMappingDto(x.m.Id, x.m.Group, x.m.RoleId, x.Key, x.Name, counts.GetValueOrDefault(x.m.RoleId), x.m.CreatedAt))];
    }

    public async Task<GroupMappingDto> AddMappingAsync(SaveGroupMappingRequest req, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        Guards.RequireRecentReauthForCritical(actor, await settings.GetAsync(ct), clock);   // decides who gets which role ⇒ critical
        var e = new Dictionary<string, string>();
        var group = (req.Group ?? "").Trim();
        if (group.Length is 0 or > 200) e["group"] = "Give the group's name (at most 200 characters).";
        if (req.RoleId is null) e["roleId"] = "Choose a role.";
        if (e.Count > 0) throw Invalid(e);
        var role = await db.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.Id == req.RoleId && r.ArchivedAt == null, ct) ?? throw Invalid(new Dictionary<string, string> { ["roleId"] = "Choose an active role." });
        if (role.Key == SystemRoles.SuperAdmin) throw Invalid(new Dictionary<string, string> { ["roleId"] = "Super Admin is given in the app only, never by a directory group." });
        await invitations.ResolveGrantableRolesAsync([role.Id], actor, "directory.mapping", client, ct);   // you may only map what you could grant yourself
        if (await db.DirectoryGroupMappings.CountAsync(ct) >= DirectoryRules.MaxMappings) throw DomainException.Conflict($"Keep at most {DirectoryRules.MaxMappings} mappings.");
        var lower = group.ToLowerInvariant();
        if (await db.DirectoryGroupMappings.AnyAsync(m => m.Group.ToLower() == lower && m.RoleId == role.Id, ct)) throw Invalid(new Dictionary<string, string> { ["group"] = "This group already gives this role." });
        var m = new DirectoryGroupMapping { Group = group, RoleId = role.Id, CreatedById = actor.UserId, CreatedAt = clock.UtcNow };
        db.DirectoryGroupMappings.Add(m);
        audit.Record("directory.mapping_add", "role", role.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["group"] = group, ["role"] = role.Key });
        audit.Security("privilege.change", Severity.Medium, $"Directory group \"{group}\" now gives the role {role.Name}", null);
        await db.SaveChangesAsync(ct);
        return (await MappingsAsync(ct)).First(x => x.Id == m.Id);
    }

    public async Task DeleteMappingAsync(Guid id, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        Guards.RequireRecentReauthForCritical(actor, await settings.GetAsync(ct), clock);
        var m = await db.DirectoryGroupMappings.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Mapping");
        db.DirectoryGroupMappings.Remove(m);
        audit.Record("directory.mapping_remove", "role", m.RoleId.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["group"] = m.Group });
        await db.SaveChangesAsync(ct);
    }

    // ================================================================ CSV sync
    /// <summary>Columns (header names, any order, case and spaces ignored): <c>email</c> (required), <c>employee_id</c>, <c>department</c>, <c>job_title</c>,
    /// <c>manager_email</c>, <c>site_code</c>, <c>status</c> (active / inactive). An empty cell leaves the value as it is.</summary>
    public async Task<DirectorySyncResult> SyncAsync(DirectorySyncRequest req, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default, bool viaApiKey = false)
    {
        var csv = req.Csv ?? "";
        if (csv.Length > DirectoryRules.MaxSyncChars) throw Invalid(new Dictionary<string, string> { ["csv"] = $"Use at most {DirectoryRules.MaxSyncChars / 1_000_000} MB." });
        List<List<string>> table;
        try { table = UserImportService.ParseCsv(csv); } catch (FormatException ex) { throw Invalid(new Dictionary<string, string> { ["csv"] = $"The file can't be read: {ex.Message}" }); }
        if (table.Count < 2) throw Invalid(new Dictionary<string, string> { ["csv"] = "Add a header row and at least one person." });
        var header = table[0].Select(h => new string(h.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant()).ToList();
        int Col(params string[] n) => header.FindIndex(h => n.Contains(h));
        int cEmail = Col("email", "emailaddress", "mail"), cEmp = Col("employeeid", "employeenumber", "nik"), cDept = Col("department", "dept"), cTitle = Col("jobtitle", "title"),
            cMgr = Col("manageremail", "manager"), cSite = Col("sitecode", "site"), cStatus = Col("status", "active");
        if (cEmail < 0) throw Invalid(new Dictionary<string, string> { ["csv"] = "The first row must name the columns, with at least email." });
        var data = table.Skip(1).Select((r, i) => (Line: i + 2, Row: r)).Where(x => x.Row.Any(c => c.Trim().Length > 0)).ToList();
        if (data.Count > DirectoryRules.MaxSyncRows) throw Invalid(new Dictionary<string, string> { ["csv"] = $"Sync at most {DirectoryRules.MaxSyncRows} people at a time." });
        static string? Cell(List<string> r, int c) => c >= 0 && c < r.Count && r[c].Trim().Length > 0 ? r[c].Trim() : null;

        var users = await db.Users.Include(u => u.Profile).Include(u => u.UserRoles).ThenInclude(r => r.Role).ToListAsync(ct);
        var byEmail = users.ToDictionary(u => u.Email, StringComparer.OrdinalIgnoreCase);
        var sites = await db.Locations.AsNoTracking().Where(l => l.ArchivedAt == null).ToDictionaryAsync(l => l.Code.ToLower(), l => l.Id, ct);
        var vendors = (await db.VendorUsers.AsNoTracking().Select(v => v.UserId).ToListAsync(ct)).ToHashSet();
        bool Super(User u) => u.UserRoles.Any(r => r.Role?.Key == SystemRoles.SuperAdmin);
        var now = clock.UtcNow;
        var rows = new List<DirectorySyncRow>(); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var toSuspend = new List<User>();
        int updated = 0, unchanged = 0, notFound = 0, failed = 0;
        foreach (var (line, r) in data)
        {
            var email = (Cell(r, cEmail) ?? "").ToLowerInvariant();
            if (email.Length == 0) { rows.Add(new(line, "", "error", "No e-mail.")); failed++; continue; }
            if (!seen.Add(email)) { rows.Add(new(line, email, "error", "Duplicate of an earlier row.")); failed++; continue; }
            if (!byEmail.TryGetValue(email, out var u)) { rows.Add(new(line, email, "not_found", "Not in the app; invite them first.")); notFound++; continue; }
            // validate the whole row first; only a clean row is applied
            var problems = new List<string>(); var apply = new List<(string Label, Action<UserProfile> Put)>();
            var profile = u.Profile ?? new UserProfile { UserId = u.Id };
            void Text(string label, string? value, string? current, Action<UserProfile, string> put, int max)
            {
                if (value is null) return;
                if (value.Length > max) problems.Add($"The {label} is longer than {max} characters.");
                else if (current != value) apply.Add((label, p => put(p, value)));
            }
            Text("employee ID", Cell(r, cEmp), profile.EmployeeId, (p, v) => p.EmployeeId = v, 50);
            Text("department", Cell(r, cDept), profile.Department, (p, v) => p.Department = v, 100);
            Text("job title", Cell(r, cTitle), profile.JobTitle, (p, v) => p.JobTitle = v, 100);
            if (Cell(r, cMgr)?.ToLowerInvariant() is { } me)
            {
                if (!byEmail.TryGetValue(me, out var mgr)) problems.Add($"Manager {me} is not in the app.");
                else if (mgr.Id == u.Id) problems.Add("A person cannot be their own manager.");
                else if (profile.ManagerId != mgr.Id) apply.Add(("manager", p => p.ManagerId = mgr.Id));
            }
            if (Cell(r, cSite)?.ToLowerInvariant() is { } sc)
            {
                if (!sites.TryGetValue(sc, out var sid)) problems.Add($"No active place with the code {sc}.");
                else if (profile.SiteId != sid) apply.Add(("site", p => p.SiteId = sid));
            }
            var inactive = Cell(r, cStatus)?.ToLowerInvariant() is "inactive" or "false" or "0" or "no" or "suspended" or "terminated" or "left";
            if (inactive && u.Status == UserStatus.Active)
            {
                if (Super(u)) problems.Add("Super Admins are suspended in the app, not by a sync.");
                else if (u.Id == actor.UserId) problems.Add("You cannot suspend yourself.");
            }
            if (problems.Count > 0) { rows.Add(new(line, email, "error", string.Join(" ", problems))); failed++; continue; }
            if (inactive && u.Status == UserStatus.Active) toSuspend.Add(u);
            if (apply.Count > 0)
            {
                if (u.Profile is null) { u.Profile = profile; }
                foreach (var (_, put) in apply) put(u.Profile);
                u.UpdatedAt = now; u.Version++;
                rows.Add(new(line, email, "updated", string.Join(", ", apply.Select(a => a.Label)))); updated++;
            }
            else if (!toSuspend.Contains(u)) { rows.Add(new(line, email, "unchanged", null)); unchanged++; }
        }
        if (req.SuspendMissing)
            foreach (var u in byEmail.Values.Where(u => u.Status == UserStatus.Active && !seen.Contains(u.Email) && !Super(u) && u.Id != actor.UserId && !vendors.Contains(u.Id)))
                toSuspend.Add(u);
        foreach (var u in toSuspend.Distinct())
            rows.Add(new(0, u.Email, "suspended", seen.Contains(u.Email) ? "Marked inactive in the file." : "Not in the file."));
        if (!req.DryRun)
        {
            if (!viaApiKey) Guards.RequireRecentReauthForCritical(actor, await settings.GetAsync(ct), clock);   // a key (nightly job) has no session to re-authenticate
            foreach (var u in toSuspend.Distinct())
            {
                u.Status = UserStatus.Suspended; u.UpdatedAt = now; u.Version++;
                await sessions.RevokeAllForUserAsync(u.Id, RevokeReason.Suspended, null, ct);
                audit.Record("user.suspend", "user", u.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["source"] = "directory sync" });
            }
            audit.Record("directory.sync", "directory", null, failed == 0 ? Outcome.Success : Outcome.Failure, client, actor.UserId, actor.Email,
                new Dictionary<string, string> { ["rows"] = data.Count.ToString(CultureInfo.InvariantCulture), ["updated"] = updated.ToString(CultureInfo.InvariantCulture),
                    ["suspended"] = toSuspend.Distinct().Count().ToString(CultureInfo.InvariantCulture), ["failed"] = failed.ToString(CultureInfo.InvariantCulture) });
            await db.SaveChangesAsync(ct);
        }
        else db.ResetTracking();
        return new DirectorySyncResult(req.DryRun, data.Count, updated, toSuspend.Distinct().Count(), unchanged, notFound, failed, [.. rows.Take(1000)]);

    }
}
