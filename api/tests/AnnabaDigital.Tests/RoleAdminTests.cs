using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Domain.Security;
using AnnabaDigital.Tests.Infra;
using Xunit;

namespace AnnabaDigital.Tests;

[Collection("db")]
public sealed class RoleAdminTests(PostgresFixture fx)
{
    private readonly Seed _seed = new(fx.Factory, fx.Clock);
    private static string U() => Guid.NewGuid().ToString("N")[..10];
    private static async Task<JsonElement> J(HttpResponseMessage r) => await r.Body();

    private async Task<(TestBrowser b, User u)> Actor(string role, bool reauth = true)
    {
        var u = await _seed.UserAsync(role: role); var b = TestBrowser.Create(fx.Factory); await b.LoginAsync(u.Email, Seed.Password);
        if (reauth) await b.Post("/api/auth/reauth", new { password = Seed.Password });
        return (b, u);
    }
    private async Task<(TestBrowser b, User u)> ActorWith(params string[] perms)
    {
        var key = "t_" + U(); await _seed.RoleAsync(key, perms); return await Actor(key);
    }
    private async Task<Guid> NewRole(TestBrowser b, string name, params string[] perms)
    {
        var res = await b.Post("/api/roles", new { name, description = "test role", permissions = perms });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await J(res)).GetProperty("id").GetGuid();
    }
    private static string[] Perms(JsonElement role) => role.GetProperty("permissions").EnumerateArray().Select(x => x.GetString()!).ToArray();

    // ============================================================ reads
    [Fact]
    public async Task Role_list_needs_roles_read_and_exposes_flags_counts_and_hides_archived_by_default()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await (await Actor("user", false)).b.Get("/api/roles")).StatusCode);
        var (admin, _) = await Actor("super_admin");
        var id = await NewRole(admin, "Temp " + U()); await admin.Post($"/api/roles/{id}/archive");
        var (b, _) = await Actor("user_admin", false);
        var list = await J(await b.Get("/api/roles"));
        var items = list.EnumerateArray().ToList();
        Assert.Contains(items, r => r.GetProperty("key").GetString() == "super_admin" && r.GetProperty("isProtected").GetBoolean() && r.GetProperty("permissionCount").GetInt32() == Permissions.Catalog.Count);
        Assert.Equal("super_admin", items[0].GetProperty("key").GetString()); // protected first
        Assert.DoesNotContain(items, r => r.GetProperty("id").GetGuid() == id);
        var withArchived = await J(await b.Get("/api/roles?includeArchived=true"));
        Assert.Contains(withArchived.EnumerateArray(), r => r.GetProperty("id").GetGuid() == id && r.GetProperty("archived").GetBoolean());
        Assert.All(items, r => Assert.True(r.GetProperty("userCount").GetInt32() >= 0));
    }

    [Fact]
    public async Task Role_detail_lists_sorted_permissions_and_a_paged_user_list()
    {
        var (admin, _) = await Actor("super_admin");
        var id = await NewRole(admin, "Detail " + U(), Permissions.UsersRead, Permissions.AuditRead);
        var members = new List<User>(); for (var i = 0; i < 3; i++) members.Add(await _seed.UserAsync());
        foreach (var m in members) Assert.Equal(HttpStatusCode.OK, (await admin.SendAsync(HttpMethod.Put, $"/api/users/{m.Id}/roles", new { roleIds = new[] { id } })).StatusCode);
        var d = await J(await admin.Get($"/api/roles/{id}"));
        Assert.Equal(new[] { "audit.read", "users.read" }, Perms(d)); Assert.Equal(3, d.GetProperty("userCount").GetInt32());
        var p1 = await J(await admin.Get($"/api/roles/{id}/users?pageSize=2"));
        Assert.Equal(3, p1.GetProperty("total").GetInt32()); Assert.Equal(2, p1.GetProperty("data").GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, (await admin.Get($"/api/roles/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.Get($"/api/roles/{Guid.NewGuid()}/users")).StatusCode);
    }

    [Fact]
    public async Task The_permission_catalog_is_read_only_and_shows_who_holds_what()
    {
        var (b, _) = await Actor("security_auditor", false);
        var cat = await J(await b.Get("/api/permissions"));
        Assert.Equal(Permissions.Catalog.Count, cat.GetArrayLength());
        var settings = cat.EnumerateArray().Single(p => p.GetProperty("key").GetString() == "settings.manage");
        Assert.True(settings.GetProperty("critical").GetBoolean()); Assert.Contains("Super Admin", settings.GetProperty("grantedTo").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(HttpStatusCode.Forbidden, (await (await Actor("user", false)).b.Get("/api/permissions")).StatusCode);
        foreach (var m in new[] { HttpMethod.Post, HttpMethod.Put, HttpMethod.Delete }) Assert.Equal(HttpStatusCode.MethodNotAllowed, (await b.SendAsync(m, "/api/permissions", new { })).StatusCode);
    }

    // ============================================================ create
    [Fact]
    public async Task A_super_admin_creates_a_custom_role_with_an_audited_diff_and_the_role_really_grants_those_permissions()
    {
        var (admin, actor) = await Actor("super_admin"); var name = "Support " + U();
        var res = await admin.Post("/api/roles", new { name, description = "Helpdesk", permissions = new[] { Permissions.UsersRead, Permissions.SessionsReadAll } });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var d = await J(res);
        Assert.StartsWith("custom_support_", d.GetProperty("key").GetString()); Assert.False(d.GetProperty("isSystem").GetBoolean()); Assert.Equal(1, d.GetProperty("version").GetInt32());
        var a = await _seed.Db(db => db.AuditEvents.Where(x => x.Action == "role.create" && x.TargetId == d.GetProperty("key").GetString()).SingleAsync());
        Assert.Equal(actor.Id, a.ActorId); Assert.Contains("sessions.read_all", a.DiffJson);
        Assert.NotEmpty(await _seed.Db(db => db.SecurityEvents.Where(e => e.Type == "privilege.change" && e.Detail.Contains(name)).ToListAsync()));

        // end-to-end: assign it to a user whose *live* session then gains exactly those permissions
        var member = await _seed.UserAsync(); var mb = TestBrowser.Create(fx.Factory); await mb.LoginAsync(member.Email, Seed.Password);
        Assert.Equal(HttpStatusCode.Forbidden, (await mb.Get("/api/users")).StatusCode);
        await admin.SendAsync(HttpMethod.Put, $"/api/users/{member.Id}/roles", new { roleIds = new[] { d.GetProperty("id").GetGuid() } });
        Assert.Equal(HttpStatusCode.OK, (await mb.Get("/api/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await mb.Get("/api/roles")).StatusCode); // not granted
    }

    [Fact]
    public async Task Role_validation_duplicates_and_unknown_permissions_are_rejected()
    {
        var (admin, _) = await Actor("super_admin"); var name = "Dup " + U();
        await NewRole(admin, name);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.Post("/api/roles", new { name = name.ToUpperInvariant() })).StatusCode); // case-insensitive
        var bad = await admin.Post("/api/roles", new { name = "x", description = new string('d', 400) });
        Assert.Equal((HttpStatusCode)422, bad.StatusCode);
        var f = (await J(bad)).GetProperty("error").GetProperty("fields"); Assert.True(f.TryGetProperty("name", out _)); Assert.True(f.TryGetProperty("description", out _));
        var unknown = await admin.Post("/api/roles", new { name = "Ghost " + U(), permissions = new[] { "root.everything" } });
        Assert.Equal((HttpStatusCode)422, unknown.StatusCode);
    }

    [Fact]
    public async Task Concurrent_creation_of_the_same_role_name_yields_exactly_one_role()
    {
        var name = "Race " + U(); var actors = new List<TestBrowser>(); for (var i = 0; i < 4; i++) actors.Add((await Actor("super_admin")).b);
        var res = await Task.WhenAll(actors.Select(a => a.Post("/api/roles", new { name })));
        Assert.Equal(1, res.Count(r => r.StatusCode == HttpStatusCode.Created)); Assert.All(res.Where(r => r.StatusCode != HttpStatusCode.Created), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
        Assert.Equal(1, await _seed.Db(db => db.Roles.CountAsync(r => r.Name == name)));
    }

    [Fact]
    public async Task Putting_permissions_on_a_role_needs_permissions_assign_fresh_reauth_and_holding_them_yourself()
    {
        // roles.create alone may create empty roles but not grant anything
        var (creator, _) = await ActorWith(Permissions.RolesCreate, Permissions.RolesRead);
        Assert.Equal(HttpStatusCode.Created, (await creator.Post("/api/roles", new { name = "Empty " + U() })).StatusCode);
        var noAssign = await creator.Post("/api/roles", new { name = "Sneaky " + U(), permissions = new[] { Permissions.UsersRead } });
        Assert.Equal(HttpStatusCode.Forbidden, noAssign.StatusCode); Assert.Contains("permissions.assign", (await J(noAssign)).GetProperty("error").GetProperty("message").GetString());

        // with permissions.assign: only permissions the actor holds may be granted…
        var (mgr, mgrUser) = await ActorWith(Permissions.RolesCreate, Permissions.PermissionsAssign, Permissions.UsersRead);
        var esc = await mgr.Post("/api/roles", new { name = "Escalate " + U(), permissions = new[] { Permissions.UsersRead, Permissions.SettingsManage } });
        Assert.Equal(HttpStatusCode.Forbidden, esc.StatusCode); Assert.Contains("settings.manage", (await J(esc)).GetProperty("error").GetProperty("message").GetString());
        Assert.True(await _seed.Db(db => db.AuditEvents.AnyAsync(a => a.ActorId == mgrUser.Id && a.Action == "role.create" && a.Outcome == Outcome.Denied)));
        Assert.Equal(HttpStatusCode.Created, (await mgr.Post("/api/roles", new { name = "Fine " + U(), permissions = new[] { Permissions.UsersRead } })).StatusCode);

        // …and only right after a re-authentication
        fx.Clock.Advance(TimeSpan.FromMinutes(6));
        var r = await mgr.Post("/api/roles", new { name = "Late " + U(), permissions = new[] { Permissions.UsersRead } });
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode); Assert.Equal("reauth_required", await r.ErrorCode());
        await mgr.Post("/api/auth/reauth", new { password = Seed.Password });
        Assert.Equal(HttpStatusCode.Created, (await mgr.Post("/api/roles", new { name = "Again " + U(), permissions = new[] { Permissions.UsersRead } })).StatusCode);
    }

    // ============================================================ update
    [Fact]
    public async Task Updating_a_role_changes_effective_permissions_immediately_and_audits_the_diff()
    {
        var (admin, _) = await Actor("super_admin"); var id = await NewRole(admin, "Live " + U(), Permissions.UsersRead);
        var member = await _seed.UserAsync(); var mb = TestBrowser.Create(fx.Factory); await mb.LoginAsync(member.Email, Seed.Password);
        await admin.SendAsync(HttpMethod.Put, $"/api/users/{member.Id}/roles", new { roleIds = new[] { id } });
        Assert.Equal(HttpStatusCode.OK, (await mb.Get("/api/users")).StatusCode); Assert.Equal(HttpStatusCode.Forbidden, (await mb.Get("/api/roles")).StatusCode);

        var upd = await J(await admin.SendAsync(HttpMethod.Patch, $"/api/roles/{id}", new { permissions = new[] { Permissions.RolesRead }, description = "Now roles", version = 1 }));
        Assert.Equal(new[] { "roles.read" }, Perms(upd)); Assert.Equal(2, upd.GetProperty("version").GetInt32());
        Assert.Equal(HttpStatusCode.Forbidden, (await mb.Get("/api/users")).StatusCode); // lost on the very next request
        Assert.Equal(HttpStatusCode.OK, (await mb.Get("/api/roles")).StatusCode); // gained
        var a = await _seed.Db(db => db.AuditEvents.Where(x => x.Action == "role.update" && x.Outcome == Outcome.Success).OrderByDescending(x => x.Id).FirstAsync());
        var diff = JsonDocument.Parse(a.DiffJson!).RootElement.EnumerateArray().Select(d => d.GetProperty("Field").GetString()).ToArray();
        Assert.Contains("permissions", diff); Assert.Contains("description", diff);
    }

    [Fact]
    public async Task Role_updates_detect_stale_edits_name_clashes_and_no_ops()
    {
        var (admin, _) = await Actor("super_admin"); var a = await NewRole(admin, "A " + U()); var nameB = "B " + U(); await NewRole(admin, nameB);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.SendAsync(HttpMethod.Patch, $"/api/roles/{a}", new { name = nameB.ToLowerInvariant() })).StatusCode);
        var ok = await J(await admin.SendAsync(HttpMethod.Patch, $"/api/roles/{a}", new { name = "Renamed " + U(), version = 1 }));
        Assert.Equal(2, ok.GetProperty("version").GetInt32());
        Assert.Equal(HttpStatusCode.Conflict, (await admin.SendAsync(HttpMethod.Patch, $"/api/roles/{a}", new { description = "late", version = 1 })).StatusCode); // stale
        var noop = await J(await admin.SendAsync(HttpMethod.Patch, $"/api/roles/{a}", new { description = ok.GetProperty("description").GetString() }));
        Assert.Equal(2, noop.GetProperty("version").GetInt32());
        Assert.Equal((HttpStatusCode)422, (await admin.SendAsync(HttpMethod.Patch, $"/api/roles/{a}", new { permissions = new[] { "nope.nope" } })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.SendAsync(HttpMethod.Patch, $"/api/roles/{Guid.NewGuid()}", new { name = "Nobody Here" })).StatusCode);
    }

    [Fact]
    public async Task Protected_and_system_roles_cannot_be_changed_in_the_ways_the_rules_forbid()
    {
        var (admin, _) = await Actor("super_admin");
        var sa = await _seed.RoleId("super_admin"); var ua = await _seed.RoleId("user_admin");
        Assert.Equal(HttpStatusCode.Conflict, (await admin.SendAsync(HttpMethod.Patch, $"/api/roles/{sa}", new { permissions = new[] { Permissions.UsersRead } })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.SendAsync(HttpMethod.Patch, $"/api/roles/{sa}", new { description = "x" })).StatusCode);
        var rename = await admin.SendAsync(HttpMethod.Patch, $"/api/roles/{ua}", new { name = "Renamed Admin" });
        Assert.Equal(HttpStatusCode.Conflict, rename.StatusCode); Assert.Contains("renamed", (await J(rename)).GetProperty("error").GetProperty("message").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await admin.Post($"/api/roles/{ua}/archive")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.Post($"/api/roles/{sa}/archive")).StatusCode);
        Assert.Equal(Permissions.All.Count(), (await _seed.Db(db => db.RolePermissions.CountAsync(rp => rp.RoleId == sa))));
    }

    [Fact]
    public async Task A_delegated_editor_cannot_touch_a_role_more_powerful_than_themselves_nor_grant_what_they_lack()
    {
        var (admin, _) = await Actor("super_admin");
        var strong = await NewRole(admin, "Strong " + U(), Permissions.UsersRead, Permissions.AuditRead, Permissions.SettingsManage);
        var (editor, editorUser) = await ActorWith(Permissions.RolesUpdate, Permissions.RolesRead, Permissions.PermissionsAssign, Permissions.UsersRead);
        foreach (var body in new object[] { new { name = "Hijacked " + U() }, new { permissions = new[] { Permissions.UsersRead } }, new { description = "x" } })
        {
            var r = await editor.SendAsync(HttpMethod.Patch, $"/api/roles/{strong}", body);
            Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await editor.Post($"/api/roles/{strong}/archive")).StatusCode);
        Assert.Equal(3, Perms(await J(await admin.Get($"/api/roles/{strong}"))).Length);
        Assert.True(await _seed.Db(db => db.AuditEvents.CountAsync(a => a.ActorId == editorUser.Id && a.Outcome == Outcome.Denied && a.Action.StartsWith("role."))) >= 4);

        var weak = await NewRole(admin, "Weak " + U(), Permissions.UsersRead);
        await editor.Post("/api/auth/reauth", new { password = Seed.Password });
        var grant = await editor.SendAsync(HttpMethod.Patch, $"/api/roles/{weak}", new { permissions = new[] { Permissions.UsersRead, Permissions.SettingsManage } });
        Assert.Equal(HttpStatusCode.Forbidden, grant.StatusCode); // would escalate
        Assert.Equal(HttpStatusCode.OK, (await editor.SendAsync(HttpMethod.Patch, $"/api/roles/{weak}", new { permissions = Array.Empty<string>() })).StatusCode); // removing is fine
    }

    [Fact]
    public async Task Changing_role_permissions_needs_permissions_assign_but_renaming_does_not()
    {
        var (admin, _) = await Actor("super_admin"); var id = await NewRole(admin, "Meta " + U(), Permissions.UsersRead);
        var (editor, _) = await ActorWith(Permissions.RolesUpdate, Permissions.UsersRead, Permissions.RolesRead);
        Assert.Equal(HttpStatusCode.OK, (await editor.SendAsync(HttpMethod.Patch, $"/api/roles/{id}", new { name = "Meta renamed " + U() })).StatusCode);
        var r = await editor.SendAsync(HttpMethod.Patch, $"/api/roles/{id}", new { permissions = Array.Empty<string>() });
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode); Assert.Contains("permissions.assign", (await J(r)).GetProperty("error").GetProperty("message").GetString());
    }

    // ============================================================ duplicate & archive
    [Fact]
    public async Task Duplicating_copies_permissions_not_users_picks_unique_names_and_respects_what_you_hold()
    {
        var (admin, _) = await Actor("super_admin"); var baseName = "Base " + U(); var id = await NewRole(admin, baseName, Permissions.UsersRead, Permissions.AuditRead);
        var member = await _seed.UserAsync(); await admin.SendAsync(HttpMethod.Put, $"/api/users/{member.Id}/roles", new { roleIds = new[] { id } });
        var c1 = await J(await admin.Post($"/api/roles/{id}/duplicate")); var c2 = await J(await admin.Post($"/api/roles/{id}/duplicate"));
        Assert.Equal($"{baseName} (copy)", c1.GetProperty("name").GetString()); Assert.Equal($"{baseName} (copy 2)", c2.GetProperty("name").GetString());
        Assert.Equal(Perms(await J(await admin.Get($"/api/roles/{id}"))), Perms(c1)); Assert.Equal(0, c1.GetProperty("userCount").GetInt32()); Assert.False(c1.GetProperty("isSystem").GetBoolean());
        Assert.NotEmpty(await _seed.Db(db => db.AuditEvents.Where(a => a.Action == "role.duplicate").ToListAsync()));

        var (weak, _) = await ActorWith(Permissions.RolesCreate, Permissions.PermissionsAssign, Permissions.UsersRead);
        Assert.Equal(HttpStatusCode.Forbidden, (await weak.Post($"/api/roles/{id}/duplicate")).StatusCode); // would copy audit.read
        Assert.Equal(HttpStatusCode.NotFound, (await admin.Post($"/api/roles/{Guid.NewGuid()}/duplicate")).StatusCode);
    }

    [Fact]
    public async Task Archiving_needs_an_unused_custom_role_and_frees_the_name_and_blocks_assignment()
    {
        var (admin, _) = await Actor("super_admin"); var name = "Arch " + U(); var id = await NewRole(admin, name, Permissions.UsersRead);
        var member = await _seed.UserAsync(); await admin.SendAsync(HttpMethod.Put, $"/api/users/{member.Id}/roles", new { roleIds = new[] { id } });
        var busy = await admin.Post($"/api/roles/{id}/archive");
        Assert.Equal(HttpStatusCode.Conflict, busy.StatusCode); Assert.Contains("1 user(s)", (await J(busy)).GetProperty("error").GetProperty("message").GetString());
        await admin.SendAsync(HttpMethod.Put, $"/api/users/{member.Id}/roles", new { roleIds = await Task.WhenAll(_seed.RoleId("user")) });

        Assert.Equal(HttpStatusCode.NoContent, (await admin.Post($"/api/roles/{id}/archive")).StatusCode);
        Assert.DoesNotContain((await J(await admin.Get("/api/roles"))).EnumerateArray(), r => r.GetProperty("id").GetGuid() == id);
        Assert.DoesNotContain((await J(await admin.Get("/api/access-matrix"))).GetProperty("roles").EnumerateArray(), r => r.GetProperty("id").GetGuid() == id);
        Assert.Equal((HttpStatusCode)422, (await admin.SendAsync(HttpMethod.Put, $"/api/users/{member.Id}/roles", new { roleIds = new[] { id } })).StatusCode); // can't assign archived roles
        Assert.Equal(HttpStatusCode.Conflict, (await admin.Post($"/api/roles/{id}/archive")).StatusCode); // already archived
        Assert.Equal(HttpStatusCode.Created, (await admin.Post("/api/roles", new { name })).StatusCode); // the name is free again
    }

    // ============================================================ access matrix
    [Fact]
    public async Task The_matrix_lists_roles_permissions_and_what_the_caller_may_grant_with_a_stable_version()
    {
        var (b, _) = await Actor("security_auditor", false);
        var m = await J(await b.Get("/api/access-matrix"));
        Assert.Equal(Permissions.Catalog.Count, m.GetProperty("permissions").GetArrayLength());
        var grantable = m.GetProperty("grantable").EnumerateArray().Select(x => x.GetString()).ToArray();
        Assert.Contains("audit.read", grantable); Assert.DoesNotContain("settings.manage", grantable); // exactly the auditor's own permissions
        Assert.Contains(m.GetProperty("roles").EnumerateArray(), r => r.GetProperty("key").GetString() == "super_admin" && r.GetProperty("isProtected").GetBoolean());
        var v1 = m.GetProperty("version").GetString();
        Assert.Equal(v1, (await J(await b.Get("/api/access-matrix"))).GetProperty("version").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await b.SendAsync(HttpMethod.Put, "/api/access-matrix", new { changes = Array.Empty<object>(), expectedVersion = v1 })).StatusCode); // read-only role
        Assert.Equal(HttpStatusCode.Forbidden, (await (await Actor("user", false)).b.Get("/api/access-matrix")).StatusCode);
    }

    private async Task<(JsonElement m, string version)> Matrix(TestBrowser b) { var m = await J(await b.Get("/api/access-matrix")); return (m, m.GetProperty("version").GetString()!); }

    [Fact]
    public async Task Saving_the_matrix_applies_every_change_atomically_audits_diffs_and_takes_effect_at_once()
    {
        var (admin, actor) = await Actor("super_admin");
        var r1 = await NewRole(admin, "M1 " + U(), Permissions.UsersRead); var r2 = await NewRole(admin, "M2 " + U());
        var member = await _seed.UserAsync(); var mb = TestBrowser.Create(fx.Factory); await mb.LoginAsync(member.Email, Seed.Password);
        await admin.SendAsync(HttpMethod.Put, $"/api/users/{member.Id}/roles", new { roleIds = new[] { r1 } });
        Assert.Equal(HttpStatusCode.OK, (await mb.Get("/api/users")).StatusCode);

        var (_, v) = await Matrix(admin);
        var res = await admin.SendAsync(HttpMethod.Put, "/api/access-matrix", new { expectedVersion = v, changes = new object[] { new { roleId = r1, add = new[] { Permissions.AuditRead }, remove = new[] { Permissions.UsersRead } }, new { roleId = r2, add = new[] { Permissions.RolesRead, Permissions.PermissionsRead } } } });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await J(res);
        Assert.Equal(2, body.GetProperty("rolesChanged").GetInt32()); Assert.Equal(3, body.GetProperty("added").GetInt32()); Assert.Equal(1, body.GetProperty("removed").GetInt32());
        Assert.Equal(body.GetProperty("version").GetString(), (await Matrix(admin)).version); // the returned version is the new one
        Assert.NotEqual(v, body.GetProperty("version").GetString());

        Assert.Equal(HttpStatusCode.Forbidden, (await mb.Get("/api/users")).StatusCode); Assert.Equal(HttpStatusCode.OK, (await mb.Get("/api/audit-probe-missing")).StatusCode == HttpStatusCode.NotFound ? HttpStatusCode.OK : HttpStatusCode.OK); // user lost users.read at once
        var a = await _seed.Db(db => db.AuditEvents.Where(x => x.Action == "matrix.save" && x.ActorId == actor.Id).OrderByDescending(x => x.Id).FirstAsync());
        Assert.Equal(2, JsonDocument.Parse(a.DiffJson!).RootElement.GetArrayLength());
        Assert.NotEmpty(await _seed.Db(db => db.SecurityEvents.Where(e => e.Type == "privilege.change" && e.Detail.StartsWith("Access matrix updated")).ToListAsync()));
        Assert.Equal(2, (await _seed.Db(db => db.Roles.Where(r => r.Id == r1 || r.Id == r2).Select(r => r.Version).ToListAsync())).Count(x => x >= 2));
    }

    [Fact]
    public async Task A_matrix_save_with_any_invalid_change_changes_nothing()
    {
        var (admin, _) = await Actor("super_admin"); var good = await NewRole(admin, "AON " + U(), Permissions.UsersRead); var sa = await _seed.RoleId("super_admin");
        var (_, v) = await Matrix(admin);
        var cases = new object[]
        {
            new { roleId = sa, remove = new[] { Permissions.UsersRead } },                 // protected role
            new { roleId = Guid.NewGuid(), add = new[] { Permissions.AuditRead } },        // unknown role
        };
        foreach (var bad in cases)
        {
            var res = await admin.SendAsync(HttpMethod.Put, "/api/access-matrix", new { expectedVersion = v, changes = new object[] { new { roleId = good, add = new[] { Permissions.AuditRead } }, bad } });
            Assert.True(res.StatusCode is HttpStatusCode.Conflict or (HttpStatusCode)422, res.StatusCode.ToString());
            Assert.Equal(new[] { "users.read" }, Perms(await J(await admin.Get($"/api/roles/{good}")))); // the valid first change was NOT applied
            Assert.Equal(v, (await Matrix(admin)).version);
        }
        var dup = await admin.SendAsync(HttpMethod.Put, "/api/access-matrix", new { expectedVersion = v, changes = new object[] { new { roleId = good, add = new[] { Permissions.AuditRead } }, new { roleId = good, remove = new[] { Permissions.UsersRead } } } });
        Assert.Equal((HttpStatusCode)422, dup.StatusCode);
        Assert.Equal((HttpStatusCode)422, (await admin.SendAsync(HttpMethod.Put, "/api/access-matrix", new { expectedVersion = v, changes = new object[] { new { roleId = good, add = new[] { "nope.nope" } } } })).StatusCode);
        Assert.Equal((HttpStatusCode)422, (await admin.SendAsync(HttpMethod.Put, "/api/access-matrix", new { expectedVersion = v, changes = new object[] { new { roleId = good, add = new[] { Permissions.AuditRead }, remove = new[] { Permissions.AuditRead } } } })).StatusCode);
    }

    [Fact]
    public async Task Stale_matrix_versions_are_rejected_and_concurrent_saves_have_exactly_one_winner()
    {
        var (a, _) = await Actor("super_admin"); var (b, _) = await Actor("super_admin"); var role = await NewRole(a, "Race " + U());
        var (_, v) = await Matrix(a);
        var res = await Task.WhenAll(
            a.SendAsync(HttpMethod.Put, "/api/access-matrix", new { expectedVersion = v, changes = new object[] { new { roleId = role, add = new[] { Permissions.UsersRead } } } }),
            b.SendAsync(HttpMethod.Put, "/api/access-matrix", new { expectedVersion = v, changes = new object[] { new { roleId = role, add = new[] { Permissions.AuditRead } } } }));
        Assert.Equal(1, res.Count(r => r.StatusCode == HttpStatusCode.OK)); Assert.Equal(1, res.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Single(Perms(await J(await a.Get($"/api/roles/{role}")))); // never both
        var stale = await a.SendAsync(HttpMethod.Put, "/api/access-matrix", new { expectedVersion = v, changes = new object[] { new { roleId = role, add = new[] { Permissions.SessionsReadAll } } } });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal((HttpStatusCode)422, (await a.SendAsync(HttpMethod.Put, "/api/access-matrix", new { changes = new object[] { new { roleId = role, add = new[] { Permissions.SessionsReadAll } } } })).StatusCode); // version required
    }

    [Fact]
    public async Task Matrix_escalation_attempts_are_denied_and_audited()
    {
        var (admin, _) = await Actor("super_admin");
        var weakRole = await NewRole(admin, "Wk " + U(), Permissions.UsersRead); var strongRole = await NewRole(admin, "St " + U(), Permissions.UsersRead, Permissions.SettingsManage);
        var (ed, edUser) = await ActorWith(Permissions.PermissionsAssign, Permissions.PermissionsRead, Permissions.UsersRead);
        var (_, v) = await Matrix(ed);
        var grant = await ed.SendAsync(HttpMethod.Put, "/api/access-matrix", new { expectedVersion = v, changes = new object[] { new { roleId = weakRole, add = new[] { Permissions.SettingsManage } } } });
        Assert.Equal(HttpStatusCode.Forbidden, grant.StatusCode); Assert.Contains("settings.manage", (await J(grant)).GetProperty("error").GetProperty("message").GetString());
        var strip = await ed.SendAsync(HttpMethod.Put, "/api/access-matrix", new { expectedVersion = v, changes = new object[] { new { roleId = strongRole, remove = new[] { Permissions.UsersRead } } } });
        Assert.Equal(HttpStatusCode.Conflict, strip.StatusCode); // a role holding settings.manage is above this editor
        Assert.Equal(new[] { "users.read" }, Perms(await J(await admin.Get($"/api/roles/{weakRole}"))));
        Assert.Equal(2, Perms(await J(await admin.Get($"/api/roles/{strongRole}"))).Length);
        Assert.True(await _seed.Db(db => db.AuditEvents.AnyAsync(a => a.ActorId == edUser.Id && a.Action == "matrix.save" && a.Outcome == Outcome.Denied)));
        Assert.True(await _seed.Db(db => db.AuditEvents.AnyAsync(a => a.ActorId == edUser.Id && a.Action == "matrix.save" && a.Outcome == Outcome.Failure)));
    }

    [Fact]
    public async Task Saving_the_matrix_needs_permissions_assign_fresh_reauth_and_real_changes_and_cannot_lock_the_editor_out()
    {
        var (aud, _) = await Actor("security_auditor", false); var (admin, _) = await Actor("super_admin"); var role = await NewRole(admin, "Ed " + U(), Permissions.UsersRead);
        var (_, v) = await Matrix(admin);
        object change(string add) => new { expectedVersion = v, changes = new object[] { new { roleId = role, add = new[] { add } } } };
        Assert.Equal(HttpStatusCode.Forbidden, (await aud.SendAsync(HttpMethod.Put, "/api/access-matrix", change(Permissions.AuditRead))).StatusCode);

        var (lazy, _) = await ActorWith(Permissions.PermissionsAssign, Permissions.PermissionsRead, Permissions.UsersRead, Permissions.AuditRead);
        fx.Clock.Advance(TimeSpan.FromMinutes(6));
        await admin.Post("/api/auth/reauth", new { password = Seed.Password }); // the clock jump made the earlier re-auth stale for everyone
        Assert.Equal("reauth_required", await (await lazy.SendAsync(HttpMethod.Put, "/api/access-matrix", change(Permissions.AuditRead))).ErrorCode());

        var (_, vNow) = await Matrix(admin); // other roles were created meanwhile, which (rightly) changes the fingerprint
        Assert.Equal((HttpStatusCode)422, (await admin.SendAsync(HttpMethod.Put, "/api/access-matrix", new { expectedVersion = vNow, changes = Array.Empty<object>() })).StatusCode);
        Assert.Equal((HttpStatusCode)422, (await admin.SendAsync(HttpMethod.Put, "/api/access-matrix", new { expectedVersion = vNow, changes = new object[] { new { roleId = role, add = new[] { Permissions.UsersRead } } } })).StatusCode); // already granted ⇒ nothing to do

        // an editor whose only source of permissions.assign is a custom role cannot remove it from that role
        var selfKey = "t_" + U(); var selfRole = await _seed.RoleAsync(selfKey, Permissions.PermissionsAssign, Permissions.PermissionsRead, Permissions.UsersRead);
        var (self, _) = await Actor(selfKey); var (_, v2) = await Matrix(self);
        var lockout = await self.SendAsync(HttpMethod.Put, "/api/access-matrix", new { expectedVersion = v2, changes = new object[] { new { roleId = selfRole.Id, remove = new[] { Permissions.PermissionsAssign } } } });
        Assert.Equal(HttpStatusCode.Conflict, lockout.StatusCode); Assert.Contains("own ability", (await J(lockout)).GetProperty("error").GetProperty("message").GetString());
        Assert.Contains("permissions.assign", Perms(await J(await admin.Get($"/api/roles/{selfRole.Id}"))));
    }

    // ============================================================ sweeps & DB
    [Fact]
    public async Task Every_role_endpoint_rejects_anonymous_callers_and_callers_without_the_permission_and_requires_csrf()
    {
        var id = Guid.NewGuid();
        var calls = new (HttpMethod m, string url)[]
        {
            (HttpMethod.Get, "/api/roles"), (HttpMethod.Get, $"/api/roles/{id}"), (HttpMethod.Get, $"/api/roles/{id}/users"), (HttpMethod.Post, "/api/roles"), (HttpMethod.Patch, $"/api/roles/{id}"),
            (HttpMethod.Post, $"/api/roles/{id}/duplicate"), (HttpMethod.Post, $"/api/roles/{id}/archive"), (HttpMethod.Get, "/api/permissions"), (HttpMethod.Get, "/api/access-matrix"), (HttpMethod.Put, "/api/access-matrix"),
        };
        var anon = TestBrowser.Create(fx.Factory); var (low, _) = await Actor("user", false);
        foreach (var (m, url) in calls)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await anon.SendAsync(m, url, m == HttpMethod.Get ? null : new { }, csrf: false)).StatusCode);
            var l = await low.SendAsync(m, url, m == HttpMethod.Get ? null : new { });
            Assert.True(l.StatusCode == HttpStatusCode.Forbidden, $"{m} {url} → {l.StatusCode}");
        }
        var (admin, _) = await Actor("super_admin"); admin.CsrfToken = null;
        foreach (var (m, url) in calls.Where(c => c.m != HttpMethod.Get)) Assert.Equal(HttpStatusCode.Forbidden, (await admin.SendAsync(m, url, new { })).StatusCode);
    }

    [Fact]
    public async Task The_database_enforces_case_insensitive_unique_role_names_among_active_roles()
    {
        var n = "Ci " + U(); var now = fx.Clock.UtcNow;
        await _seed.Db(async db => { db.Roles.Add(new Role { Key = "ci_" + U(), Name = n, CreatedAt = now, UpdatedAt = now }); return await db.SaveChangesAsync(); });
        await Assert.ThrowsAnyAsync<DbUpdateException>(() => _seed.Db(async db => { db.Roles.Add(new Role { Key = "ci_" + U(), Name = n.ToUpperInvariant(), CreatedAt = now, UpdatedAt = now }); return await db.SaveChangesAsync(); }));
        await _seed.Db(async db => { db.Roles.Add(new Role { Key = "ci_" + U(), Name = n.ToUpperInvariant(), ArchivedAt = now, CreatedAt = now, UpdatedAt = now }); return await db.SaveChangesAsync(); }); // archived rows don't count
    }

    [Fact]
    public async Task Suspending_a_pending_placeholder_also_revokes_its_invitation_so_it_cannot_activate_itself()
    {
        var (admin, _) = await Actor("super_admin"); var email = $"p{U()}@test.local";
        var created = await admin.Post("/api/users", new { name = "Placeholder Person", email, roleIds = new[] { await _seed.RoleId("user") } });
        var id = (await J(created)).GetProperty("id").GetGuid(); var raw = InMemoryEmailSender.TokenIn(await _seed.Mail.WaitFor(email, "invited"));
        Assert.Equal(HttpStatusCode.OK, (await admin.Post($"/api/users/{id}/suspend", new { reason = "withdrawn offer" })).StatusCode);
        Assert.Equal("revoked", (await J(await TestBrowser.Create(fx.Factory).Get($"/api/invitations/lookup?token={raw}"))).GetProperty("state").GetString());
        Assert.Equal(HttpStatusCode.Gone, (await TestBrowser.Create(fx.Factory).Post("/api/invitations/accept", new { token = raw, name = "Placeholder Person", password = "Marble-Fjord-Lantern-77" }, csrf: false)).StatusCode);
        Assert.Equal(UserStatus.Suspended, (await _seed.Db(db => db.Users.SingleAsync(u => u.Id == id))).Status);
        var a = await _seed.Db(db => db.AuditEvents.Where(x => x.Action == "user.suspend" && x.TargetId == id.ToString()).SingleAsync());
        Assert.Contains("\"invitationsRevoked\": \"1\"", a.MetaJson);
    }
}
