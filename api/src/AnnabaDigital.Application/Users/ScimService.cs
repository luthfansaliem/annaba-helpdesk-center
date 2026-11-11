using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Application.Users;

/// <summary>A SCIM protocol error (RFC 7644 §3.12), written as the SCIM error body.</summary>
public sealed class ScimException(int status, string detail, string? scimType = null) : Exception(detail)
{
    public int Status { get; } = status;
    public string? ScimType { get; } = scimType;
}

/// <summary>
/// INT-003 / ADM-004. SCIM 2.0 (RFC 7643/7644) for identity providers that push people and groups: Users and Groups with filter <c>attr eq "value"</c>, paging,
/// PUT and PATCH. Reached on the public API with an API key that has the <c>directory.write</c> scope and belongs to someone holding <c>directory.manage</c>.
/// <list type="bullet">
/// <item>New people are active at once with the base User role and sign in with SSO (no password, no invitation); their e-mail is vouched for by the provider.</item>
/// <item><c>active: false</c> suspends and DELETE deactivates; both end the person's sessions. Super Admins and the key's owner are refused (managed in the app).</item>
/// <item>The e-mail (<c>userName</c>) of an existing person is not changed over SCIM: that would let a provider move an account to another mailbox.</item>
/// <item>Group membership drives the directory-managed roles (<see cref="DirectoryRoleSync"/>).</item>
/// </list>
/// </summary>
public sealed partial class ScimService(IAppDb db, IClock clock, AuditWriter audit, AuthorizationService authz, SessionService sessions, DirectoryRoleSync roles, IAppLinks links)
{
    public const string UserSchema = "urn:ietf:params:scim:schemas:core:2.0:User", GroupSchema = "urn:ietf:params:scim:schemas:core:2.0:Group",
        Enterprise = "urn:ietf:params:scim:schemas:extension:enterprise:2.0:User", ListSchema = "urn:ietf:params:scim:api:messages:2.0:ListResponse",
        PatchSchema = "urn:ietf:params:scim:api:messages:2.0:PatchOp", ErrorSchema = "urn:ietf:params:scim:api:messages:2.0:Error";
    private static readonly ClientInfo Scim = new(null, "scim", null);

    [GeneratedRegex("""^\s*([A-Za-z.:0-9]+)\s+eq\s+"((?:[^"\\]|\\.)*)"\s*$""", RegexOptions.IgnoreCase)] private static partial Regex EqFilter();
    [GeneratedRegex("""^members\[value\s+eq\s+"([^"]+)"\]$""", RegexOptions.IgnoreCase)] private static partial Regex MemberPath();

    private async Task RequireAsync(SessionPrincipal p, CancellationToken ct)
    {
        if (!(await authz.GetEffectivePermissionsAsync(p.UserId, ct)).Contains(Permissions.DirectoryManage)) throw new ScimException(403, "The key's owner needs the directory.manage permission.");
    }
    private string Location(string kind, Guid id) => $"{links.Origin.TrimEnd('/')}/api/v1/scim/v2/{kind}/{id}";
    private static string Iso(DateTimeOffset t) => t.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
    private void Trace(string what, string id, SessionPrincipal p) => audit.Record("directory.scim", "directory", id, Outcome.Success, Scim, p.UserId, p.Email, new Dictionary<string, string> { ["op"] = what });

    public static object ServiceProviderConfig() => new Dictionary<string, object?>
    {
        ["schemas"] = new[] { "urn:ietf:params:scim:schemas:core:2.0:ServiceProviderConfig" },
        ["patch"] = new { supported = true }, ["bulk"] = new { supported = false, maxOperations = 0, maxPayloadSize = 0 },
        ["filter"] = new { supported = true, maxResults = DirectoryRules.ScimPageMax }, ["changePassword"] = new { supported = false },
        ["sort"] = new { supported = false }, ["etag"] = new { supported = false },
        ["authenticationSchemes"] = new[] { new { type = "oauthbearertoken", name = "API key", description = "Authorization: Bearer adk_… (scope directory.write)", primary = true } },
    };

