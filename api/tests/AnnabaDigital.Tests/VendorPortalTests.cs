using System.Net;
using System.Text.Json;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Tests.Infra;
using Xunit;

namespace AnnabaDigital.Tests;

/// <summary>Vendor portal (R2-11, PRD Q6): a vendor's person sees and works only their company's work orders and reaches nothing else of the organisation.</summary>
[Collection("db")]
public sealed class VendorPortalTests(PostgresFixture fx)
{
    private static string Uniq() => Guid.NewGuid().ToString("N")[..8];
    private const HttpStatusCode Invalid = (HttpStatusCode)422;
    private static async Task<JsonElement> Ok(HttpResponseMessage r) { Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync()); return await r.Body(); }
    private static Guid IdOf(JsonElement e) => e.GetProperty("id").GetGuid();
    private static string Str(JsonElement e, string p) => e.GetProperty(p).GetString()!;
    private static async Task<string> FieldError(HttpResponseMessage r, string key) { Assert.Equal(Invalid, r.StatusCode); return (await r.Body()).GetProperty("error").GetProperty("fields").GetProperty(key).GetString()!; }

    [Fact]
    public async Task A_vendors_person_works_only_their_companys_work_orders_and_reaches_nothing_else()
    {
        var seed = new Seed(fx.Factory, fx.Clock);
        async Task<(User, TestBrowser)> Person(string role) { var u = await seed.UserAsync(role: role); var b = TestBrowser.Create(fx.Factory); await b.LoginAsync(u.Email, Seed.Password); return (u, b); }
        var (_, mgr) = await Person("ga_manager"); var (_, sup) = await Person("ga_supervisor"); var (au, agent) = await Person("ga_agent"); var (_, req) = await Person("user"); var (_, admin) = await Person("super_admin");
        var (vu, tech) = await Person("vendor"); var (pu, plain) = await Person("user");
        var cat = IdOf(await Ok(await admin.Post("/api/catalog/categories", new { name = $"Cat {Uniq()}" })));
        var svc = IdOf(await Ok(await admin.Post("/api/catalog/services", new { name = $"AC {Uniq()}", categoryId = cat })));
        await Ok(await admin.Post($"/api/catalog/services/{svc}/publish"));
        var cool = IdOf(await Ok(await mgr.Post("/api/vendors", new { name = $"CoolCo {Uniq()}" })));
        var other = IdOf(await Ok(await mgr.Post("/api/vendors", new { name = $"OtherCo {Uniq()}" })));

        // --- linking: only people with nothing but the Vendor role, one vendor each
        Assert.Contains("Invite", await FieldError(await mgr.Post($"/api/vendors/{cool}/portal-users", new { email = "nobody@nowhere.test" }), "email"));
        Assert.Contains("staff rights", await FieldError(await mgr.Post($"/api/vendors/{cool}/portal-users", new { email = au.Email }), "email"));
        Assert.Contains("Vendor role", await FieldError(await mgr.Post($"/api/vendors/{cool}/portal-users", new { email = pu.Email }), "email"));
        Assert.Equal(HttpStatusCode.Forbidden, (await agent.Post($"/api/vendors/{cool}/portal-users", new { email = vu.Email })).StatusCode);
        var linked = await Ok(await mgr.Post($"/api/vendors/{cool}/portal-users", new { email = vu.Email.ToUpperInvariant() }));
        Assert.Equal(vu.Id, linked.GetProperty("portalUsers")[0].GetProperty("userId").GetGuid());
        Assert.Contains("already linked", await FieldError(await mgr.Post($"/api/vendors/{other}/portal-users", new { email = vu.Email }), "email"));
        Assert.Equal(JsonValueKind.Null, (await Ok(await agent.Get($"/api/vendors/{cool}"))).GetProperty("portalUsers").ValueKind);
        Assert.Equal(Str(linked, "name"), Str(await Ok(await tech.Get("/api/auth/me")), "vendorName"));

        // --- three work orders: CoolCo's, OtherCo's and an in-house one
        var t = IdOf(await Ok(await req.Post($"/api/portal/services/{svc}/requests", new { subject = "AC leaking", answers = new { } })));
        var mine = IdOf(await Ok(await sup.Post($"/api/tickets/{t}/work-orders", new { title = "Fix the leak", vendorId = cool, assigneeId = au.Id, tasks = new[] { new { text = "Check the drain pipe", required = true } } })));
        var theirs = IdOf(await Ok(await sup.Post($"/api/tickets/{t}/work-orders", new { title = "Quote a new unit", vendorId = other })));
        var inhouse = IdOf(await Ok(await sup.Post($"/api/tickets/{t}/work-orders", new { title = "Clean up the floor" })));
        var list = (await Ok(await tech.Get("/api/work-orders?status=all"))).GetProperty("data").EnumerateArray().Select(IdOf).ToList();
        Assert.Equal([mine], list);
        foreach (var x in new[] { theirs, inhouse }) Assert.Equal(HttpStatusCode.NotFound, (await tech.Get($"/api/work-orders/{x}")).StatusCode);

        // --- doing the work: start (the agent stays the coordinator), tick, time, complete; no money
        var wo = await Ok(await tech.Get($"/api/work-orders/{mine}"));
        Assert.True(wo.GetProperty("canWork").GetBoolean()); Assert.Equal(JsonValueKind.Null, wo.GetProperty("costs").ValueKind);
        await Ok(await tech.Post($"/api/work-orders/{mine}/action", new { action = "start" }));
        await Ok(await tech.Post($"/api/work-orders/{mine}/tasks/{IdOf(wo.GetProperty("tasks")[0])}", new { done = true }));
        await Ok(await tech.Post($"/api/work-orders/{mine}/time", new { minutes = 45 }));
        Assert.Equal(HttpStatusCode.Forbidden, (await tech.Post($"/api/work-orders/{mine}/costs", new { kind = "part", description = "Pipe", quantity = 1, unitCost = 10 })).StatusCode);
        var done = await Ok(await tech.Post($"/api/work-orders/{mine}/action", new { action = "complete", note = "Drain pipe cleared" }));
        Assert.Equal("done", Str(done, "status")); Assert.Equal(au.Id, done.GetProperty("assigneeId").GetGuid());
        Assert.Equal(HttpStatusCode.Forbidden, (await tech.Post($"/api/work-orders/{mine}/action", new { action = "cancel", reason = "No" })).StatusCode);

        // --- nothing else of the organisation is reachable, whatever the endpoint asks for
        foreach (var path in new[] { "/api/portal/locations", "/api/portal/services", "/api/portal/people?q=a", "/api/kb/articles", "/api/tickets/mine", $"/api/tickets/{t}", "/api/costs/settings", "/api/maintenance/plans", "/api/vendors" })
        {
            var r = await tech.Get(path);
            Assert.True(r.StatusCode is HttpStatusCode.Forbidden, $"{path} answered {(int)r.StatusCode}");
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await tech.Post($"/api/portal/services/{svc}/requests", new { subject = "Can I ask?", answers = new { } })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await tech.Get("/api/notifications")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await plain.Get("/api/portal/locations")).StatusCode);                              // ordinary people are untouched

        // --- unlinked: the company's work is gone
        await Ok(await mgr.SendAsync(HttpMethod.Delete, $"/api/vendors/{cool}/portal-users/{vu.Id}"));
        Assert.Empty((await Ok(await tech.Get("/api/work-orders?status=all"))).GetProperty("data").EnumerateArray());
    }
}
