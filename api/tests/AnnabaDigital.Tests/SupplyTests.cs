using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Tests.Infra;
using Xunit;

namespace AnnabaDigital.Tests;

/// <summary>Supply requests (R3-7: INV-002, INV-005): anyone asks for stock items; above the approval amount the requester's manager decides first; the desk issues
/// stock through the ledger, partly when short (the rest is a back-order) and closes what will not come; parts on a work order come out of stock at their cost.</summary>
[Collection("db")]
public sealed class SupplyTests(PostgresFixture fx)
{
    private const HttpStatusCode Invalid = (HttpStatusCode)422;
    private static async Task<JsonElement> Ok(HttpResponseMessage r) { Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync()); return await r.Body(); }
    private static Guid IdOf(JsonElement e) => e.GetProperty("id").GetGuid();
    private static async Task<JsonElement> Fields(HttpResponseMessage r) { Assert.Equal(Invalid, r.StatusCode); return (await r.Body()).GetProperty("error").GetProperty("fields"); }
    private static JsonElement Line(JsonElement r, Guid item) => r.GetProperty("lines").EnumerateArray().Single(l => l.GetProperty("itemId").GetGuid() == item);

    [Fact]
    public async Task Requests_wait_for_the_manager_above_the_limit_and_are_issued_from_stock_with_back_orders()
    {
        var iso = await fx.NewIsolatedAsync(); var f = iso.Factory; var clock = iso.Clock; var seed = new Seed(f, clock);
        async Task<(User, TestBrowser)> Person(string role) { var u = await seed.UserAsync(role: role); var b = TestBrowser.Create(f); await b.LoginAsync(u.Email, Seed.Password); return (u, b); }
        var (gu, ga) = await Person("ga_manager"); var (_, agent) = await Person("ga_agent"); var (ru, req) = await Person("user"); var (bu, boss) = await Person("user"); var (_, other) = await Person("user");
        var (_, admin) = await Person("super_admin");
        var store = IdOf(await Ok(await admin.Post("/api/locations", new { type = "site", code = "ST-" + Guid.NewGuid().ToString("N")[..4], name = "Store room" })));
        var paper = IdOf(await Ok(await ga.Post("/api/inventory/items", new { sku = "ATK-A4", name = "HVS paper A4", category = "atk", unit = "ream", reorderLevel = 2, unitCost = 50000 })));
        var pen = IdOf(await Ok(await ga.Post("/api/inventory/items", new { sku = "ATK-PEN", name = "Pen black", category = "atk", unit = "pcs", reorderLevel = 0, unitCost = 3000 })));
        await Ok(await ga.Post($"/api/inventory/items/{paper}/moves", new { kind = "receipt", locationId = store, quantity = 6 }));
        await Ok(await ga.Post($"/api/inventory/items/{pen}/moves", new { kind = "receipt", locationId = store, quantity = 100 }));

        // settings: GA Manager sets the default place and the approval amount; a requester cannot
        Assert.Equal(HttpStatusCode.Forbidden, (await req.SendAsync(HttpMethod.Put, "/api/supplies/settings", new { approvalAbove = 1 })).StatusCode);
        await Ok(await ga.SendAsync(HttpMethod.Put, "/api/supplies/settings", new { approvalAbove = 300000, defaultLocationId = store }));
        Assert.Equal(HttpStatusCode.OK, (await admin.SendAsync(HttpMethod.Put, $"/api/users/{ru.Id}/manager", new { managerId = bu.Id })).StatusCode);

        // anyone signed in sees the catalogue with what is available at the default place, and asks without any permission
        var cat = await Ok(await req.Get("/api/supplies/catalog"));
        Assert.Equal(6, cat.EnumerateArray().Single(i => IdOf(i) == paper).GetProperty("available").GetInt32());
        var fe = await Fields(await req.Post("/api/supplies/requests", new { lines = Array.Empty<object>() }));
        Assert.True(fe.TryGetProperty("lines", out _));
        Assert.True((await Fields(await req.Post("/api/supplies/requests", new { lines = new[] { new { itemId = pen, quantity = 0 } } }))).TryGetProperty("lines", out _));

        // small request: open at once, the desk is told
        var small = await Ok(await req.Post("/api/supplies/requests", new { note = "For the new hire", lines = new[] { new { itemId = pen, quantity = 5 } } }));
        Assert.Equal("open", small.GetProperty("status").GetString());
        Assert.StartsWith("SR-", small.GetProperty("number").GetString());
        Assert.Equal(15000m, small.GetProperty("estimatedCost").GetDecimal());
        Assert.True(await seed.Db(db => db.Notifications.IgnoreQueryFilters().AnyAsync(n => n.UserId == gu.Id && n.Kind == "supply.new")));

        // big request (8 reams = 400 000 ≥ 300 000): waits for the requester's manager; others cannot see or decide it
        var big = await Ok(await req.Post("/api/supplies/requests", new { lines = new[] { new { itemId = paper, quantity = 8 }, new { itemId = pen, quantity = 10 } } }));
        var bigId = IdOf(big);
        Assert.Equal("pending", big.GetProperty("status").GetString());
        Assert.True(await seed.Db(db => db.Notifications.IgnoreQueryFilters().AnyAsync(n => n.UserId == bu.Id && n.Kind == "supply.approval")));
        Assert.Equal(HttpStatusCode.NotFound, (await other.Get($"/api/supplies/requests/{bigId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Post($"/api/supplies/requests/{bigId}/decide", new { approve = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ga.Post($"/api/supplies/requests/{bigId}/decide", new { approve = true })).StatusCode);   // the manager decides, not the desk
        Assert.Equal(HttpStatusCode.Conflict, (await ga.Post($"/api/supplies/requests/{bigId}/fulfil", new { lines = new Dictionary<Guid, int> { [pen] = 1 } })).StatusCode);
        var approvals = await Ok(await boss.Get("/api/supplies/requests?scope=approvals"));
        Assert.Equal(bigId, IdOf(approvals.GetProperty("data")[0]));
        Assert.True(approvals.GetProperty("data")[0].GetProperty("canDecide").GetBoolean());
        Assert.True((await Fields(await boss.Post($"/api/supplies/requests/{bigId}/decide", new { approve = false }))).TryGetProperty("note", out _));   // a rejection needs a reason
        var approved = await Ok(await boss.Post($"/api/supplies/requests/{bigId}/decide", new { approve = true }));
        Assert.Equal("open", approved.GetProperty("status").GetString());

        // the desk: requesters and agents without supplies.fulfil cannot see it or issue
        Assert.Equal(HttpStatusCode.Forbidden, (await req.Get("/api/supplies/requests?scope=desk")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await agent.Post($"/api/supplies/requests/{bigId}/fulfil", new { lines = new Dictionary<Guid, int> { [pen] = 1 } })).StatusCode);
        var desk = await Ok(await ga.Get("/api/supplies/requests?scope=desk&status=active"));
        Assert.Equal(2, desk.GetProperty("total").GetInt32());

        // only 6 reams in stock: asking for 8 at once is refused with what is left; 6 now, the pens in full, 2 reams stay on back-order
        Assert.True((await Fields(await ga.Post($"/api/supplies/requests/{bigId}/fulfil", new { lines = new Dictionary<Guid, int> { [pen] = 11 } }))).EnumerateObject().Any());
        var short_ = await ga.Post($"/api/supplies/requests/{bigId}/fulfil", new { lines = new Dictionary<Guid, int> { [paper] = 8 } });
        Assert.Equal(HttpStatusCode.Conflict, short_.StatusCode);
        Assert.Contains("Only 6 ream", (await short_.Body()).GetProperty("error").GetProperty("message").GetString());
        var part = await Ok(await ga.Post($"/api/supplies/requests/{bigId}/fulfil", new { lines = new Dictionary<Guid, int> { [paper] = 6, [pen] = 10 } }));
        Assert.Equal("partly_fulfilled", part.GetProperty("status").GetString());
        Assert.Equal(2, Line(part, paper).GetProperty("backOrder").GetInt32());
        Assert.Equal(0, Line(part, pen).GetProperty("backOrder").GetInt32());
        Assert.True(await seed.Db(db => db.Notifications.IgnoreQueryFilters().AnyAsync(n => n.UserId == ru.Id && n.Kind == "supply.update" && n.Title.StartsWith("Part of"))));
        Assert.Equal(HttpStatusCode.Conflict, (await req.Post($"/api/supplies/requests/{bigId}/cancel")).StatusCode);   // something was issued
        // the ledger: issues grouped under the request, stock went down
        var moves = await seed.Db(db => db.StockMovements.IgnoreQueryFilters().Where(m => m.GroupId == bigId).ToListAsync());
        Assert.Equal(2, moves.Count); Assert.All(moves, m => Assert.Equal(StockMovementKind.Issue, m.Kind));
        Assert.Equal(0, (await Ok(await ga.Get($"/api/inventory/items/{paper}"))).GetProperty("total").GetInt32());

        // new stock arrives; the back-order is issued and the request is fulfilled
        await Ok(await ga.Post($"/api/inventory/items/{paper}/moves", new { kind = "receipt", locationId = store, quantity = 10 }));
        var done = await Ok(await ga.Post($"/api/supplies/requests/{bigId}/fulfil", new { lines = new Dictionary<Guid, int> { [paper] = 2 } }));
        Assert.Equal("fulfilled", done.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await ga.Post($"/api/supplies/requests/{bigId}/fulfil", new { lines = new Dictionary<Guid, int> { [paper] = 1 } })).StatusCode);

        // the small request: the requester cancels it before anything is issued; another one is closed by the desk with a reason
        Assert.Equal(HttpStatusCode.NotFound, (await other.Post($"/api/supplies/requests/{IdOf(small)}/cancel")).StatusCode);
        Assert.Equal("cancelled", (await Ok(await req.Post($"/api/supplies/requests/{IdOf(small)}/cancel"))).GetProperty("status").GetString());
        var third = IdOf(await Ok(await req.Post("/api/supplies/requests", new { lines = new[] { new { itemId = pen, quantity = 3 } } })));
        Assert.True((await Fields(await ga.Post($"/api/supplies/requests/{third}/close", new { note = "" }))).TryGetProperty("note", out _));
        Assert.Equal("rejected", (await Ok(await ga.Post($"/api/supplies/requests/{third}/close", new { note = "Pens are in every meeting room" }))).GetProperty("status").GetString());

        // the requester's list, and the summary
        var mine = await Ok(await req.Get("/api/supplies/requests"));
        Assert.Equal(3, mine.GetProperty("total").GetInt32());
        Assert.Equal(0, (await Ok(await other.Get("/api/supplies/requests"))).GetProperty("total").GetInt32());
        var sum = await Ok(await ga.Get("/api/supplies/summary"));
        Assert.Equal(1, sum.GetProperty("fulfilled30").GetInt32());
        Assert.Equal(8 * 50000m + 10 * 3000m, sum.GetProperty("issued30Cost").GetDecimal());
        Assert.Equal(HttpStatusCode.Forbidden, (await req.Get("/api/supplies/summary")).StatusCode);
    }

    [Fact]
    public async Task Without_a_manager_the_desk_decides_and_work_order_parts_come_out_of_stock()
    {
        var iso = await fx.NewIsolatedAsync(); var f = iso.Factory; var clock = iso.Clock; var seed = new Seed(f, clock);
        async Task<(User, TestBrowser)> Person(string role) { var u = await seed.UserAsync(role: role); var b = TestBrowser.Create(f); await b.LoginAsync(u.Email, Seed.Password); return (u, b); }
        var (_, ga) = await Person("ga_manager"); var (_, req) = await Person("user"); var (_, admin) = await Person("super_admin");
        var store = IdOf(await Ok(await admin.Post("/api/locations", new { type = "site", code = "ST-" + Guid.NewGuid().ToString("N")[..4], name = "Workshop" })));
        var filter = IdOf(await Ok(await ga.Post("/api/inventory/items", new { sku = "AC-FILTER", name = "AC filter", category = "other", unit = "pcs", reorderLevel = 1, unitCost = 120000 })));
        await Ok(await ga.Post($"/api/inventory/items/{filter}/moves", new { kind = "receipt", locationId = store, quantity = 4 }));
        await Ok(await ga.SendAsync(HttpMethod.Put, "/api/supplies/settings", new { approvalAbove = 0, defaultLocationId = store }));

        // no manager on the profile: the request goes to the desk to approve
        var r = await Ok(await req.Post("/api/supplies/requests", new { lines = new[] { new { itemId = filter, quantity = 1 } } }));
        Assert.Equal("pending", r.GetProperty("status").GetString());
        Assert.True((await Ok(await ga.Get($"/api/supplies/requests/{IdOf(r)}"))).GetProperty("canDecide").GetBoolean());
        Assert.Equal("open", (await Ok(await ga.Post($"/api/supplies/requests/{IdOf(r)}/decide", new { approve = true }))).GetProperty("status").GetString());

        // INV-005: a part on a work order drawn from stock, at the item's cost; removing the line puts it back
        var u = Guid.NewGuid().ToString("N")[..6];
        var team = IdOf(await Ok(await admin.Post("/api/teams", new { name = $"Team {u}" })));
        var cat = IdOf(await Ok(await admin.Post("/api/catalog/categories", new { name = $"Facilities {u}" })));
        var svc = IdOf(await Ok(await admin.Post("/api/catalog/services", new { name = $"AC repair {u}", categoryId = cat, owningTeamId = team })));
        await Ok(await admin.Post($"/api/catalog/services/{svc}/publish"));
        var t = IdOf(await Ok(await req.Post($"/api/portal/services/{svc}/requests", new { subject = "AC smells", answers = new { }, locationId = store })));
        var wo = IdOf(await Ok(await ga.Post($"/api/tickets/{t}/work-orders", new { title = "Replace AC filter" })));
        Assert.True((await Fields(await ga.Post($"/api/work-orders/{wo}/parts", new { itemId = filter, locationId = store, quantity = 0 }))).TryGetProperty("quantity", out _));
        Assert.Equal(HttpStatusCode.Conflict, (await ga.Post($"/api/work-orders/{wo}/parts", new { itemId = filter, locationId = store, quantity = 5 })).StatusCode);
        var d = await Ok(await ga.Post($"/api/work-orders/{wo}/parts", new { itemId = filter, locationId = store, quantity = 2 }));
        var line = d.GetProperty("costs").EnumerateArray().Single();
        Assert.Equal("part", line.GetProperty("kind").GetString());
        Assert.Equal(240000m, line.GetProperty("total").GetDecimal());
        Assert.True(line.GetProperty("fromStock").GetBoolean());
        Assert.Equal(2, (await Ok(await ga.Get($"/api/inventory/items/{filter}"))).GetProperty("total").GetInt32());
        Assert.Equal(HttpStatusCode.Forbidden, (await req.Post($"/api/work-orders/{wo}/parts", new { itemId = filter, locationId = store, quantity = 1 })).StatusCode);
        await Ok(await ga.SendAsync(HttpMethod.Delete, $"/api/work-orders/{wo}/costs/{IdOf(line)}"));
        Assert.Equal(4, (await Ok(await ga.Get($"/api/inventory/items/{filter}"))).GetProperty("total").GetInt32());
        var back = await seed.Db(db => db.StockMovements.IgnoreQueryFilters().Where(m => m.GroupId == wo).OrderBy(m => m.Quantity).Select(m => m.Quantity).ToListAsync());
        Assert.Equal([-2, 2], back);
    }
}
