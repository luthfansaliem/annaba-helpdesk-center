using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Tests.Infra;
using Xunit;

namespace AnnabaDigital.Tests;

/// <summary>
/// Work orders (TKT-006/007/014): who sees and changes them, numbering, checklists from templates, the moves, what completing needs, photo evidence, time and costs,
/// and how they hold, cancel and resolve their ticket.
/// </summary>
[Collection("db")]
public sealed class WorkOrderTests(PostgresFixture fx)
{
    private readonly Seed _seed = new(fx.Factory, fx.Clock);
    private static string Uniq() => Guid.NewGuid().ToString("N")[..8];
    private const HttpStatusCode Invalid = (HttpStatusCode)422;
    private static async Task<JsonElement> Ok(HttpResponseMessage r) { Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync()); return await r.Body(); }
    private static Guid IdOf(JsonElement e) => e.GetProperty("id").GetGuid();
    private static string Str(JsonElement e, string p) => e.GetProperty(p).GetString()!;
    private static async Task<JsonElement> Fields(HttpResponseMessage r) { Assert.Equal(Invalid, r.StatusCode); return (await r.Body()).GetProperty("error").GetProperty("fields"); }
    private static byte[] Png() { var b = new byte[64]; new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(b, 0); Random.Shared.NextBytes(b.AsSpan(8)); return b; }

    private async Task<(User U, TestBrowser B)> Person(string role) { var u = await _seed.UserAsync(role: role); var b = TestBrowser.Create(fx.Factory); await b.LoginAsync(u.Email, Seed.Password); return (u, b); }

    private sealed record World(TestBrowser Sup, User SupUser, TestBrowser Agent, User AgentUser, TestBrowser Agent2, User Agent2User, TestBrowser Requester, Guid ServiceId, Guid TeamId);

    private async Task<World> Build()
    {
        var (su, sup) = await Person("ga_supervisor"); var (au, agent) = await Person("ga_agent"); var (a2u, agent2) = await Person("ga_agent"); var (_, req) = await Person("user"); var (_, admin) = await Person("super_admin");
        var team = IdOf(await Ok(await admin.Post("/api/teams", new { name = $"Team {Uniq()}" })));
        await Ok(await admin.SendAsync(HttpMethod.Put, $"/api/teams/{team}/members", new { members = new[] { new { userId = au.Id, isLead = false }, new { userId = a2u.Id, isLead = false } } }));
        var cat = IdOf(await Ok(await admin.Post("/api/catalog/categories", new { name = $"Cat {Uniq()}" })));
        var svc = IdOf(await Ok(await admin.Post("/api/catalog/services", new { name = $"Svc {Uniq()}", categoryId = cat, owningTeamId = team })));
        await Ok(await admin.Post($"/api/catalog/services/{svc}/publish"));
        return new World(sup, su, agent, au, agent2, a2u, req, svc, team);
    }

    private static async Task<(Guid Id, string Number)> Ticket(World w, string subject = "Leaking roof")
    {
        var t = await Ok(await w.Requester.Post($"/api/portal/services/{w.ServiceId}/requests", new { subject, answers = new { } }));
        return (IdOf(t), Str(t, "number"));
    }
    private static async Task<JsonElement> NewWo(World w, Guid ticket, object body) => await Ok(await w.Sup.Post($"/api/tickets/{ticket}/work-orders", body));
    private static Task<HttpResponseMessage> Act(TestBrowser b, Guid wo, string action, object? extra = null)
    {
        var body = new Dictionary<string, object?> { ["action"] = action };
        if (extra is not null) foreach (var p in extra.GetType().GetProperties()) body[p.Name] = p.GetValue(extra);
        return b.Post($"/api/work-orders/{wo}/action", body);
    }

    // ============================================================ who
    [Fact]
    public async Task Supervisors_create_agents_of_the_ticket_see_and_do_requesters_and_strangers_see_nothing()
    {
        var w = await Build(); var (tid, number) = await Ticket(w);
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Agent.Post($"/api/tickets/{tid}/work-orders", new { title = "Fix it" })).StatusCode);   // agents do work, they don't create it
        var wo = await NewWo(w, tid, new { title = "Replace roof tiles", assigneeId = w.AgentUser.Id });
        Assert.Equal($"{number}-W1", Str(wo, "number"));
        var second = await NewWo(w, tid, new { title = "Check gutters" });
        Assert.Equal($"{number}-W2", Str(second, "number"));

        Assert.Equal(HttpStatusCode.Forbidden, (await w.Requester.Get($"/api/tickets/{tid}/work-orders")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Requester.Get($"/api/work-orders/{IdOf(wo)}")).StatusCode);
        var list = await Ok(await w.Agent.Get($"/api/tickets/{tid}/work-orders"));
        Assert.Equal(2, list.GetProperty("items").GetArrayLength()); Assert.False(list.GetProperty("canManage").GetBoolean());

        var (_, stranger) = await Person("ga_agent");                                                             // another agent, in no team
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Get($"/api/work-orders/{IdOf(wo)}")).StatusCode);
        // given the work, a stranger sees that work order (and only that one), even without seeing the ticket
        var (su2, s2) = await Person("ga_agent");
        var theirs = await NewWo(w, tid, new { title = "Bring the ladder", assigneeId = su2.Id });
        Assert.Equal(HttpStatusCode.OK, (await s2.Get($"/api/work-orders/{IdOf(theirs)}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await s2.Get($"/api/work-orders/{IdOf(wo)}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await s2.Get($"/api/tickets/{tid}")).StatusCode);
        var mine = await Ok(await s2.Get("/api/work-orders?mine=true"));
        Assert.Equal(1, mine.GetProperty("total").GetInt32());
        Assert.Contains(await _seed.Db(db => db.Notifications.Where(n => n.UserId == su2.Id && n.Kind == "workorder.assigned").Select(n => n.Title).ToListAsync()), t => t.Contains("-W3"));
    }

    // ============================================================ checklist and completing
    [Fact]
    public async Task A_checklist_comes_from_a_template_and_completing_needs_its_required_lines_an_after_photo_and_a_note()
    {
        var w = await Build(); var (tid, _) = await Ticket(w);
        var name = $"AC service {Uniq()}";
        var tpl = await Ok(await w.Sup.Post("/api/checklist-templates", new { name, items = new object[] { new { text = "Clean the filters", required = true }, new { text = "Check refrigerant", required = true }, new { text = "Note the noise level", required = false } } }));
        Assert.True((await Fields(await w.Sup.Post("/api/checklist-templates", new { name = name.ToUpperInvariant(), items = new[] { new { text = "x line" } } }))).TryGetProperty("name", out _));
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Agent.Post("/api/checklist-templates", new { name = "Agent list", items = new[] { new { text = "One" } } })).StatusCode);

        var wo = await NewWo(w, tid, new { title = "Service the AC", assigneeId = w.AgentUser.Id, templateId = IdOf(tpl), tasks = new[] { new { text = "Photograph the unit", required = true } }, requireAfterPhoto = true });
        var id = IdOf(wo);
        Assert.Equal(["Clean the filters", "Check refrigerant", "Note the noise level", "Photograph the unit"], wo.GetProperty("tasks").EnumerateArray().Select(k => Str(k, "text")).ToArray());

        Assert.Equal(HttpStatusCode.Conflict, (await w.Agent.Post($"/api/work-orders/{id}/tasks/{IdOf(wo.GetProperty("tasks")[0])}", new { done = true })).StatusCode);   // start first
        var started = await Ok(await Act(w.Agent, id, "start"));
        Assert.Equal("in_progress", Str(started, "status"));
        Assert.Equal(2, started.GetProperty("missing").GetArrayLength());                                        // the required lines, and the after photo
        var done = await Fields(await Act(w.Agent, id, "complete", new { note = "All good" }));
        Assert.Contains("required checklist lines", Str(done, "complete")); Assert.Contains("after", Str(done, "complete"));
        Assert.True((await Fields(await Act(w.Agent, id, "complete"))).TryGetProperty("note", out _));

        foreach (var k in started.GetProperty("tasks").EnumerateArray().Where(k => k.GetProperty("required").GetBoolean()))
            await Ok(await w.Agent.Post($"/api/work-orders/{id}/tasks/{IdOf(k)}", new { done = true, note = "ok" }));
        Assert.Contains("after", Str(await Fields(await Act(w.Agent, id, "complete", new { note = "All good" })), "complete"));
        Assert.Equal(Invalid, (await w.Agent.UploadAsync($"/api/work-orders/{id}/files?stage=during", "p.png", Png(), "image/png")).StatusCode);
        var withPhoto = await Ok(await w.Agent.UploadAsync($"/api/work-orders/{id}/files?stage=after", "after.png", Png(), "image/png"));
        var file = withPhoto.GetProperty("files")[0]; Assert.Equal("after", Str(file, "stage"));
        Assert.Equal(HttpStatusCode.OK, (await w.Agent.Get($"/api/work-orders/{id}/files/{IdOf(file)}")).StatusCode);
        var finished = await Ok(await Act(w.Agent, id, "complete", new { note = "Filters cleaned, gas topped up" }));
        Assert.Equal("done", Str(finished, "status")); Assert.Empty(finished.GetProperty("allowed").EnumerateArray());
        Assert.Equal(HttpStatusCode.Conflict, (await w.Agent.Post($"/api/work-orders/{id}/time", new { minutes = 30 })).StatusCode);   // finished: frozen

        var timeline = (await Ok(await w.Sup.Get($"/api/tickets/{tid}"))).GetProperty("timeline").EnumerateArray().Where(e => e.GetProperty("internal").GetBoolean()).Select(e => e.GetProperty("body").GetString()).ToList();
        Assert.Contains(timeline, b => b!.Contains("-W1 started")); Assert.Contains(timeline, b => b!.Contains("-W1 done: Filters cleaned"));
        Assert.DoesNotContain((await Ok(await w.Requester.Get($"/api/tickets/{tid}"))).GetProperty("timeline").EnumerateArray(), e => (e.GetProperty("body").GetString() ?? "").Contains("-W1"));   // staff only

        Assert.Equal(HttpStatusCode.NoContent, (await w.Sup.Post($"/api/checklist-templates/{IdOf(tpl)}/archive")).StatusCode);
        Assert.True((await Fields(await w.Sup.Post($"/api/tickets/{tid}/work-orders", new { title = "Again", templateId = IdOf(tpl) }))).TryGetProperty("templateId", out _));
    }

    [Fact]
    public async Task Only_the_assignee_does_a_given_work_order_and_unassigned_work_is_taken_by_starting_it()
    {
        var w = await Build(); var (tid, _) = await Ticket(w);
        var given = IdOf(await NewWo(w, tid, new { title = "Given to agent one", assigneeId = w.AgentUser.Id }));
        Assert.Equal(HttpStatusCode.Forbidden, (await Act(w.Agent2, given, "start")).StatusCode);
        var free = IdOf(await NewWo(w, tid, new { title = "Anyone can take this" }));
        var taken = await Ok(await Act(w.Agent2, free, "start"));
        Assert.Equal(w.Agent2User.Id, taken.GetProperty("assigneeId").GetGuid());
        Assert.Equal(HttpStatusCode.Forbidden, (await Act(w.Agent, free, "hold", new { reason = "not mine" })).StatusCode);
        Assert.True((await Fields(await Act(w.Agent2, free, "hold"))).TryGetProperty("reason", out _));
        Assert.Equal("on_hold", Str(await Ok(await Act(w.Agent2, free, "hold", new { reason = "Waiting for parts" })), "status"));
        Assert.Equal(HttpStatusCode.Forbidden, (await Act(w.Agent2, free, "cancel", new { reason = "no" })).StatusCode);   // cancelling is a manager's call
        Assert.Equal(HttpStatusCode.Conflict, (await Act(w.Agent2, free, "start")).StatusCode);                              // on hold: resume, not start
        Assert.Equal("in_progress", Str(await Ok(await Act(w.Agent2, free, "resume")), "status"));
        Assert.Equal("cancelled", Str(await Ok(await Act(w.Sup, free, "cancel", new { reason = "Done by the landlord" })), "status"));
    }

    // ============================================================ time and money
    [Fact]
    public async Task Time_is_logged_by_whoever_does_the_work_and_costs_only_by_those_who_may_see_money()
    {
        var w = await Build(); var (tid, _) = await Ticket(w);
        var id = IdOf(await NewWo(w, tid, new { title = "Repaint the lobby", assigneeId = w.AgentUser.Id }));
        var today = DateOnly.FromDateTime(fx.Clock.UtcNow.UtcDateTime);
        var bad = await Fields(await w.Agent.Post($"/api/work-orders/{id}/time", new { minutes = 0, date = today.AddDays(1).ToString("yyyy-MM-dd") }));
        Assert.True(bad.TryGetProperty("minutes", out _)); Assert.True(bad.TryGetProperty("date", out _));
        var t1 = await Ok(await w.Agent.Post($"/api/work-orders/{id}/time", new { minutes = 90, note = "First coat" }));
        Assert.Equal(90, t1.GetProperty("totalMinutes").GetInt32());
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Agent2.Post($"/api/work-orders/{id}/time", new { minutes = 10 })).StatusCode);   // not their work
        Assert.Equal(JsonValueKind.Null, t1.GetProperty("costs").ValueKind);                                        // agents don't see money
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Agent.Post($"/api/work-orders/{id}/costs", new { kind = "part", description = "Paint", quantity = 2, unitCost = 150000 })).StatusCode);

        var badCost = await Fields(await w.Sup.Post($"/api/work-orders/{id}/costs", new { kind = "gold", description = "x", quantity = 0, unitCost = 1.234m }));
        foreach (var k in new[] { "kind", "description", "quantity", "unitCost" }) Assert.True(badCost.TryGetProperty(k, out _), k);
        await Ok(await w.Sup.Post($"/api/work-orders/{id}/costs", new { kind = "part", description = "Paint, white 5 L", quantity = 2, unitCost = 150000 }));
        var withCost = await Ok(await w.Sup.Post($"/api/work-orders/{id}/costs", new { kind = "service", description = "Scaffolding hire", quantity = 1, unitCost = 250000.50m }));
        Assert.Equal(550000.50m, withCost.GetProperty("costTotal").GetDecimal());
        var line = withCost.GetProperty("costs")[0];
        Assert.Equal(300000m, line.GetProperty("total").GetDecimal());
        var after = await Ok(await w.Sup.SendAsync(HttpMethod.Delete, $"/api/work-orders/{id}/costs/{IdOf(line)}"));
        Assert.Equal(250000.50m, after.GetProperty("costTotal").GetDecimal());

        var timeId = IdOf(t1.GetProperty("times")[0]);
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Agent2.SendAsync(HttpMethod.Delete, $"/api/work-orders/{id}/time/{timeId}")).StatusCode);   // not their entry
        Assert.Equal(0, (await Ok(await w.Agent.SendAsync(HttpMethod.Delete, $"/api/work-orders/{id}/time/{timeId}"))).GetProperty("totalMinutes").GetInt32());
        await Ok(await Act(w.Sup, id, "cancel", new { reason = "Postponed to next year" }));
        Assert.Equal(HttpStatusCode.Conflict, (await w.Sup.Post($"/api/work-orders/{id}/costs", new { kind = "part", description = "More paint", quantity = 1, unitCost = 10 })).StatusCode);
    }

    // ============================================================ the ticket
    [Fact]
    public async Task Required_work_orders_hold_the_ticket_and_the_last_one_done_resolves_it_when_asked()
    {
        var w = await Build(); var (tid, number) = await Ticket(w);
        await Ok(await w.Agent.Post($"/api/tickets/{tid}/transition", new { action = "start" }));
        var a = IdOf(await NewWo(w, tid, new { title = "Electrician", assigneeId = w.AgentUser.Id, resolveTicket = true }));
        var b = IdOf(await NewWo(w, tid, new { title = "Plasterer", assigneeId = w.AgentUser.Id }));
        var optional = IdOf(await NewWo(w, tid, new { title = "Nice to have: repaint", required = false }));
        Assert.True((await Ok(await w.Sup.Get($"/api/tickets/{tid}/work-orders"))).GetProperty("resolveTicket").GetBoolean());

        var resolve = await w.Agent.Post($"/api/tickets/{tid}/transition", new { action = "resolve", resolutionCode = "fixed", resolutionNote = "Done by hand" });
        Assert.Equal(HttpStatusCode.Conflict, resolve.StatusCode); Assert.Contains("2 required work orders", (await resolve.Body()).GetProperty("error").GetProperty("message").GetString());

        await Ok(await Act(w.Agent, a, "start")); await Ok(await Act(w.Agent, a, "complete", new { note = "New circuit" }));
        Assert.Equal("inprogress", Str(await Ok(await w.Agent.Get($"/api/tickets/{tid}")), "status"));         // one still open
        await Ok(await Act(w.Agent, b, "start")); await Ok(await Act(w.Agent, b, "complete", new { note = "Wall repaired" }));
        var t = await Ok(await w.Requester.Get($"/api/tickets/{tid}"));
        Assert.Equal("resolved", Str(t, "status")); Assert.Equal("fixed", Str(t, "resolutionCode"));
        Assert.Equal($"All required work orders are done ({number}-W1, {number}-W2).", Str(t, "resolutionNote"));    // the optional one did not hold it
        Assert.Equal("open", Str(await Ok(await w.Sup.Get($"/api/work-orders/{optional}")), "status"));
        Assert.Contains("ticket.auto_resolve", await _seed.Db(db => db.AuditEvents.Where(x => x.TargetId == tid.ToString()).Select(x => x.Action).ToListAsync()));
        Assert.Equal("in_progress", Str(await Ok(await Act(w.Sup, optional, "start")), "status"));             // optional work can still be done after resolving; only a closed, cancelled or rejected ticket freezes them
    }

    [Fact]
    public async Task Without_the_setting_the_ticket_waits_and_cancelling_the_ticket_cancels_its_open_work_orders()
    {
        var w = await Build(); var (tid, _) = await Ticket(w);
        await Ok(await w.Agent.Post($"/api/tickets/{tid}/transition", new { action = "start" }));
        var a = IdOf(await NewWo(w, tid, new { title = "Only job", assigneeId = w.AgentUser.Id }));
        await Ok(await Act(w.Agent, a, "start")); await Ok(await Act(w.Agent, a, "complete", new { note = "Done" }));
        Assert.Equal("inprogress", Str(await Ok(await w.Agent.Get($"/api/tickets/{tid}")), "status"));        // not asked to resolve itself
        await Ok(await w.Agent.Post($"/api/tickets/{tid}/transition", new { action = "resolve", resolutionCode = "fixed", resolutionNote = "All done now" }));   // nothing holds it any more

        var (t2, _) = await Ticket(w, "Broken window");
        var open1 = IdOf(await NewWo(w, t2, new { title = "Measure the glass" })); var open2 = IdOf(await NewWo(w, t2, new { title = "Fit the glass" }));
        await Ok(await w.Sup.Post($"/api/tickets/{t2}/transition", new { action = "cancel", reason = "Duplicate request" }));
        foreach (var x in new[] { open1, open2 })
        {
            var d = await Ok(await w.Sup.Get($"/api/work-orders/{x}"));
            Assert.Equal("cancelled", Str(d, "status")); Assert.Equal("The ticket was cancelled.", Str(d, "cancelReason"));
        }
        Assert.Equal(HttpStatusCode.Conflict, (await w.Sup.Post($"/api/tickets/{t2}/work-orders", new { title = "Too late" })).StatusCode);
    }

    [Fact]
    public async Task Editing_keeps_ticked_lines_and_reassigning_tells_the_new_person()
    {
        var w = await Build(); var (tid, _) = await Ticket(w);
        var wo = await NewWo(w, tid, new { title = "Fix the door", assigneeId = w.AgentUser.Id, tasks = new[] { new { text = "Oil the hinges", required = true }, new { text = "Replace the lock", required = true } } });
        var id = IdOf(wo);
        await Ok(await Act(w.Agent, id, "start"));
        var first = wo.GetProperty("tasks")[0];
        await Ok(await w.Agent.Post($"/api/work-orders/{id}/tasks/{IdOf(first)}", new { done = true }));
        var cur = await Ok(await w.Sup.Get($"/api/work-orders/{id}"));
        Assert.True((await Fields(await w.Sup.SendAsync(HttpMethod.Put, $"/api/work-orders/{id}", new { title = "x" }))).TryGetProperty("title", out _));
        var upd = await Ok(await w.Sup.SendAsync(HttpMethod.Put, $"/api/work-orders/{id}", new
        {
            title = "Fix the front door", assigneeId = w.Agent2User.Id, required = true, version = cur.GetProperty("version").GetInt32(),
            tasks = new object[] { new { id = IdOf(first), text = "Oil the hinges", required = true }, new { text = "Check the closer", required = false } },
        }));
        Assert.Equal(["Oil the hinges", "Check the closer"], upd.GetProperty("tasks").EnumerateArray().Select(k => Str(k, "text")).ToArray());
        Assert.NotEqual(JsonValueKind.Null, upd.GetProperty("tasks")[0].GetProperty("doneAt").ValueKind);        // still ticked
        Assert.Equal(w.Agent2User.Id, upd.GetProperty("assigneeId").GetGuid());
        Assert.True(await _seed.Db(db => db.Notifications.AnyAsync(n => n.UserId == w.Agent2User.Id && n.Kind == "workorder.assigned")));
        var stale = await w.Sup.SendAsync(HttpMethod.Put, $"/api/work-orders/{id}", new { title = "Stale edit", version = cur.GetProperty("version").GetInt32() });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
    }
}
