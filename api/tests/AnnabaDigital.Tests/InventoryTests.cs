using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Tests.Infra;
using Xunit;

namespace AnnabaDigital.Tests;

/// <summary>Inventory (R3-6: INV-001/003/004, RPT-005): items and who keeps them, the ledger (receipts, issues, adjustments, transfers), a balance that never goes
/// below zero even under a race, the low-stock alert once per dip, the reorder list, the stock value and stock-takes that post their differences.</summary>
[Collection("db")]
public sealed class InventoryTests(PostgresFixture fx)
{
    private const HttpStatusCode Invalid = (HttpStatusCode)422;
    private static async Task<JsonElement> Ok(HttpResponseMessage r) { Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync()); return await r.Body(); }
    private static Guid IdOf(JsonElement e) => e.GetProperty("id").GetGuid();
    private static async Task<JsonElement> Fields(HttpResponseMessage r) { Assert.Equal(Invalid, r.StatusCode); return (await r.Body()).GetProperty("error").GetProperty("fields"); }

    [Fact]
    public async Task Stock_moves_only_through_the_ledger_never_below_zero_and_warns_when_low()
    {
        var iso = await fx.NewIsolatedAsync(); var f = iso.Factory; var clock = iso.Clock; var seed = new Seed(f, clock);
        async Task<(User, TestBrowser)> Person(string role) { var u = await seed.UserAsync(role: role); var b = TestBrowser.Create(f); await b.LoginAsync(u.Email, Seed.Password); return (u, b); }
        var (mu, mgr) = await Person("ga_manager"); var (_, sup) = await Person("ga_supervisor"); var (_, agent) = await Person("ga_agent"); var (_, req) = await Person("user"); var (_, admin) = await Person("super_admin");
        var hq = IdOf(await Ok(await admin.Post("/api/locations", new { type = "site", code = "HQ-" + Guid.NewGuid().ToString("N")[..4], name = "Head office" })));
        var branch = IdOf(await Ok(await admin.Post("/api/locations", new { type = "site", code = "BR-" + Guid.NewGuid().ToString("N")[..4], name = "Branch" })));

        // items: kept by GA Manager, read by agents, hidden from requesters
        Assert.Equal(HttpStatusCode.Forbidden, (await req.Get("/api/inventory/items")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await agent.Post("/api/inventory/items", new { sku = "X", name = "X" })).StatusCode);
        var fi = await Fields(await mgr.Post("/api/inventory/items", new { sku = "bad sku!", name = "", category = "weapons", reorderLevel = -1 }));
        foreach (var k in new[] { "sku", "name", "category", "reorderLevel" }) Assert.True(fi.TryGetProperty(k, out _), k);
        var paper = await Ok(await mgr.Post("/api/inventory/items", new { sku = "atk-hvs-a4", name = "HVS paper A4 80 g", category = "atk", unit = "ream", reorderLevel = 10, reorderQuantity = 50, unitCost = 45000 }));
        var id = IdOf(paper);
        Assert.Equal("ATK-HVS-A4", paper.GetProperty("sku").GetString());
        Assert.True((await Fields(await mgr.Post("/api/inventory/items", new { sku = "ATK-HVS-A4", name = "Copy" }))).TryGetProperty("sku", out _));

        // receipts and issues need inventory.manage; adjustments need inventory.adjust and a reason
        Assert.Equal(HttpStatusCode.Forbidden, (await agent.Post($"/api/inventory/items/{id}/moves", new { kind = "receipt", locationId = hq, quantity = 5 })).StatusCode);
        var r1 = await Ok(await mgr.Post($"/api/inventory/items/{id}/moves", new { kind = "receipt", locationId = hq, quantity = 30, unitCost = 46000, reference = "PO-77 Toko Sinar" }));
        Assert.Equal(30, r1[0].GetProperty("balanceAfter").GetInt32());
        Assert.Equal(46000m, (await Ok(await agent.Get($"/api/inventory/items/{id}"))).GetProperty("unitCost").GetDecimal());   // the last receipt's cost
        var tooMuch = await mgr.Post($"/api/inventory/items/{id}/moves", new { kind = "issue", locationId = hq, quantity = 31 });
        Assert.Equal(HttpStatusCode.Conflict, tooMuch.StatusCode);
        Assert.Contains("Only 30 ream", (await tooMuch.Body()).GetProperty("error").GetProperty("message").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await mgr.Post($"/api/inventory/items/{id}/moves", new { kind = "issue", locationId = branch, quantity = 1 })).StatusCode);   // none at the branch
        Assert.True((await Fields(await sup.Post($"/api/inventory/items/{id}/moves", new { kind = "adjustment", locationId = hq, quantity = -2 }))).TryGetProperty("reference", out _));
        Assert.Equal(HttpStatusCode.Forbidden, (await sup.Post($"/api/inventory/items/{id}/moves", new { kind = "issue", locationId = hq, quantity = 1 })).StatusCode);
        await Ok(await sup.Post($"/api/inventory/items/{id}/moves", new { kind = "adjustment", locationId = hq, quantity = -2, reference = "Water damage" }));

        // a transfer moves stock between places in two ledger lines
        var t = await Ok(await mgr.Post($"/api/inventory/items/{id}/moves", new { kind = "transfer", locationId = hq, toLocationId = branch, quantity = 8, reference = "Branch restock" }));
        Assert.Equal(2, t.GetArrayLength());
        var item = await Ok(await agent.Get($"/api/inventory/items/{id}"));
        Assert.Equal(28, item.GetProperty("total").GetInt32());
        Assert.Equal(20, item.GetProperty("stock").EnumerateArray().Single(s => s.GetProperty("locationId").GetGuid() == hq).GetProperty("quantity").GetInt32());
        Assert.True(item.GetProperty("stock").EnumerateArray().Single(s => s.GetProperty("locationId").GetGuid() == branch).GetProperty("low").GetBoolean());   // 8 ≤ 10

        // two issues racing for the last stock: the database keeps the balance at zero or above
        var results = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => mgr.Post($"/api/inventory/items/{id}/moves", new { kind = "issue", locationId = branch, quantity = 4, reference = "Race" })));
        Assert.Equal(2, results.Count(r => r.IsSuccessStatusCode)); Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        var balance = await seed.Db(db => db.StockBalances.IgnoreQueryFilters().Where(b => b.ItemId == id && b.LocationId == branch).Select(b => b.Quantity).FirstAsync());
        Assert.Equal(0, balance);
        var ledger = await seed.Db(db => db.StockMovements.IgnoreQueryFilters().Where(m => m.ItemId == id).SumAsync(m => m.Quantity));
        Assert.Equal(20, ledger);                                                                      // the ledger sums to the balances (20 at HQ, 0 at the branch)

        // the low-stock alert: once when HQ falls to 10, again only after it recovered and fell again
        Task<int> Alerts() => seed.Db(db => db.Notifications.IgnoreQueryFilters().CountAsync(n => n.UserId == mu.Id && n.Kind == "stock.low" && n.Title.Contains("Head office")));
        Assert.Equal(0, await Alerts());
        await Ok(await mgr.Post($"/api/inventory/items/{id}/moves", new { kind = "issue", locationId = hq, quantity = 10, reference = "Finance" }));
        Assert.Equal(1, await Alerts());
        await Ok(await mgr.Post($"/api/inventory/items/{id}/moves", new { kind = "issue", locationId = hq, quantity = 1, reference = "HR" }));
        Assert.Equal(1, await Alerts());
        await Ok(await mgr.Post($"/api/inventory/items/{id}/moves", new { kind = "receipt", locationId = hq, quantity = 20 }));
        await Ok(await mgr.Post($"/api/inventory/items/{id}/moves", new { kind = "issue", locationId = hq, quantity = 25 }));
        Assert.Equal(2, await Alerts());

        // the reorder list and the summary
        var reorder = await Ok(await agent.Get("/api/inventory/reorder"));
        var hqLine = reorder.EnumerateArray().Single(l => l.GetProperty("locationId").GetGuid() == hq);
        Assert.Equal(4, hqLine.GetProperty("quantity").GetInt32()); Assert.Equal(50, hqLine.GetProperty("suggested").GetInt32()); Assert.Equal(2300000m, hqLine.GetProperty("cost").GetDecimal());
        var sum = await Ok(await agent.Get("/api/inventory/summary"));
        Assert.Equal(1, sum.GetProperty("items").GetInt32()); Assert.Equal(2, sum.GetProperty("lowLines").GetInt32());
        Assert.Equal(4 * 46000m, sum.GetProperty("stockValue").GetDecimal());
        Assert.Equal(8 + 10 + 1 + 25, sum.GetProperty("issued30").GetInt32()); Assert.Equal(50, sum.GetProperty("received30").GetInt32());

        // an item with stock cannot be archived
        Assert.Equal(HttpStatusCode.Conflict, (await mgr.Post($"/api/inventory/items/{id}/archive")).StatusCode);
        var history = await Ok(await agent.Get($"/api/inventory/movements?itemId={id}&pageSize=100"));
        Assert.Equal(10, history.GetArrayLength());
        Assert.Contains(history.EnumerateArray(), m => m.GetProperty("kind").GetString() == "transfer_in" && m.GetProperty("reference").GetString() == "Branch restock");
    }

    [Fact]
    public async Task A_stock_take_freezes_what_is_expected_and_posts_the_differences()
    {
        var iso = await fx.NewIsolatedAsync(); var f = iso.Factory; var clock = iso.Clock; var seed = new Seed(f, clock);
        async Task<TestBrowser> Person(string role) { var u = await seed.UserAsync(role: role); var b = TestBrowser.Create(f); await b.LoginAsync(u.Email, Seed.Password); return b; }
        var mgr = await Person("ga_manager"); var sup = await Person("ga_supervisor"); var agent = await Person("ga_agent"); var admin = await Person("super_admin");
        var pantry = IdOf(await Ok(await admin.Post("/api/locations", new { type = "site", code = "PN-" + Guid.NewGuid().ToString("N")[..4], name = "Pantry store" })));
        async Task<Guid> Item(string sku, int qty)
        {
            var id = IdOf(await Ok(await mgr.Post("/api/inventory/items", new { sku, name = sku, category = "pantry", unit = "pack", reorderLevel = 2 })));
            await Ok(await mgr.Post($"/api/inventory/items/{id}/moves", new { kind = "receipt", locationId = pantry, quantity = qty }));
            return id;
        }
        var coffee = await Item("COF-" + Guid.NewGuid().ToString("N")[..4], 12); var tea = await Item("TEA-" + Guid.NewGuid().ToString("N")[..4], 6); var sugar = await Item("SUG-" + Guid.NewGuid().ToString("N")[..4], 9);

        Assert.Equal(HttpStatusCode.Forbidden, (await agent.Post("/api/inventory/stock-takes", new { locationId = pantry })).StatusCode);
        var take = await Ok(await sup.Post("/api/inventory/stock-takes", new { locationId = pantry, note = "Month end" }));
        var tid = IdOf(take);
        Assert.Equal(3, take.GetProperty("lines").GetArrayLength());
        Assert.Equal(HttpStatusCode.Conflict, (await sup.Post("/api/inventory/stock-takes", new { locationId = pantry })).StatusCode);   // one open count per place
        Assert.Equal(HttpStatusCode.Conflict, (await sup.Post($"/api/inventory/stock-takes/{tid}/close")).StatusCode);                  // nothing counted yet

        var counted = await Ok(await sup.SendAsync(HttpMethod.Put, $"/api/inventory/stock-takes/{tid}/counts", new { counts = new Dictionary<Guid, int?> { [coffee] = 10, [tea] = 6 } }));
        Assert.Equal(2, counted.GetProperty("counted").GetInt32()); Assert.Equal(1, counted.GetProperty("variances").GetInt32());
        Assert.True((await Fields(await sup.SendAsync(HttpMethod.Put, $"/api/inventory/stock-takes/{tid}/counts", new { counts = new Dictionary<Guid, int?> { [sugar] = -1 } }))).EnumerateObject().Any());
        var closed = await Ok(await sup.Post($"/api/inventory/stock-takes/{tid}/close"));
        Assert.NotEqual(JsonValueKind.Null, closed.GetProperty("closedAt").ValueKind);
        Assert.Equal(10, (await Ok(await agent.Get($"/api/inventory/items/{coffee}"))).GetProperty("total").GetInt32());   // 12 → 10
        Assert.Equal(9, (await Ok(await agent.Get($"/api/inventory/items/{sugar}"))).GetProperty("total").GetInt32());     // not counted: unchanged
        var moves = await Ok(await agent.Get($"/api/inventory/movements?itemId={coffee}"));
        Assert.Equal("count", moves[0].GetProperty("kind").GetString()); Assert.Equal(-2, moves[0].GetProperty("quantity").GetInt32());
        Assert.Equal(HttpStatusCode.Conflict, (await sup.Post($"/api/inventory/stock-takes/{tid}/close")).StatusCode);
        Assert.Single((await Ok(await agent.Get("/api/inventory/stock-takes"))).EnumerateArray());
    }
}