    private static object Page(IEnumerable<object> items, int total, int start) { var l = items.ToList(); return new Dictionary<string, object?> { ["schemas"] = new[] { ListSchema }, ["totalResults"] = total, ["startIndex"] = start, ["itemsPerPage"] = l.Count, ["Resources"] = l }; }
    private static (string Attr, string Value)? Filter(string? f)
    {
        if (string.IsNullOrWhiteSpace(f)) return null;
        var m = EqFilter().Match(f);
        if (!m.Success) throw new ScimException(400, "Only filters of the form attribute eq \"value\" are supported.", "invalidFilter");
        return (m.Groups[1].Value.ToLowerInvariant(), Regex.Unescape(m.Groups[2].Value));
    }

    // ================================================================ users
    private async Task<object> UserJsonAsync(User u, CancellationToken ct)
    {
        var p = u.Profile ?? new UserProfile();
        var parts = u.Name.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var ext = new Dictionary<string, object?> { ["employeeNumber"] = p.EmployeeId, ["department"] = p.Department };
        if (p.ManagerId is { } m) ext["manager"] = new { value = m.ToString(), displayName = await db.Users.AsNoTracking().Where(x => x.Id == m).Select(x => x.Name).FirstOrDefaultAsync(ct) };
        return new Dictionary<string, object?>
        {
            ["schemas"] = new[] { UserSchema, Enterprise }, ["id"] = u.Id.ToString(), ["externalId"] = p.ExternalId, ["userName"] = u.Email,
            ["name"] = new { formatted = u.Name, givenName = parts.ElementAtOrDefault(0), familyName = parts.ElementAtOrDefault(1) }, ["displayName"] = u.Name,
            ["active"] = u.Status == UserStatus.Active, ["emails"] = new[] { new { value = u.Email, type = "work", primary = true } }, ["title"] = p.JobTitle,
            [Enterprise] = ext,
            ["meta"] = new { resourceType = "User", created = Iso(u.CreatedAt), lastModified = Iso(u.UpdatedAt), location = Location("Users", u.Id) },
        };
    }

    public async Task<object> ListUsersAsync(string? filter, int? startIndex, int? count, SessionPrincipal p, CancellationToken ct = default)
    {
        await RequireAsync(p, ct);
        var q = db.Users.AsNoTracking().Include(u => u.Profile).AsQueryable();
        if (Filter(filter) is { } f)
        {
            var v = f.Value.Trim().ToLowerInvariant();
            q = f.Attr switch
            {
                "username" or "emails.value" or "emails" => q.Where(u => u.Email == v),
                "externalid" => q.Where(u => u.Profile != null && u.Profile.ExternalId == f.Value),
                "id" => Guid.TryParse(f.Value, out var g) ? q.Where(u => u.Id == g) : q.Where(_ => false),
                "displayname" => q.Where(u => u.Name.ToLower() == v),
                _ => throw new ScimException(400, $"Filtering on {f.Attr} is not supported.", "invalidFilter"),
            };
        }
        var start = Math.Max(1, startIndex ?? 1); var take = Math.Clamp(count ?? 100, 0, DirectoryRules.ScimPageMax);
        var total = await q.CountAsync(ct);
        var rows = await q.OrderBy(u => u.CreatedAt).ThenBy(u => u.Id).Skip(start - 1).Take(take).ToListAsync(ct);
        var items = new List<object>(); foreach (var u in rows) items.Add(await UserJsonAsync(u, ct));
        return Page(items, total, start);
    }

    private async Task<User> LoadUserAsync(Guid id, CancellationToken ct)
        => await db.Users.Include(u => u.Profile).Include(u => u.UserRoles).ThenInclude(r => r.Role).FirstOrDefaultAsync(u => u.Id == id, ct) ?? throw new ScimException(404, "No such user.");

    public async Task<object> GetUserAsync(Guid id, SessionPrincipal p, CancellationToken ct = default) { await RequireAsync(p, ct); return await UserJsonAsync(await LoadUserAsync(id, ct), ct); }

