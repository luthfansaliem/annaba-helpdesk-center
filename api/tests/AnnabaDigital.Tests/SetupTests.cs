using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Domain.Security;
using AnnabaDigital.Infrastructure;
using AnnabaDigital.Tests.Infra;
using Xunit;

namespace AnnabaDigital.Tests;

/// <summary>ADM-001: the go-live checklist and the quick start that fills the gaps. Every test that counts things works in a brand-new organisation, so other tests' data cannot interfere.</summary>
[Collection("db")]
public sealed class SetupTests(PostgresFixture fx)
{
    private readonly Seed _seed = new(fx.Factory, fx.Clock);
    private const string StrongPw = "Cobalt-Meadow-Harvest-58";
    private const HttpStatusCode Invalid = (HttpStatusCode)422;
    private static string Uniq() => Guid.NewGuid().ToString("N")[..8];
    private static JsonElement Step(JsonElement status, string key) => status.GetProperty("steps").EnumerateArray().Single(s => s.GetProperty("key").GetString() == key);
    private static bool Done(JsonElement status, string key) => Step(status, key).GetProperty("done").GetBoolean();
    private static string[] Strings(JsonElement list) => [.. list.EnumerateArray().Select(x => x.GetString()!)];

    private async Task<(TestBrowser Admin, Guid AdminId)> FreshOrganisation()
    {
        var pu = await _seed.UserAsync(role: "super_admin"); var p = TestBrowser.Create(fx.Factory); await p.LoginAsync(pu.Email, Seed.Password);
        await p.Post("/api/auth/reauth", new { password = Seed.Password });
        var slug = "s" + Uniq(); var email = $"{slug}.admin@test.local";
        var res = await p.Post("/api/tenants", new { name = $"Org {slug}", slug, adminEmail = email });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var token = InMemoryEmailSender.TokenIn(await _seed.Mail.WaitFor(email, "invited"));
        Assert.Equal(HttpStatusCode.OK, (await TestBrowser.Create(fx.Factory).Post("/api/invitations/accept", new { token, name = "Org Admin", password = StrongPw }, csrf: false)).StatusCode);
        var b = TestBrowser.Create(fx.Factory); Assert.Equal(HttpStatusCode.OK, (await b.LoginAsync(email, StrongPw)).StatusCode);
        var me = await (await b.Get("/api/auth/me")).Body();
        return (b, me.GetProperty("id").GetGuid());
    }
    private static async Task<JsonElement> Status(TestBrowser b) { var r = await b.Get("/api/setup/status"); Assert.Equal(HttpStatusCode.OK, r.StatusCode); return await r.Body(); }
    private static async Task<JsonElement> Get(TestBrowser b, string url) { var r = await b.Get(url); Assert.Equal(HttpStatusCode.OK, r.StatusCode); return await r.Body(); }
    private static JsonElement Rows(JsonElement e) => e.ValueKind == JsonValueKind.Array ? e : e.GetProperty("data");

    // ============================================================ the checklist
    [Fact]
    public async Task A_new_organisation_starts_with_a_calendar_a_catalog_and_service_levels_and_the_rest_to_do()
    {
        var (admin, _) = await FreshOrganisation(); var s = await Status(admin);
        Assert.False(s.GetProperty("ready").GetBoolean()); Assert.Equal(6, s.GetProperty("total").GetInt32()); Assert.True(3 == s.GetProperty("done").GetInt32(), s.ToString());
        Assert.True(Done(s, "calendar")); Assert.True(Done(s, "sla")); Assert.True(Step(s, "sla").GetProperty("count").GetInt32() == 4); Assert.True(Done(s, "catalog")); Assert.True(Step(s, "catalog").GetProperty("count").GetInt32() > 5);          // the starter catalog is published
        foreach (var k in new[] { "site", "team", "routing" }) Assert.False(Done(s, k), k);
        foreach (var k in new[] { "site", "calendar", "team", "catalog", "sla", "routing" }) Assert.True(Step(s, k).GetProperty("required").GetBoolean(), k);
        foreach (var k in new[] { "approvals", "scanner", "test" }) Assert.False(Step(s, k).GetProperty("required").GetBoolean(), k);                  // advice, not a blocker
        Assert.True(Done(s, "scanner"));                                                                                                            // the test host registers a stand-in scanner; the no-op one reports false (see ClamAvScannerTests)
        Assert.All(s.GetProperty("steps").EnumerateArray(), x => Assert.StartsWith("/", x.GetProperty("link").GetString()));
    }

