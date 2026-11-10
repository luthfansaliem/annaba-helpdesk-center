using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using AnnabaDigital.Application.Helpdesk;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Tests.Infra;
using Xunit;

namespace AnnabaDigital.Tests;

/// <summary>SLA policies, the ticket clock and escalation (SLA-002/004/005/006) and assignment rules (TKT-003): admin rules, routing at creation, and the clock through a ticket's life.</summary>
[Collection("db")]
public sealed class SlaTicketTests(PostgresFixture fx)
{
    private readonly Seed _seed = new(fx.Factory, fx.Clock);
    private static string Uniq() => Guid.NewGuid().ToString("N")[..8];
    private static readonly HttpMethod Patch = HttpMethod.Patch;
    private const HttpStatusCode Invalid = (HttpStatusCode)422;

    private async Task<(User User, TestBrowser B)> Person(string role)
    {
        var u = await _seed.UserAsync(role: role); var b = TestBrowser.Create(fx.Factory); await b.LoginAsync(u.Email, Seed.Password); return (u, b);
    }
    private static async Task<JsonElement> Ok(HttpResponseMessage r) { Assert.True(r.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK, await r.Content.ReadAsStringAsync()); return await r.Body(); }
    private static async Task<List<JsonElement>> List(HttpResponseMessage r) => (await Ok(r)).EnumerateArray().ToList();
    private static Guid IdOf(JsonElement e) => e.GetProperty("id").GetGuid();
    private static string Str(JsonElement e, string p) => e.GetProperty(p).GetString()!;
    private static int Ver(JsonElement e) => e.GetProperty("version").GetInt32();
    private static DateTimeOffset At(JsonElement e, string p) => e.GetProperty(p).GetDateTimeOffset();
    /// <summary>A moment this many minutes before the application's clock (which the tests keep still), so timestamps written straight to the database agree with what the service computes.</summary>
    private DateTimeOffset Ago(int minutes) => fx.Clock.UtcNow.AddMinutes(-minutes);
    private Task Sql(FormattableString sql) => _seed.Db(async db => await db.Database.ExecuteSqlInterpolatedAsync(sql));

    private sealed record World(TestBrowser Admin, TestBrowser Requester, User RequesterUser, TestBrowser Agent, User AgentUser, TestBrowser Supervisor, Guid ServiceId, Guid TeamId, Guid CalendarId);

    /// <summary>A published service whose tickets land in a team with one agent, on a 24/7 calendar so durations are exact.</summary>
    private async Task<World> Build()
    {
        var (_, admin) = await Person("super_admin"); var (ru, requester) = await Person("user"); var (au, agent) = await Person("ga_agent"); var (_, sup) = await Person("ga_supervisor");
        var team = IdOf(await Ok(await admin.Post("/api/teams", new { name = $"Team {Uniq()}" })));
        await Ok(await admin.SendAsync(HttpMethod.Put, $"/api/teams/{team}/members", new { members = new[] { new { userId = au.Id, isLead = false } } }));
        var cat = IdOf(await Ok(await admin.Post("/api/catalog/categories", new { name = $"Cat {Uniq()}" })));
        var svc = IdOf(await Ok(await admin.Post("/api/catalog/services", new { name = $"Svc {Uniq()}", categoryId = cat, owningTeamId = team, defaultPriority = "medium" })));
        await Ok(await admin.SendAsync(HttpMethod.Put, $"/api/catalog/services/{svc}/form", new { fields = new object[] { new { key = "what", type = "text", label = "What?", required = true } } }));
        await Ok(await admin.Post($"/api/catalog/services/{svc}/publish"));
        var cal = IdOf(await Ok(await admin.Post("/api/calendars", new { name = $"24/7 {Uniq()}", timeZone = "Asia/Jakarta", isAlwaysOpen = true, periods = Array.Empty<object>(), holidays = Array.Empty<object>() })));
        return new World(admin, requester, ru, agent, au, sup, svc, team, cal);
    }

