using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Tests.Infra;
using Xunit;

namespace AnnabaDigital.Tests;

/// <summary>Field mode (R3-11, ADR-GA-07): the device gets only the person's own open work orders (no costs), and replayed changes with an Idempotency-Key are
/// applied once, remembered only when they succeed, and per person.</summary>
[Collection("db")]
public sealed class FieldModeTests(PostgresFixture fx)
{
    private static async Task<JsonElement> Ok(HttpResponseMessage r) { Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync()); return await r.Body(); }
    private static Guid IdOf(JsonElement e) => e.GetProperty("id").GetGuid();

    [Fact]
    public async Task The_device_gets_only_my_open_work_orders_and_replayed_changes_apply_once()
    {
        var iso = await fx.NewIsolatedAsync(); var f = iso.Factory; var seed = new Seed(f, iso.Clock);
        async Task<(User, TestBrowser)> Person(string role) { var u = await seed.UserAsync(role: role); var b = TestBrowser.Create(f); await b.LoginAsync(u.Email, Seed.Password); return (u, b); }
        var (_, admin) = await Person("super_admin"); var (_, sup) = await Person("ga_supervisor"); var (au, agent) = await Person("ga_agent"); var (bu, other) = await Person("ga_agent"); var (_, req) = await Person("user");
        var u = Guid.NewGuid().ToString("N")[..6];
        var team = IdOf(await Ok(await admin.Post("/api/teams", new { name = $"Team {u}" })));
        await Ok(await admin.SendAsync(HttpMethod.Put, $"/api/teams/{team}/members", new { members = new[] { new { userId = au.Id, isLead = false }, new { userId = bu.Id, isLead = false } } }));
        var cat = IdOf(await Ok(await admin.Post("/api/catalog/categories", new { name = $"Facilities {u}" })));
        var svc = IdOf(await Ok(await admin.Post("/api/catalog/services", new { name = $"AC repair {u}", categoryId = cat, owningTeamId = team })));
        await Ok(await admin.Post($"/api/catalog/services/{svc}/publish"));
        var t = IdOf(await Ok(await req.Post($"/api/portal/services/{svc}/requests", new { subject = "AC broken", answers = new { } })));
        var mine = IdOf(await Ok(await sup.Post($"/api/tickets/{t}/work-orders", new { title = "Fix the AC", assigneeId = au.Id, tasks = new[] { new { text = "Clean filter", required = true } } })));
        var theirs = IdOf(await Ok(await sup.Post($"/api/tickets/{t}/work-orders", new { title = "Check the pipes", assigneeId = bu.Id })));
        var done = IdOf(await Ok(await sup.Post($"/api/tickets/{t}/work-orders", new { title = "Already done", assigneeId = au.Id, required = false })));
        await Ok(await sup.Post($"/api/work-orders/{done}/action", new { action = "cancel", reason = "Not needed" }));
        await Ok(await sup.Post($"/api/work-orders/{mine}/costs", new { kind = "part", description = "Filter", quantity = 1, unitCost = 50000 }));

        // the snapshot: only my open work orders, with the checklist, without costs
        var snap = await Ok(await agent.Get("/api/work-orders/offline"));
        var items = snap.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal([mine], items.Select(IdOf));
        Assert.Equal(1, items[0].GetProperty("tasks").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, items[0].GetProperty("costs").ValueKind);
        Assert.Equal(HttpStatusCode.Forbidden, (await req.Get("/api/work-orders/offline")).StatusCode);

        // a replay with the same key: the change happens once, the second answer is the work order as it is
        static Task<HttpResponseMessage> Send(TestBrowser b, string path, object body, string key) => b.SendAsync(HttpMethod.Post, path, body, headers: new Dictionary<string, string> { ["Idempotency-Key"] = key });
        var start = await Ok(await Send(agent, $"/api/work-orders/{mine}/action", new { action = "start" }, "op-start-1"));
        Assert.Equal("in_progress", start.GetProperty("status").GetString());
        var k = "op-time-" + Guid.NewGuid().ToString("N")[..8];
        await Ok(await Send(agent, $"/api/work-orders/{mine}/time", new { minutes = 30 }, k));
        var again = await Send(agent, $"/api/work-orders/{mine}/time", new { minutes = 30 }, k);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode); Assert.Equal("true", again.Headers.GetValues("Idempotent-Replayed").Single());
        Assert.Equal(30, (await again.Body()).GetProperty("totalMinutes").GetInt32());
        Assert.Equal(1, await seed.Db(db => db.WorkOrderTimes.IgnoreQueryFilters().CountAsync(x => x.WorkOrderId == mine)));

        // a refused change is not remembered: the same key works once the problem is gone
        var stale = await Send(agent, $"/api/work-orders/{mine}/action", new { action = "hold", reason = "Waiting for parts", version = 1 }, "op-hold-1");
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);                            // changed meanwhile (started, time logged): the version is old
        var current = (await Ok(await agent.Get($"/api/work-orders/{mine}"))).GetProperty("version").GetInt32();
        Assert.Equal("on_hold", (await Ok(await Send(agent, $"/api/work-orders/{mine}/action", new { action = "hold", reason = "Waiting for parts", version = current }, "op-hold-1"))).GetProperty("status").GetString());

        // keys are per person, and must be well-formed
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(other, $"/api/work-orders/{mine}/time", new { minutes = 5 }, k)).StatusCode);   // not their work order: no replay answer for them
        Assert.Equal((HttpStatusCode)422, (await Send(agent, $"/api/work-orders/{mine}/time", new { minutes = 5 }, "bad key!")).StatusCode);
        Assert.Equal(2, await seed.Db(db => db.OfflineOps.IgnoreQueryFilters().CountAsync(o => o.UserId == au.Id && o.Key.StartsWith("op-") && (o.Key == k || o.Key == "op-hold-1"))));
        _ = theirs;
    }
}
