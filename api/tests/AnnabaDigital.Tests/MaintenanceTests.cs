using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using AnnabaDigital.Application.Helpdesk;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Tests.Infra;
using Xunit;

namespace AnnabaDigital.Tests;

/// <summary>Preventive maintenance (PM-001/002/003, R2 slice 5): plans, who may change them, the scheduler making visits ahead of time without duplicates, catching up after downtime, pausing, and the compliance report.</summary>
[Collection("db")]
public sealed class MaintenanceTests(PostgresFixture fx)
{
    private static string Uniq() => Guid.NewGuid().ToString("N")[..8];
    private const HttpStatusCode Invalid = (HttpStatusCode)422;
    private static async Task<JsonElement> Ok(HttpResponseMessage r) { Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync()); return r.StatusCode == HttpStatusCode.NoContent ? default : await r.Body(); }
    private static Guid IdOf(JsonElement e) => e.GetProperty("id").GetGuid();
    private static string Str(JsonElement e, string p) => e.GetProperty(p).GetString()!;
    private static async Task<JsonElement> Fields(HttpResponseMessage r) { Assert.Equal(Invalid, r.StatusCode); return (await r.Body()).GetProperty("error").GetProperty("fields"); }

    private sealed record World(ApiFactory F, TestClock Clock, Seed Seed, TestBrowser Manager, TestBrowser Supervisor, User SupervisorUser, TestBrowser Agent, User AgentUser, TestBrowser Requester, Guid ServiceId, Guid TeamId, Guid TypeId);

    private async Task<World> Build(bool isolated = false)
    {
        ApiFactory f; TestClock clock;
        if (isolated) { var iso = await fx.NewIsolatedAsync(); f = iso.Factory; clock = iso.Clock; } else { f = fx.Factory; clock = fx.Clock; }
        var seed = new Seed(f, clock);
        async Task<(User, TestBrowser)> Person(string role) { var u = await seed.UserAsync(role: role); var b = TestBrowser.Create(f); await b.LoginAsync(u.Email, Seed.Password); return (u, b); }
        var (_, mgr) = await Person("ga_manager"); var (su, sup) = await Person("ga_supervisor"); var (au, agent) = await Person("ga_agent"); var (_, req) = await Person("user"); var (_, admin) = await Person("super_admin");
        var team = IdOf(await Ok(await admin.Post("/api/teams", new { name = $"Team {Uniq()}" })));
        await Ok(await admin.SendAsync(HttpMethod.Put, $"/api/teams/{team}/members", new { members = new[] { new { userId = au.Id, isLead = false } } }));
        var cat = IdOf(await Ok(await admin.Post("/api/catalog/categories", new { name = $"Cat {Uniq()}" })));
        var svc = IdOf(await Ok(await admin.Post("/api/catalog/services", new { name = $"Planned maintenance {Uniq()}", categoryId = cat, owningTeamId = team })));   // left unpublished: staff-only
        var type = IdOf(await Ok(await mgr.Post("/api/asset-types", new { name = $"Air conditioner {Uniq()}" })));
        return new World(f, clock, seed, mgr, sup, su, agent, au, req, svc, team, type);
    }

    /// <summary>The organisation's date (the default calendar's zone: Asia/Jakarta).</summary>
    private static DateOnly Today(TestClock c) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(c.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Asia/Jakarta")).DateTime);
    private static string D(DateOnly d) => d.ToString("yyyy-MM-dd");
    private static async Task<Guid> NewAsset(World w, string name, Guid? locationId = null)
        => IdOf(await Ok(await w.Manager.Post("/api/assets", new { name, typeId = w.TypeId, status = "in_use", locationId })));
    /// <summary>Moves the clock; sessions do not survive weeks, so the people sign in again.</summary>
    private static async Task Later(World w, int days) { w.Clock.Advance(TimeSpan.FromDays(days)); await w.Supervisor.LoginAsync(w.SupervisorUser.Email, Seed.Password); await w.Agent.LoginAsync(w.AgentUser.Email, Seed.Password); }
    private static async Task<int> Run(World w) { using var s = w.F.Services.CreateScope(); return await s.ServiceProvider.GetRequiredService<MaintenanceService>().RunDueAsync(); }

    [Fact]
    public async Task Plans_need_a_service_a_target_and_a_rhythm_and_only_planners_change_them()
    {
        var w = await Build();
        var bad = await Fields(await w.Supervisor.Post("/api/maintenance/plans", new { name = "x", unit = "fortnight", every = 1, startsOn = D(Today(w.Clock)) }));
        foreach (var k in new[] { "name", "serviceId", "target", "unit" }) Assert.True(bad.TryGetProperty(k, out _), k);
        var asset = await NewAsset(w, "Lobby AC");
        bad = await Fields(await w.Supervisor.Post("/api/maintenance/plans", new { name = $"Too often {Uniq()}", serviceId = w.ServiceId, assetId = asset, assetTypeId = w.TypeId, unit = "month", every = 30, startsOn = D(Today(w.Clock)), endsOn = D(Today(w.Clock).AddDays(-1)), leadDays = 120 }));
        foreach (var k in new[] { "target", "every", "endsOn", "leadDays" }) Assert.True(bad.TryGetProperty(k, out _), k);

        Assert.Equal(HttpStatusCode.Forbidden, (await w.Agent.Post("/api/maintenance/plans", new { name = "Agent plan" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Requester.Get("/api/maintenance/plans")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await w.Agent.Get("/api/maintenance/plans")).StatusCode);

        // a plan far in the future makes nothing yet and can be removed; its next dates are shown
        var start = Today(w.Clock).AddDays(60);
        var later = await Ok(await w.Supervisor.Post("/api/maintenance/plans", new { name = $"Later {Uniq()}", serviceId = w.ServiceId, assetId = asset, unit = "month", every = 3, startsOn = D(start), leadDays = 7 }));
        Assert.Equal(D(start), Str(later, "nextDueOn"));
        Assert.Equal(new[] { D(start), D(start.AddMonths(3)), D(start.AddMonths(6)), D(start.AddMonths(9)), D(start.AddMonths(12)) }, later.GetProperty("upcoming").EnumerateArray().Select(x => x.GetString()).ToArray());
        Assert.Empty(later.GetProperty("visits").EnumerateArray());
        Assert.True((await Fields(await w.Supervisor.Post("/api/maintenance/plans", new { name = Str(later, "name").ToUpperInvariant(), serviceId = w.ServiceId, assetId = asset, unit = "month", every = 1, startsOn = D(start) }))).TryGetProperty("name", out _));
        Assert.Equal(HttpStatusCode.NoContent, (await w.Supervisor.SendAsync(HttpMethod.Delete, $"/api/maintenance/plans/{IdOf(later)}")).StatusCode);

        // a plan for a place (no asset) starting today makes its visit at once
        var room = IdOf(await Ok(await w.Manager.Post("/api/locations", new { type = "site", code = "PM-" + Uniq(), name = "Server room" })));
        var placePlan = await Ok(await w.Supervisor.Post("/api/maintenance/plans", new { name = $"Fire extinguisher check {Uniq()}", serviceId = w.ServiceId, locationId = room, unit = "week", every = 2, startsOn = D(Today(w.Clock)), leadDays = 0 }));
        var visit = Assert.Single(placePlan.GetProperty("visits").EnumerateArray());
        Assert.Equal("open", Str(visit, "state")); Assert.Equal("Server room", Str(visit, "locationName"));
        var ticket = await w.Seed.Db(db => db.Tickets.FirstAsync(t => t.Id == visit.GetProperty("ticketId").GetGuid()));
        Assert.Equal(room, ticket.LocationId); Assert.Null(ticket.AssetId); Assert.Equal(w.TeamId, ticket.TeamId); Assert.Equal(SlaState.None, ticket.SlaState);
        Assert.Equal(HttpStatusCode.Conflict, (await w.Supervisor.SendAsync(HttpMethod.Delete, $"/api/maintenance/plans/{IdOf(placePlan)}")).StatusCode);   // it has history: pause instead
    }

    [Fact]
    public async Task Saving_a_plan_makes_the_work_due_within_its_lead_time_once_and_finishing_it_counts_as_on_time()
    {
        var w = await Build();
        var site = IdOf(await Ok(await w.Manager.Post("/api/locations", new { type = "site", code = "PS-" + Uniq(), name = "Tower A" })));
        var floor = IdOf(await Ok(await w.Manager.Post("/api/locations", new { type = "floor", parentId = site, code = "PF-" + Uniq(), name = "Floor 3" })));
        var elsewhere = IdOf(await Ok(await w.Manager.Post("/api/locations", new { type = "site", code = "PE-" + Uniq(), name = "Warehouse" })));
        var a1 = await NewAsset(w, "AC meeting room", floor); var a2 = await NewAsset(w, "AC pantry", site); await NewAsset(w, "AC warehouse", elsewhere);
        var gone = await NewAsset(w, "AC old", floor);
        await Ok(await w.Manager.Post($"/api/assets/{gone}/retire", new { reason = "Replaced by a new unit" }));
        var vendor = IdOf(await Ok(await w.Manager.Post("/api/vendors", new { name = $"CoolCo {Uniq()}" })));
        var checklist = IdOf(await Ok(await w.Supervisor.Post("/api/checklist-templates", new { name = $"AC service {Uniq()}", items = new[] { new { text = "Clean the filters", required = true }, new { text = "Note the pressure", required = false } } })));

        var due = Today(w.Clock).AddDays(3);
        var plan = await Ok(await w.Supervisor.Post("/api/maintenance/plans", new
        {
            name = $"AC quarterly {Uniq()}", instructions = "Switch the unit off first.", serviceId = w.ServiceId, assetTypeId = w.TypeId, locationId = site, unit = "month", every = 3, startsOn = D(due), leadDays = 7,
            checklistTemplateId = checklist, teamId = w.TeamId, assigneeId = w.AgentUser.Id, vendorId = vendor, requireAfterPhoto = false,
        }));
        Assert.Equal(2, plan.GetProperty("targets").GetInt32());
        Assert.Equal(D(due.AddMonths(3)), Str(plan, "nextDueOn"));
        var visits = plan.GetProperty("visits").EnumerateArray().ToList();
        Assert.Equal(2, visits.Count);
        Assert.Equal(new[] { a1, a2 }.OrderBy(x => x), visits.Select(v => v.GetProperty("assetId").GetGuid()).OrderBy(x => x));
        Assert.All(visits, v => { Assert.Equal("open", Str(v, "state")); Assert.Equal(D(due), Str(v, "dueOn")); });

        // the ticket and work order carry the plan
        var woId = visits[0].GetProperty("workOrderId").GetGuid();
        var wo = await Ok(await w.Agent.Get($"/api/work-orders/{woId}"));
        Assert.Equal(Str(plan, "name"), Str(wo, "title")); Assert.Equal("Switch the unit off first.", Str(wo, "instructions"));
        Assert.Equal(w.AgentUser.Id, wo.GetProperty("assigneeId").GetGuid()); Assert.Equal(vendor, wo.GetProperty("vendorId").GetGuid());
        Assert.Equal(new[] { "Clean the filters", "Note the pressure" }, wo.GetProperty("tasks").EnumerateArray().Select(t => Str(t, "text")).ToArray());
        Assert.True(wo.GetProperty("dueAt").GetDateTimeOffset() > w.Clock.UtcNow);
        var ticket = await Ok(await w.Agent.Get($"/api/tickets/{wo.GetProperty("ticketId").GetGuid()}"));
        Assert.Equal("assigned", Str(ticket, "status")); Assert.Contains(Str(plan, "name"), Str(ticket, "subject"));
        Assert.Equal(visits[0].GetProperty("assetId").GetGuid(), ticket.GetProperty("asset").GetProperty("id").GetGuid());
        Assert.True(await w.Seed.Db(db => db.Notifications.AnyAsync(n => n.UserId == w.AgentUser.Id && n.Kind == "workorder.assigned" && n.Link == $"/work-orders/{woId}")));

        // running the scheduler again makes nothing more
        await Run(w);
        Assert.Equal(2, await w.Seed.Db(db => db.MaintenanceVisits.CountAsync(v => v.PlanId == IdOf(plan))));

        // doing the work resolves the ticket and counts as on time
        await Ok(await w.Agent.Post($"/api/work-orders/{woId}/action", new { action = "start" }));
        var line = wo.GetProperty("tasks")[0];
        await Ok(await w.Agent.Post($"/api/work-orders/{woId}/tasks/{IdOf(line)}", new { done = true }));
        await Ok(await w.Agent.Post($"/api/work-orders/{woId}/action", new { action = "complete", note = "Filters cleaned" }));
        Assert.Equal("resolved", Str(await Ok(await w.Agent.Get($"/api/tickets/{wo.GetProperty("ticketId").GetGuid()}")), "status"));
        var after = await Ok(await w.Agent.Get($"/api/maintenance/plans/{IdOf(plan)}"));
        Assert.Contains(after.GetProperty("visits").EnumerateArray(), v => Str(v, "state") == "on_time");
        Assert.False(after.GetProperty("canManage").GetBoolean());

        // the list shows it with one visit still open
        var row = (await Ok(await w.Agent.Get($"/api/maintenance/plans?q={Uri.EscapeDataString(Str(plan, "name"))}"))).GetProperty("data")[0];
        Assert.Equal(1, row.GetProperty("openVisits").GetInt32()); Assert.Equal("month", Str(row, "unit"));
        Assert.Contains(IdOf(plan), (await Ok(await w.Agent.Get($"/api/maintenance/plans?assetId={a1}"))).GetProperty("data").EnumerateArray().Select(IdOf));
    }

    [Fact]
    public async Task After_downtime_overtaken_dates_are_missed_only_the_current_one_is_made_pausing_skips_and_two_runs_never_duplicate()
    {
        var w = await Build(isolated: true);
        var asset = await NewAsset(w, "Generator");
        var start = Today(w.Clock).AddDays(1);
        var plan = await Ok(await w.Supervisor.Post("/api/maintenance/plans", new { name = "Generator weekly", serviceId = w.ServiceId, assetId = asset, unit = "week", every = 1, startsOn = D(start), leadDays = 0, teamId = w.TeamId }));
        var id = IdOf(plan);
        Assert.Empty(plan.GetProperty("visits").EnumerateArray());

        // the scheduler was down for three weeks: start, +7 and +14 were overtaken; +21 is due today and is made
        await Later(w, 22);
        Assert.Equal(1, await Run(w));
        Assert.Equal(0, await Run(w));
        var states = (await Ok(await w.Agent.Get($"/api/maintenance/plans/{id}"))).GetProperty("visits").EnumerateArray().OrderBy(v => Str(v, "dueOn")).Select(v => (Str(v, "dueOn"), Str(v, "state"))).ToList();
        Assert.Equal(new[] { (D(start), "missed"), (D(start.AddDays(7)), "missed"), (D(start.AddDays(14)), "missed"), (D(start.AddDays(21)), "open") }, states);
        Assert.True(await w.Seed.Db(db => db.AuditEvents.AnyAsync(a => a.Action == "pm.catch_up" && a.TargetId == id.ToString())));

        var report = await Ok(await w.Agent.Get($"/api/maintenance/compliance?from={D(start)}&to={D(start.AddDays(21))}&by=plan"));
        var total = report.GetProperty("total");
        Assert.Equal(4, total.GetProperty("planned").GetInt32()); Assert.Equal(3, total.GetProperty("missed").GetInt32()); Assert.Equal(1, total.GetProperty("open").GetInt32());
        Assert.Equal(0, total.GetProperty("percent").GetInt32());
        Assert.Equal("In-house", Str((await Ok(await w.Agent.Get($"/api/maintenance/compliance?from={D(start)}&to={D(start.AddDays(21))}&by=vendor"))).GetProperty("rows")[0], "label"));
        Assert.Equal(Invalid, (await w.Agent.Get($"/api/maintenance/compliance?from={D(start)}&to={D(start.AddDays(-1))}")).StatusCode);
        Assert.Equal(Invalid, (await w.Agent.Get($"/api/maintenance/compliance?from={D(start)}&to={D(start.AddDays(400))}")).StatusCode);
        Assert.Equal(Invalid, (await w.Agent.Get("/api/maintenance/compliance?by=colour")).StatusCode);

        // paused for a fortnight: nothing is made and nothing is missed; resuming continues from today
        await Ok(await w.Supervisor.Post($"/api/maintenance/plans/{id}/pause"));
        await Later(w, 14);
        Assert.Equal(0, await Run(w));
        var resumed = await Ok(await w.Supervisor.Post($"/api/maintenance/plans/{id}/resume"));
        Assert.Equal(5, resumed.GetProperty("visits").GetArrayLength());           // +35 is today: made on resume
        Assert.DoesNotContain(resumed.GetProperty("visits").EnumerateArray(), v => Str(v, "dueOn") == D(start.AddDays(28)));

        // two scheduler instances at once make the next visit once
        await Later(w, 7);
        var runs = await Task.WhenAll(Run(w), Run(w));
        Assert.Equal(1, runs.Sum());
        Assert.Equal(1, await w.Seed.Db(db => db.MaintenanceVisits.CountAsync(v => v.PlanId == id && v.DueOn == start.AddDays(42))));
    }

    [Fact]
    public void Due_dates_are_counted_from_the_start_so_month_ends_do_not_drift()
    {
        var jan31 = new DateOnly(2027, 1, 31);
        Assert.Equal(new DateOnly(2027, 2, 28), MaintenanceRules.DueOn(jan31, PmUnit.Month, 1, 1));
        Assert.Equal(new DateOnly(2027, 3, 31), MaintenanceRules.DueOn(jan31, PmUnit.Month, 1, 2));
        Assert.Equal(2, MaintenanceRules.FirstIndexOnOrAfter(jan31, PmUnit.Month, 1, new DateOnly(2027, 3, 1)));
        Assert.Equal(0, MaintenanceRules.FirstIndexOnOrAfter(jan31, PmUnit.Week, 2, new DateOnly(2027, 1, 1)));
        Assert.Equal(3, MaintenanceRules.FirstIndexOnOrAfter(jan31, PmUnit.Week, 2, new DateOnly(2027, 3, 10)));
        Assert.Equal("late", MaintenanceRules.VisitState(false, WorkOrderStatus.Done, DateTimeOffset.UnixEpoch.AddDays(2), DateTimeOffset.UnixEpoch.AddDays(1), jan31, jan31));
        Assert.Equal("overdue", MaintenanceRules.VisitState(false, WorkOrderStatus.InProgress, null, null, jan31, jan31.AddDays(1)));
        Assert.Equal(["2027-01-31", "2028-01-31"], MaintenanceRules.Upcoming(jan31, PmUnit.Year, 1, 0, new DateOnly(2028, 6, 1), 5).Select(d => d.ToString("yyyy-MM-dd")));
    }
}
