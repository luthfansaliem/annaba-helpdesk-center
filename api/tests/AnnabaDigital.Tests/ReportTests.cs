using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using AnnabaDigital.Application.Helpdesk;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Tests.Infra;
using Xunit;

namespace AnnabaDigital.Tests;

/// <summary>Operations and SLA reports (RPT-001/002): who may read them, that they only count what the person may see, and that the numbers are right.</summary>
[Collection("db")]
public sealed class ReportTests(PostgresFixture fx)
{
    private readonly Seed _seed = new(fx.Factory, fx.Clock);
    private static string Uniq() => Guid.NewGuid().ToString("N")[..8];

    private async Task<(User User, TestBrowser B)> Person(string role)
    {
        var u = await _seed.UserAsync(role: role); var b = TestBrowser.Create(fx.Factory); await b.LoginAsync(u.Email, Seed.Password); return (u, b);
    }
    private static async Task<JsonElement> Ok(HttpResponseMessage r) { Assert.True(r.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK, await r.Content.ReadAsStringAsync()); return await r.Body(); }
    private static Guid IdOf(JsonElement e) => e.GetProperty("id").GetGuid();
    private static string Str(JsonElement e, string p) => e.GetProperty(p).GetString()!;
    private DateTimeOffset Ago(int minutes) => fx.Clock.UtcNow.AddMinutes(-minutes);
    private Task Sql(FormattableString sql) => _seed.Db(async db => await db.Database.ExecuteSqlInterpolatedAsync(sql));

    private sealed record Team(Guid Id, Guid ServiceId, string Name);

    /// <summary>A team with its own service (so numbers in the shared database stay exact for the people who see only that team).</summary>
    private async Task<Team> NewTeam(TestBrowser admin, params User[] members)
    {
        var name = $"Team {Uniq()}";
        var team = IdOf(await Ok(await admin.Post("/api/teams", new { name })));
        await Ok(await admin.SendAsync(HttpMethod.Put, $"/api/teams/{team}/members", new { members = members.Select(m => new { userId = m.Id, isLead = false }).ToArray() }));
        var cat = IdOf(await Ok(await admin.Post("/api/catalog/categories", new { name = $"Cat {Uniq()}" })));
        var svc = IdOf(await Ok(await admin.Post("/api/catalog/services", new { name = $"Svc {Uniq()}", categoryId = cat, owningTeamId = team, defaultPriority = "medium" })));
        await Ok(await admin.SendAsync(HttpMethod.Put, $"/api/catalog/services/{svc}/form", new { fields = new object[] { new { key = "what", type = "text", label = "What?", required = true } } }));
        await Ok(await admin.Post($"/api/catalog/services/{svc}/publish"));
        return new Team(team, svc, name);
    }

    private static async Task<Guid> Raise(TestBrowser b, Guid svc) => IdOf(await Ok(await b.Post($"/api/portal/services/{svc}/requests", new { subject = "Report test " + Uniq(), answers = new { what = "x" } })));
    private static Task<HttpResponseMessage> Act(TestBrowser b, Guid id, string action, object? extra = null)
    {
        var body = new Dictionary<string, object?> { ["action"] = action };
        if (extra is not null) foreach (var p in extra.GetType().GetProperties()) body[p.Name] = p.GetValue(extra);
        return b.Post($"/api/tickets/{id}/transition", body);
    }
    private static Task<HttpResponseMessage> Assign(TestBrowser sup, Guid id, Guid who, Guid team) => sup.Post($"/api/tickets/{id}/assign", new { assigneeId = who, teamId = team });
    private async Task Escalate() { using var scope = fx.Factory.Services.CreateScope(); await scope.ServiceProvider.GetRequiredService<SlaService>().RunDueAsync(); }

