using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Tests.Infra;
using Xunit;

namespace AnnabaDigital.Tests;

/// <summary>Directory (R3-9: INT-003, ADM-004 sync): SSO groups and SCIM groups give the mapped roles and take them away again, never Super Admin; the HR CSV sync
/// updates employee data and suspends leavers but never a Super Admin or the person running it; SCIM creates, updates, suspends and removes people.</summary>
[Collection("db")]
public sealed class DirectoryTests(PostgresFixture fx)
{
    private const HttpStatusCode Invalid = (HttpStatusCode)422;
    private static async Task<JsonElement> Ok(HttpResponseMessage r) { Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync()); return r.StatusCode == HttpStatusCode.NoContent ? default : await r.Body(); }
    private static Guid IdOf(JsonElement e) => e.GetProperty("id").GetGuid();
    private static async Task<JsonElement> Fields(HttpResponseMessage r) { Assert.Equal(Invalid, r.StatusCode); return (await r.Body()).GetProperty("error").GetProperty("fields"); }
    private static Task<List<string>> RolesOf(Seed seed, Guid user) => seed.Db(db => db.UserRoles.Where(r => r.UserId == user).Join(db.Roles.IgnoreQueryFilters(), ur => ur.RoleId, r => r.Id, (ur, r) => r.Key).ToListAsync());
    private static Task<UserStatus> StatusOf(Seed seed, Guid user) => seed.Db(db => db.Users.IgnoreQueryFilters().Where(u => u.Id == user).Select(u => u.Status).FirstAsync());

    [Fact]
    public async Task Sso_groups_give_and_take_the_mapped_roles_but_never_super_admin()
    {
        var iso = await fx.NewIsolatedAsync(); var seed = new Seed(iso.Factory, iso.Clock);
        var idp = new FakeOidc();
        var f = iso.Factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => { s.RemoveAll<IOidcClient>(); s.AddSingleton<IOidcClient>(idp); }));
        async Task<(User, TestBrowser)> Person(string role) { var u = await seed.UserAsync(role: role); var b = TestBrowser.Create(iso.Factory); await b.LoginAsync(u.Email, Seed.Password); return (u, b); }
        var (_, admin) = await Person("super_admin"); var (_, ua) = await Person("user_admin"); var (_, ga) = await Person("ga_manager"); var (worker, _) = await Person("user");
        await Ok(await admin.Post("/api/auth/reauth", new { password = Seed.Password })); await Ok(await ua.Post("/api/auth/reauth", new { password = Seed.Password }));
        var roles = (await Ok(await admin.Get("/api/roles"))).EnumerateArray().ToDictionary(r => r.GetProperty("key").GetString()!, IdOf);

        // only directory.manage; never Super Admin; only roles the person could grant themselves
        Assert.Equal(HttpStatusCode.Forbidden, (await ga.Get("/api/directory/mappings")).StatusCode);
        Assert.True((await Fields(await admin.Post("/api/directory/mappings", new { group = "Admins", roleId = roles["super_admin"] }))).TryGetProperty("roleId", out _));
        Assert.Equal(HttpStatusCode.Forbidden, (await ua.Post("/api/directory/mappings", new { group = "GA", roleId = roles["ga_agent"] })).StatusCode);   // user admins don't hold the agent's permissions
        var map = await Ok(await admin.Post("/api/directory/mappings", new { group = "GA-Agents", roleId = roles["ga_agent"] }));
        Assert.Equal("ga_agent", map.GetProperty("roleKey").GetString());
        Assert.True((await Fields(await admin.Post("/api/directory/mappings", new { group = "ga-agents", roleId = roles["ga_agent"] }))).TryGetProperty("group", out _));

        // sign in with the group: the role is given; without it, taken; a token without a groups claim changes nothing
        var http = f.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false });
        async Task SignIn(IReadOnlyList<string>? groups)
        {
            HttpRequestMessage Req(string url, string? cookie = null)
            {
                var r = new HttpRequestMessage(HttpMethod.Get, url);
                r.Headers.Add("X-Internal-Proxy-Key", ApiFactory.ProxyKey); r.Headers.Add("X-Forwarded-For", TestBrowser.NewIp());
                if (cookie is not null) r.Headers.Add("Cookie", cookie);
                return r;
            }
            var start = await http.SendAsync(Req("/api/auth/sso/start?returnTo=%2Fdashboard"));
            Assert.Equal(HttpStatusCode.Redirect, start.StatusCode);
            var state = System.Web.HttpUtility.ParseQueryString(start.Headers.Location!.Query)["state"]!;
            var cookie = start.Headers.GetValues("Set-Cookie").Select(c => c.Split(';')[0]).First(c => c.StartsWith("sso="));
            var code = "code-" + Guid.NewGuid().ToString("N"); idp.Codes[code] = new OidcIdentity("https://idp.test", "sub-" + worker.Id, worker.Email, true, worker.Name, groups);
            var cb = await http.SendAsync(Req($"/api/auth/sso/callback?code={code}&state={Uri.EscapeDataString(state)}", cookie));
            Assert.Equal(HttpStatusCode.Redirect, cb.StatusCode);
            Assert.DoesNotContain("error", cb.Headers.Location!.ToString());
        }
        await SignIn(["Everyone", "ga-agents"]);
        Assert.Contains("ga_agent", await RolesOf(seed, worker.Id)); Assert.Contains("user", await RolesOf(seed, worker.Id));
        await SignIn(null);
        Assert.Contains("ga_agent", await RolesOf(seed, worker.Id));
        await SignIn(["Everyone"]);
        Assert.DoesNotContain("ga_agent", await RolesOf(seed, worker.Id)); Assert.Contains("user", await RolesOf(seed, worker.Id));   // roles set by hand stay
        Assert.True(await seed.Db(db => db.AuditEvents.IgnoreQueryFilters().AnyAsync(a => a.Action == "directory.roles_synced" && a.TargetId == worker.Id.ToString())));

        // removing the mapping stops it giving the role
        await Ok(await admin.SendAsync(HttpMethod.Delete, $"/api/directory/mappings/{IdOf(map)}"));
        await SignIn(["GA-Agents"]);
        Assert.DoesNotContain("ga_agent", await RolesOf(seed, worker.Id));
    }

    [Fact]
    public async Task The_hr_csv_updates_employee_data_and_suspends_leavers_but_never_a_super_admin()
    {
        var iso = await fx.NewIsolatedAsync(); var f = iso.Factory; var seed = new Seed(f, iso.Clock);
        async Task<(User, TestBrowser)> Person(string role) { var u = await seed.UserAsync(role: role); var b = TestBrowser.Create(f); await b.LoginAsync(u.Email, Seed.Password); return (u, b); }
        var (me, ua) = await Person("user_admin"); var (boss, _) = await Person("user"); var (ani, _) = await Person("user"); var (leaver, _) = await Person("user");
        var (sa, _) = await Person("super_admin"); var (quiet, _) = await Person("user"); var (_, req) = await Person("user");
        var site = await Ok(await TestBrowser.Create(f).Pipe(async b => { await b.LoginAsync(sa.Email, Seed.Password); return await b.Post("/api/locations", new { type = "site", code = "JKT1", name = "Jakarta HQ" }); }));

        var csv = string.Join("\n",
            "Email,Employee ID,Department,Job Title,Manager Email,Site Code,Status",
            $"{ani.Email},E-001,Finance,Analyst,{boss.Email},jkt1,active",
            $"{leaver.Email},E-002,,,,,inactive",
            $"{sa.Email},,,,,,inactive",
            $"{boss.Email},E-003,,,{boss.Email},,",
            "nobody@example.com,E-009,,,,,",
            $"{ani.Email},E-001,,,,,");
        Assert.Equal(HttpStatusCode.Forbidden, (await req.Post("/api/directory/sync", new { csv })).StatusCode);
        var dry = await Ok(await ua.Post("/api/directory/sync", new { csv, dryRun = true }));
        Assert.Equal(2, dry.GetProperty("updated").GetInt32()); Assert.Equal(1, dry.GetProperty("suspended").GetInt32());
        Assert.Equal(1, dry.GetProperty("notFound").GetInt32()); Assert.Equal(3, dry.GetProperty("failed").GetInt32());   // super admin, own manager, duplicate
        var details = dry.GetProperty("details").EnumerateArray().ToList();
        Assert.Contains(details, d => d.GetProperty("email").GetString() == sa.Email && d.GetProperty("detail").GetString()!.Contains("Super Admin"));
        Assert.Contains(details, d => d.GetProperty("email").GetString() == boss.Email && d.GetProperty("detail").GetString()!.Contains("own manager"));
        Assert.Equal(UserStatus.Active, await StatusOf(seed, leaver.Id));                   // a dry run writes nothing
        Assert.Null(await seed.Db(db => db.Users.IgnoreQueryFilters().Where(u => u.Id == ani.Id).Select(u => u.Profile!.EmployeeId).FirstAsync()));

        var applied = await ua.Post("/api/directory/sync", new { csv, dryRun = false });
        Assert.Equal(HttpStatusCode.Forbidden, applied.StatusCode);                          // re-authentication first
        await Ok(await ua.Post("/api/auth/reauth", new { password = Seed.Password }));
        await Ok(await ua.Post("/api/directory/sync", new { csv, dryRun = false }));
        var detail = await Ok(await ua.Get($"/api/users/{ani.Id}"));
        Assert.Equal("E-001", detail.GetProperty("employeeId").GetString()); Assert.Equal("Jakarta HQ", detail.GetProperty("siteName").GetString());
        Assert.Equal("Finance", detail.GetProperty("department").GetString()); Assert.Equal(boss.Id, detail.GetProperty("managerId").GetGuid());
        Assert.Equal(UserStatus.Suspended, await StatusOf(seed, leaver.Id));
        Assert.Equal(UserStatus.Active, await StatusOf(seed, sa.Id));

        // people missing from the file: suspended only when asked, never the person running it or a Super Admin
        var short1 = $"Email,Status\n{ani.Email},active\n{boss.Email},active";
        var miss = await Ok(await ua.Post("/api/directory/sync", new { csv = short1, dryRun = false, suspendMissing = true }));
        Assert.Equal(UserStatus.Suspended, await StatusOf(seed, quiet.Id));
        Assert.Equal(UserStatus.Active, await StatusOf(seed, me.Id)); Assert.Equal(UserStatus.Active, await StatusOf(seed, sa.Id));
        Assert.True(miss.GetProperty("suspended").GetInt32() >= 1);
        Assert.True(await seed.Db(db => db.AuditEvents.IgnoreQueryFilters().AnyAsync(a => a.Action == "directory.sync")));
        Assert.True((await Fields(await ua.Post("/api/directory/sync", new { csv = "Name\nAni" }))).TryGetProperty("csv", out _));
        _ = site;
    }

    [Fact]
    public async Task Scim_creates_updates_suspends_and_removes_people_and_groups_drive_roles()
    {
        var iso = await fx.NewIsolatedAsync(); var f = iso.Factory; var seed = new Seed(f, iso.Clock);
        var (au, admin) = (await seed.UserAsync(role: "super_admin"), TestBrowser.Create(f)); await admin.LoginAsync(au.Email, Seed.Password);
        await Ok(await admin.Post("/api/auth/reauth", new { password = Seed.Password }));
        var roles = (await Ok(await admin.Get("/api/roles"))).EnumerateArray().ToDictionary(r => r.GetProperty("key").GetString()!, IdOf);
        await Ok(await admin.Post("/api/directory/mappings", new { group = "GA Team", roleId = roles["ga_agent"] }));
        var key = (await Ok(await admin.Post("/api/integrations/keys", new { name = "Entra", scopes = new[] { "directory.write" } }))).GetProperty("token").GetString()!;
        var other = (await Ok(await admin.Post("/api/integrations/keys", new { name = "BI reader", scopes = new[] { "tickets.read" } }))).GetProperty("token").GetString()!;
        Task<HttpResponseMessage> Scim(string? token, HttpMethod m, string path, string? json = null)
        {
            var r = new HttpRequestMessage(m, "/api/v1/scim/v2" + path);
            r.Headers.Add("X-Internal-Proxy-Key", ApiFactory.ProxyKey);
            if (token is not null) r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (json is not null) r.Content = new StringContent(json, Encoding.UTF8, "application/scim+json");
            return f.CreateClient().SendAsync(r);
        }
        async Task<JsonElement> J(HttpResponseMessage r, HttpStatusCode want) { var s = await r.Content.ReadAsStringAsync(); Assert.True(r.StatusCode == want, $"{r.StatusCode}: {s}"); return s.Length == 0 ? default : JsonDocument.Parse(s).RootElement.Clone(); }

        Assert.Equal(HttpStatusCode.Unauthorized, (await Scim(null, HttpMethod.Get, "/Users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Scim(other, HttpMethod.Get, "/Users")).StatusCode);
        var spc = await Scim(key, HttpMethod.Get, "/ServiceProviderConfig"); var spcBody = await spc.Content.ReadAsStringAsync();
        Assert.True(spc.StatusCode == HttpStatusCode.OK && spcBody.Contains("\"patch\""), $"{spc.StatusCode} {spc.Content.Headers.ContentType} [{spcBody}]");

        // create: active at once, base role, enterprise data; a second time is a uniqueness error in the SCIM shape
        var body = """{"schemas":["urn:ietf:params:scim:schemas:core:2.0:User","urn:ietf:params:scim:schemas:extension:enterprise:2.0:User"],"userName":"Dewi.Lestari@Example.com","externalId":"aad-1","name":{"givenName":"Dewi","familyName":"Lestari"},"active":true,"title":"Engineer","urn:ietf:params:scim:schemas:extension:enterprise:2.0:User":{"department":"IT","employeeNumber":"E-77"}}""";
        var created = await J(await Scim(key, HttpMethod.Post, "/Users", body), HttpStatusCode.Created);
        var id = Guid.Parse(created.GetProperty("id").GetString()!);
        Assert.Equal("dewi.lestari@example.com", created.GetProperty("userName").GetString()); Assert.Equal("Dewi Lestari", created.GetProperty("displayName").GetString());
        Assert.Equal("E-77", created.GetProperty("urn:ietf:params:scim:schemas:extension:enterprise:2.0:User").GetProperty("employeeNumber").GetString());
        Assert.Equal(UserStatus.Active, await StatusOf(seed, id)); Assert.Equal(["user"], await RolesOf(seed, id));
        var dup = await J(await Scim(key, HttpMethod.Post, "/Users", body), HttpStatusCode.Conflict);
        Assert.Equal("uniqueness", dup.GetProperty("scimType").GetString());
        Assert.Equal("urn:ietf:params:scim:api:messages:2.0:Error", dup.GetProperty("schemas")[0].GetString());

        // find by userName and externalId; page through
        Assert.Equal(1, (await J(await Scim(key, HttpMethod.Get, "/Users?filter=" + Uri.EscapeDataString("userName eq \"dewi.lestari@example.com\"")), HttpStatusCode.OK)).GetProperty("totalResults").GetInt32());
        Assert.Equal(1, (await J(await Scim(key, HttpMethod.Get, "/Users?filter=" + Uri.EscapeDataString("externalId eq \"aad-1\"")), HttpStatusCode.OK)).GetProperty("totalResults").GetInt32());
        Assert.Equal("invalidFilter", (await J(await Scim(key, HttpMethod.Get, "/Users?filter=" + Uri.EscapeDataString("userName sw \"d\"")), HttpStatusCode.BadRequest)).GetProperty("scimType").GetString());

        // PATCH: a new title; the e-mail is not changed over SCIM; active false suspends
        await J(await Scim(key, HttpMethod.Patch, $"/Users/{id}", """{"schemas":["urn:ietf:params:scim:api:messages:2.0:PatchOp"],"Operations":[{"op":"replace","path":"title","value":"Lead engineer"}]}"""), HttpStatusCode.OK);
        Assert.Equal("Lead engineer", await seed.Db(db => db.Users.IgnoreQueryFilters().Where(u => u.Id == id).Select(u => u.Profile!.JobTitle).FirstAsync()));
        Assert.Equal("mutability", (await J(await Scim(key, HttpMethod.Patch, $"/Users/{id}", """{"Operations":[{"op":"replace","path":"userName","value":"someone@else.com"}]}"""), HttpStatusCode.BadRequest)).GetProperty("scimType").GetString());

        // groups: membership gives the mapped role, leaving takes it away
        var g = await J(await Scim(key, HttpMethod.Post, "/Groups", $$"""{"displayName":"GA Team","members":[{"value":"{{id}}"}]}"""), HttpStatusCode.Created);
        var gid = g.GetProperty("id").GetString();
        Assert.Contains("ga_agent", await RolesOf(seed, id));
        await J(await Scim(key, HttpMethod.Patch, $"/Groups/{gid}", $$"""{"Operations":[{"op":"remove","path":"members[value eq \"{{id}}\"]"}]}"""), HttpStatusCode.OK);
        Assert.DoesNotContain("ga_agent", await RolesOf(seed, id));
        await J(await Scim(key, HttpMethod.Patch, $"/Groups/{gid}", $$"""{"Operations":[{"op":"add","path":"members","value":[{"value":"{{id}}"}]}]}"""), HttpStatusCode.OK);
        Assert.Contains("ga_agent", await RolesOf(seed, id));

        await J(await Scim(key, HttpMethod.Patch, $"/Users/{id}", """{"Operations":[{"op":"replace","value":{"active":false}}]}"""), HttpStatusCode.OK);
        Assert.Equal(UserStatus.Suspended, await StatusOf(seed, id));
        await J(await Scim(key, HttpMethod.Patch, $"/Users/{id}", """{"Operations":[{"op":"replace","path":"active","value":true}]}"""), HttpStatusCode.OK);
        Assert.Equal(UserStatus.Active, await StatusOf(seed, id));

        // DELETE deactivates, leaves the groups and drops the managed role; a Super Admin and the key's owner are refused
        await J(await Scim(key, HttpMethod.Delete, $"/Users/{id}"), HttpStatusCode.NoContent);
        Assert.Equal(UserStatus.Deactivated, await StatusOf(seed, id)); Assert.DoesNotContain("ga_agent", await RolesOf(seed, id));
        Assert.Equal(HttpStatusCode.Conflict, (await Scim(key, HttpMethod.Delete, $"/Users/{au.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Scim(key, HttpMethod.Get, $"/Users/{Guid.NewGuid()}")).StatusCode);
        Assert.True(await seed.Db(db => db.AuditEvents.IgnoreQueryFilters().AnyAsync(a => a.Action == "directory.scim")));
    }
}

internal static class PipeExtensions
{
    public static async Task<T> Pipe<T>(this TestBrowser b, Func<TestBrowser, Task<T>> f) => await f(b);
}