    [Fact]
    public async Task Only_people_who_configure_the_helpdesk_see_the_checklist_and_the_quick_start_needs_all_four_setup_permissions()
    {
        foreach (var role in new[] { "user", "ga_agent", "ga_supervisor", "security_auditor", "user_admin" })
        {
            var u = await _seed.UserAsync(role: role); var b = TestBrowser.Create(fx.Factory); await b.LoginAsync(u.Email, Seed.Password);
            Assert.Equal(HttpStatusCode.Forbidden, (await b.Get("/api/setup/status")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await b.Post("/api/setup/quick-start", new { siteName = "HQ", teamName = "GA", memberIds = new[] { u.Id } })).StatusCode);
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await TestBrowser.Create(fx.Factory).Get("/api/setup/status")).StatusCode);
        var m = await _seed.UserAsync(role: "ga_manager"); var mb = TestBrowser.Create(fx.Factory); await mb.LoginAsync(m.Email, Seed.Password);
        Assert.Equal(HttpStatusCode.OK, (await mb.Get("/api/setup/status")).StatusCode);                                                           // the GA manager runs the helpdesk

        // someone with only some of the four permissions is refused, not half-served
        var partial = await _seed.UserAsync(role: "user");
        await _seed.Db(async db =>
        {
            var role = new AnnabaDigital.Domain.Entities.Role { Key = "partial_" + Uniq(), Name = "Partial " + Uniq(), IsSystem = false, CreatedAt = fx.Clock.UtcNow, UpdatedAt = fx.Clock.UtcNow };
            foreach (var p in new[] { Permissions.LocationsManage, Permissions.TeamsManage, Permissions.SlaManage }) role.Permissions.Add(new() { PermissionKey = p, GrantedAt = fx.Clock.UtcNow });
            db.Roles.Add(role); db.UserRoles.Add(new() { UserId = partial.Id, RoleId = role.Id, AssignedAt = fx.Clock.UtcNow }); await db.SaveChangesAsync(); return 0;
        });
        var pb = TestBrowser.Create(fx.Factory); await pb.LoginAsync(partial.Email, Seed.Password);
        Assert.Equal(HttpStatusCode.OK, (await pb.Get("/api/setup/status")).StatusCode);
        var denied = await pb.Post("/api/setup/quick-start", new { siteName = "HQ", teamName = "GA", memberIds = new[] { partial.Id } });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode); Assert.Contains("rules.manage", (await denied.Body()).GetProperty("error").GetProperty("message").GetString());
    }

    // ============================================================ the quick start
    [Fact]
    public async Task The_quick_start_names_what_is_missing()
    {
        var (admin, id) = await FreshOrganisation();
        var r = await admin.Post("/api/setup/quick-start", new { });
        Assert.Equal(Invalid, r.StatusCode);
        var f = (await r.Body()).GetProperty("error").GetProperty("fields");
        Assert.True(f.TryGetProperty("siteName", out _)); Assert.True(f.TryGetProperty("teamName", out _)); Assert.True(f.TryGetProperty("memberIds", out _));
        var lead = await admin.Post("/api/setup/quick-start", new { siteName = "HQ", teamName = "GA", memberIds = new[] { id }, leadId = Guid.NewGuid() });
        Assert.Equal(Invalid, lead.StatusCode); Assert.True((await lead.Body()).GetProperty("error").GetProperty("fields").TryGetProperty("leadId", out _));
        Assert.Equal(Invalid, (await admin.Post("/api/setup/quick-start", new { siteName = "HQ", teamName = "GA", memberIds = new[] { Guid.NewGuid() } })).StatusCode);   // not a person of this organisation
        Assert.False((await Status(admin)).GetProperty("ready").GetBoolean());                                                                      // a refusal leaves nothing half-made
        Assert.Empty(Rows(await Get(admin, "/api/locations")).EnumerateArray());
    }

    [Fact]
    public async Task The_quick_start_makes_a_working_helpdesk_with_defaults_and_can_be_run_again_without_duplicates()
    {
        var (admin, id) = await FreshOrganisation();
        var r = await admin.Post("/api/setup/quick-start", new { siteName = "Head Office Jakarta", teamName = "General Affairs", memberIds = new[] { id }, leadId = id });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode); var body = await r.Body();
        Assert.Equal(["site", "team", "routing"], Strings(body.GetProperty("created"))); Assert.Equal(["sla"], Strings(body.GetProperty("skipped")));       // service levels already came with the organisation
        Assert.True(body.GetProperty("status").GetProperty("ready").GetBoolean()); Assert.Equal(6, body.GetProperty("status").GetProperty("done").GetInt32());

        var places = Rows(await Get(admin, "/api/locations")).EnumerateArray().ToList();
        Assert.Single(places); Assert.Equal("HEADOFFICEJAKARTA"[..12], places[0].GetProperty("code").GetString()); Assert.Equal("site", places[0].GetProperty("type").GetString());
        var team = Rows(await Get(admin, "/api/teams")).EnumerateArray().Single(); Assert.Equal("General Affairs", team.GetProperty("name").GetString()); Assert.Equal(1, team.GetProperty("members").GetInt32());
        var detail = await Get(admin, $"/api/teams/{team.GetProperty("id").GetGuid()}"); Assert.True(detail.GetProperty("members")[0].GetProperty("isLead").GetBoolean());
        var policies = Rows(await Get(admin, "/api/sla/policies")).EnumerateArray().ToList();
        Assert.Equal(["urgent", "high", "medium", "low"], policies.Select(p => p.GetProperty("priority").GetString()!).ToArray());
        Assert.All(policies, p => Assert.True(p.GetProperty("resolutionMinutes").GetInt32() > p.GetProperty("firstResponseMinutes").GetInt32()));
        var rule = Rows(await Get(admin, "/api/assignment-rules")).EnumerateArray().Single(); Assert.Equal("leastloaded", rule.GetProperty("strategy").GetString()); Assert.Equal(team.GetProperty("id").GetGuid(), rule.GetProperty("teamId").GetGuid());

        // a new request would now get a service level and land with the team
        var svc = (await Get(admin, "/api/portal/catalog")).GetProperty("services")[0].GetProperty("id").GetGuid();
        var sla = await (await admin.Post("/api/sla/preview", new { serviceId = svc, priority = "medium" })).Body(); Assert.Equal("Standard requests", sla.GetProperty("policyName").GetString());
        var route = await (await admin.Post("/api/assignment-rules/preview", new { serviceId = svc, priority = "medium" })).Body(); Assert.Equal("General Affairs", route.GetProperty("teamName").GetString());

        // everything is audited under the person who ran it
        var actions = await _seed.Db(db => db.AuditEvents.IgnoreQueryFilters().Where(a => a.ActorId == id).Select(a => a.Action).ToListAsync());
        foreach (var a in new[] { "location.create", "team.create", "team.members", "rule.create" }) Assert.Contains(a, actions);

        // again: nothing is created twice
        var again = await (await admin.Post("/api/setup/quick-start", new { siteName = "Another", teamName = "Another team", memberIds = new[] { id } })).Body();
        Assert.Empty(again.GetProperty("created").EnumerateArray()); Assert.Equal(["site", "team", "sla", "routing"], Strings(again.GetProperty("skipped")));
        Assert.Single(Rows(await Get(admin, "/api/locations")).EnumerateArray()); Assert.Single(Rows(await Get(admin, "/api/teams")).EnumerateArray());
        Assert.Equal(4, Rows(await Get(admin, "/api/sla/policies")).GetArrayLength()); Assert.Single(Rows(await Get(admin, "/api/assignment-rules")).EnumerateArray());
    }

    [Fact]
    public async Task The_quick_start_keeps_what_the_organisation_already_has_and_routes_to_its_staffed_team()
    {
        var (admin, id) = await FreshOrganisation();
        var team = (await (await admin.Post("/api/teams", new { name = "Facilities crew" })).Body()).GetProperty("id").GetGuid();
        var empty = await admin.Post("/api/setup/quick-start", new { siteName = "HQ" });                                                              // the team exists but nobody is in it yet
        Assert.Equal(Invalid, empty.StatusCode); Assert.True((await empty.Body()).GetProperty("error").GetProperty("fields").TryGetProperty("memberIds", out _));

        var filled = await (await admin.Post("/api/setup/quick-start", new { siteName = "HQ", memberIds = new[] { id } })).Body();                  // people are added to the existing team, no second team appears
        Assert.Equal(["site", "team", "routing"], Strings(filled.GetProperty("created")));
        Assert.Equal("Facilities crew", Rows(await Get(admin, "/api/teams")).EnumerateArray().Single().GetProperty("name").GetString());
        Assert.Equal(team, Rows(await Get(admin, "/api/assignment-rules")).EnumerateArray().Single().GetProperty("teamId").GetGuid());

        // an organisation that archived every starter policy gets the defaults again; one that still has policies keeps them untouched
        var (other, oid) = await FreshOrganisation();
        foreach (var p in Rows(await Get(other, "/api/sla/policies")).EnumerateArray().ToList()) Assert.Equal(HttpStatusCode.OK, (await other.Post($"/api/sla/policies/{p.GetProperty("id").GetGuid()}/archive")).StatusCode);
        Assert.Empty(Rows(await Get(other, "/api/sla/policies")).EnumerateArray());
        var r = await (await other.Post("/api/setup/quick-start", new { siteName = "HQ", teamName = "GA", memberIds = new[] { oid } })).Body();
        Assert.Equal(["site", "team", "sla", "routing"], Strings(r.GetProperty("created")));
        Assert.Equal(["Urgent requests", "High priority requests", "Standard requests", "Low priority requests"], Rows(await Get(other, "/api/sla/policies")).EnumerateArray().Select(p => p.GetProperty("name").GetString()!).ToArray());
        Assert.Equal(4, Rows(await Get(admin, "/api/sla/policies")).GetArrayLength());                                                           // the first organisation is untouched by the second
    }

    // ============================================================ the roles that come with each organisation
    [Fact]
    public void System_roles_are_coherent_the_manager_runs_the_helpdesk_and_nobody_but_the_super_admin_holds_everything()
    {
        var catalog = Permissions.All.ToHashSet();
        var roles = SystemRoles.Seed.ToDictionary(r => r.Key, r => r.Permissions.ToHashSet());
        Assert.All(roles, kv => Assert.True(kv.Value.IsSubsetOf(catalog), $"{kv.Key} holds a permission that does not exist"));
        Assert.Equal(catalog, roles["super_admin"]);
        Assert.Empty(roles["user"]);
        Assert.Equal(1, roles.Count(kv => kv.Value.SetEquals(catalog)));

        string[] configure = [Permissions.LocationsManage, Permissions.CalendarsManage, Permissions.TeamsManage, Permissions.CatalogManage, Permissions.FormsManage, Permissions.SlaManage, Permissions.RulesManage, Permissions.ApprovalsManage];
        Assert.True(roles["ga_manager"].IsSupersetOf(configure));
        foreach (var m in configure.Where(p => p.EndsWith(".manage") && catalog.Contains(p[..^6] + "read"))) Assert.Contains(m[..^6] + "read", roles["ga_manager"]);                      // whoever can change a screen can also open it
        Assert.True(roles["ga_manager"].IsSupersetOf(roles["ga_supervisor"])); Assert.True(roles["ga_supervisor"].IsSupersetOf(roles["ga_agent"]));
        Assert.DoesNotContain(roles["ga_manager"], p => p.StartsWith("users.") || p.StartsWith("roles.") || p.StartsWith("invitations.") || p == Permissions.AuditRead);        // running the helpdesk is not running the identity system
        foreach (var r in new[] { "ga_agent", "ga_supervisor" }) Assert.Empty(roles[r].Intersect(configure));                                         // agents and supervisors work tickets; they do not change the setup
        Assert.Contains(Permissions.TicketsReadAll, roles["ga_supervisor"]); Assert.DoesNotContain(Permissions.TicketsReadAll, roles["ga_agent"]);
        Assert.DoesNotContain(roles["user_admin"], p => p.StartsWith("tickets.") || p.StartsWith("sla.") || p.StartsWith("rules."));                 // the user administrator cannot read other people's tickets
        Assert.DoesNotContain(roles["security_auditor"], p => p.EndsWith(".manage") || p.EndsWith(".update") || p.EndsWith(".create"));              // the auditor only reads
    }
}

