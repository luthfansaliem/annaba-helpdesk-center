using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Tests.Infra;
using Xunit;

namespace AnnabaDigital.Tests;

/// <summary>Satisfaction (TKT-009 / RPT-006, R2 slice 6): who may rate and when, changing a rating, the low-score alert, the report's scope and groupings, and the vendor scorecard.</summary>
[Collection("db")]
public sealed class RatingTests(PostgresFixture fx)
{
    private static string Uniq() => Guid.NewGuid().ToString("N")[..8];
    private const HttpStatusCode Invalid = (HttpStatusCode)422;
    private static async Task<JsonElement> Ok(HttpResponseMessage r) { Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync()); return await r.Body(); }
    private static Guid IdOf(JsonElement e) => e.GetProperty("id").GetGuid();
    private static string Str(JsonElement e, string p) => e.GetProperty(p).GetString()!;

    private sealed record World(ApiFactory F, TestClock Clock, Seed Seed, TestBrowser Manager, TestBrowser Supervisor, TestBrowser Agent, User AgentUser, TestBrowser Lead, User LeadUser,
        TestBrowser Requester, User RequesterUser, Guid ServiceId, Guid TeamId, string TeamName);

    private async Task<World> Build(bool isolated = false)
    {
        ApiFactory f; TestClock clock;
        if (isolated) { var iso = await fx.NewIsolatedAsync(); f = iso.Factory; clock = iso.Clock; } else { f = fx.Factory; clock = fx.Clock; }
        var seed = new Seed(f, clock);
        async Task<(User, TestBrowser)> Person(string role) { var u = await seed.UserAsync(role: role); var b = TestBrowser.Create(f); await b.LoginAsync(u.Email, Seed.Password); return (u, b); }
        var (_, mgr) = await Person("ga_manager"); var (_, sup) = await Person("ga_supervisor"); var (au, agent) = await Person("ga_agent"); var (lu, lead) = await Person("ga_agent");
        var (ru, req) = await Person("user"); var (_, admin) = await Person("super_admin");
        var teamName = $"Team {Uniq()}";
        var team = IdOf(await Ok(await admin.Post("/api/teams", new { name = teamName })));
        await Ok(await admin.SendAsync(HttpMethod.Put, $"/api/teams/{team}/members", new { members = new[] { new { userId = au.Id, isLead = false }, new { userId = lu.Id, isLead = true } } }));
        var cat = IdOf(await Ok(await admin.Post("/api/catalog/categories", new { name = $"Cat {Uniq()}" })));
        var svc = IdOf(await Ok(await admin.Post("/api/catalog/services", new { name = $"Svc {Uniq()}", categoryId = cat, owningTeamId = team })));
        await Ok(await admin.Post($"/api/catalog/services/{svc}/publish"));
        return new World(f, clock, seed, mgr, sup, agent, au, lead, lu, req, ru, svc, team, teamName);
    }

    private static async Task<Guid> Resolved(World w, string subject = "Printer jams")
    {
        var id = IdOf(await Ok(await w.Requester.Post($"/api/portal/services/{w.ServiceId}/requests", new { subject, answers = new { } })));
        await Ok(await w.Supervisor.Post($"/api/tickets/{id}/assign", new { assigneeId = w.AgentUser.Id, teamId = w.TeamId }));
        await Ok(await w.Agent.Post($"/api/tickets/{id}/transition", new { action = "start" }));
        await Ok(await w.Agent.Post($"/api/tickets/{id}/transition", new { action = "resolve", resolutionCode = "fixed", resolutionNote = "Done" }));
        return id;
    }

    [Fact]
    public async Task The_requester_rates_a_resolved_request_can_change_it_and_a_low_score_alerts_the_team_lead_once()
    {
        var w = await Build();
        var open = IdOf(await Ok(await w.Requester.Post($"/api/portal/services/{w.ServiceId}/requests", new { subject = "Not yet done", answers = new { } })));
        Assert.Equal(HttpStatusCode.Conflict, (await w.Requester.SendAsync(HttpMethod.Put, $"/api/tickets/{open}/rating", new { score = 5 })).StatusCode);   // not resolved yet
        Assert.False((await Ok(await w.Requester.Get($"/api/tickets/{open}"))).GetProperty("rating").GetProperty("canRate").GetBoolean());

        var id = await Resolved(w);
        var mine = (await Ok(await w.Requester.Get($"/api/tickets/{id}"))).GetProperty("rating");
        Assert.True(mine.GetProperty("canRate").GetBoolean()); Assert.Equal(JsonValueKind.Null, mine.GetProperty("score").ValueKind); Assert.Equal(JsonValueKind.String, mine.GetProperty("rateUntil").ValueKind);
        Assert.False((await Ok(await w.Agent.Get($"/api/tickets/{id}"))).GetProperty("rating").GetProperty("canRate").GetBoolean());
        Assert.Equal(HttpStatusCode.NotFound, (await w.Agent.SendAsync(HttpMethod.Put, $"/api/tickets/{id}/rating", new { score = 5 })).StatusCode);          // staff can't rate for the requester

        var bad = (await w.Requester.SendAsync(HttpMethod.Put, $"/api/tickets/{id}/rating", new { score = 6, comment = new string('x', 1001) }));
        Assert.Equal(Invalid, bad.StatusCode);
        var fields = (await bad.Body()).GetProperty("error").GetProperty("fields");
        Assert.True(fields.TryGetProperty("score", out _)); Assert.True(fields.TryGetProperty("comment", out _));

        var r = await Ok(await w.Requester.SendAsync(HttpMethod.Put, $"/api/tickets/{id}/rating", new { score = 2, comment = "Took three visits." }));
        Assert.Equal(2, r.GetProperty("score").GetInt32()); Assert.Equal("Took three visits.", Str(r, "comment"));
        async Task<int> Alerts() => await w.Seed.Db(db => db.Notifications.CountAsync(n => n.UserId == w.LeadUser.Id && n.Kind == "ticket.low_rating" && n.TicketId == id));
        Assert.Equal(1, await Alerts());
        await w.Seed.Mail.WaitFor(w.LeadUser.Email, "rated 2 out of 5");
        Assert.Equal(0, await w.Seed.Db(db => db.Notifications.CountAsync(n => n.UserId == w.AgentUser.Id && n.Kind == "ticket.low_rating")));       // the lead, not the whole team

        await Ok(await w.Requester.SendAsync(HttpMethod.Put, $"/api/tickets/{id}/rating", new { score = 1 }));
        Assert.Equal(1, await Alerts());                                                                                                             // once per ticket
        var staffView = await Ok(await w.Agent.Get($"/api/tickets/{id}"));
        Assert.Equal(1, staffView.GetProperty("rating").GetProperty("score").GetInt32());
        Assert.Contains(staffView.GetProperty("timeline").EnumerateArray(), e => e.GetProperty("body").GetString() == "Changed the rating from 2 to 1 out of 5.");
        Assert.Equal(1, await w.Seed.Db(db => db.TicketRatings.CountAsync(x => x.TicketId == id)));
    }

    [Fact]
    public async Task The_report_shows_everything_to_supervisors_only_own_ratings_to_agents_and_feeds_the_vendor_scorecard()
    {
        var w = await Build();
        var a = await Resolved(w, "Lamp broken"); var b = await Resolved(w, "Door squeaks");
        var vendor = IdOf(await Ok(await w.Manager.Post("/api/vendors", new { name = $"FixCo {Uniq()}" })));
        await Ok(await w.Requester.Post($"/api/tickets/{b}/transition", new { action = "reopen", reason = "Still squeaks" }));
        await Ok(await w.Supervisor.Post($"/api/tickets/{b}/work-orders", new { title = "Oil the hinges", vendorId = vendor, assigneeId = w.AgentUser.Id }));
        await Ok(await w.Agent.Post($"/api/tickets/{b}/transition", new { action = "start" }));
        var wo = (await Ok(await w.Agent.Get($"/api/tickets/{b}/work-orders"))).GetProperty("items")[0];
        await Ok(await w.Agent.Post($"/api/work-orders/{IdOf(wo)}/action", new { action = "start" }));
        await Ok(await w.Agent.Post($"/api/work-orders/{IdOf(wo)}/action", new { action = "complete", note = "Oiled" }));
        await Ok(await w.Agent.Post($"/api/tickets/{b}/transition", new { action = "resolve", resolutionCode = "fixed", resolutionNote = "Oiled" }));
        await Ok(await w.Requester.SendAsync(HttpMethod.Put, $"/api/tickets/{a}/rating", new { score = 5, comment = "Quick, thanks!" }));
        await Ok(await w.Requester.SendAsync(HttpMethod.Put, $"/api/tickets/{b}/rating", new { score = 3 }));

        var byTeam = await Ok(await w.Manager.Get("/api/reports/csat?days=30&by=team"));
        Assert.False(byTeam.GetProperty("ownOnly").GetBoolean());
        var row = byTeam.GetProperty("rows").EnumerateArray().Single(x => Str(x, "label") == w.TeamName);
        Assert.Equal(2, row.GetProperty("count").GetInt32()); Assert.Equal(4.0, row.GetProperty("average").GetDouble()); Assert.Equal(50, row.GetProperty("satisfiedPercent").GetInt32());
        Assert.Contains(byTeam.GetProperty("comments").EnumerateArray(), c => Str(c, "comment") == "Quick, thanks!" && c.GetProperty("score").GetInt32() == 5);

        var own = await Ok(await w.Agent.Get("/api/reports/csat?days=30&by=agent"));
        Assert.True(own.GetProperty("ownOnly").GetBoolean());
        Assert.Equal(2, own.GetProperty("count").GetInt32());                                           // only the agent's own two
        Assert.Equal(new[] { 0, 0, 1, 0, 1 }, own.GetProperty("distribution").EnumerateArray().Select(x => x.GetInt32()).ToArray());
        Assert.Equal(0, (await Ok(await w.Lead.Get("/api/reports/csat?days=30"))).GetProperty("count").GetInt32());   // the lead worked none of them

        var byVendor = await Ok(await w.Manager.Get("/api/reports/csat?days=30&by=vendor"));
        Assert.Contains(byVendor.GetProperty("rows").EnumerateArray(), x => x.GetProperty("key").GetString() == vendor.ToString() && x.GetProperty("count").GetInt32() == 1);
        var score = (await Ok(await w.Manager.Get($"/api/vendors/{vendor}"))).GetProperty("score");
        Assert.Equal(3.0, score.GetProperty("rating").GetDouble()); Assert.Equal(1, score.GetProperty("ratings").GetInt32());

        Assert.Equal(HttpStatusCode.Forbidden, (await w.Requester.Get("/api/reports/csat")).StatusCode);
        Assert.Equal(Invalid, (await w.Manager.Get("/api/reports/csat?by=mood")).StatusCode);
    }

    [Fact]
    public async Task Rating_closes_fourteen_days_after_resolution_and_planned_maintenance_is_never_rated()
    {
        var w = await Build(isolated: true);
        var id = await Resolved(w);
        w.Clock.Advance(TimeSpan.FromDays(15));
        await w.Requester.LoginAsync(w.RequesterUser.Email, Seed.Password);
        var late = await w.Requester.SendAsync(HttpMethod.Put, $"/api/tickets/{id}/rating", new { score = 4 });
        Assert.Equal(HttpStatusCode.Conflict, late.StatusCode);
        Assert.Contains("14 days", await late.Content.ReadAsStringAsync());

        // a maintenance visit's ticket, filed for the plan's owner
        var owner = await w.Seed.UserAsync(role: "ga_manager"); var ownerBrowser = TestBrowser.Create(w.F);
        await ownerBrowser.LoginAsync(owner.Email, Seed.Password);
        var room = IdOf(await Ok(await ownerBrowser.Post("/api/locations", new { type = "site", code = "R-" + Uniq(), name = "Pump room" })));
        var plan = await Ok(await ownerBrowser.Post("/api/maintenance/plans", new { name = "Pump check", serviceId = w.ServiceId, locationId = room, unit = "week", every = 1, startsOn = w.Clock.UtcNow.AddDays(2).ToString("yyyy-MM-dd"), leadDays = 14, assigneeId = w.AgentUser.Id }));
        var visit = plan.GetProperty("visits")[0];
        await w.Agent.LoginAsync(w.AgentUser.Email, Seed.Password);
        var woId = visit.GetProperty("workOrderId").GetGuid();
        await Ok(await w.Agent.Post($"/api/work-orders/{woId}/action", new { action = "start" }));
        await Ok(await w.Agent.Post($"/api/work-orders/{woId}/action", new { action = "complete", note = "Checked" }));
        var ticket = visit.GetProperty("ticketId").GetGuid();
        Assert.Equal("resolved", Str(await Ok(await ownerBrowser.Get($"/api/tickets/{ticket}")), "status"));
        Assert.False((await Ok(await ownerBrowser.Get($"/api/tickets/{ticket}"))).GetProperty("rating").GetProperty("canRate").GetBoolean());
        Assert.Equal(HttpStatusCode.Conflict, (await ownerBrowser.SendAsync(HttpMethod.Put, $"/api/tickets/{ticket}/rating", new { score = 5 })).StatusCode);
    }
}