    /// <summary>The attributes this service understands, flattened (<c>name.givenName</c>, <c>{enterprise}:department</c> …). Unknown ones are ignored, as RFC 7644 allows.</summary>
    private static void Flatten(JsonElement e, string prefix, Dictionary<string, JsonElement> into)
    {
        if (e.ValueKind != JsonValueKind.Object) { into[prefix] = e; return; }
        foreach (var prop in e.EnumerateObject())
        {
            var key = prefix.Length == 0 ? prop.Name : prefix.Equals(Enterprise, StringComparison.OrdinalIgnoreCase) ? $"{Enterprise}:{prop.Name}" : $"{prefix}.{prop.Name}";
            if (prop.Value.ValueKind == JsonValueKind.Object && (prop.Name.Equals("name", StringComparison.OrdinalIgnoreCase) || prop.Name.Equals(Enterprise, StringComparison.OrdinalIgnoreCase)))
                Flatten(prop.Value, prop.Name.Equals(Enterprise, StringComparison.OrdinalIgnoreCase) ? Enterprise : key, into);
            else into[key] = prop.Value;
        }
    }
    private static string? Str(JsonElement e) => e.ValueKind switch { JsonValueKind.String => e.GetString(), JsonValueKind.Null => null, _ => e.ToString() };
    private static bool? Bool(JsonElement e) => e.ValueKind switch { JsonValueKind.True => true, JsonValueKind.False => false, JsonValueKind.String => bool.TryParse(e.GetString(), out var b) ? b : null, _ => null };

    /// <summary>Applies flattened attributes to a person. Returns the status the provider asked for (null: unchanged).</summary>
    private async Task<bool?> ApplyAsync(User u, Dictionary<string, JsonElement> a, bool replace, CancellationToken ct)
    {
        var p = u.Profile ??= new UserProfile { UserId = u.Id };
        JsonElement? Get(params string[] keys) { foreach (var k in keys) foreach (var (key, v) in a) if (key.Equals(k, StringComparison.OrdinalIgnoreCase)) return v; return null; }
        string? Clip(string? s, int max) => s is null ? null : s.Trim().Length == 0 ? null : s.Trim()[..Math.Min(s.Trim().Length, max)];
        var given = Get("name.givenName") is { } gv ? Str(gv) : null; var family = Get("name.familyName") is { } fv ? Str(fv) : null;
        var name = (Get("displayName") is { } dn ? Str(dn) : null) ?? (Get("name.formatted") is { } nf ? Str(nf) : null) ?? (given is null && family is null ? null : $"{given} {family}".Trim());
        if (Clip(name, 120) is { } n) u.Name = n;
        if (Get("userName") is { } un && Str(un)?.Trim().ToLowerInvariant() is { } mail && u.Email.Length > 0 && mail != u.Email)
            throw new ScimException(400, "The e-mail (userName) of an existing person is changed in the app, not over SCIM.", "mutability");
        if (Get("externalId") is { } ex) p.ExternalId = Clip(Str(ex), 200); else if (replace) p.ExternalId = null;
        if (Get("title") is { } ti) p.JobTitle = Clip(Str(ti), 100); else if (replace) p.JobTitle = null;
        if (Get($"{Enterprise}:department") is { } de) p.Department = Clip(Str(de), 100); else if (replace) p.Department = null;
        if (Get($"{Enterprise}:employeeNumber") is { } en) p.EmployeeId = Clip(Str(en), 50); else if (replace) p.EmployeeId = null;
        if (Get($"{Enterprise}:manager", $"{Enterprise}:manager.value") is { } mg)
        {
            var mv = mg.ValueKind == JsonValueKind.Object && mg.TryGetProperty("value", out var inner) ? Str(inner) : Str(mg);
            if (mv is null) p.ManagerId = null;
            else if (Guid.TryParse(mv, out var mid) && mid != u.Id && await db.Users.AnyAsync(x => x.Id == mid, ct)) p.ManagerId = mid;
            else throw new ScimException(400, "The manager is not a person here.", "invalidValue");
        }
        else if (replace) p.ManagerId = null;
        return Get("active") is { } ac ? Bool(ac) ?? throw new ScimException(400, "active must be true or false.", "invalidValue") : null;
    }