    /// <summary>Makes <paramref name="policy"/> the first one tried, so it wins over the defaults for its own service.</summary>
    private static async Task First(TestBrowser admin, Guid item, string path = "/api/sla")
    {
        var list = path == "/api/sla" ? "/api/sla/policies" : path; var reorder = path == "/api/sla" ? "/api/sla/policies/reorder" : $"{path}/reorder";
        var ids = (await List(await admin.Get(list))).Select(IdOf).ToList(); ids.Remove(item); ids.Insert(0, item);
        await Ok(await admin.Post(reorder, new { ids }));
    }
    private async Task<Guid> Policy(World w, string name, int first, int resolve, string? priority = null, int e1 = 50, int e2 = 80)
    {
        var id = IdOf(await Ok(await w.Admin.Post("/api/sla/policies", new { name = $"{name} {Uniq()}", serviceId = w.ServiceId, priority, calendarId = w.CalendarId, firstResponseMinutes = first, resolutionMinutes = resolve, escalate1Percent = e1, escalate2Percent = e2 })));
        await First(w.Admin, id);
        return id;
    }

    private static Task<HttpResponseMessage> Raise(TestBrowser b, Guid svc, string subject = "Printer is broken") => b.Post($"/api/portal/services/{svc}/requests", new { subject, answers = new { what = "paper jam" } });
    private static Task<HttpResponseMessage> Act(TestBrowser b, Guid id, string action, object? extra = null)
    {
        var body = new Dictionary<string, object?> { ["action"] = action };
        if (extra is not null) foreach (var p in extra.GetType().GetProperties()) body[p.Name] = p.GetValue(extra);
        return b.Post($"/api/tickets/{id}/transition", body);
    }
    private static JsonElement Sla(JsonElement ticket) => ticket.GetProperty("sla");
    private static async Task<JsonElement> Staff(World w, Guid id) => await Ok(await w.Supervisor.Get($"/api/tickets/{id}"));
    private async Task Escalate() { using var scope = fx.Factory.Services.CreateScope(); await scope.ServiceProvider.GetRequiredService<SlaService>().RunDueAsync(); }