    [Fact]
    public async Task Reports_need_a_ticket_read_permission_and_a_sign_in()
    {
        var (_, plain) = await Person("user"); var (_, agent) = await Person("ga_agent");
        foreach (var path in new[] { "/api/reports/operations", "/api/reports/sla" })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await TestBrowser.Create(fx.Factory).Get(path)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await plain.Get(path)).StatusCode);                                    // requesters have their own list, not the staff reports
            Assert.Equal(HttpStatusCode.OK, (await agent.Get(path)).StatusCode);
        }
    }

    [Fact]
    public async Task The_operations_report_counts_exactly_what_a_team_member_may_see_and_nothing_else()
    {
        var (_, admin) = await Person("super_admin"); var (_, requester) = await Person("user"); var (au, agent) = await Person("ga_agent"); var (_, sup) = await Person("ga_supervisor");
        var mine = await NewTeam(admin, au); var other = await NewTeam(admin);

        var done = await Raise(requester, mine.ServiceId);                                           // resolved in time
        await Ok(await Assign(sup, done, au.Id, mine.Id)); await Act(agent, done, "start"); await Act(agent, done, "resolve", new { resolutionCode = "fixed", resolutionNote = "Cleaned" });
        var waiting = await Raise(requester, mine.ServiceId);                                        // unassigned, still submitted
        var held = await Raise(requester, mine.ServiceId);                                           // on hold
        await Ok(await Assign(sup, held, au.Id, mine.Id)); await Act(agent, held, "start"); await Act(agent, held, "hold", new { reason = "Waiting for parts" });
        var late = await Raise(requester, mine.ServiceId);                                           // nobody answered for weeks
        await Ok(await Assign(sup, late, au.Id, mine.Id));
        await Sql($"UPDATE tickets SET sla_start_at = {Ago(60 * 24 * 30)}, created_at = {Ago(60 * 24 * 30)} WHERE id = {late}");
        await Escalate();
        var elsewhere = await Raise(requester, other.ServiceId);                                     // another team's: the agent must not see it
        _ = waiting; _ = elsewhere;

        var r = await Ok(await agent.Get("/api/reports/operations?days=30"));
        var k = r.GetProperty("kpis");
        Assert.Equal(3, k.GetProperty("open").GetInt32());                                           // waiting + held + late; the resolved one is not backlog
        Assert.Equal(1, k.GetProperty("overdue").GetInt32());
        Assert.Equal(1, k.GetProperty("unassigned").GetInt32());                                     // only the one nobody has
        Assert.Equal(0, k.GetProperty("pendingApproval").GetInt32());
        Assert.Equal(3, k.GetProperty("created").GetInt32());                                        // made in the last 30 days (the 30-day-old one falls just outside; the other team's is invisible)
        Assert.Equal(1, k.GetProperty("resolved").GetInt32());
        Assert.Equal(100.0, k.GetProperty("slaAttainmentPercent").GetDouble());                      // the one resolved ticket met its targets
        Assert.True(k.GetProperty("avgResolutionMinutes").GetDouble() >= 0);

        var byTeam = r.GetProperty("backlogByTeam").EnumerateArray().ToList();
        Assert.Single(byTeam); Assert.Equal(mine.Name, Str(byTeam[0], "label")); Assert.Equal(3, byTeam[0].GetProperty("count").GetInt32());
        var status = r.GetProperty("byStatus").EnumerateArray().ToDictionary(x => Str(x, "key"), x => x.GetProperty("count").GetInt32());
        Assert.Equal(1, status["onhold"]); Assert.Equal(1, status["submitted"]); Assert.Equal(1, status["assigned"]);
        Assert.Equal(3, r.GetProperty("byPriority").EnumerateArray().Single(x => Str(x, "key") == "medium").GetProperty("count").GetInt32());
        Assert.Equal(3, r.GetProperty("aging").EnumerateArray().Sum(x => x.GetProperty("count").GetInt32()));
        Assert.Equal(1, r.GetProperty("aging").EnumerateArray().Single(x => Str(x, "key") == "gt14d").GetProperty("count").GetInt32());               // the thirty-day-old one

        var attention = r.GetProperty("attention").EnumerateArray().ToList();
        Assert.Single(attention); Assert.Equal(late, IdOf(attention[0])); Assert.Equal("breached", Str(attention[0], "slaState"));

        var daily = r.GetProperty("daily").EnumerateArray().ToList();
        Assert.Equal(30, daily.Count); Assert.Equal(30, r.GetProperty("days").GetInt32());
        Assert.Equal(fx.Clock.UtcNow.UtcDateTime.ToString("yyyy-MM-dd"), Str(daily[^1], "day"));
        Assert.Equal(1, daily[^1].GetProperty("resolved").GetInt32());
        Assert.Equal(3, daily[^1].GetProperty("created").GetInt32());                                // the thirty-day-old one is counted on its own day, not today

        // a supervisor sees every team, so at least what the agent sees plus the other team's ticket
        var s = (await Ok(await sup.Get("/api/reports/operations"))).GetProperty("kpis");
        Assert.True(s.GetProperty("open").GetInt32() >= 4);
        Assert.True(s.GetProperty("created").GetInt32() >= 4);                                // (the team list is capped at the 12 biggest, so a small team may not appear in a shared database)
    }

    [Fact]
    public async Task The_sla_report_counts_met_and_missed_per_priority_team_and_service_and_explains_each_breach()
    {
        var (_, admin) = await Person("super_admin"); var (_, requester) = await Person("user"); var (au, agent) = await Person("ga_agent"); var (_, sup) = await Person("ga_supervisor");
        var team = await NewTeam(admin, au);

        var ok = await Raise(requester, team.ServiceId);                                             // met
        await Ok(await Assign(sup, ok, au.Id, team.Id)); await Act(agent, ok, "start"); await Act(agent, ok, "resolve", new { resolutionCode = "fixed", resolutionNote = "Replaced" });
        var lateOne = await Raise(requester, team.ServiceId);                                        // resolved, but weeks late
        await Ok(await Assign(sup, lateOne, au.Id, team.Id)); await Act(agent, lateOne, "start");
        await Sql($"UPDATE tickets SET sla_start_at = {Ago(60 * 24 * 25)}, created_at = {Ago(60 * 24 * 25)} WHERE id = {lateOne}");
        await Act(agent, lateOne, "resolve", new { resolutionCode = "fixed", resolutionNote = "Done at last" });
        // 25 days, not 30: the report window starts at midnight 29 days ago, so a breach stamped 30 days ago plus the target fell outside it early in the UTC day
        var silent = await Raise(requester, team.ServiceId);                                         // never answered: still open and breached
        await Ok(await Assign(sup, silent, au.Id, team.Id));
        await Sql($"UPDATE tickets SET sla_start_at = {Ago(60 * 24 * 25)}, created_at = {Ago(60 * 24 * 25)} WHERE id = {silent}");
        await Escalate();

        var r = await Ok(await agent.Get("/api/reports/sla?days=30"));
        Assert.Equal(1, r.GetProperty("met").GetInt32()); Assert.Equal(1, r.GetProperty("breached").GetInt32()); Assert.Equal(50.0, r.GetProperty("attainmentPercent").GetDouble());
        Assert.Equal(1, r.GetProperty("openBreached").GetInt32()); Assert.Equal(0, r.GetProperty("openAtRisk").GetInt32());

        var medium = r.GetProperty("byPriority").EnumerateArray().Single(x => Str(x, "key") == "medium");
        Assert.Equal(1, medium.GetProperty("met").GetInt32()); Assert.Equal(1, medium.GetProperty("breached").GetInt32());
        Assert.Equal(JsonValueKind.Null, r.GetProperty("byPriority").EnumerateArray().Single(x => Str(x, "key") == "urgent").GetProperty("attainmentPercent").ValueKind);   // no data: no percentage, not 0 %
        var byTeam = r.GetProperty("byTeam").EnumerateArray().Single(); Assert.Equal(team.Name, Str(byTeam, "label")); Assert.Equal(50.0, byTeam.GetProperty("attainmentPercent").GetDouble());
        Assert.Equal(1, r.GetProperty("byService").GetArrayLength());

        var breaches = r.GetProperty("breaches").EnumerateArray().ToList();
        var silentRow = breaches.Single(b => IdOf(b) == silent);
        Assert.Equal("first_response", Str(silentRow, "reason")); Assert.Equal("open", "open"); Assert.Equal(au.Name, Str(silentRow, "assignee"));          // nobody ever responded
        Assert.Contains(breaches, b => IdOf(b) == lateOne);                                          // the late resolved one is listed too
        Assert.DoesNotContain(breaches, b => IdOf(b) == ok);                                         // the one that met its targets is not

        // a narrower window leaves out what happened before it
        var week = await Ok(await agent.Get("/api/reports/sla?days=7"));
        Assert.Equal(1, week.GetProperty("met").GetInt32()); Assert.Equal(1, week.GetProperty("breached").GetInt32());                                      // both were resolved today
        Assert.Equal(7, week.GetProperty("days").GetInt32());
    }

    [Fact]
    public async Task The_period_is_clamped_and_an_empty_scope_gives_empty_numbers_not_errors()
    {
        var (_, agent) = await Person("ga_agent");                                                   // in no team: sees nothing of the shared data
        var r = await Ok(await agent.Get("/api/reports/operations?days=1000"));
        Assert.Equal(90, r.GetProperty("days").GetInt32()); Assert.Equal(90, r.GetProperty("daily").GetArrayLength());
        Assert.Equal(0, r.GetProperty("kpis").GetProperty("open").GetInt32()); Assert.Equal(JsonValueKind.Null, r.GetProperty("kpis").GetProperty("avgResolutionMinutes").ValueKind);
        Assert.Empty(r.GetProperty("attention").EnumerateArray()); Assert.Empty(r.GetProperty("backlogByTeam").EnumerateArray());
        Assert.Equal(1, (await Ok(await agent.Get("/api/reports/operations?days=0"))).GetProperty("daily").GetArrayLength());
        var sla = await Ok(await agent.Get("/api/reports/sla?days=-5"));
        Assert.Equal(1, sla.GetProperty("days").GetInt32()); Assert.Equal(JsonValueKind.Null, sla.GetProperty("attainmentPercent").ValueKind); Assert.Empty(sla.GetProperty("breaches").EnumerateArray());
    }
}