    private async Task SetActiveAsync(User u, bool active, SessionPrincipal p, CancellationToken ct)
    {
        if (active && u.Status is UserStatus.Suspended or UserStatus.Deactivated) { u.Status = UserStatus.Active; u.DeactivatedAt = null; Trace("activate", u.Id.ToString(), p); }
        else if (!active && u.Status == UserStatus.Active) await EndAsync(u, UserStatus.Suspended, p, ct);
    }
    private async Task EndAsync(User u, UserStatus to, SessionPrincipal p, CancellationToken ct)
    {
        if (u.UserRoles.Any(r => r.Role?.Key == SystemRoles.SuperAdmin)) throw new ScimException(409, "Super Admins are suspended or removed in the app, not over SCIM.", "mutability");
        if (u.Id == p.UserId) throw new ScimException(409, "The API key's owner cannot be suspended or removed with their own key.", "mutability");
        u.Status = to; if (to == UserStatus.Deactivated) u.DeactivatedAt = clock.UtcNow;
        await sessions.RevokeAllForUserAsync(u.Id, to == UserStatus.Suspended ? RevokeReason.Suspended : RevokeReason.Admin, null, ct);
        audit.Record(to == UserStatus.Suspended ? "user.suspend" : "user.deactivate", "user", u.Id.ToString(), Outcome.Success, Scim, p.UserId, p.Email, new Dictionary<string, string> { ["source"] = "scim" });
    }