    // ============================================================ SLA policies: admin
    [Fact]
    public async Task SLA_policies_need_their_permissions_start_with_defaults_and_are_validated()
    {
        var w = await Build(); var (_, manager) = await Person("ga_manager"); var (_, plain) = await Person("user");
        Assert.Equal(HttpStatusCode.Forbidden, (await plain.Get("/api/sla/policies")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await w.Supervisor.Get("/api/sla/policies")).StatusCode);                                      // supervisors can read
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Supervisor.Post("/api/sla/policies", new { name = "Nope", firstResponseMinutes = 10, resolutionMinutes = 20 })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await TestBrowser.Create(fx.Factory).Get("/api/sla/policies")).StatusCode);

        var defaults = (await List(await w.Supervisor.Get("/api/sla/policies"))).Where(p => Str(p, "name").EndsWith("requests")).ToList();
        Assert.True(defaults.Count >= 4); Assert.Contains(defaults, p => Str(p, "name") == "Urgent requests" && p.GetProperty("firstResponseMinutes").GetInt32() == 15);

        object P(string name, int fr, int res, int? e1 = null, int? e2 = null) => new { name, serviceId = w.ServiceId, firstResponseMinutes = fr, resolutionMinutes = res, escalate1Percent = e1, escalate2Percent = e2, calendarId = w.CalendarId };
        var made = await Ok(await manager.Post("/api/sla/policies", P($"Gold {Uniq()}", 30, 120)));                                      // a manager manages them
        Assert.Equal(50, made.GetProperty("escalate1Percent").GetInt32()); Assert.Equal(80, made.GetProperty("escalate2Percent").GetInt32()); Assert.Equal(1, made.GetProperty("conditions").GetInt32());
        Assert.Equal(Invalid, (await manager.Post("/api/sla/policies", P("x", 30, 120))).StatusCode);                                    // name too short
        Assert.Equal(Invalid, (await manager.Post("/api/sla/policies", P($"A {Uniq()}", 0, 120))).StatusCode);
        Assert.Equal(Invalid, (await manager.Post("/api/sla/policies", P($"B {Uniq()}", 120, 30))).StatusCode);                          // resolution before first response
        Assert.Equal(Invalid, (await manager.Post("/api/sla/policies", P($"C {Uniq()}", 30, 120, 90, 80))).StatusCode);                  // thresholds must rise
        Assert.Equal(Invalid, (await manager.Post("/api/sla/policies", P($"D {Uniq()}", 30, 120, 50, 100))).StatusCode);
        Assert.Equal(Invalid, (await manager.Post("/api/sla/policies", P(Str(made, "name"), 30, 120))).StatusCode);                      // duplicate name
        Assert.Equal(Invalid, (await manager.Post("/api/sla/policies", new { name = $"E {Uniq()}", serviceId = Guid.NewGuid(), firstResponseMinutes = 1, resolutionMinutes = 2 })).StatusCode);
        Assert.Equal(Invalid, (await manager.Post("/api/sla/policies", new { name = $"F {Uniq()}", priority = "asap", firstResponseMinutes = 1, resolutionMinutes = 2 })).StatusCode);

        var upd = await Ok(await manager.SendAsync(Patch, $"/api/sla/policies/{IdOf(made)}", new { name = Str(made, "name"), serviceId = w.ServiceId, calendarId = w.CalendarId, firstResponseMinutes = 45, resolutionMinutes = 180, escalate1Percent = 40, escalate2Percent = 70, version = Ver(made) }));
        Assert.Equal(45, upd.GetProperty("firstResponseMinutes").GetInt32());
        Assert.Equal(HttpStatusCode.Conflict, (await manager.SendAsync(Patch, $"/api/sla/policies/{IdOf(made)}", new { name = Str(made, "name"), firstResponseMinutes = 1, resolutionMinutes = 2, version = Ver(made) })).StatusCode);
        Assert.Contains("sla.policy.update", await _seed.Db(db => db.AuditEvents.Where(a => a.TargetId == IdOf(made).ToString()).Select(a => a.Action).ToListAsync()));

        Assert.Equal("true", (await Ok(await manager.Post($"/api/sla/policies/{IdOf(made)}/archive"))).GetProperty("archived").GetBoolean().ToString().ToLowerInvariant());
        Assert.Equal(HttpStatusCode.Conflict, (await manager.Post($"/api/sla/policies/{IdOf(made)}/archive")).StatusCode);
        Assert.False((await Ok(await manager.Post($"/api/sla/policies/{IdOf(made)}/restore"))).GetProperty("archived").GetBoolean());
    }

    [Fact]
    public async Task Policies_are_tried_in_order_and_the_order_is_saved_as_a_whole()
    {
        var w = await Build();
        var a = await Policy(w, "Wide", 60, 600);
        var all = (await List(await w.Admin.Get("/api/sla/policies"))).Select(IdOf).ToList();
        Assert.Equal(a, all[0]);
        Assert.Equal(Invalid, (await w.Admin.Post("/api/sla/policies/reorder", new { ids = all.Skip(1).ToArray() })).StatusCode);                  // must list every active policy
        Assert.Equal(Invalid, (await w.Admin.Post("/api/sla/policies/reorder", new { ids = all.Append(all[0]).ToArray() })).StatusCode);
        Assert.Equal(Invalid, (await w.Admin.Post("/api/sla/policies/reorder", new { ids = all.Select(_ => Guid.NewGuid()).ToArray() })).StatusCode);

        var preview = await Ok(await w.Admin.Post("/api/sla/preview", new { serviceId = w.ServiceId, priority = "medium" }));
        Assert.Equal(a, preview.GetProperty("policyId").GetGuid()); Assert.Equal(600, preview.GetProperty("resolutionMinutes").GetInt32());
        var rest = all.Skip(1).Append(a).ToArray();                                                                                             // move it last: the defaults win again
        await Ok(await w.Admin.Post("/api/sla/policies/reorder", new { ids = rest }));
        var after = await Ok(await w.Admin.Post("/api/sla/preview", new { serviceId = w.ServiceId, priority = "medium" }));
        Assert.Equal("Standard requests", Str(after, "policyName"));
        Assert.Equal(Invalid, (await w.Admin.Post("/api/sla/preview", new { serviceId = Guid.NewGuid() })).StatusCode);
    }

    // ============================================================ the clock on a ticket
    [Fact]
    public async Task A_new_ticket_gets_the_matching_policy_with_exact_deadlines_and_requesters_only_see_the_expected_date()
    {
        var w = await Build(); var policy = await Policy(w, "Exact", 30, 240);
        var t = await Ok(await Raise(w.Requester, w.ServiceId));
        Assert.True(t.GetProperty("sla").ValueKind == JsonValueKind.Null); Assert.NotEqual(JsonValueKind.Null, t.GetProperty("expectedBy").ValueKind);            // a requester sees one date, nothing else
        var s = await Staff(w, IdOf(t)); var sla = Sla(s);
        Assert.Equal("ontrack", Str(sla, "state")); Assert.StartsWith("Exact", Str(sla, "policyName"));
        Assert.InRange((At(sla, "resolutionDueAt") - At(s, "createdAt")).TotalMinutes, 239.9, 240.1);
        Assert.InRange((At(sla, "firstResponseDueAt") - At(s, "createdAt")).TotalMinutes, 29.9, 30.1);
        Assert.Equal(At(sla, "resolutionDueAt"), At(s, "expectedBy"));
        Assert.Equal(policy.ToString(), (await _seed.Db(db => db.Tickets.Where(x => x.Id == IdOf(t)).Select(x => x.SlaPolicyId).SingleAsync())).ToString());

        var listRow = (await Ok(await w.Supervisor.Get($"/api/tickets?q={Str(t, "number")}"))).GetProperty("data")[0];
        Assert.Equal("ontrack", Str(listRow, "slaState"));
        var mine = (await Ok(await w.Requester.Get("/api/tickets/mine"))).GetProperty("data").EnumerateArray().First(x => IdOf(x) == IdOf(t));
        Assert.Equal(JsonValueKind.Null, mine.GetProperty("slaState").ValueKind);                                                                              // state is for staff
    }

    [Fact]
    public async Task The_first_public_answer_from_staff_is_the_first_response_and_notes_and_the_requester_do_not_count()
    {
        var w = await Build(); await Policy(w, "FR", 30, 240);
        var id = IdOf(await Ok(await Raise(w.Requester, w.ServiceId)));
        await Ok(await w.Requester.Post($"/api/tickets/{id}/comments", new { body = "Any news?" }));
        await Ok(await w.Agent.Post($"/api/tickets/{id}/comments", new { body = "Looking at it", @internal = true }));
        Assert.Equal(JsonValueKind.Null, Sla(await Staff(w, id)).GetProperty("firstRespondedAt").ValueKind);
        await Ok(await w.Agent.Post($"/api/tickets/{id}/comments", new { body = "We are on it." }));
        var first = At(Sla(await Staff(w, id)), "firstRespondedAt");
        await Ok(await w.Agent.Post($"/api/tickets/{id}/comments", new { body = "Update." }));
        Assert.Equal(first, At(Sla(await Staff(w, id)), "firstRespondedAt"));                                                                                  // it is set once

        var other = IdOf(await Ok(await Raise(w.Requester, w.ServiceId)));                                                                                    // starting work also counts
        await Ok(await Act(w.Agent, other, "start"));
        Assert.NotEqual(JsonValueKind.Null, Sla(await Staff(w, other)).GetProperty("firstRespondedAt").ValueKind);
    }

    [Fact]
    public async Task Holding_a_ticket_pauses_the_clock_and_resuming_pushes_the_deadline_out_by_the_time_held()
    {
        var w = await Build(); await Policy(w, "Pause", 30, 240);
        var id = IdOf(await Ok(await Raise(w.Requester, w.ServiceId)));
        await Ok(await Act(w.Agent, id, "start"));
        var before = At(Sla(await Staff(w, id)), "resolutionDueAt");

        var held = await Ok(await Act(w.Agent, id, "hold", new { reason = "Waiting for parts" }));
        Assert.Equal("paused", Str(Sla(await Staff(w, id)), "state")); Assert.NotEqual(JsonValueKind.Null, Sla(await Staff(w, id)).GetProperty("pausedAt").ValueKind);
        _ = held;
        await Sql($"UPDATE tickets SET sla_paused_at = {Ago(120)} WHERE id = {id}");                                                       // pretend it has been on hold for two hours
        var resumed = await Ok(await Act(w.Agent, id, "resume"));
        var sla = Sla(await Staff(w, id));
        Assert.Equal("ontrack", Str(sla, "state")); Assert.Equal(JsonValueKind.Null, sla.GetProperty("pausedAt").ValueKind);
        Assert.InRange((At(sla, "resolutionDueAt") - before).TotalMinutes, 119, 121);
        _ = resumed;
    }

    [Fact]
    public async Task Changing_the_priority_re_matches_the_policy_without_giving_back_time_already_used()
    {
        var w = await Build();
        var urgent = await Policy(w, "Urgent only", 5, 60, priority: "urgent"); var normal = await Policy(w, "Normal", 60, 600, priority: "medium");
        await First(w.Admin, normal); await First(w.Admin, urgent);                                                                                           // either order works: the conditions differ
        var t = await Ok(await Raise(w.Requester, w.ServiceId)); var id = IdOf(t);
        Assert.StartsWith("Normal", Str(Sla(await Staff(w, id)), "policyName"));
        await Sql($"UPDATE tickets SET sla_start_at = {Ago(30)}, created_at = {Ago(30)} WHERE id = {id}");

        var up = await Ok(await w.Agent.SendAsync(Patch, $"/api/tickets/{id}", new { subject = "Printer is broken", priority = "urgent", version = Ver(await Staff(w, id)) }));
        _ = up;
        var s = await Staff(w, id);
        Assert.StartsWith("Urgent only", Str(Sla(s), "policyName")); Assert.Equal(60, Sla(s).GetProperty("resolutionMinutes").GetInt32());
        Assert.InRange((At(Sla(s), "resolutionDueAt") - fx.Clock.UtcNow).TotalMinutes, 28, 31);                                                          // 60 minutes counted from the original start, 30 already gone
    }

    [Fact]
    public async Task Resolving_in_time_is_met_resolving_late_is_breached_and_a_reopened_ticket_starts_a_fresh_clock_but_keeps_the_breach()
    {
        var w = await Build(); await Policy(w, "Resolve", 30, 60);
        var ok = IdOf(await Ok(await Raise(w.Requester, w.ServiceId)));
        await Ok(await Act(w.Agent, ok, "start")); await Ok(await Act(w.Agent, ok, "resolve", new { resolutionCode = "fixed", resolutionNote = "Cleaned the sensor" }));
        Assert.Equal("met", Str(Sla(await Staff(w, ok)), "state"));

        var late = IdOf(await Ok(await Raise(w.Requester, w.ServiceId)));
        await Ok(await Act(w.Agent, late, "start"));
        await Sql($"UPDATE tickets SET sla_start_at = {Ago(180)}, created_at = {Ago(180)} WHERE id = {late}");
        await Ok(await Act(w.Agent, late, "resolve", new { resolutionCode = "fixed", resolutionNote = "Replaced the roller" }));
        var breached = Sla(await Staff(w, late));
        Assert.Equal("breached", Str(breached, "state")); var when = At(breached, "breachedAt");

        await Ok(await Act(w.Requester, late, "reopen", new { reason = "It jams again" }));
        var again = Sla(await Staff(w, late));
        Assert.Equal("ontrack", Str(again, "state")); Assert.Equal(when, At(again, "breachedAt"));                                                              // the breach stays on record
        Assert.InRange((At(again, "resolutionDueAt") - fx.Clock.UtcNow).TotalMinutes, 58, 61);
    }

    // ============================================================ escalation by the periodic check
    [Fact]
    public async Task The_periodic_check_escalates_once_per_threshold_and_a_breach_is_permanent()
    {
        var w = await Build(); await Policy(w, "Escalate", 60, 600);
        var id = IdOf(await Ok(await Raise(w.Requester, w.ServiceId)));
        await Ok(await w.Agent.Post($"/api/tickets/{id}/comments", new { body = "We are on it." }));                                                           // answered: only the resolution target is left
        var escalations = async () => (await Staff(w, id)).GetProperty("timeline").EnumerateArray().Where(e => Str(e, "kind") == "escalation").Select(e => Str(e, "body")).ToList();
        async Task Age(int minutes) { await Sql($"UPDATE tickets SET sla_start_at = {Ago(minutes)}, created_at = {Ago(minutes)}, first_responded_at = {Ago(minutes - 5)} WHERE id = {id}"); await Escalate(); }

        await Age(100); Assert.Empty(await escalations()); Assert.Equal("ontrack", Str(Sla(await Staff(w, id)), "state"));
        await Age(330); var l1 = await escalations(); Assert.Single(l1); Assert.Contains("50%", l1[0]); Assert.Equal("ontrack", Str(Sla(await Staff(w, id)), "state"));
        await Age(340); Assert.Single(await escalations());                                                                                                     // running again adds nothing
        await Age(500); Assert.Equal(2, (await escalations()).Count); Assert.Equal("atrisk", Str(Sla(await Staff(w, id)), "state"));
        await Age(700); var l3 = await escalations(); Assert.Equal(3, l3.Count); Assert.Contains("breached", l3[2]);
        var s = await Staff(w, id); Assert.Equal("breached", Str(Sla(s), "state")); Assert.Equal(3, Sla(s).GetProperty("escalationLevel").GetInt32()); Assert.NotEqual(JsonValueKind.Null, Sla(s).GetProperty("breachedAt").ValueKind);

        Assert.DoesNotContain((await Ok(await w.Requester.Get($"/api/tickets/{id}"))).GetProperty("timeline").EnumerateArray(), e => Str(e, "kind") == "escalation");     // internal: never shown to requesters
        Assert.Contains("ticket.sla_escalation", await _seed.Db(db => db.AuditEvents.Where(a => a.TargetId == id.ToString()).Select(a => a.Action).ToListAsync()));

        var breachedList = (await Ok(await w.Supervisor.Get($"/api/tickets?sla=breached&teamId={w.TeamId}"))).GetProperty("data").EnumerateArray().Select(IdOf).ToList();
        Assert.Contains(id, breachedList);
        Assert.DoesNotContain(id, (await Ok(await w.Supervisor.Get($"/api/tickets?sla=ontrack&teamId={w.TeamId}"))).GetProperty("data").EnumerateArray().Select(IdOf));
        Assert.Equal(Invalid, (await w.Supervisor.Get("/api/tickets?sla=nonsense")).StatusCode);
    }

    [Fact]
    public async Task A_ticket_on_hold_is_not_escalated_and_the_queue_can_be_sorted_by_deadline()
    {
        var w = await Build(); await Policy(w, "Hold", 60, 600);
        var held = IdOf(await Ok(await Raise(w.Requester, w.ServiceId)));
        await Ok(await Act(w.Agent, held, "start")); await Ok(await Act(w.Agent, held, "hold", new { reason = "Waiting" }));
        await Sql($"UPDATE tickets SET sla_start_at = {Ago(2880)}, created_at = {Ago(2880)} WHERE id = {held}");
        await Escalate();
        Assert.Equal("paused", Str(Sla(await Staff(w, held)), "state"));                                                                                        // the clock is stopped, so nothing is overdue

        var soon = IdOf(await Ok(await Raise(w.Requester, w.ServiceId))); var later = IdOf(await Ok(await Raise(w.Requester, w.ServiceId)));
        await Sql($"UPDATE tickets SET resolution_due_at = {Ago(-60)} WHERE id = {soon}");
        await Sql($"UPDATE tickets SET resolution_due_at = {Ago(-300)} WHERE id = {later}");
        var order = (await Ok(await w.Supervisor.Get($"/api/tickets?teamId={w.TeamId}&status=open&sort=due"))).GetProperty("data").EnumerateArray().Select(IdOf).ToList();
        Assert.True(order.IndexOf(soon) < order.IndexOf(later));
    }

    // ============================================================ assignment rules: admin
    [Fact]
    public async Task Assignment_rules_need_their_permissions_are_validated_and_ordered()
    {
        var w = await Build(); var (_, manager) = await Person("ga_manager"); var (_, plain) = await Person("user");
        Assert.Equal(HttpStatusCode.Forbidden, (await plain.Get("/api/assignment-rules")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await w.Supervisor.Get("/api/assignment-rules")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Supervisor.Post("/api/assignment-rules", new { name = "Nope", teamId = w.TeamId })).StatusCode);

        object R(string name, Guid? team = null, string? strategy = "round_robin", Guid? service = null) => new { name, teamId = team ?? w.TeamId, strategy, serviceId = service ?? w.ServiceId };
        var made = await Ok(await manager.Post("/api/assignment-rules", R($"Route {Uniq()}")));
        Assert.Equal("roundrobin", Str(made, "strategy")); Assert.Equal(1, made.GetProperty("conditions").GetInt32());
        Assert.Equal(Invalid, (await manager.Post("/api/assignment-rules", R("x"))).StatusCode);
        Assert.Equal(Invalid, (await manager.Post("/api/assignment-rules", R($"B {Uniq()}", Guid.NewGuid()))).StatusCode);
        Assert.Equal(Invalid, (await manager.Post("/api/assignment-rules", new { name = $"C {Uniq()}" })).StatusCode);                                         // a team is required
        Assert.Equal(Invalid, (await manager.Post("/api/assignment-rules", R($"D {Uniq()}", strategy: "random"))).StatusCode);
        Assert.Equal(Invalid, (await manager.Post("/api/assignment-rules", R(Str(made, "name")))).StatusCode);                                                 // duplicate name

        var upd = await Ok(await manager.SendAsync(Patch, $"/api/assignment-rules/{IdOf(made)}", new { name = Str(made, "name"), teamId = w.TeamId, strategy = "least_loaded", serviceId = w.ServiceId, version = Ver(made) }));
        Assert.Equal("leastloaded", Str(upd, "strategy"));
        Assert.Equal(HttpStatusCode.Conflict, (await manager.SendAsync(Patch, $"/api/assignment-rules/{IdOf(made)}", new { name = Str(made, "name"), teamId = w.TeamId, version = Ver(made) })).StatusCode);
        Assert.Contains("rule.update", await _seed.Db(db => db.AuditEvents.Where(a => a.TargetId == IdOf(made).ToString()).Select(a => a.Action).ToListAsync()));

        Assert.Equal(HttpStatusCode.Conflict, (await w.Admin.Post($"/api/teams/{w.TeamId}/archive")).StatusCode);                                              // a team that rules send tickets to can't be archived
        var ids = (await List(await manager.Get("/api/assignment-rules"))).Select(IdOf).ToList();
        Assert.Equal(Invalid, (await manager.Post("/api/assignment-rules/reorder", new { ids = Array.Empty<Guid>() })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await manager.Post("/api/assignment-rules/reorder", new { ids = Enumerable.Reverse(ids).ToArray() })).StatusCode);
        Assert.True((await Ok(await manager.Post($"/api/assignment-rules/{IdOf(made)}/archive"))).GetProperty("archived").GetBoolean());
        Assert.False((await Ok(await manager.Post($"/api/assignment-rules/{IdOf(made)}/restore"))).GetProperty("archived").GetBoolean());
    }

    // ============================================================ routing at creation
    [Fact]
    public async Task Without_a_matching_rule_the_service_team_gets_the_ticket_and_nobody_is_assigned()
    {
        var w = await Build();
        var t = await Ok(await Raise(w.Requester, w.ServiceId));
        Assert.Equal("submitted", Str(t, "status")); Assert.Equal(JsonValueKind.Null, t.GetProperty("assigneeId").ValueKind); Assert.Equal(w.TeamId, t.GetProperty("teamId").GetGuid());
    }

    [Fact]
    public async Task Round_robin_alternates_between_the_people_of_the_team_who_work_tickets()
    {
        var w = await Build(); var (second, _) = await Person("ga_agent"); var (bystander, _) = await Person("user");
        await Ok(await w.Admin.SendAsync(HttpMethod.Put, $"/api/teams/{w.TeamId}/members", new { members = new[] { new { userId = w.AgentUser.Id, isLead = false }, new { userId = second.Id, isLead = false }, new { userId = bystander.Id, isLead = false } } }));
        var rule = IdOf(await Ok(await w.Admin.Post("/api/assignment-rules", new { name = $"RR {Uniq()}", teamId = w.TeamId, strategy = "round_robin", serviceId = w.ServiceId })));
        await First(w.Admin, rule, "/api/assignment-rules");

        var picked = new List<Guid>();
        for (var i = 0; i < 4; i++)
        {
            var t = await Ok(await Raise(w.Requester, w.ServiceId));
            Assert.Equal("assigned", Str(t, "status")); picked.Add(t.GetProperty("assigneeId").GetGuid());
            Assert.Contains(t.GetProperty("timeline").EnumerateArray(), e => Str(e, "kind") == "assigned" && Str(e, "body").Contains("by rule"));
        }
        Assert.DoesNotContain(bystander.Id, picked);                                                                                                            // someone who can't work tickets is never picked
        Assert.Equal(new[] { w.AgentUser.Id, second.Id }.Order().ToArray(), picked.Take(2).Order().ToArray());                                                 // the first two go to different people
        Assert.True(picked[0] == picked[2] && picked[1] == picked[3], $"picks: {string.Join(" > ", picked)} (agent {w.AgentUser.Id}, second {second.Id})");                                                                                 // then it goes round again
    }

    [Fact]
    public async Task Least_loaded_prefers_whoever_has_the_fewest_open_tickets_and_preview_shows_why()
    {
        var w = await Build(); var (second, _) = await Person("ga_agent");
        await Ok(await w.Admin.SendAsync(HttpMethod.Put, $"/api/teams/{w.TeamId}/members", new { members = new[] { new { userId = w.AgentUser.Id, isLead = false }, new { userId = second.Id, isLead = false } } }));
        var rule = IdOf(await Ok(await w.Admin.Post("/api/assignment-rules", new { name = $"LL {Uniq()}", teamId = w.TeamId, strategy = "least_loaded", serviceId = w.ServiceId })));
        await First(w.Admin, rule, "/api/assignment-rules");

        var first = await Ok(await Raise(w.Requester, w.ServiceId)); var a = first.GetProperty("assigneeId").GetGuid();
        var other = a == w.AgentUser.Id ? second.Id : w.AgentUser.Id;
        var preview = await Ok(await w.Admin.Post("/api/assignment-rules/preview", new { serviceId = w.ServiceId }));
        Assert.Equal(other, preview.GetProperty("assigneeId").GetGuid());                                                                                       // the one with nothing open is next
        Assert.Equal(1, preview.GetProperty("candidates").EnumerateArray().Single(c => IdOf(c) == a).GetProperty("openTickets").GetInt32());
        Assert.Equal(other, (await Ok(await Raise(w.Requester, w.ServiceId))).GetProperty("assigneeId").GetGuid());
        Assert.Equal(Invalid, (await w.Admin.Post("/api/assignment-rules/preview", new { })).StatusCode);
    }

    [Fact]
    public async Task The_first_matching_rule_wins_and_conditions_on_priority_and_place_are_respected()
    {
        var w = await Build(); var (second, secondBrowser) = await Person("ga_agent");
        var team2 = IdOf(await Ok(await w.Admin.Post("/api/teams", new { name = $"Team2 {Uniq()}" })));
        await Ok(await w.Admin.SendAsync(HttpMethod.Put, $"/api/teams/{team2}/members", new { members = new[] { new { userId = second.Id, isLead = false } } }));
        var site = IdOf(await Ok(await w.Admin.Post("/api/locations", new { type = "site", code = "R-" + Uniq(), name = "Rule Site" })));
        var floor = IdOf(await Ok(await w.Admin.Post("/api/locations", new { type = "building", parentId = site, code = "RB" + Uniq(), name = "Rule Tower" })));

        var general = IdOf(await Ok(await w.Admin.Post("/api/assignment-rules", new { name = $"General {Uniq()}", teamId = w.TeamId, strategy = "none", serviceId = w.ServiceId })));
        var atSite = IdOf(await Ok(await w.Admin.Post("/api/assignment-rules", new { name = $"AtSite {Uniq()}", teamId = team2, strategy = "round_robin", serviceId = w.ServiceId, locationId = site })));
        await First(w.Admin, general, "/api/assignment-rules");                                                                                                  // general first: it catches everything for this service
        var t1 = await Ok(await w.Requester.Post($"/api/portal/services/{w.ServiceId}/requests", new { subject = "Printer at the tower", answers = new { what = "x" }, locationId = floor }));
        Assert.Equal(w.TeamId, t1.GetProperty("teamId").GetGuid()); Assert.Equal("submitted", Str(t1, "status"));

        await First(w.Admin, atSite, "/api/assignment-rules");                                                                                                   // the narrower rule first: a ticket inside the site goes to team 2
        var t2 = await Ok(await w.Requester.Post($"/api/portal/services/{w.ServiceId}/requests", new { subject = "Printer at the tower", answers = new { what = "x" }, locationId = floor }));
        Assert.Equal(team2, t2.GetProperty("teamId").GetGuid()); Assert.Equal(second.Id, t2.GetProperty("assigneeId").GetGuid());
        var t3 = await Ok(await Raise(w.Requester, w.ServiceId));                                                                                                // no place: the narrow rule doesn't match, the general one does
        Assert.Equal(w.TeamId, t3.GetProperty("teamId").GetGuid());
        Assert.Equal(HttpStatusCode.OK, (await secondBrowser.Get($"/api/tickets/{IdOf(t2)}")).StatusCode);                                                      // and the agent it was routed to can open it
    }
}