/// <summary>The ClamAV adapter speaks clamd's INSTREAM protocol and fails closed. A small fake daemon stands in for clamd.</summary>
public sealed class ClamAvScannerTests
{
    private sealed class FakeClamd : IDisposable
    {
        private readonly TcpListener _l = new(IPAddress.Loopback, 0); public byte[] Received = []; public string Command = "";
        private readonly Func<byte[], string> _reply; private readonly Task _loop;
        public FakeClamd(Func<byte[], string> reply) { _reply = reply; _l.Start(); _loop = Task.Run(Serve); }
        public int Port => ((IPEndPoint)_l.LocalEndpoint).Port;
        private async Task Serve()
        {
            try
            {
                using var c = await _l.AcceptTcpClientAsync(); await using var s = c.GetStream();
                var cmd = new List<byte>(); var one = new byte[1];
                while (await s.ReadAsync(one) == 1) { if (one[0] == 0) break; cmd.Add(one[0]); }
                Command = Encoding.ASCII.GetString([.. cmd]);
                var data = new MemoryStream(); var len = new byte[4];
                while (true)
                {
                    await s.ReadExactlyAsync(len); var n = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(len); if (n == 0) break;
                    var chunk = new byte[n]; await s.ReadExactlyAsync(chunk); data.Write(chunk);
                }
                Received = data.ToArray();
                await s.WriteAsync(Encoding.ASCII.GetBytes(_reply(Received) + "\0"));
            }
            catch (ObjectDisposedException) { } catch (SocketException) { } catch (InvalidOperationException) { }
        }
        public void Dispose() { _l.Stop(); }
    }

