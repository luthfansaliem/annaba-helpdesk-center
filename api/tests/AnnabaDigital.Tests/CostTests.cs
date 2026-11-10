using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using AnnabaDigital.Application.Helpdesk;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Tests.Infra;
using Xunit;

namespace AnnabaDigital.Tests;

/// <summary>Cost and budget (CST-001..004, RPT-003, R2 slice 8): attribution and rounding, labour from time, the report and its export, cost centres, budgets and their alerts.</summary>
[Collection("db")]
public sealed class CostTests(PostgresFixture fx)
{
    private static string Uniq() => Guid.NewGuid().ToString("N")[..8];
    private const HttpStatusCode Invalid = (HttpStatusCode)422;
    private static async Task<JsonElement> Ok(HttpResponseMessage r) { Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync()); return r.StatusCode == HttpStatusCode.NoContent ? default : await r.Body(); }
    private static Guid IdOf(JsonElement e) => e.GetProperty("id").GetGuid();
    private static string Str(JsonElement e, string p) => e.GetProperty(p).GetString()!;
    private static async Task<JsonElement> Fields(HttpResponseMessage r) { Assert.Equal(Invalid, r.StatusCode); return (await r.Body()).GetProperty("error").GetProperty("fields"); }

    private sealed record World(ApiFactory F, Seed Seed, TestBrowser Manager, User ManagerUser, TestBrowser Supervisor, TestBrowser Agent, User AgentUser, TestBrowser Requester, User RequesterUser,
        Guid ServiceId, Guid CategoryId, string CategoryName, Guid TeamId);

    private async Task<World> Build()
    {
        var iso = await fx.NewIsolatedAsync(); var f = iso.Factory;
        var seed = new Seed(f, iso.Clock);
        async Task<(User, TestBrowser)> Person(string role) { var u = await seed.UserAsync(role: role); var b = TestBrowser.Create(f); await b.LoginAsync(u.Email, Seed.Password); return (u, b); }
        var (mu, mgr) = await Person("ga_manager"); var (_, sup) = await Person("ga_supervisor"); var (au, agent) = await Person("ga_agent"); var (ru, req) = await Person("user"); var (_, admin) = await Person("super_admin");
        var team = IdOf(await Ok(await admin.Post("/api/teams", new { name = $"Team {Uniq()}" })));
        await Ok(await admin.SendAsync(HttpMethod.Put, $"/api/teams/{team}/members", new { members = new[] { new { userId = au.Id, isLead = false } } }));
        var catName = $"Facilities {Uniq()}";
        var cat = IdOf(await Ok(await admin.Post("/api/catalog/categories", new { name = catName })));
        var svc = IdOf(await Ok(await admin.Post("/api/catalog/services", new { name = $"AC repair {Uniq()}", categoryId = cat, owningTeamId = team })));
        await Ok(await admin.Post($"/api/catalog/services/{svc}/publish"));
        return new World(f, seed, mgr, mu, sup, agent, au, req, ru, svc, cat, catName, team);
    }

    /// <summary>A ticket at <paramref name="place"/> with one work order (optionally a vendor's) carrying the given cost lines and minutes.</summary>
    private static async Task<Guid> WorkWithCosts(World w, Guid place, Guid? vendor, int minutes, params (string Kind, decimal Qty, decimal Unit)[] lines)
    {
        var t = IdOf(await Ok(await w.Requester.Post($"/api/portal/services/{w.ServiceId}/requests", new { subject = "AC broken", answers = new { }, locationId = place })));
        var wo = IdOf(await Ok(await w.Supervisor.Post($"/api/tickets/{t}/work-orders", new { title = "Fix the AC", vendorId = vendor, assigneeId = w.AgentUser.Id })));
        foreach (var (k, q, u) in lines) await Ok(await w.Supervisor.Post($"/api/work-orders/{wo}/costs", new { kind = k, description = $"{k} line", quantity = q, unitCost = u }));
        if (minutes > 0) { await Ok(await w.Agent.Post($"/api/work-orders/{wo}/action", new { action = "start" })); await Ok(await w.Agent.Post($"/api/work-orders/{wo}/time", new { minutes })); }
        return wo;
    }

    [Fact]
    public async Task Costs_are_attributed_rounded_and_reported_with_labour_from_time_and_a_safe_export()
    {
        var w = await Build();
        var bad = await Fields(await w.Manager.SendAsync(HttpMethod.Put, "/api/costs/settings", new { currency = "rupiah", labourRatePerHour = -1 }));
        Assert.True(bad.TryGetProperty("currency", out _)); Assert.True(bad.TryGetProperty("labourRatePerHour", out _));
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Supervisor.SendAsync(HttpMethod.Put, "/api/costs/settings", new { currency = "IDR", labourRatePerHour = 1 })).StatusCode);
        await Ok(await w.Manager.SendAsync(HttpMethod.Put, "/api/costs/settings", new { currency = "idr", labourRatePerHour = 120000 }));

        var site = IdOf(await Ok(await w.Manager.Post("/api/locations", new { type = "site", code = "CS-" + Uniq(), name = "Tower A" })));
        var floor = IdOf(await Ok(await w.Manager.Post("/api/locations", new { type = "floor", parentId = site, code = "CF-" + Uniq(), name = "Floor 2" })));
        var other = IdOf(await Ok(await w.Manager.Post("/api/locations", new { type = "site", code = "CO-" + Uniq(), name = "Warehouse" })));
        var cc = await Ok(await w.Manager.Post("/api/costs/centers", new { code = "ga-100", name = "General Affairs Jakarta", locationIds = new[] { site } }));
        Assert.Equal("GA-100", Str(cc, "code"));
        Assert.True((await Fields(await w.Manager.Post("/api/costs/centers", new { code = "GA-200", name = "Other", locationIds = new[] { site } }))).TryGetProperty("locationIds", out _));   // one cost centre per place
        var vendor = IdOf(await Ok(await w.Manager.Post("/api/vendors", new { name = $"CoolCo {Uniq()}" })));
        await w.Seed.Db(async db =>
        {
            var p = await db.Profiles.FirstOrDefaultAsync(x => x.UserId == w.RequesterUser.Id);
            if (p is null) db.Profiles.Add(new UserProfile { UserId = w.RequesterUser.Id, Department = "=Finance" }); else p.Department = "=Finance";
            await db.SaveChangesAsync(); return 0;
        });

        // 1.5 × 2.03 = 3.045 → 3.05 (half away from zero); 90 minutes at 120,000/h = 180,000; a vendor invoice of 250,000
        var wo = await WorkWithCosts(w, floor, vendor, 90, ("part", 1.5m, 2.03m), ("invoice", 1, 250000));
        await WorkWithCosts(w, other, null, 0, ("service", 2, 1000));

        var detail = await Ok(await w.Supervisor.Get($"/api/work-orders/{wo}"));
        Assert.Equal(3.05m + 250000m, detail.GetProperty("costTotal").GetDecimal()); Assert.Equal(180000m, detail.GetProperty("labourCost").GetDecimal());
        Assert.Equal(JsonValueKind.Null, (await Ok(await w.Agent.Get($"/api/work-orders/{wo}"))).GetProperty("labourCost").ValueKind);

        async Task<JsonElement> Report(string by) => await Ok(await w.Supervisor.Get($"/api/costs/report?by={by}"));
        var bySite = await Report("site");
        Assert.Equal("IDR", Str(bySite, "currency"));
        var tower = bySite.GetProperty("rows").EnumerateArray().Single(r => Str(r, "label") == "Tower A");
        Assert.Equal(3.05m, tower.GetProperty("part").GetDecimal()); Assert.Equal(250000m, tower.GetProperty("invoice").GetDecimal()); Assert.Equal(180000m, tower.GetProperty("time").GetDecimal());
        Assert.Equal(430003.05m, tower.GetProperty("total").GetDecimal()); Assert.Equal(1, tower.GetProperty("workOrders").GetInt32());
        Assert.Equal(432003.05m, bySite.GetProperty("total").GetProperty("total").GetDecimal());
        Assert.Contains((await Report("cost_center")).GetProperty("rows").EnumerateArray(), r => Str(r, "label") == "GA-100 · General Affairs Jakarta" && r.GetProperty("total").GetDecimal() == 430003.05m);
        Assert.Contains((await Report("cost_center")).GetProperty("rows").EnumerateArray(), r => Str(r, "key") == "none" && r.GetProperty("total").GetDecimal() == 2000m);
        Assert.Contains((await Report("vendor")).GetProperty("rows").EnumerateArray(), r => Str(r, "key") == vendor.ToString());
        Assert.Equal(432003.05m, (await Report("category")).GetProperty("rows").EnumerateArray().Single(r => Str(r, "label") == w.CategoryName).GetProperty("total").GetDecimal());
        Assert.Contains((await Report("department")).GetProperty("rows").EnumerateArray(), r => Str(r, "label") == "=Finance");
        Assert.Single((await Report("month")).GetProperty("rows").EnumerateArray());

        var csv = await (await w.Supervisor.Get("/api/costs/report.csv?by=department")).Content.ReadAsStringAsync();
        Assert.Contains("total (IDR)", csv); Assert.Contains("'=Finance,", csv);                                  // a formula-looking value is defused
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Agent.Get("/api/costs/report")).StatusCode);
        Assert.Equal(Invalid, (await w.Supervisor.Get("/api/costs/report?by=colour")).StatusCode);
        Assert.Equal(Invalid, (await w.Supervisor.Get("/api/costs/report?from=2026-05-01&to=2026-04-01")).StatusCode);
    }

    [Fact]
    public async Task Budgets_compare_with_the_years_costs_and_alert_budget_managers_at_80_and_100_percent_once()
    {
        var w = await Build();
        var site = IdOf(await Ok(await w.Manager.Post("/api/locations", new { type = "site", code = "BS-" + Uniq(), name = "Plant" })));
        var hall = IdOf(await Ok(await w.Manager.Post("/api/locations", new { type = "floor", parentId = site, code = "BH-" + Uniq(), name = "Hall" })));
        var year = DateTime.UtcNow.Year;
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Supervisor.Post("/api/costs/budgets", new { scope = "site", scopeId = site, year, amount = 1000 })).StatusCode);
        var bad = await Fields(await w.Manager.Post("/api/costs/budgets", new { scope = "site", scopeId = hall, year = 1999, amount = 0.001 }));
        foreach (var k in new[] { "scopeId", "year", "amount" }) Assert.True(bad.TryGetProperty(k, out _), k);
        var list = await Ok(await w.Manager.Post("/api/costs/budgets", new { scope = "site", scopeId = site, year, amount = 1000, note = "AC upkeep" }));
        var budget = list.GetProperty("items").EnumerateArray().Single();
        Assert.Equal("Plant", Str(budget, "scopeName")); Assert.Equal(0m, budget.GetProperty("spent").GetDecimal()); Assert.Equal("ok", Str(budget, "state"));
        Assert.True((await Fields(await w.Manager.Post("/api/costs/budgets", new { scope = "site", scopeId = site, year, amount = 5 }))).TryGetProperty("scopeId", out _));   // one per target and year
        await Ok(await w.Manager.Post("/api/costs/budgets", new { scope = "category", scopeId = w.CategoryId, year, amount = 100000 }));

        async Task<int> Run() { using var s = w.F.Services.CreateScope(); return await s.ServiceProvider.GetRequiredService<CostService>().RunBudgetAlertsAsync(); }
        async Task<string[]> Alerts() => [.. await w.Seed.Db(db => db.Notifications.Where(n => n.UserId == w.ManagerUser.Id && n.Kind == "budget.threshold").OrderBy(n => n.CreatedAt).Select(n => n.Title).ToListAsync())];
        Assert.Equal(0, await Run());

        await WorkWithCosts(w, hall, null, 0, ("part", 1, 850));
        var near = (await Ok(await w.Supervisor.Get($"/api/costs/budgets?year={year}"))).GetProperty("items").EnumerateArray().Single(b => Str(b, "scope") == "site");
        Assert.Equal(850m, near.GetProperty("spent").GetDecimal()); Assert.Equal(85, near.GetProperty("percent").GetInt32()); Assert.Equal("near", Str(near, "state"));
        Assert.False((await Ok(await w.Supervisor.Get($"/api/costs/budgets?year={year}"))).GetProperty("canManage").GetBoolean());
        Assert.Equal(1, await Run());
        Assert.Equal(0, await Run());
        Assert.Equal([$"Budget at 85%: Plant ({year})"], await Alerts());
        await w.Seed.Mail.WaitFor(w.ManagerUser.Email, "Budget at 85%");

        await WorkWithCosts(w, hall, null, 0, ("other", 1, 200));
        Assert.Equal(1, await Run());
        Assert.Equal($"Budget used up: Plant ({year})", (await Alerts()).Last());
        // a raised budget is judged afresh
        var id = near.GetProperty("id").GetGuid();
        await Ok(await w.Manager.SendAsync(HttpMethod.Put, $"/api/costs/budgets/{id}", new { scope = "site", scopeId = site, year, amount = 1200, version = near.GetProperty("version").GetInt32() }));
        Assert.Equal(1, await Run());
        Assert.Equal($"Budget at 87%: Plant ({year})", (await Alerts()).Last());
        Assert.Equal(HttpStatusCode.NoContent, (await w.Manager.SendAsync(HttpMethod.Delete, $"/api/costs/budgets/{id}")).StatusCode);
    }
}
