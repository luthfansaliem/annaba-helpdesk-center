using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using AnnabaDigital.Application.Helpdesk;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Tests.Infra;
using Xunit;

namespace AnnabaDigital.Tests;

/// <summary>Visitors (R3-4: VIS-001/002/005, RPT-005): pre-registration by employees, reception check-in and check-out with badge, ID digits and acknowledgement,
/// the block list, hosts told when their visitor arrives, no-shows, blanking personal details after the retention period, and the visitor summary.</summary>
[Collection("db")]
public sealed class VisitTests(PostgresFixture fx)
{
    private const HttpStatusCode Invalid = (HttpStatusCode)422;
    private static async Task<JsonElement> Ok(HttpResponseMessage r) { Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync()); return await r.Body(); }
    private static Guid IdOf(JsonElement e) => e.GetProperty("id").GetGuid();
    private static string Status(JsonElement e) => e.GetProperty("status").GetString()!;
    private static async Task<JsonElement> Fields(HttpResponseMessage r) { Assert.Equal(Invalid, r.StatusCode); return (await r.Body()).GetProperty("error").GetProperty("fields"); }

    private sealed record World(ApiFactory F, TestClock Clock, Seed Seed, TestBrowser Manager, TestBrowser Supervisor, TestBrowser Desk, User DeskUser, TestBrowser Requester, User RequesterUser, TestBrowser Other);
    private async Task<World> Build(bool isolated = false)
    {
        ApiFactory f; TestClock clock;
        if (isolated) { var iso = await fx.NewIsolatedAsync(); f = iso.Factory; clock = iso.Clock; } else { f = fx.Factory; clock = fx.Clock; }
        var seed = new Seed(f, clock);
        async Task<(User, TestBrowser)> Person(string role) { var u = await seed.UserAsync(role: role); var b = TestBrowser.Create(f); await b.LoginAsync(u.Email, Seed.Password); return (u, b); }
        var (_, mgr) = await Person("ga_manager"); var (_, sup) = await Person("ga_supervisor"); var (du, desk) = await Person("receptionist"); var (ru, req) = await Person("user"); var (_, other) = await Person("user");
        return new World(f, clock, seed, mgr, sup, desk, du, req, ru, other);
    }
    private static object Visitor(TestClock c, string name, string? company = "PT Maju", int inHours = 1) => new
    {
        visitorName = name, company, email = "guest@example.test", phone = "+62 811 000", vehiclePlate = "b 1 abc", purpose = "Contract signing",
        expectedFrom = c.UtcNow.AddHours(inHours), expectedUntil = c.UtcNow.AddHours(inHours + 2),
    };

    [Fact]
    public async Task Employees_register_their_visitors_and_reception_checks_them_in_and_out()
    {
        var w = await Build();
        var name = "Budi " + Guid.NewGuid().ToString("N")[..6];
        var visit = await Ok(await w.Requester.Post("/api/visits", Visitor(w.Clock, name)));
        var id = IdOf(visit);
        Assert.Equal("expected", Status(visit)); Assert.Equal("B 1 ABC", visit.GetProperty("vehiclePlate").GetString());
        Assert.True(visit.GetProperty("canChange").GetBoolean()); Assert.False(visit.GetProperty("canReceive").GetBoolean());
        Assert.True((await Fields(await w.Requester.Post("/api/visits", new { visitorName = "", expectedFrom = w.Clock.UtcNow.AddHours(2), expectedUntil = w.Clock.UtcNow.AddHours(1), email = "nope" }))).EnumerateObject().Count() >= 3);
        Assert.True((await Fields(await w.Requester.Post("/api/visits", new { visitorName = "X", hostId = w.DeskUser.Id, expectedFrom = w.Clock.UtcNow.AddHours(1), expectedUntil = w.Clock.UtcNow.AddHours(2) }))).TryGetProperty("hostId", out _));

        // only the host, reception and read-all see it
        Assert.Equal(HttpStatusCode.NotFound, (await w.Other.Get($"/api/visits/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Requester.Get("/api/visits?scope=day")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Requester.Get("/api/visits?scope=all")).StatusCode);
        Assert.Contains((await Ok(await w.Desk.Get($"/api/visits?scope=day&q={Uri.EscapeDataString(name)}"))).GetProperty("data").EnumerateArray(), v => IdOf(v) == id);
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Requester.Post($"/api/visits/{id}/check-in", new { badgeNumber = "V-1" })).StatusCode);

        // the organisation asks for an acknowledgement and the last ID digits
        await Ok(await w.Manager.SendAsync(HttpMethod.Put, "/api/visitors/settings", new { acknowledgement = "Visitors keep their badge visible and are escorted.", askIdLast4 = true, retentionDays = 180 }));
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Desk.SendAsync(HttpMethod.Put, "/api/visitors/settings", new { retentionDays = 30 })).StatusCode);
        var f = await Fields(await w.Desk.Post($"/api/visits/{id}/check-in", new { }));
        foreach (var k in new[] { "badgeNumber", "idLast4", "acknowledged" }) Assert.True(f.TryGetProperty(k, out _), k);
        var badge = "V-" + Guid.NewGuid().ToString("N")[..5];
        var inside = await Ok(await w.Desk.Post($"/api/visits/{id}/check-in", new { badgeNumber = badge, idLast4 = "12ab", acknowledged = true }));
        Assert.Equal("checked_in", Status(inside)); Assert.Equal("12AB", inside.GetProperty("idLast4").GetString());
        Assert.NotEqual(JsonValueKind.Null, inside.GetProperty("acknowledgedAt").ValueKind);
        Assert.Equal(1, await w.Seed.Db(db => db.Notifications.IgnoreQueryFilters().CountAsync(n => n.UserId == w.RequesterUser.Id && n.Kind == "visitor.arrived" && n.Title.StartsWith(name))));
        Assert.Equal(JsonValueKind.Null, (await Ok(await w.Requester.Get($"/api/visits/{id}"))).GetProperty("idLast4").ValueKind);   // the host does not see the ID digits

        // the same badge cannot be with two visitors; a visitor on site cannot be changed or cancelled
        var second = IdOf(await Ok(await w.Desk.Post("/api/visits", new { visitorName = "Walk-in " + badge, hostId = w.RequesterUser.Id, expectedFrom = w.Clock.UtcNow, expectedUntil = w.Clock.UtcNow.AddHours(1) })));
        Assert.True((await Fields(await w.Desk.Post($"/api/visits/{second}/check-in", new { badgeNumber = badge, idLast4 = "9999", acknowledged = true }))).TryGetProperty("badgeNumber", out _));
        Assert.Equal(HttpStatusCode.Conflict, (await w.Requester.Post($"/api/visits/{id}/cancel")).StatusCode);
        Assert.Equal("checked_out", Status(await Ok(await w.Desk.Post($"/api/visits/{id}/check-out"))));
        Assert.Equal(HttpStatusCode.Conflict, (await w.Desk.Post($"/api/visits/{id}/check-out")).StatusCode);
        Assert.Equal("checked_in", Status(await Ok(await w.Desk.Post($"/api/visits/{second}/check-in", new { badgeNumber = badge, idLast4 = "9999", acknowledged = true }))));   // the badge is free again
        await Ok(await w.Manager.SendAsync(HttpMethod.Put, "/api/visitors/settings", new { acknowledgement = (string?)null, askIdLast4 = false, retentionDays = 180 }));
    }

    [Fact]
    public async Task The_block_list_flags_visitors_and_stops_check_in()
    {
        var w = await Build(isolated: true);
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Desk.Get("/api/visitors/blocks")).StatusCode);
        Assert.True((await Fields(await w.Manager.Post("/api/visitors/blocks", new { name = "Joe" }))).TryGetProperty("reason", out _));
        var early = IdOf(await Ok(await w.Requester.Post("/api/visits", Visitor(w.Clock, "Joe   Bloggs", "Acme"))));
        var blocks = await Ok(await w.Manager.Post("/api/visitors/blocks", new { name = "joe bloggs", reason = "Threatened staff in March" }));
        var entry = IdOf(blocks[0]);
        // the visit already expected is flagged; a new one is flagged at registration and managers are told
        Assert.StartsWith("On the block list", (await Ok(await w.Desk.Get($"/api/visits/{early}"))).GetProperty("flag").GetString());
        var later = await Ok(await w.Requester.Post("/api/visits", Visitor(w.Clock, "JOE BLOGGS", "Other Co")));
        Assert.Equal(JsonValueKind.Null, later.GetProperty("flag").ValueKind);                                   // the host is not shown why
        Assert.NotEqual(JsonValueKind.Null, (await Ok(await w.Desk.Get($"/api/visits/{IdOf(later)}"))).GetProperty("flag").ValueKind);
        Assert.True(await w.Seed.Db(db => db.Notifications.IgnoreQueryFilters().AnyAsync(n => n.Kind == "visitor.flagged")));
        var refused = await w.Desk.Post($"/api/visits/{early}/check-in", new { badgeNumber = "B-1" });
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("block list", (await refused.Body()).GetProperty("error").GetProperty("message").GetString());
        Assert.Equal(2, (await Ok(await w.Desk.Get("/api/visits/summary"))).GetProperty("flagged").GetInt32());   // both visits are flagged
        // an entry with a company only matches that company
        await Ok(await w.Manager.SendAsync(HttpMethod.Delete, $"/api/visitors/blocks/{entry}"));
        await Ok(await w.Manager.Post("/api/visitors/blocks", new { name = "Joe Bloggs", company = "acme", reason = "Unpaid damage" }));
        Assert.Equal(JsonValueKind.Null, (await Ok(await w.Desk.Get($"/api/visits/{IdOf(later)}"))).GetProperty("flag").ValueKind);
        Assert.Equal(HttpStatusCode.Conflict, (await w.Desk.Post($"/api/visits/{early}/check-in", new { badgeNumber = "B-1" })).StatusCode);
        Assert.Equal("checked_in", Status(await Ok(await w.Desk.Post($"/api/visits/{IdOf(later)}/check-in", new { badgeNumber = "B-2" }))));
    }

    [Fact]
    public async Task No_shows_are_marked_details_are_blanked_after_retention_and_the_summary_counts()
    {
        var w = await Build(isolated: true);
        await Ok(await w.Manager.SendAsync(HttpMethod.Put, "/api/visitors/settings", new { retentionDays = 7, askIdLast4 = false }));
        Assert.True((await Fields(await w.Manager.SendAsync(HttpMethod.Put, "/api/visitors/settings", new { retentionDays = 2 }))).TryGetProperty("retentionDays", out _));
        var came = IdOf(await Ok(await w.Requester.Post("/api/visits", Visitor(w.Clock, "Siti Rahma", "PT Sumber", 0))));
        var never = IdOf(await Ok(await w.Requester.Post("/api/visits", Visitor(w.Clock, "Andi No-show", null, 0))));
        await Ok(await w.Desk.Post($"/api/visits/{came}/check-in", new { badgeNumber = "G-7" }));
        var sum = await Ok(await w.Desk.Get("/api/visits/summary"));
        Assert.Equal(1, sum.GetProperty("onSiteNow").GetInt32()); Assert.Equal(1, sum.GetProperty("expectedToday").GetInt32());
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Requester.Get("/api/visits/summary")).StatusCode);
        await Ok(await w.Desk.Post($"/api/visits/{came}/check-out"));

        async Task<VisitService.RunResult> Run() { using var s = w.F.Services.CreateScope(); return await s.ServiceProvider.GetRequiredService<VisitService>().RunAsync(); }
        w.Clock.Advance(TimeSpan.FromHours(3));
        Assert.Equal(1, (await Run()).NoShows);
        var desk = TestBrowser.Create(w.F); await desk.LoginAsync(w.DeskUser.Email, Seed.Password);
        Assert.Equal("no_show", Status(await Ok(await desk.Get($"/api/visits/{never}"))));
        Assert.Equal(1, (await Ok(await desk.Get("/api/visits/summary"))).GetProperty("noShows30").GetInt32());
        Assert.Equal(0, (await Run()).Anonymised);                                                            // not yet past the 7 days

        w.Clock.Advance(TimeSpan.FromDays(8));
        Assert.Equal(2, (await Run()).Anonymised);
        desk = TestBrowser.Create(w.F); await desk.LoginAsync(w.DeskUser.Email, Seed.Password);
        var gone = await Ok(await desk.Get($"/api/visits/{came}"));
        Assert.Equal("Visitor", gone.GetProperty("visitorName").GetString()); Assert.True(gone.GetProperty("anonymised").GetBoolean());
        foreach (var k in new[] { "company", "email", "phone", "vehiclePlate", "badgeNumber", "purpose" }) Assert.Equal(JsonValueKind.Null, gone.GetProperty(k).ValueKind);
        Assert.Equal("checked_out", Status(gone));                                                            // the fact of the visit stays
        Assert.Equal(1, (await Ok(await desk.Get("/api/visits/summary"))).GetProperty("visitors30").GetInt32());
        Assert.Equal(0, (await Run()).Anonymised);
    }
}
