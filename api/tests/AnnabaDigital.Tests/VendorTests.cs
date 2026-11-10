using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using AnnabaDigital.Application.Helpdesk;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Tests.Infra;
using Xunit;

namespace AnnabaDigital.Tests;

/// <summary>Vendors (VND-001/004/005, R2 slice 4): the directory, who may do what, places covered, contracts and documents, blocking, giving work orders to a vendor, the scorecard, and contract reminders.</summary>
[Collection("db")]
public sealed class VendorTests(PostgresFixture fx)
{
    private static string Uniq() => Guid.NewGuid().ToString("N")[..8];
    private const HttpStatusCode Invalid = (HttpStatusCode)422;
    private static async Task<JsonElement> Ok(HttpResponseMessage r) { Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync()); return await r.Body(); }
    private static Guid IdOf(JsonElement e) => e.GetProperty("id").GetGuid();
    private static string Str(JsonElement e, string p) => e.GetProperty(p).GetString()!;
    private static async Task<JsonElement> Fields(HttpResponseMessage r) { Assert.Equal(Invalid, r.StatusCode); return (await r.Body()).GetProperty("error").GetProperty("fields"); }
    private static byte[] Pdf() => Encoding.ASCII.GetBytes("%PDF-1.7\n1 0 obj\n<<>>\nendobj\n%%EOF");

    private sealed record World(ApiFactory F, TestClock Clock, Seed Seed, TestBrowser Manager, User ManagerUser, TestBrowser Supervisor, TestBrowser Agent, User AgentUser, TestBrowser Requester, Guid ServiceId);

    private async Task<World> Build(bool isolated = false)
    {
        ApiFactory f; TestClock clock;
        if (isolated) { var iso = await fx.NewIsolatedAsync(); f = iso.Factory; clock = iso.Clock; } else { f = fx.Factory; clock = fx.Clock; }
        var seed = new Seed(f, clock);
        async Task<(User, TestBrowser)> Person(string role) { var u = await seed.UserAsync(role: role); var b = TestBrowser.Create(f); await b.LoginAsync(u.Email, Seed.Password); return (u, b); }
        var (mu, mgr) = await Person("ga_manager"); var (_, sup) = await Person("ga_supervisor"); var (au, agent) = await Person("ga_agent"); var (_, req) = await Person("user"); var (_, admin) = await Person("super_admin");
        var team = IdOf(await Ok(await admin.Post("/api/teams", new { name = $"Team {Uniq()}" })));
        await Ok(await admin.SendAsync(HttpMethod.Put, $"/api/teams/{team}/members", new { members = new[] { new { userId = au.Id, isLead = false } } }));
        var cat = IdOf(await Ok(await admin.Post("/api/catalog/categories", new { name = $"Cat {Uniq()}" })));
        var svc = IdOf(await Ok(await admin.Post("/api/catalog/services", new { name = $"Svc {Uniq()}", categoryId = cat, owningTeamId = team })));
        await Ok(await admin.Post($"/api/catalog/services/{svc}/publish"));
        return new World(f, clock, seed, mgr, mu, sup, agent, au, req, svc);
    }

    private static async Task<JsonElement> NewVendor(World w, string name, object? extra = null)
    {
        var body = new Dictionary<string, object?> { ["name"] = name };
        if (extra is not null) foreach (var p in extra.GetType().GetProperties()) body[p.Name] = p.GetValue(extra);
        return await Ok(await w.Manager.Post("/api/vendors", body));
    }

    [Fact]
    public async Task Managers_keep_the_directory_staff_read_it_and_requesters_see_nothing()
    {
        var w = await Build();
        var site = IdOf(await Ok(await w.Manager.Post("/api/locations", new { type = "site", code = "V-" + Uniq(), name = "Vendor HQ" })));
        var floor = IdOf(await Ok(await w.Manager.Post("/api/locations", new { type = "floor", parentId = site, code = "VF-" + Uniq(), name = "Floor 1" })));
        var name = $"CoolCo {Uniq()}";
        var v = await NewVendor(w, name, new { services = "AC and refrigeration", email = "OFFICE@coolco.test", phone = "+62 21 555", website = "https://coolco.test", taxId = "01.234.567.8-901.000",
            contacts = new[] { new { name = "Budi", role = "Technician lead", phone = "+62 812 1", email = "budi@coolco.test" } }, locationIds = new[] { site } });
        Assert.Equal("office@coolco.test", Str(v, "email")); Assert.Equal("Budi", Str(v.GetProperty("contacts")[0], "name")); Assert.Equal("Vendor HQ", Str(v.GetProperty("sites")[0], "path"));

        var bad = await Fields(await w.Manager.Post("/api/vendors", new { name = name.ToUpperInvariant(), email = "nope", website = "ftp://x", contacts = new[] { new { name = "x", email = "bad" } } }));
        foreach (var k in new[] { "name", "email", "website", "contacts[0].name", "contacts[0].email" }) Assert.True(bad.TryGetProperty(k, out _), k);

        foreach (var b in new[] { w.Supervisor, w.Agent }) Assert.Equal(HttpStatusCode.OK, (await b.Get($"/api/vendors/{IdOf(v)}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Supervisor.Post("/api/vendors", new { name = "Not allowed" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Requester.Get("/api/vendors")).StatusCode);
        Assert.False((await Ok(await w.Agent.Get($"/api/vendors/{IdOf(v)}"))).GetProperty("canManage").GetBoolean());

        // places covered include what is below them
        async Task<string[]> Names(string q) => [.. (await Ok(await w.Agent.Get("/api/vendors?" + q))).GetProperty("data").EnumerateArray().Select(x => Str(x, "name"))];
        Assert.Contains(name, await Names($"locationId={floor}"));
        Assert.Contains(name, await Names("q=refrigeration"));
        Assert.DoesNotContain(name, await Names($"locationId={IdOf(await Ok(await w.Manager.Post("/api/locations", new { type = "site", code = "VO-" + Uniq(), name = "Elsewhere" })))}"));

        var cur = await Ok(await w.Manager.Get($"/api/vendors/{IdOf(v)}"));
        var upd = await Ok(await w.Manager.SendAsync(HttpMethod.Put, $"/api/vendors/{IdOf(v)}", new { name, services = "AC only", contacts = Array.Empty<object>(), locationIds = Array.Empty<Guid>(), version = cur.GetProperty("version").GetInt32() }));
        Assert.Empty(upd.GetProperty("contacts").EnumerateArray()); Assert.Empty(upd.GetProperty("sites").EnumerateArray());
        Assert.Equal(HttpStatusCode.Conflict, (await w.Manager.SendAsync(HttpMethod.Put, $"/api/vendors/{IdOf(v)}", new { name, version = cur.GetProperty("version").GetInt32() })).StatusCode);
    }

    [Fact]
    public async Task Contracts_and_documents_are_kept_with_the_vendor_and_documents_are_checked_like_ticket_files()
    {
        var w = await Build();
        var v = IdOf(await NewVendor(w, $"CleanCo {Uniq()}"));
        var bad = await Fields(await w.Manager.Post($"/api/vendors/{v}/contracts", new { title = "x", startsOn = "2026-05-01", endsOn = "2026-04-01", value = -1 }));
        foreach (var k in new[] { "title", "endsOn", "value" }) Assert.True(bad.TryGetProperty(k, out _), k);
        var withContract = await Ok(await w.Manager.Post($"/api/vendors/{v}/contracts", new { title = "Cleaning 2026", reference = "CTR-001", startsOn = "2026-01-01", endsOn = "2026-12-31", value = 120000000 }));
        var contract = withContract.GetProperty("contracts")[0];
        Assert.Equal("active", Str(contract, "state")); Assert.Equal(120000000m, contract.GetProperty("value").GetDecimal());
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Agent.Post($"/api/vendors/{v}/contracts", new { title = "Agent", startsOn = "2026-01-01" })).StatusCode);

        var doc = await Ok(await w.Manager.UploadAsync($"/api/vendors/{v}/documents?contractId={IdOf(contract)}", "signed contract.pdf", Pdf(), "application/pdf"));
        var d = doc.GetProperty("documents")[0];
        Assert.Equal("signed contract.pdf", Str(d, "fileName")); Assert.Equal(IdOf(contract), d.GetProperty("contractId").GetGuid());
        var dl = await w.Agent.Get($"/api/vendors/{v}/documents/{IdOf(d)}");
        Assert.Equal(HttpStatusCode.OK, dl.StatusCode); Assert.Equal(Pdf(), await dl.Content.ReadAsByteArrayAsync()); Assert.Contains("sandbox", dl.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal(Invalid, (await w.Manager.UploadAsync($"/api/vendors/{v}/documents", "run.exe", Pdf())).StatusCode);                       // the same allow-list
        Assert.Equal(Invalid, (await w.Manager.UploadAsync($"/api/vendors/{v}/documents", "notes.txt", Encoding.ASCII.GetBytes("x " + MarkerScanner.Marker))).StatusCode);   // and the scan

        var gone = await Ok(await w.Manager.SendAsync(HttpMethod.Delete, $"/api/vendors/{v}/documents/{IdOf(d)}"));
        Assert.Empty(gone.GetProperty("documents").EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await w.Agent.Get($"/api/vendors/{v}/documents/{IdOf(d)}")).StatusCode);
        Assert.Empty((await Ok(await w.Manager.SendAsync(HttpMethod.Delete, $"/api/vendors/{v}/contracts/{IdOf(contract)}"))).GetProperty("contracts").EnumerateArray());
    }

    [Fact]
    public async Task Work_orders_go_to_vendors_that_are_not_blocked_and_the_scorecard_counts_their_work()
    {
        var w = await Build();
        var vendor = await NewVendor(w, $"FixIt {Uniq()}", new { phone = "+62 21 777" }); var vid = IdOf(vendor);
        var t = IdOf(await Ok(await w.Requester.Post($"/api/portal/services/{w.ServiceId}/requests", new { subject = "Broken pump", answers = new { } })));
        var due = w.Clock.UtcNow.AddDays(2);
        var wo = await Ok(await w.Supervisor.Post($"/api/tickets/{t}/work-orders", new { title = "Replace the pump", vendorId = vid, purchaseOrder = "PO-2026-0042", assigneeId = w.AgentUser.Id, dueAt = due }));
        Assert.Equal(Str(vendor, "name"), Str(wo, "vendorName")); Assert.Equal("+62 21 777", Str(wo, "vendorPhone")); Assert.Equal("PO-2026-0042", Str(wo, "purchaseOrder"));
        Assert.Equal(Str(vendor, "name"), Str((await Ok(await w.Agent.Get($"/api/tickets/{t}/work-orders"))).GetProperty("items")[0], "vendorName"));
        Assert.True((await Fields(await w.Supervisor.Post($"/api/tickets/{t}/work-orders", new { title = "Too long PO", purchaseOrder = new string('x', 51) }))).TryGetProperty("purchaseOrder", out _));

        await Ok(await w.Supervisor.Post($"/api/work-orders/{IdOf(wo)}/costs", new { kind = "part", description = "Pump", quantity = 1, unitCost = 2500000 }));
        await Ok(await w.Agent.Post($"/api/work-orders/{IdOf(wo)}/action", new { action = "start" }));
        await Ok(await w.Agent.Post($"/api/work-orders/{IdOf(wo)}/action", new { action = "complete", note = "Pump replaced by FixIt" }));
        var other = IdOf(await Ok(await w.Supervisor.Post($"/api/tickets/{t}/work-orders", new { title = "Check the pressure", vendorId = vid })));

        var score = (await Ok(await w.Manager.Get($"/api/vendors/{vid}"))).GetProperty("score");
        Assert.Equal(2, score.GetProperty("workOrders").GetInt32()); Assert.Equal(1, score.GetProperty("done").GetInt32()); Assert.Equal(1, score.GetProperty("open").GetInt32());
        Assert.Equal(1, score.GetProperty("onTime").GetInt32()); Assert.Equal(0, score.GetProperty("late").GetInt32());
        Assert.Equal(2500000m, score.GetProperty("cost").GetDecimal());
        Assert.Equal(JsonValueKind.Null, (await Ok(await w.Agent.Get($"/api/vendors/{vid}"))).GetProperty("score").GetProperty("cost").ValueKind);   // agents never see money
        var supView = await Ok(await w.Supervisor.Get($"/api/vendors/{vid}"));
        Assert.Equal(2500000m, supView.GetProperty("score").GetProperty("cost").GetDecimal());
        Assert.Equal(2, supView.GetProperty("workOrders").GetArrayLength());
        Assert.Equal(1, (await Ok(await w.Manager.Get($"/api/vendors?q={Uri.EscapeDataString(Str(vendor, "name"))}"))).GetProperty("data")[0].GetProperty("openWorkOrders").GetInt32());

        // blocked: no new work, existing work stays
        Assert.True((await Fields(await w.Manager.Post($"/api/vendors/{vid}/block", new { reason = "x" }))).TryGetProperty("reason", out _));
        var blocked = await Ok(await w.Manager.Post($"/api/vendors/{vid}/block", new { reason = "Missed three visits" }));
        Assert.True(blocked.GetProperty("blocked").GetBoolean());
        Assert.True((await Fields(await w.Supervisor.Post($"/api/tickets/{t}/work-orders", new { title = "Another job", vendorId = vid }))).TryGetProperty("vendorId", out _));
        var cur = await Ok(await w.Supervisor.Get($"/api/work-orders/{other}"));
        Assert.Equal(HttpStatusCode.OK, (await w.Supervisor.SendAsync(HttpMethod.Put, $"/api/work-orders/{other}", new { title = "Check the pressure again", vendorId = vid, required = true, version = cur.GetProperty("version").GetInt32() })).StatusCode);   // keeping it is allowed
        Assert.DoesNotContain((await Ok(await w.Supervisor.Get("/api/vendors/pick"))).EnumerateArray(), x => IdOf(x) == vid);
        Assert.Contains((await Ok(await w.Manager.Get("/api/vendors?status=blocked"))).GetProperty("data").EnumerateArray(), x => IdOf(x) == vid);
        Assert.False((await Ok(await w.Manager.Post($"/api/vendors/{vid}/unblock"))).GetProperty("blocked").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Agent.Get("/api/vendors/pick")).StatusCode);
    }

    [Fact]
    public async Task Contract_reminders_go_to_vendor_managers_once_per_end_date()
    {
        var w = await Build(isolated: true);
        var today = DateOnly.FromDateTime(w.Clock.UtcNow.UtcDateTime);
        string D(int days) => today.AddDays(days).ToString("yyyy-MM-dd");
        var v = IdOf(await NewVendor(w, "SecureCo"));
        await Ok(await w.Manager.Post($"/api/vendors/{v}/contracts", new { title = "Guards", startsOn = D(-300), endsOn = D(20) }));
        await Ok(await w.Manager.Post($"/api/vendors/{v}/contracts", new { title = "Old", startsOn = D(-900), endsOn = D(-200) }));
        await Ok(await w.Manager.Post($"/api/vendors/{v}/contracts", new { title = "Open-ended", startsOn = D(-10) }));
        async Task<int> Run() { using var s = w.F.Services.CreateScope(); return await s.ServiceProvider.GetRequiredService<VendorService>().RunContractRemindersAsync(); }
        Assert.Equal(1, await Run());
        var titles = await w.Seed.Db(db => db.Notifications.Where(n => n.UserId == w.ManagerUser.Id && n.Kind == "vendor.contract").Select(n => n.Title).ToListAsync());
        Assert.Equal([$"Contract with SecureCo ends on {D(20)}"], titles);
        await w.Seed.Mail.WaitFor(w.ManagerUser.Email, "Contract with SecureCo");
        Assert.Equal(0, await Run());
        var contracts = (await Ok(await w.Manager.Get($"/api/vendors/{v}"))).GetProperty("contracts").EnumerateArray().ToList();
        Assert.Equal("ended", Str(contracts.Single(c => Str(c, "title") == "Old"), "state"));
    }
}