    private static ClamAvAttachmentScanner Scanner(int port, int timeout = 5) => new(Options.Create(new StorageOptions { ClamAvHost = "127.0.0.1", ClamAvPort = port, ClamAvTimeoutSeconds = timeout }), NullLogger<ClamAvAttachmentScanner>.Instance);

    [Fact]
    public async Task A_clean_file_is_streamed_in_full_and_passes_while_an_infected_one_is_refused_with_the_signature_name()
    {
        var big = new byte[200_000]; Random.Shared.NextBytes(big);                                                                                  // several chunks
        using (var d = new FakeClamd(_ => "stream: OK"))
        {
            var r = await Scanner(d.Port).ScanAsync(new MemoryStream(big), "a.pdf");
            Assert.True(r.Clean); Assert.Equal("zINSTREAM", d.Command); Assert.Equal(big, d.Received);
        }
        using (var d = new FakeClamd(_ => "stream: Eicar-Test-Signature FOUND"))
        {
            var r = await Scanner(d.Port).ScanAsync(new MemoryStream(Encoding.ASCII.GetBytes("X5O!P%@AP")), "virus.txt");
            Assert.False(r.Clean); Assert.Contains("Eicar-Test-Signature", r.Reason);
        }
    }

    [Fact]
    public async Task The_scanner_fails_closed_when_the_daemon_is_down_slow_or_confused()
    {
        var free = new TcpListener(IPAddress.Loopback, 0); free.Start(); var port = ((IPEndPoint)free.LocalEndpoint).Port; free.Stop();
        var down = await Scanner(port).ScanAsync(new MemoryStream([1, 2, 3]), "a.txt");
        Assert.False(down.Clean); Assert.Equal("scanner unavailable", down.Reason);

        using (var d = new FakeClamd(_ => "INSTREAM size limit exceeded. ERROR"))
        { var r = await Scanner(d.Port).ScanAsync(new MemoryStream([1, 2, 3]), "a.txt"); Assert.False(r.Clean); Assert.Equal("scanner error", r.Reason); }

        var silent = new TcpListener(IPAddress.Loopback, 0); silent.Start();
        var accept = silent.AcceptTcpClientAsync();                                                                                                  // accepts and never answers
        var slow = await Scanner(((IPEndPoint)silent.LocalEndpoint).Port, timeout: 1).ScanAsync(new MemoryStream([1, 2, 3]), "a.txt");
        Assert.False(slow.Clean); Assert.Equal("scanner unavailable", slow.Reason);
        silent.Stop(); _ = accept.ContinueWith(_ => { }, TaskContinuationOptions.OnlyOnFaulted);
    }

    [Fact]
    public void Only_the_no_op_scanner_reports_itself_as_inactive()
    {
        Assert.False(new NoopAttachmentScanner().Active);
        Assert.True(((IAttachmentScanner)Scanner(1)).Active);
    }
}