    public async Task<object> CreateUserAsync(JsonElement body, SessionPrincipal p, CancellationToken ct = default)
    {
        await RequireAsync(p, ct);
        var a = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase); Flatten(body, "", a);
        var email = (a.TryGetValue("userName", out var un) ? Str(un) : null)?.Trim().ToLowerInvariant() ?? "";
        if (!System.Net.Mail.MailAddress.TryCreate(email, out var parsed) || parsed.Address != email || email.Length > 254) throw new ScimException(400, "userName must be the person's e-mail address.", "invalidValue");
        if (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == email, ct)) throw new ScimException(409, "A person with this e-mail already exists.", "uniqueness");
        var now = clock.UtcNow;
        var u = new User { Email = email, Name = email, Status = UserStatus.Active, EmailVerifiedAt = now, CreatedAt = now, UpdatedAt = now };
        u.Profile = new UserProfile { UserId = u.Id };
        var active = await ApplyAsync(u, a, false, ct) ?? true;
        if (!active) u.Status = UserStatus.Suspended;
        var baseRole = await db.Roles.AsNoTracking().Where(r => r.Key == SystemRoles.User && r.ArchivedAt == null).Select(r => (Guid?)r.Id).FirstOrDefaultAsync(ct);
        if (baseRole is { } br) u.UserRoles.Add(new UserRole { UserId = u.Id, RoleId = br, AssignedBy = p.UserId, AssignedAt = now });
        db.Users.Add(u);
        audit.Record("user.create", "user", u.Id.ToString(), Outcome.Success, Scim, p.UserId, p.Email, new Dictionary<string, string> { ["email"] = email, ["mode"] = "scim" });
        Trace("user.create", u.Id.ToString(), p);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { throw new ScimException(409, "A person with this e-mail already exists.", "uniqueness"); }
        return await UserJsonAsync(u, ct);
    }

    public async Task<object> ReplaceUserAsync(Guid id, JsonElement body, SessionPrincipal p, CancellationToken ct = default)
    {
        await RequireAsync(p, ct);
        var u = await LoadUserAsync(id, ct);
        var a = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase); Flatten(body, "", a);
        if (await ApplyAsync(u, a, true, ct) is { } active) await SetActiveAsync(u, active, p, ct);
        u.UpdatedAt = clock.UtcNow; u.Version++; Trace("user.replace", id.ToString(), p);
        await db.SaveChangesAsync(ct);
        return await UserJsonAsync(u, ct);
    }

    public async Task<object> PatchUserAsync(Guid id, JsonElement body, SessionPrincipal p, CancellationToken ct = default)
    {
        await RequireAsync(p, ct);
        var u = await LoadUserAsync(id, ct);
        foreach (var op in Ops(body))
        {
            var a = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            var kind = op.TryGetProperty("op", out var o) ? (o.GetString() ?? "").ToLowerInvariant() : "";
            var path = op.TryGetProperty("path", out var pa) ? pa.GetString() : null;
            var value = op.TryGetProperty("value", out var va) ? va : default;
            if (kind == "remove")
            {
                if (path is null) throw new ScimException(400, "remove needs a path.", "noTarget");
                using var nul = JsonDocument.Parse("null"); a[path] = nul.RootElement.Clone();
            }
            else if (kind is "add" or "replace") { if (path is null) Flatten(value, "", a); else if (value.ValueKind == JsonValueKind.Object && path.Equals("name", StringComparison.OrdinalIgnoreCase)) Flatten(value, "name", a); else a[path] = value; }
            else throw new ScimException(400, $"Unknown operation {kind}.", "invalidSyntax");
            if (await ApplyAsync(u, a, false, ct) is { } active) await SetActiveAsync(u, active, p, ct);
        }
        u.UpdatedAt = clock.UtcNow; u.Version++; Trace("user.patch", id.ToString(), p);
        await db.SaveChangesAsync(ct);
        return await UserJsonAsync(u, ct);
    }

    public async Task DeleteUserAsync(Guid id, SessionPrincipal p, CancellationToken ct = default)
    {
        await RequireAsync(p, ct);
        var u = await LoadUserAsync(id, ct);
        if (u.Status != UserStatus.Deactivated) await EndAsync(u, UserStatus.Deactivated, p, ct);
        foreach (var g in await db.ScimGroups.Where(g => g.MemberIds.Contains(id)).ToListAsync(ct)) { g.MemberIds = [.. g.MemberIds.Where(m => m != id)]; g.UpdatedAt = clock.UtcNow; }
        u.UpdatedAt = clock.UtcNow; u.Version++; Trace("user.delete", id.ToString(), p);
        await db.SaveChangesAsync(ct);
        await roles.SyncAsync(id, [], "scim", Scim, ct);
        await db.SaveChangesAsync(ct);
    }

    private static IEnumerable<JsonElement> Ops(JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object || !body.TryGetProperty("Operations", out var ops) && !body.TryGetProperty("operations", out ops) || ops.ValueKind != JsonValueKind.Array)
            throw new ScimException(400, "A PATCH body needs Operations.", "invalidSyntax");
        return ops.EnumerateArray().ToList();
    }

    // ================================================================ groups
    private async Task<object> GroupJsonAsync(ScimGroup g, CancellationToken ct)
    {
        var names = await db.Users.AsNoTracking().Where(u => g.MemberIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Name, ct);
        return new Dictionary<string, object?>
        {
            ["schemas"] = new[] { GroupSchema }, ["id"] = g.Id.ToString(), ["externalId"] = g.ExternalId, ["displayName"] = g.DisplayName,
            ["members"] = g.MemberIds.Where(names.ContainsKey).Select(m => new { value = m.ToString(), display = names[m], type = "User" }).ToList(),
            ["meta"] = new { resourceType = "Group", created = Iso(g.CreatedAt), lastModified = Iso(g.UpdatedAt), location = Location("Groups", g.Id) },
        };
    }

    public async Task<object> ListGroupsAsync(string? filter, int? startIndex, int? count, SessionPrincipal p, CancellationToken ct = default)
    {
        await RequireAsync(p, ct);
        var q = db.ScimGroups.AsNoTracking().AsQueryable();
        if (Filter(filter) is { } f)
            q = f.Attr switch
            {
                "displayname" => q.Where(g => g.DisplayName.ToLower() == f.Value.ToLower()),
                "externalid" => q.Where(g => g.ExternalId == f.Value),
                "id" => Guid.TryParse(f.Value, out var gid) ? q.Where(g => g.Id == gid) : q.Where(_ => false),
                _ => throw new ScimException(400, $"Filtering on {f.Attr} is not supported.", "invalidFilter"),
            };
        var start = Math.Max(1, startIndex ?? 1); var take = Math.Clamp(count ?? 100, 0, DirectoryRules.ScimPageMax);
        var total = await q.CountAsync(ct);
        var rows = await q.OrderBy(g => g.CreatedAt).ThenBy(g => g.Id).Skip(start - 1).Take(take).ToListAsync(ct);
        var items = new List<object>(); foreach (var g in rows) items.Add(await GroupJsonAsync(g, ct));
        return Page(items, total, start);
    }

    public async Task<object> GetGroupAsync(Guid id, SessionPrincipal p, CancellationToken ct = default)
    {
        await RequireAsync(p, ct);
        return await GroupJsonAsync(await db.ScimGroups.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id, ct) ?? throw new ScimException(404, "No such group."), ct);
    }

    private async Task<List<Guid>> MembersAsync(JsonElement arr, CancellationToken ct)
    {
        if (arr.ValueKind != JsonValueKind.Array) throw new ScimException(400, "members must be a list.", "invalidValue");
        var ids = new List<Guid>();
        foreach (var m in arr.EnumerateArray())
        {
            var v = m.ValueKind == JsonValueKind.Object && m.TryGetProperty("value", out var x) ? x.GetString() : m.GetString();
            if (!Guid.TryParse(v, out var g)) throw new ScimException(400, "A member's value must be a user id.", "invalidValue");
            ids.Add(g);
        }
        ids = [.. ids.Distinct()];
        var known = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).Select(u => u.Id).ToListAsync(ct);
        if (known.Count != ids.Count) throw new ScimException(400, "A member is not a person here.", "invalidValue");
        return ids;
    }

    private async Task SaveGroupAsync(ScimGroup g, HashSet<Guid> touched, string what, SessionPrincipal p, CancellationToken ct)
    {
        if (g.DisplayName.Length is 0 or > 200) throw new ScimException(400, "displayName is required (at most 200 characters).", "invalidValue");
        var lower = g.DisplayName.ToLower();
        if (await db.ScimGroups.AnyAsync(x => x.Id != g.Id && x.DisplayName.ToLower() == lower, ct)) throw new ScimException(409, "A group with this name already exists.", "uniqueness");
        if (g.MemberIds.Count > DirectoryRules.MaxGroupMembers) throw new ScimException(400, $"A group can have at most {DirectoryRules.MaxGroupMembers} members.", "invalidValue");
        g.UpdatedAt = clock.UtcNow; Trace(what, g.Id.ToString(), p);
        await db.SaveChangesAsync(ct);
        foreach (var uid in touched) await roles.SyncAsync(uid, await roles.ScimGroupsOfAsync(uid, ct), "scim", Scim, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task<object> CreateGroupAsync(JsonElement body, SessionPrincipal p, CancellationToken ct = default)
    {
        await RequireAsync(p, ct);
        var g = new ScimGroup
        {
            DisplayName = (body.TryGetProperty("displayName", out var dn) ? dn.GetString() : null)?.Trim() ?? "",
            ExternalId = body.TryGetProperty("externalId", out var ex) ? ex.GetString() : null, CreatedAt = clock.UtcNow,
        };
        if (body.TryGetProperty("members", out var mem)) g.MemberIds = await MembersAsync(mem, ct);
        db.ScimGroups.Add(g);
        await SaveGroupAsync(g, [.. g.MemberIds], "group.create", p, ct);
        return await GroupJsonAsync(g, ct);
    }

    public async Task<object> ReplaceGroupAsync(Guid id, JsonElement body, SessionPrincipal p, CancellationToken ct = default)
    {
        await RequireAsync(p, ct);
        var g = await db.ScimGroups.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new ScimException(404, "No such group.");
        var before = g.MemberIds.ToHashSet();
        if (body.TryGetProperty("displayName", out var dn)) g.DisplayName = dn.GetString()?.Trim() ?? "";
        g.ExternalId = body.TryGetProperty("externalId", out var ex) ? ex.GetString() : null;
        g.MemberIds = body.TryGetProperty("members", out var mem) ? await MembersAsync(mem, ct) : [];
        await SaveGroupAsync(g, [.. before, .. g.MemberIds], "group.replace", p, ct);
        return await GroupJsonAsync(g, ct);
    }

    public async Task<object> PatchGroupAsync(Guid id, JsonElement body, SessionPrincipal p, CancellationToken ct = default)
    {
        await RequireAsync(p, ct);
        var g = await db.ScimGroups.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new ScimException(404, "No such group.");
        var before = g.MemberIds.ToHashSet(); var renamed = false;
        var members = g.MemberIds.ToList();
        foreach (var op in Ops(body))
        {
            var kind = op.TryGetProperty("op", out var o) ? (o.GetString() ?? "").ToLowerInvariant() : "";
            var path = op.TryGetProperty("path", out var pa) ? pa.GetString() ?? "" : "";
            var value = op.TryGetProperty("value", out var va) ? va : default;
            if (path.Length == 0 && kind is "add" or "replace" && value.ValueKind == JsonValueKind.Object)
            {
                if (value.TryGetProperty("displayName", out var dn)) { g.DisplayName = dn.GetString()?.Trim() ?? ""; renamed = true; }
                if (value.TryGetProperty("externalId", out var ex)) g.ExternalId = ex.GetString();
                if (value.TryGetProperty("members", out var mem)) members = kind == "replace" ? await MembersAsync(mem, ct) : [.. members.Union(await MembersAsync(mem, ct))];
            }
            else if (path.Equals("displayName", StringComparison.OrdinalIgnoreCase) && kind is "add" or "replace") { g.DisplayName = value.GetString()?.Trim() ?? ""; renamed = true; }
            else if (path.Equals("externalId", StringComparison.OrdinalIgnoreCase)) g.ExternalId = kind == "remove" ? null : value.GetString();
            else if (path.Equals("members", StringComparison.OrdinalIgnoreCase))
                members = kind switch
                {
                    "add" => [.. members.Union(await MembersAsync(value, ct))],
                    "replace" => await MembersAsync(value, ct),
                    "remove" => value.ValueKind == JsonValueKind.Array ? [.. members.Except(await MembersAsync(value, ct))] : [],
                    _ => throw new ScimException(400, $"Unknown operation {kind}.", "invalidSyntax"),
                };
            else if (kind == "remove" && MemberPath().Match(path) is { Success: true } mp && Guid.TryParse(mp.Groups[1].Value, out var gone)) members.Remove(gone);
            else throw new ScimException(400, $"The path {path} is not supported.", "invalidPath");
        }
        g.MemberIds = members;
        // a rename changes which mappings apply, so every member is re-synced
        await SaveGroupAsync(g, renamed ? [.. before, .. members] : [.. before.Except(members), .. members.Except(before)], "group.patch", p, ct);
        return await GroupJsonAsync(g, ct);
    }

    public async Task DeleteGroupAsync(Guid id, SessionPrincipal p, CancellationToken ct = default)
    {
        await RequireAsync(p, ct);
        var g = await db.ScimGroups.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new ScimException(404, "No such group.");
        var members = g.MemberIds.ToList();
        db.ScimGroups.Remove(g); Trace("group.delete", id.ToString(), p);
        await db.SaveChangesAsync(ct);
        foreach (var uid in members) await roles.SyncAsync(uid, await roles.ScimGroupsOfAsync(uid, ct), "scim", Scim, ct);
        await db.SaveChangesAsync(ct);
    }
}
