using System.Net;
using System.Text.Json;
using AnnabaDigital.Tests.Infra;
using Xunit;

namespace AnnabaDigital.Tests;

/// <summary>
/// Module analytics (docs/52): the summary above each module list. Each one is counted on an isolated organisation so the numbers are exact,
/// follows the module's own permission (requesters get 403), and work orders follow "the work orders I may see".
/// </summary>
[Collection("db")]
public sealed class InsightsTests(PostgresFixture fx)
{
    private static string Uniq() => Guid.NewGuid().ToString("N")[..8];
    private static async Task<JsonElement> Ok(HttpResponseMessage r) { Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync()); return await r.Body(); }
    private static Guid IdOf(JsonElement e) => e.GetProperty("id").GetGuid();
    private static int Int(JsonElement e, string p) => e.GetProperty(p).GetInt32();
    private static string D(DateOnly d) => d.ToString("yyyy-MM-dd");

    [Fact]
    public async Task Every_module_summary_counts_its_own_records_and_follows_the_module_permission()
    {
        var iso = await fx.NewIsolatedAsync(); var f = iso.Factory; var clock = iso.Clock; var seed = new Seed(f, clock);
        async Task<(Domain.Entities.User, TestBrowser)> Person(string role) { var u = await seed.UserAsync(role: role); var b = TestBrowser.Create(f); await b.LoginAsync(u.Email, Seed.Password); return (u, b); }
        var (_, admin) = await Person("super_admin"); var (supUser, sup) = await Person("ga_supervisor"); var (agentUser, agent) = await Person("ga_agent"); var (reqUser, req) = await Person("user");
        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);

        var team = IdOf(await Ok(await admin.Post("/api/teams", new { name = $"Team {Uniq()}" })));
        await Ok(await admin.SendAsync(HttpMethod.Put, $"/api/teams/{team}/members", new { members = new[] { new { userId = agentUser.Id, isLead = false } } }));
        var cat = IdOf(await Ok(await admin.Post("/api/catalog/categories", new { name = $"Cat {Uniq()}" })));
        var svc = IdOf(await Ok(await admin.Post("/api/catalog/services", new { name = $"Repairs {Uniq()}", categoryId = cat, owningTeamId = team })));
        await Ok(await admin.Post($"/api/catalog/services/{svc}/publish"));
        var site = IdOf(await Ok(await admin.Post("/api/locations", new { type = "site", code = "S-" + Uniq(), name = "Head office" })));
        var floor = IdOf(await Ok(await admin.Post("/api/locations", new { type = "floor", parentId = site, code = "F-" + Uniq(), name = "Floor 2" })));

        // ---------------------------------------------------------------- permissions: requesters see none of them
        foreach (var url in new[] { "/api/work-orders/summary", "/api/assets/summary", "/api/vendors/summary", "/api/maintenance/summary", "/api/kb/summary" })
            Assert.Equal(HttpStatusCode.Forbidden, (await req.Get(url)).StatusCode);

        // ---------------------------------------------------------------- assets: status, warranty, place, value, type, site
        var ac = IdOf(await Ok(await admin.Post("/api/asset-types", new { name = $"AC {Uniq()}" })));
        var car = IdOf(await Ok(await admin.Post("/api/asset-types", new { name = $"Car {Uniq()}" })));
        async Task<Guid> Asset(Guid type, string status, Guid? place, int? warrantyDays, decimal cost)
            => IdOf(await Ok(await admin.Post("/api/assets", new { name = $"Asset {Uniq()}", typeId = type, status, locationId = place, purchaseCost = cost, warrantyEnd = warrantyDays is { } w ? D(today.AddDays(w)) : null })));
        await Asset(ac, "in_use", floor, 10, 1000);      // warranty within 30 days
        await Asset(ac, "in_repair", floor, -5, 2000);   // warranty ended
        await Asset(ac, "in_use", null, 60, 3000);       // no place; warranty within 90
        var gone = await Asset(car, "stored", floor, null, 4000);
        await Ok(await admin.Post($"/api/assets/{gone}/retire", new { reason = "Sold" }));
        var a = await Ok(await agent.Get("/api/assets/summary"));
        Assert.Equal(3, Int(a, "active")); Assert.Equal(1, Int(a, "retired"));
        Assert.Equal(1, Int(a, "warranty30")); Assert.Equal(2, Int(a, "warranty90")); Assert.Equal(1, Int(a, "warrantyExpired")); Assert.Equal(1, Int(a, "unplaced"));
        Assert.Equal(6000m, a.GetProperty("value").GetDecimal());
        Assert.Equal(3, a.GetProperty("byType")[0].GetProperty("count").GetInt32());
        Assert.Equal("Head office", a.GetProperty("bySite")[0].GetProperty("label").GetString()); Assert.Equal(2, a.GetProperty("bySite")[0].GetProperty("count").GetInt32());   // the floor counts for its site
        Assert.Equal(1, a.GetProperty("byStatus").EnumerateArray().First(x => x.GetProperty("key").GetString() == "in_repair").GetProperty("count").GetInt32());

        // ---------------------------------------------------------------- vendors and work orders
        var v1 = IdOf(await Ok(await admin.Post("/api/vendors", new { name = $"CoolCo {Uniq()}" })));
        var v2 = IdOf(await Ok(await admin.Post("/api/vendors", new { name = $"Blocked {Uniq()}" })));
        await Ok(await admin.Post($"/api/vendors/{v2}/block", new { reason = "Unpaid invoices" }));
        await Ok(await admin.Post($"/api/vendors/{v1}/contracts", new { title = "Service", startsOn = D(today.AddDays(-30)), endsOn = D(today.AddDays(45)) }));
        await Ok(await admin.Post($"/api/vendors/{v1}/contracts", new { title = "Lease", startsOn = D(today.AddDays(-30)), endsOn = D(today.AddDays(400)) }));

        var t1 = IdOf(await Ok(await req.Post($"/api/portal/services/{svc}/requests", new { subject = "Broken AC", answers = new { } })));
        var t2 = IdOf(await Ok(await req.Post($"/api/portal/services/{svc}/requests", new { subject = "Leaking tap", answers = new { } })));
        var soon = clock.UtcNow.AddHours(2); var later = clock.UtcNow.AddDays(10);
        var w1 = IdOf(await Ok(await sup.Post($"/api/tickets/{t1}/work-orders", new { title = "Fix the AC", vendorId = v1, assigneeId = agentUser.Id, dueAt = soon, required = false })));
        var w2 = IdOf(await Ok(await sup.Post($"/api/tickets/{t1}/work-orders", new { title = "Clean the filter", vendorId = v1, assigneeId = agentUser.Id, dueAt = later, required = false })));
        await Ok(await sup.Post($"/api/tickets/{t2}/work-orders", new { title = "Replace the washer", assigneeId = agentUser.Id, dueAt = soon, required = false }));
        await Ok(await sup.Post($"/api/work-orders/{w1}/costs", new { kind = "invoice", description = "Invoice", quantity = 1, unitCost = 750000 }));
        await Ok(await agent.Post($"/api/work-orders/{w2}/action", new { action = "start" }));
        await Ok(await agent.Post($"/api/work-orders/{w2}/action", new { action = "complete", note = "Done early" }));   // on time
        clock.Advance(TimeSpan.FromHours(3));                                                                                   // w1 and the washer are now overdue
        // sessions idle out when the clock jumps: sign in again
        agent = TestBrowser.Create(f); await agent.LoginAsync(agentUser.Email, Seed.Password);
        sup = TestBrowser.Create(f); await sup.LoginAsync(supUser.Email, Seed.Password);
        req = TestBrowser.Create(f); await req.LoginAsync(reqUser.Email, Seed.Password);

        var s = await Ok(await agent.Get("/api/work-orders/summary"));
        Assert.Equal(2, Int(s, "open")); Assert.Equal(2, Int(s, "overdue")); Assert.Equal(1, Int(s, "done30")); Assert.Equal(1, Int(s, "onTime30")); Assert.Equal(0, Int(s, "late30"));
        Assert.Equal(1, Int(s, "inHouseOpen")); Assert.Equal(1, Int(s, "vendorOpen"));
        Assert.Equal(12, s.GetProperty("weekly").GetArrayLength()); Assert.Equal(3, s.GetProperty("weekly").EnumerateArray().Sum(x => x.GetProperty("created").GetInt32()));
        Assert.Equal(1, s.GetProperty("byVendor")[0].GetProperty("count").GetInt32());
        Assert.Equal(2, s.GetProperty("byStatus").EnumerateArray().First(x => x.GetProperty("key").GetString() == "open").GetProperty("count").GetInt32());

        var vs = await Ok(await sup.Get("/api/vendors/summary"));
        Assert.Equal(1, Int(vs, "active")); Assert.Equal(1, Int(vs, "blocked")); Assert.Equal(2, Int(vs, "contractsActive")); Assert.Equal(1, Int(vs, "contractsExpiring60"));
        Assert.Equal(1, Int(vs, "openWorkOrders")); Assert.Equal(1, Int(vs, "overdueWorkOrders"));
        Assert.Equal(750000m, vs.GetProperty("spend12Months").GetDecimal());
        var top = vs.GetProperty("top")[0]; Assert.Equal(v1, IdOf(top)); Assert.Equal(2, Int(top, "workOrders")); Assert.Equal(1, Int(top, "onTime"));
        // money only for people who may see work-order costs
        var va = await Ok(await agent.Get("/api/vendors/summary"));
        Assert.Equal(JsonValueKind.Null, va.GetProperty("spend12Months").ValueKind); Assert.Equal(JsonValueKind.Null, va.GetProperty("top")[0].GetProperty("spend").ValueKind);

        // ---------------------------------------------------------------- maintenance
        await Ok(await sup.Post("/api/maintenance/plans", new { name = $"AC service {Uniq()}", serviceId = svc, assetTypeId = ac, unit = "month", every = 1, startsOn = D(today.AddDays(5)), leadDays = 0 }));
        var paused = IdOf(await Ok(await sup.Post("/api/maintenance/plans", new { name = $"Generator {Uniq()}", serviceId = svc, locationId = floor, unit = "month", every = 6, startsOn = D(today.AddDays(60)), leadDays = 0 })));
        Assert.True((await sup.Post($"/api/maintenance/plans/{paused}/pause")).IsSuccessStatusCode);
        var m = await Ok(await agent.Get("/api/maintenance/summary"));
        Assert.Equal(1, Int(m, "active")); Assert.Equal(1, Int(m, "paused")); Assert.Equal(1, Int(m, "due7")); Assert.Equal(1, Int(m, "due30"));
        Assert.Equal(6, m.GetProperty("monthly").GetArrayLength()); Assert.Single(m.GetProperty("upcoming").EnumerateArray());

        // ---------------------------------------------------------------- knowledge base (for writers)
        var art = IdOf(await Ok(await agent.Post("/api/kb/articles", new { title = $"Book a room {Uniq()}", body = "Use New request.", visibility = "everyone" })));
        await Ok(await agent.Post("/api/kb/articles", new { title = $"Draft {Uniq()}", body = "Not yet.", visibility = "everyone" }));
        await Ok(await sup.Post($"/api/kb/articles/{art}/publish"));
        await Ok(await req.Get($"/api/kb/articles/{art}")); await Ok(await req.Get($"/api/kb/articles/{art}"));
        Assert.True((await req.Post($"/api/kb/articles/{art}/feedback", new { helpful = true })).IsSuccessStatusCode);
        Assert.True((await req.Post($"/api/kb/articles/{art}/solved")).IsSuccessStatusCode);
        var k = await Ok(await agent.Get("/api/kb/summary"));
        Assert.Equal(1, Int(k, "published")); Assert.Equal(1, Int(k, "drafts")); Assert.Equal(1, Int(k, "helpful")); Assert.Equal(1, Int(k, "deflections"));
        Assert.True(Int(k, "views") >= 2);
        Assert.Equal(art, IdOf(k.GetProperty("top")[0])); Assert.Equal(100, Int(k.GetProperty("top")[0], "helpfulPercent"));
    }
}
