using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Application.Users;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Application.Roles;

/// <summary>
/// ROLE-001 / PERM-001..003. Rules enforced here (each has tests):
/// • protected roles (Super Admin) are immutable;  • you may only put permissions on a role that you hold yourself (no escalation);
/// • you may only modify a role whose *current* permissions you hold (a delegated admin cannot strip or rewrite a more powerful role);
/// • changing permissions needs <c>permissions.assign</c> and a recent re-authentication;  • matrix saves are all-or-nothing and detect concurrent edits;
/// • you cannot remove your own ability to manage permissions;  • every change is audited with a diff and takes effect on the next request of affected users.
/// </summary>
public sealed partial class RoleAdminService(IAppDb db, IClock clock, SettingsProvider settings, AuthorizationService authz, AuditWriter audit)
{
    [GeneratedRegex("[^a-z0-9]+")] private static partial Regex NonSlug();

    // ============================================================ helpers
    private static string Show(IEnumerable<string> keys) => keys.Any() ? string.Join(", ", keys.Order()) : "—";

    private async Task DenyAsync(string action, string target, string reason, SessionPrincipal actor, ClientInfo client, CancellationToken ct)
    {
        audit.Record(action, "role", target, Outcome.Denied, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["reason"] = reason });
        await db.SaveChangesAsync(ct);
        throw DomainException.DeniedBecause(reason);
    }

    private async Task<T> RunAsync<T>(string action, string target, SessionPrincipal actor, ClientInfo client, Func<Task<T>> work, CancellationToken ct)
    {
        try { return await db.InTransactionAsync(work, ct); }
        catch (DomainException e) when (e.Code is ErrorCodes.Invariant or ErrorCodes.Conflict)
        {
            db.ResetTracking(); // never let a rolled-back transaction's tracked changes reach the audit save
            audit.Record(action, "role", target, Outcome.Failure, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["reason"] = e.Code, ["detail"] = e.Message });
            await db.SaveChangesAsync(ct);
            throw;
        }
    }

    private static string[] ValidatePermissionKeys(string[]? keys, string field = "permissions")
    {
        var list = (keys ?? []).Select(k => k.Trim()).Where(k => k.Length > 0).Distinct().ToArray();
        var unknown = list.Where(k => !Permissions.All.Contains(k)).ToArray();
        if (unknown.Length > 0) throw DomainException.Validation("Unknown permission.", new Dictionary<string, string> { [field] = $"Unknown permission(s): {string.Join(", ", unknown.Take(3))}." });
        return list;
    }

    private static void ValidateText(string? name, string? description, bool nameRequired)
    {
        var f = new Dictionary<string, string>();
        if (name is not null || nameRequired) { var n = (name ?? "").Trim(); if (n.Length < 2 || n.Length > 100) f["name"] = "Use 2–100 characters."; }
        if (description is { Length: > 300 }) f["description"] = "Use at most 300 characters.";
        if (f.Count > 0) throw DomainException.Validation("Please correct the highlighted fields.", f);
    }

    private async Task<IReadOnlySet<string>> ActorPermsAsync(SessionPrincipal actor, CancellationToken ct) => await authz.GetEffectivePermissionsAsync(actor.UserId, ct);

    /// <summary>Changing which permissions a role grants is the most sensitive operation: needs <c>permissions.assign</c> + fresh reauth.</summary>
    private async Task RequireAssignAsync(string action, string target, SessionPrincipal actor, IReadOnlySet<string> mine, ClientInfo client, CancellationToken ct)
    {
        if (!mine.Contains(Permissions.PermissionsAssign)) await DenyAsync(action, target, $"changing role permissions requires {Permissions.PermissionsAssign}", actor, client, ct);
        Guards.RequireRecentReauthForCritical(actor, await settings.GetAsync(ct), clock);
    }

    private async Task EnsureCanGrantAsync(string action, string target, IEnumerable<string> toAdd, IReadOnlySet<string> mine, SessionPrincipal actor, ClientInfo client, CancellationToken ct)
    {
        var missing = toAdd.Where(p => !mine.Contains(p)).ToArray();
        if (missing.Length > 0) await DenyAsync(action, target, $"you can't grant permissions you don't hold: {string.Join(", ", missing)}", actor, client, ct);
    }

    private async Task EnsureCanModifyAsync(Role role, IReadOnlySet<string> mine, string action, SessionPrincipal actor, ClientInfo client, CancellationToken ct)
    {
        if (role.Permissions.Any(p => !mine.Contains(p.PermissionKey)))
            await DenyAsync(action, role.Key, "this role has permissions you don't hold, so you can't modify it", actor, client, ct);
    }

    private async Task<RoleDetail> DetailAsync(Guid id, CancellationToken ct)
    {
        var r = await db.Roles.AsNoTracking().Include(x => x.Permissions).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Role");
        var users = await db.UserRoles.CountAsync(ur => ur.RoleId == id, ct);
        return new RoleDetail(r.Id, r.Key, r.Name, r.Description, r.IsSystem, r.IsProtected, r.ArchivedAt is not null, [.. r.Permissions.Select(p => p.PermissionKey).Order()], users, r.Version, r.UpdatedAt);
    }

    // ============================================================ reads
    public async Task<IReadOnlyList<RoleListItem>> ListAsync(bool includeArchived, CancellationToken ct = default)
    {
        var rows = await db.Roles.AsNoTracking().Where(r => includeArchived || r.ArchivedAt == null)
            .Select(r => new { r.Id, r.Key, r.Name, r.Description, r.IsSystem, r.IsProtected, Archived = r.ArchivedAt != null, Perms = r.Permissions.Count, Users = db.UserRoles.Count(ur => ur.RoleId == r.Id), r.Version, Keys = r.Permissions.Select(p => p.PermissionKey).ToList() })
            .ToListAsync(ct);
        return [.. rows.OrderByDescending(r => r.IsProtected).ThenByDescending(r => r.IsSystem).ThenBy(r => r.Name).Select(r => new RoleListItem(r.Id, r.Key, r.Name, r.Description, r.IsSystem, r.IsProtected, r.Archived, r.Perms, r.Users, r.Version, [.. r.Keys.Order()]))];
    }

    public Task<RoleDetail> GetAsync(Guid id, CancellationToken ct = default) => DetailAsync(id, ct);

    public async Task<PagedResult<RoleUser>> UsersAsync(Guid id, int page, int pageSize, CancellationToken ct = default)
    {
        if (!await db.Roles.AnyAsync(r => r.Id == id, ct)) throw DomainException.NotFound("Role");
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100);
        var q = db.UserRoles.AsNoTracking().Where(ur => ur.RoleId == id).OrderBy(ur => ur.User!.Name).ThenBy(ur => ur.UserId);
        var total = await q.CountAsync(ct);
        var rows = await q.Skip((page - 1) * pageSize).Take(pageSize).Select(ur => new RoleUser(ur.UserId, ur.User!.Name, ur.User.Email, ur.User.Status.ToString().ToLowerInvariant())).ToListAsync(ct);
        return new PagedResult<RoleUser>(rows, page, pageSize, total);
    }

    public async Task<IReadOnlyList<PermissionItem>> PermissionsAsync(CancellationToken ct = default)
    {
        var grants = await db.RolePermissions.AsNoTracking().Where(rp => db.Roles.Any(r => r.Id == rp.RoleId && r.ArchivedAt == null))
            .Select(rp => new { rp.PermissionKey, RoleName = db.Roles.Where(r => r.Id == rp.RoleId).Select(r => r.Name).First() }).ToListAsync(ct);
        return [.. Permissions.Catalog.Select(c => new PermissionItem(c.Key, c.Module, c.Description, c.Critical, [.. grants.Where(g => g.PermissionKey == c.Key).Select(g => g.RoleName).Distinct().Order()]))];
    }

    // ============================================================ create / update / duplicate / archive
    private async Task<string> UniqueKeyAsync(string name, CancellationToken ct)
    {
        var slug = NonSlug().Replace(name.ToLowerInvariant(), "_").Trim('_'); if (slug.Length == 0) slug = "role";
        var key = "custom_" + (slug.Length > 40 ? slug[..40] : slug); var candidate = key; var n = 2;
        while (await db.Roles.AnyAsync(r => r.Key == candidate, ct)) candidate = $"{key}_{n++}";
        return candidate;
    }

    private async Task EnsureNameFreeAsync(string name, Guid? exceptId, CancellationToken ct)
    {
        var lower = name.Trim().ToLowerInvariant();
        if (await db.Roles.AnyAsync(r => r.ArchivedAt == null && r.Id != exceptId && r.Name.ToLower() == lower, ct)) throw DomainException.Conflict("A role with this name already exists.");
    }

    public async Task<RoleDetail> CreateAsync(SaveRoleRequest req, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        ValidateText(req.Name, req.Description, nameRequired: true);
        var perms = ValidatePermissionKeys(req.Permissions);
        var mine = await ActorPermsAsync(actor, ct);
        var name = req.Name!.Trim();
        if (perms.Length > 0)
        {
            await RequireAssignAsync("role.create", name, actor, mine, client, ct);
            await EnsureCanGrantAsync("role.create", name, perms, mine, actor, client, ct);
        }
        var id = await RunAsync("role.create", name, actor, client, async () =>
        {
            await EnsureNameFreeAsync(name, null, ct);
            var now = clock.UtcNow;
            var role = new Role { Key = await UniqueKeyAsync(name, ct), Name = name, Description = req.Description?.Trim() ?? "", IsSystem = false, CreatedAt = now, UpdatedAt = now };
            foreach (var p in perms) role.Permissions.Add(new RolePermission { PermissionKey = p, GrantedBy = actor.UserId, GrantedAt = now });
            db.Roles.Add(role);
            audit.Record("role.create", "role", role.Key, Outcome.Success, client, actor.UserId, actor.Email, diff: [new AuditDiff("permissions", "—", Show(perms))]);
            if (perms.Length > 0) audit.Security("privilege.change", Severity.Medium, $"Role “{name}” created with permissions: {Show(perms)}", null);
            try { await db.SaveChangesAsync(ct); } catch (DbUpdateException) { throw DomainException.Conflict("A role with this name already exists."); } // lost a race on the unique index
            return role.Id;
        }, ct);
        return await DetailAsync(id, ct);
    }

    public async Task<RoleDetail> UpdateAsync(Guid id, SaveRoleRequest req, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        ValidateText(req.Name, req.Description, nameRequired: false);
        var wanted = req.Permissions is null ? null : ValidatePermissionKeys(req.Permissions);
        var role = await db.Roles.Include(r => r.Permissions).FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw DomainException.NotFound("Role");
        var mine = await ActorPermsAsync(actor, ct);

        if (role.IsProtected) throw DomainException.Invariant("Protected system roles cannot be modified.");
        if (role.ArchivedAt is not null) throw DomainException.Invariant("Archived roles cannot be modified.");
        await EnsureCanModifyAsync(role, mine, "role.update", actor, client, ct);

        var have = role.Permissions.Select(p => p.PermissionKey).ToHashSet();
        var add = wanted?.Where(p => !have.Contains(p)).ToArray() ?? []; var remove = wanted is null ? [] : have.Where(p => !wanted.Contains(p)).ToArray();
        if (add.Length + remove.Length > 0)
        {
            await RequireAssignAsync("role.update", role.Key, actor, mine, client, ct);
            await EnsureCanGrantAsync("role.update", role.Key, add, mine, actor, client, ct);
        }
        var newName = req.Name?.Trim();
        if (newName is not null && newName != role.Name && role.IsSystem) throw DomainException.Invariant("System roles can't be renamed.");

        await RunAsync("role.update", role.Key, actor, client, async () =>
        {
            var r = await db.Roles.Include(x => x.Permissions).FirstAsync(x => x.Id == id, ct);
            if (req.Version is { } v && v != r.Version) throw DomainException.Conflict("This role was changed by someone else. Reload and try again.");
            var diff = new List<AuditDiff>();
            if (newName is not null && newName != r.Name) { await EnsureNameFreeAsync(newName, id, ct); diff.Add(new("name", r.Name, newName)); r.Name = newName; }
            if (req.Description is not null && req.Description.Trim() != r.Description) { diff.Add(new("description", r.Description, req.Description.Trim())); r.Description = req.Description.Trim(); }
            if (add.Length + remove.Length > 0)
            {
                foreach (var rp in r.Permissions.Where(p => remove.Contains(p.PermissionKey)).ToList()) db.RolePermissions.Remove(rp);
                foreach (var k in add) db.RolePermissions.Add(new RolePermission { RoleId = id, PermissionKey = k, GrantedBy = actor.UserId, GrantedAt = clock.UtcNow });
                diff.Add(new("permissions", Show(have), Show(wanted!)));
                audit.Security("privilege.change", Severity.Medium, $"Permissions of role “{r.Name}” changed (+{add.Length} / −{remove.Length})", null);
            }
            if (diff.Count == 0) return 0;
            r.Version++; r.UpdatedAt = clock.UtcNow;
            audit.Record("role.update", "role", r.Key, Outcome.Success, client, actor.UserId, actor.Email, diff: diff);
            try { await db.SaveChangesAsync(ct); } catch (DbUpdateException) { throw DomainException.Conflict("A role with this name already exists."); }
            return 0;
        }, ct);
        return await DetailAsync(id, ct);
    }

    public async Task<RoleDetail> DuplicateAsync(Guid id, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var src = await db.Roles.AsNoTracking().Include(r => r.Permissions).FirstOrDefaultAsync(r => r.Id == id && r.ArchivedAt == null, ct) ?? throw DomainException.NotFound("Role");
        var perms = src.Permissions.Select(p => p.PermissionKey).ToArray();
        var mine = await ActorPermsAsync(actor, ct);
        if (perms.Length > 0) { await RequireAssignAsync("role.duplicate", src.Key, actor, mine, client, ct); await EnsureCanGrantAsync("role.duplicate", src.Key, perms, mine, actor, client, ct); }
        var name = $"{src.Name} (copy)"; var n = 2;
        while (await db.Roles.AnyAsync(r => r.ArchivedAt == null && r.Name.ToLower() == name.ToLower(), ct)) name = $"{src.Name} (copy {n++})";
        var created = await CreateInternalAsync(name, src.Description, perms, actor, client, ct);
        return created;
    }

    private async Task<RoleDetail> CreateInternalAsync(string name, string description, string[] perms, SessionPrincipal actor, ClientInfo client, CancellationToken ct)
    {
        var id = await RunAsync("role.duplicate", name, actor, client, async () =>
        {
            await EnsureNameFreeAsync(name, null, ct);
            var now = clock.UtcNow;
            var role = new Role { Key = await UniqueKeyAsync(name, ct), Name = name, Description = description, IsSystem = false, CreatedAt = now, UpdatedAt = now };
            foreach (var p in perms) role.Permissions.Add(new RolePermission { PermissionKey = p, GrantedBy = actor.UserId, GrantedAt = now });
            db.Roles.Add(role);
            audit.Record("role.duplicate", "role", role.Key, Outcome.Success, client, actor.UserId, actor.Email, diff: [new AuditDiff("permissions", "—", Show(perms))]);
            try { await db.SaveChangesAsync(ct); } catch (DbUpdateException) { throw DomainException.Conflict("A role with this name already exists."); }
            return role.Id;
        }, ct);
        return await DetailAsync(id, ct);
    }

    public async Task ArchiveAsync(Guid id, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var role = await db.Roles.Include(r => r.Permissions).FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw DomainException.NotFound("Role");
        var mine = await ActorPermsAsync(actor, ct);
        if (role.IsSystem) throw DomainException.Invariant("System roles cannot be archived.");
        await EnsureCanModifyAsync(role, mine, "role.archive", actor, client, ct);
        await RunAsync("role.archive", role.Key, actor, client, async () =>
        {
            var r = await db.Roles.FirstAsync(x => x.Id == id, ct);
            if (r.ArchivedAt is not null) throw DomainException.Invariant("This role is already archived.");
            var holders = await db.UserRoles.CountAsync(ur => ur.RoleId == id, ct);
            if (holders > 0) throw DomainException.Invariant($"{holders} user(s) still have this role. Reassign them first.");
            r.ArchivedAt = clock.UtcNow; r.Version++; r.UpdatedAt = clock.UtcNow;
            audit.Record("role.archive", "role", r.Key, Outcome.Success, client, actor.UserId, actor.Email);
            await db.SaveChangesAsync(ct);
            return 0;
        }, ct);
    }

    // ============================================================ access matrix
    private static string Fingerprint(IEnumerable<Role> roles)
    {
        var sb = new StringBuilder();
        foreach (var r in roles.OrderBy(r => r.Id)) sb.Append(r.Id).Append('=').AppendJoin(',', r.Permissions.Select(p => p.PermissionKey).Order()).Append(';');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()))).ToLowerInvariant()[..32];
    }

    public async Task<MatrixView> MatrixAsync(SessionPrincipal actor, CancellationToken ct = default)
    {
        var roles = await db.Roles.AsNoTracking().Include(r => r.Permissions).Where(r => r.ArchivedAt == null).ToListAsync(ct);
        var mine = await ActorPermsAsync(actor, ct);
        return new MatrixView(Fingerprint(roles), [.. Permissions.Catalog.Select(c => new PermissionDef(c.Key, c.Module, c.Description, c.Critical))],
            [.. roles.OrderByDescending(r => r.IsProtected).ThenByDescending(r => r.IsSystem).ThenBy(r => r.Name).Select(r => new MatrixRole(r.Id, r.Key, r.Name, r.IsSystem, r.IsProtected, [.. r.Permissions.Select(p => p.PermissionKey).Order()]))],
            [.. mine.Order()]);
    }

    /// <summary>All-or-nothing: either every change is applied (one transaction, roles locked) or none is.</summary>
    public async Task<MatrixSaveResult> SaveMatrixAsync(SaveMatrixRequest req, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var changes = (req.Changes ?? []).Where(c => (c.Add?.Length ?? 0) + (c.Remove?.Length ?? 0) > 0).ToArray();
        if (changes.Length == 0) throw DomainException.Validation("No changes to save.");
        if (string.IsNullOrWhiteSpace(req.ExpectedVersion)) throw DomainException.Validation("expectedVersion is required.", new Dictionary<string, string> { ["expectedVersion"] = "Reload the matrix and try again." });
        if (changes.GroupBy(c => c.RoleId).Any(g => g.Count() > 1)) throw DomainException.Validation("Each role may appear once.");
        var mine = await ActorPermsAsync(actor, ct);
        await RequireAssignAsync("matrix.save", "access-matrix", actor, mine, client, ct);

        var parsed = changes.Select(c => (c.RoleId, Add: ValidatePermissionKeys(c.Add, "add"), Remove: ValidatePermissionKeys(c.Remove, "remove"))).ToArray();
        if (parsed.Any(p => p.Add.Intersect(p.Remove).Any())) throw DomainException.Validation("A permission can't be both added and removed.");
        var allAdds = parsed.SelectMany(p => p.Add).Distinct().ToArray();
        await EnsureCanGrantAsync("matrix.save", "access-matrix", allAdds, mine, actor, client, ct); // no escalation, checked before touching anything

        return await RunAsync("matrix.save", "access-matrix", actor, client, async () =>
        {
            await db.ExecuteSqlAsync($"SELECT id FROM roles WHERE archived_at IS NULL FOR UPDATE", ct); // serialise concurrent matrix saves
            var roles = await db.Roles.Include(r => r.Permissions).Where(r => r.ArchivedAt == null).ToListAsync(ct);
            if (Fingerprint(roles) != req.ExpectedVersion) throw DomainException.Conflict("The access matrix was changed by someone else. Reload and review before saving.");

            var projected = roles.ToDictionary(r => r.Id, r => r.Permissions.Select(p => p.PermissionKey).ToHashSet());
            var diffs = new List<AuditDiff>(); int rolesChanged = 0, added = 0, removed = 0;
            foreach (var (roleId, add, remove) in parsed)
            {
                var role = roles.FirstOrDefault(r => r.Id == roleId) ?? throw DomainException.Validation("A selected role does not exist or is archived.", new Dictionary<string, string> { ["changes"] = "Unknown role." });
                if (role.IsProtected) throw DomainException.Invariant($"{role.Name} is protected and cannot be modified.");
                if (role.Permissions.Any(p => !mine.Contains(p.PermissionKey))) throw DomainException.Invariant($"You can't modify {role.Name}: it has permissions you don't hold.");
                var have = role.Permissions.Select(p => p.PermissionKey).ToHashSet();
                var realAdd = add.Where(k => !have.Contains(k)).ToArray(); var realRemove = remove.Where(have.Contains).ToArray();
                if (realAdd.Length + realRemove.Length == 0) continue;
                foreach (var rp in role.Permissions.Where(p => realRemove.Contains(p.PermissionKey)).ToList()) db.RolePermissions.Remove(rp);
                foreach (var k in realAdd) db.RolePermissions.Add(new RolePermission { RoleId = roleId, PermissionKey = k, GrantedBy = actor.UserId, GrantedAt = clock.UtcNow });
                projected[roleId].ExceptWith(realRemove); projected[roleId].UnionWith(realAdd);
                role.Version++; role.UpdatedAt = clock.UtcNow;
                diffs.Add(new AuditDiff(role.Name, $"−{realRemove.Length} / +{realAdd.Length}", string.Join(" ", realAdd.Select(a => "+" + a).Concat(realRemove.Select(r => "-" + r)))));
                rolesChanged++; added += realAdd.Length; removed += realRemove.Length;
            }
            if (rolesChanged == 0) throw DomainException.Validation("No changes to save.");

            // The caller must still be able to manage permissions afterwards (no self-lockout from this feature).
            var actorRoleIds = await db.UserRoles.Where(ur => ur.UserId == actor.UserId).Select(ur => ur.RoleId).ToListAsync(ct);
            var stillHolds = roles.Where(r => actorRoleIds.Contains(r.Id)).Any(r => r.IsProtected || projected[r.Id].Contains(Permissions.PermissionsAssign));
            if (!stillHolds) throw DomainException.Invariant("You can't remove your own ability to manage permissions.");

            audit.Record("matrix.save", "role", "access-matrix", Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["rolesChanged"] = rolesChanged.ToString() }, diffs);
            audit.Security("privilege.change", Severity.Medium, $"Access matrix updated ({rolesChanged} role(s), +{added} / −{removed})", null);
            await db.SaveChangesAsync(ct);
            var fresh = await db.Roles.AsNoTracking().Include(r => r.Permissions).Where(r => r.ArchivedAt == null).ToListAsync(ct);
            return new MatrixSaveResult(rolesChanged, added, removed, Fingerprint(fresh));
        }, ct);
    }
}
