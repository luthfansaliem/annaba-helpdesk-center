using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using AnnabaDigital.Application.Helpdesk;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Tests.Infra;
using Xunit;

namespace AnnabaDigital.Tests;

/// <summary>Ticket lifecycle (TKT-001/002/004/008): creation from the catalog, visibility, the state machine, assignment, comments, automatic closing and tenant isolation.</summary>
[Collection("db")]
public sealed class TicketTests(PostgresFixture fx)
{
    private readonly Seed _seed = new(fx.Factory, fx.Clock);
    private const string StrongPw = "Cobalt-Meadow-Harvest-58";
    private static string Uniq() => Guid.NewGuid().ToString("N")[..8];
    private static readonly HttpMethod Patch = HttpMethod.Patch;
    private const HttpStatusCode Invalid = (HttpStatusCode)422;

    private async Task<(User User, TestBrowser B)> Person(string role)
    {
        var u = await _seed.UserAsync(role: role); var b = TestBrowser.Create(fx.Factory); await b.LoginAsync(u.Email, Seed.Password); return (u, b);
    }

    private static async Task<JsonElement> Ok(HttpResponseMessage r) { Assert.True(r.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK, await r.Content.ReadAsStringAsync()); return await r.Body(); }
    private static Guid IdOf(JsonElement e) => e.GetProperty("id").GetGuid();
    private static string Str(JsonElement e, string p) => e.GetProperty(p).GetString()!;
    private static string[] Allowed(JsonElement t) => [.. t.GetProperty("allowed").EnumerateArray().Select(a => a.GetString()!)];
    private static int Ver(JsonElement t) => t.GetProperty("version").GetInt32();

    /// <summary>One published service (owned by a new team with one agent in it) and the people around it, all in the default organisation.</summary>
    private sealed record World(TestBrowser Admin, TestBrowser Requester, User RequesterUser, TestBrowser Agent, User AgentUser, TestBrowser Supervisor, Guid ServiceId, Guid TeamId);

    private async Task<World> Build(bool withTeam = true)
    {
        var (_, admin) = await Person("super_admin"); var (ru, requester) = await Person("user"); var (au, agent) = await Person("ga_agent"); var (_, sup) = await Person("ga_supervisor");
        Guid team = Guid.Empty;
        if (withTeam)
        {
            team = IdOf(await Ok(await admin.Post("/api/teams", new { name = $"Team {Uniq()}" })));
            Assert.Equal(HttpStatusCode.OK, (await admin.SendAsync(HttpMethod.Put, $"/api/teams/{team}/members", new { members = new[] { new { userId = au.Id, isLead = false } } })).StatusCode);
        }
        var cat = IdOf(await Ok(await admin.Post("/api/catalog/categories", new { name = $"Cat {Uniq()}" })));
        var svc = IdOf(await Ok(await admin.Post("/api/catalog/services", new { name = $"Svc {Uniq()}", categoryId = cat, owningTeamId = withTeam ? team : (Guid?)null, defaultPriority = "high" })));
        Assert.Equal(HttpStatusCode.OK, (await admin.SendAsync(HttpMethod.Put, $"/api/catalog/services/{svc}/form", new
        {
            fields = new object[] { new { key = "what", type = "text", label = "What is wrong?", required = true }, new { key = "kind", type = "select", label = "Kind", options = new[] { new { value = "ac", label = "Air conditioning" } } } },
        })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.Post($"/api/catalog/services/{svc}/publish")).StatusCode);
        return new World(admin, requester, ru, agent, au, sup, svc, team);
    }

    private static Task<HttpResponseMessage> Raise(TestBrowser b, Guid svc, string subject = "Printer on floor 3 is broken", object? answers = null, Guid? location = null, Guid? onBehalf = null)
        => b.Post($"/api/portal/services/{svc}/requests", new { subject, description = "Please help", answers = answers ?? new { what = "paper jam", kind = "ac" }, locationId = location, onBehalfOfUserId = onBehalf });

    private static Task<HttpResponseMessage> Act(TestBrowser b, Guid id, string action, object? extra = null, int? version = null)
    {
        var body = new Dictionary<string, object?> { ["action"] = action, ["version"] = version };
        if (extra is not null) foreach (var p in extra.GetType().GetProperties()) body[p.Name] = p.GetValue(extra);
        return b.Post($"/api/tickets/{id}/transition", body);
    }

    // ============================================================ role seed
    [Fact]
    public async Task The_GA_roles_exist_with_the_rights_their_names_promise()
    {
        var (_, admin) = await Person("super_admin");
        var roles = (await Ok(await admin.Get("/api/roles"))).EnumerateArray().ToDictionary(r => Str(r, "key"));
        string[] Perms(string key) => [.. roles[key].GetProperty("permissions").EnumerateArray().Select(p => p.GetString()!)];
        Assert.Contains("tickets.transition", Perms("ga_agent")); Assert.DoesNotContain("tickets.assign", Perms("ga_agent")); Assert.DoesNotContain("tickets.read_all", Perms("ga_agent"));
        Assert.Contains("tickets.assign", Perms("ga_supervisor")); Assert.Contains("tickets.read_all", Perms("ga_supervisor"));
        Assert.Contains("locations.read", Perms("ga_manager")); Assert.Empty(Perms("user"));
    }

    // ============================================================ creating
    [Fact]
    public async Task A_signed_in_person_raises_a_ticket_from_a_published_service_and_it_remembers_what_it_was_made_with()
    {
        var w = await Build();
        var t = await Ok(await Raise(w.Requester, w.ServiceId));
        Assert.Matches(@"^GA-\d{4}-\d{6}$", Str(t, "number"));
        Assert.Equal("submitted", Str(t, "status")); Assert.Equal("high", Str(t, "priority"));          // priority comes from the service
        Assert.Equal(w.TeamId, t.GetProperty("teamId").GetGuid());                                        // so does the owning team
        Assert.Equal(w.RequesterUser.Id, t.GetProperty("requesterId").GetGuid()); Assert.Equal(1, t.GetProperty("formVersion").GetInt32());
        var answers = t.GetProperty("answers").EnumerateArray().ToList();
        Assert.Equal(["What is wrong?", "Kind"], answers.Select(a => Str(a, "label")).ToArray());          // labelled from the form version it used
        Assert.Equal("Air conditioning", Str(answers[1], "display"));
        Assert.Equal(["cancel"], Allowed(t));                                                              // a requester may only cancel at this point
        Assert.Equal("created", Str(t.GetProperty("timeline")[0], "kind"));
        Assert.Contains("ticket.create", await _seed.Db(db => db.AuditEvents.Where(a => a.TargetId == IdOf(t).ToString()).Select(a => a.Action).ToListAsync()));

        // a later form edit never rewrites the ticket
        await w.Admin.SendAsync(HttpMethod.Put, $"/api/catalog/services/{w.ServiceId}/form", new { fields = new object[] { new { key = "other", type = "text", label = "Something else", required = false } } });
        var again = await Ok(await w.Requester.Get($"/api/tickets/{IdOf(t)}"));
        Assert.Equal(1, again.GetProperty("formVersion").GetInt32()); Assert.Equal("What is wrong?", Str(again.GetProperty("answers")[0], "label"));
    }

    [Fact]
    public async Task Creation_is_validated_and_only_possible_for_services_the_person_can_see()
    {
        var w = await Build();
        Assert.Equal(Invalid, (await Raise(w.Requester, w.ServiceId, "ab")).StatusCode);
        var missing = await Raise(w.Requester, w.ServiceId, answers: new { kind = "ac" });
        Assert.Equal(Invalid, missing.StatusCode);
        Assert.True((await missing.Body()).GetProperty("error").GetProperty("fields").TryGetProperty("answers.what", out _));       // the question the answer belongs to
        Assert.Equal(Invalid, (await Raise(w.Requester, w.ServiceId, location: Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Raise(w.Requester, Guid.NewGuid())).StatusCode);

        await w.Admin.Post($"/api/catalog/services/{w.ServiceId}/unpublish");
        Assert.Equal(HttpStatusCode.NotFound, (await Raise(w.Requester, w.ServiceId)).StatusCode);                                 // a draft can't be requested
        Assert.Equal(HttpStatusCode.Unauthorized, (await Raise(TestBrowser.Create(fx.Factory), w.ServiceId)).StatusCode);
    }

    [Fact]
    public async Task Numbers_run_in_sequence_per_organisation_and_never_repeat()
    {
        var (_, a) = await NewTenant(); var (_, b) = await NewTenant();
        var sa = await PublishedService(a); var sb = await PublishedService(b);
        var n = new List<string>();
        for (var i = 0; i < 3; i++) n.Add(Str(await Ok(await Raise(a, sa)), "number"));
        var first = Str(await Ok(await Raise(b, sb)), "number");
        var year = DateTimeOffset.UtcNow.Year;
        Assert.Equal([$"GA-{year}-000001", $"GA-{year}-000002", $"GA-{year}-000003"], n);
        Assert.Equal($"GA-{year}-000001", first);                                                                                    // another organisation counts on its own
    }

    [Fact]
    public async Task Filing_a_request_for_someone_else_needs_the_update_permission()
    {
        var w = await Build(); var (other, otherBrowser) = await Person("user");
        Assert.Equal(HttpStatusCode.Forbidden, (await Raise(w.Requester, w.ServiceId, onBehalf: other.Id)).StatusCode);
        var t = await Ok(await Raise(w.Agent, w.ServiceId, onBehalf: other.Id));                                                     // an agent holds tickets.update
        Assert.Equal(other.Id, t.GetProperty("requesterId").GetGuid());
        Assert.Contains("on behalf", t.GetProperty("timeline")[0].GetProperty("body").GetString());
        Assert.Equal(Invalid, (await Raise(w.Agent, w.ServiceId, onBehalf: Guid.NewGuid())).StatusCode);

        Assert.Contains((await Ok(await otherBrowser.Get("/api/tickets/mine"))).GetProperty("data").EnumerateArray(), x => IdOf(x) == IdOf(t));         // the person it was filed for sees it in their own list
    }
    [Fact]
    public async Task Requesters_can_pick_places_and_look_up_colleagues_without_seeing_the_user_directory()
    {
        var w = await Build(); var code = "P-" + Uniq();
        var site = IdOf(await Ok(await w.Admin.Post("/api/locations", new { type = "site", code, name = "Path Site" })));
        var floor = IdOf(await Ok(await w.Admin.Post("/api/locations", new { type = "building", parentId = site, code = "B" + code, name = "Path Tower" })));
        var gone = IdOf(await Ok(await w.Admin.Post("/api/locations", new { type = "site", code = "G-" + Uniq(), name = "Gone Site" }))); await w.Admin.Post($"/api/locations/{gone}/archive");

        var places = (await Ok(await w.Requester.Get("/api/portal/locations"))).EnumerateArray().ToList();                          // a plain requester has no locations.read
        Assert.Equal("Path Site › Path Tower", Str(places.Single(p => IdOf(p) == floor), "path"));
        Assert.DoesNotContain(places, p => IdOf(p) == gone);                                                                         // archived places are not offered
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Requester.Get("/api/locations")).StatusCode);

        Assert.Empty((await Ok(await w.Requester.Get("/api/portal/people?q=a"))).EnumerateArray());                                  // too short: no browsing the directory
        var hits = (await Ok(await w.Requester.Get($"/api/portal/people?q={Uri.EscapeDataString(w.AgentUser.Email)}"))).EnumerateArray().ToList();
        Assert.Contains(hits, h => IdOf(h) == w.AgentUser.Id);
        Assert.All(hits, h => { Assert.False(h.TryGetProperty("email", out _)); Assert.False(h.TryGetProperty("phone", out _)); });
        Assert.Equal(HttpStatusCode.Unauthorized, (await TestBrowser.Create(fx.Factory).Get("/api/portal/people?q=ab")).StatusCode);

        // a place chosen in the request is stored on the ticket
        var t = await Ok(await Raise(w.Requester, w.ServiceId, location: floor));
        Assert.Equal("Path Tower", Str(t, "locationName"));
    }

    // ============================================================ visibility
    [Fact]
    public async Task People_see_only_the_tickets_they_are_entitled_to()
    {
        var w = await Build(); var (_, stranger) = await Person("user"); var (_, outsider) = await Person("ga_agent");
        var t = await Ok(await Raise(w.Requester, w.ServiceId)); var id = IdOf(t);

        Assert.Equal(HttpStatusCode.OK, (await w.Requester.Get($"/api/tickets/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await w.Agent.Get($"/api/tickets/{id}")).StatusCode);                                      // member of the owning team
        Assert.Equal(HttpStatusCode.OK, (await w.Supervisor.Get($"/api/tickets/{id}")).StatusCode);                                 // read_all
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Get($"/api/tickets/{id}")).StatusCode);                               // somebody else's request
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.Get($"/api/tickets/{id}")).StatusCode);                               // an agent of another team

        var mine = await Ok(await w.Requester.Get("/api/tickets/mine"));
        Assert.Contains(mine.GetProperty("data").EnumerateArray(), x => IdOf(x) == id);
        Assert.DoesNotContain((await Ok(await stranger.Get("/api/tickets/mine"))).GetProperty("data").EnumerateArray(), x => IdOf(x) == id);

        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.Get("/api/tickets")).StatusCode);                                    // the queue is for staff
        Assert.Contains((await Ok(await w.Agent.Get("/api/tickets"))).GetProperty("data").EnumerateArray(), x => IdOf(x) == id);
        Assert.DoesNotContain((await Ok(await outsider.Get("/api/tickets"))).GetProperty("data").EnumerateArray(), x => IdOf(x) == id);
        Assert.Equal(HttpStatusCode.Unauthorized, (await TestBrowser.Create(fx.Factory).Get("/api/tickets/mine")).StatusCode);

        // a ticket with no team is invisible to agents until someone routes it; supervisors always see it
        var loose = await Build(withTeam: false); var lt = IdOf(await Ok(await Raise(loose.Requester, loose.ServiceId)));
        Assert.Equal(HttpStatusCode.NotFound, (await loose.Agent.Get($"/api/tickets/{lt}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await loose.Supervisor.Get($"/api/tickets/{lt}")).StatusCode);
    }

    // ============================================================ lifecycle
    [Fact]
    public async Task A_ticket_travels_from_submitted_to_closed_and_every_step_is_on_its_timeline()
    {
        var w = await Build();
        var t = await Ok(await Raise(w.Requester, w.ServiceId)); var id = IdOf(t);

        var assigned = await Ok(await w.Supervisor.Post($"/api/tickets/{id}/assign", new { assigneeId = w.AgentUser.Id, teamId = w.TeamId }));
        Assert.Equal("assigned", Str(assigned, "status")); Assert.Equal(w.AgentUser.Id, assigned.GetProperty("assigneeId").GetGuid());
        Assert.Equal(["start", "hold", "cancel"], Allowed(await Ok(await w.Agent.Get($"/api/tickets/{id}"))));

        var started = await Ok(await Act(w.Agent, id, "start")); Assert.Equal("inprogress", Str(started, "status"));
        var held = await Ok(await Act(w.Agent, id, "hold", new { reason = "Waiting for a spare part" }));
        Assert.Equal("onhold", Str(held, "status")); Assert.Equal("Waiting for a spare part", Str(held, "holdReason"));
        var resumed = await Ok(await Act(w.Agent, id, "resume")); Assert.Equal("inprogress", Str(resumed, "status")); Assert.Equal(JsonValueKind.Null, resumed.GetProperty("holdReason").ValueKind);
        var resolved = await Ok(await Act(w.Agent, id, "resolve", new { resolutionCode = "fixed", resolutionNote = "Replaced the roller" }));
        Assert.Equal("resolved", Str(resolved, "status")); Assert.Equal("fixed", Str(resolved, "resolutionCode")); Assert.NotEqual(JsonValueKind.Null, resolved.GetProperty("resolvedAt").ValueKind);
        Assert.Equal(["confirm", "reopen"], Allowed(await Ok(await w.Requester.Get($"/api/tickets/{id}"))));

        var closed = await Ok(await Act(w.Requester, id, "confirm"));
        Assert.Equal("closed", Str(closed, "status")); Assert.NotEqual(JsonValueKind.Null, closed.GetProperty("closedAt").ValueKind);

        var steps = closed.GetProperty("timeline").EnumerateArray().Select(e => $"{Str(e, "kind")}:{(e.GetProperty("to").ValueKind == JsonValueKind.String ? Str(e, "to") : "")}").ToArray();
        Assert.Equal(["created:submitted", "assigned:assigned", "status:inprogress", "status:onhold", "status:inprogress", "status:resolved", "status:closed"], steps);
        var actions = await _seed.Db(db => db.AuditEvents.Where(a => a.TargetId == id.ToString()).Select(a => a.Action).ToListAsync());
        foreach (var a in new[] { "ticket.create", "ticket.assign", "ticket.start", "ticket.hold", "ticket.resume", "ticket.resolve", "ticket.confirm" }) Assert.Contains(a, actions);
    }

    [Fact]
    public async Task Illegal_moves_and_moves_by_the_wrong_person_are_refused()
    {
        var w = await Build(); var (colleagueUser, colleague) = await Person("ga_agent");
        Assert.Equal(HttpStatusCode.OK, (await w.Admin.SendAsync(HttpMethod.Put, $"/api/teams/{w.TeamId}/members", new { members = new[] { new { userId = w.AgentUser.Id, isLead = false }, new { userId = colleagueUser.Id, isLead = false } } })).StatusCode);
        var id = IdOf(await Ok(await Raise(w.Requester, w.ServiceId)));

        Assert.Equal(HttpStatusCode.Conflict, (await Act(w.Agent, id, "resolve", new { resolutionCode = "fixed", resolutionNote = "not started yet" })).StatusCode); // illegal from Submitted
        Assert.Equal(HttpStatusCode.Forbidden, (await Act(w.Requester, id, "start")).StatusCode);                                                                // a requester doesn't work tickets
        Assert.Equal(Invalid, (await Act(w.Agent, id, "teleport")).StatusCode);
        Assert.Equal(Invalid, (await Act(w.Agent, id, "assign")).StatusCode);                                                                                     // assignment has its own route

        await Ok(await Act(w.Agent, id, "start"));                                                                                                                // the agent picks up an unowned ticket
        Assert.Equal(HttpStatusCode.Forbidden, (await Act(colleague, id, "hold", new { reason = "not mine" })).StatusCode);                                       // someone else's ticket
        Assert.Equal(HttpStatusCode.Conflict, (await Act(w.Requester, id, "cancel", new { reason = "changed my mind" })).StatusCode);                             // work has begun
        Assert.Equal(Invalid, (await Act(w.Agent, id, "hold")).StatusCode);                                                                                       // a reason is required
        Assert.Equal(Invalid, (await Act(w.Agent, id, "hold", new { reason = "x" })).StatusCode);
        var bad = await Act(w.Agent, id, "resolve", new { resolutionCode = "magic", resolutionNote = "" });
        Assert.Equal(Invalid, bad.StatusCode);
        var fields = (await bad.Body()).GetProperty("error").GetProperty("fields");
        Assert.True(fields.TryGetProperty("resolutionCode", out _)); Assert.True(fields.TryGetProperty("resolutionNote", out _));

        Assert.Equal(HttpStatusCode.Conflict, (await Act(w.Agent, id, "start", version: 1)).StatusCode);                                                          // stale copy (and illegal now anyway)
        var cur = await Ok(await w.Agent.Get($"/api/tickets/{id}"));
        Assert.Equal(HttpStatusCode.Conflict, (await Act(w.Agent, id, "hold", new { reason = "stale" }, version: Ver(cur) - 1)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Act(w.Agent, id, "hold", new { reason = "fresh" }, version: Ver(cur))).StatusCode);
    }

    [Fact]
    public async Task A_requester_can_cancel_early_and_a_cancelled_ticket_is_final()
    {
        var w = await Build();
        var id = IdOf(await Ok(await Raise(w.Requester, w.ServiceId)));
        Assert.Equal(Invalid, (await Act(w.Requester, id, "cancel")).StatusCode);
        var c = await Ok(await Act(w.Requester, id, "cancel", new { reason = "Fixed it myself" }));
        Assert.Equal("cancelled", Str(c, "status")); Assert.Equal("Fixed it myself", Str(c, "cancelReason")); Assert.Empty(Allowed(c));
        foreach (var a in new[] { "start", "reopen", "confirm" }) Assert.Equal(HttpStatusCode.Conflict, (await Act(w.Supervisor, id, a, new { reason = "again" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await w.Requester.Post($"/api/tickets/{id}/comments", new { body = "hello?" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await w.Supervisor.Post($"/api/tickets/{id}/assign", new { assigneeId = w.AgentUser.Id })).StatusCode);
    }

    [Fact]
    public async Task A_resolution_can_be_reopened_and_a_closed_ticket_only_inside_the_window()
    {
        var w = await Build();
        var id = await Resolve(w);
        Assert.Equal(Invalid, (await Act(w.Requester, id, "reopen")).StatusCode);                                                    // needs a reason
        var re = await Ok(await Act(w.Requester, id, "reopen", new { reason = "Still jams every hour" }));
        Assert.Equal("reopened", Str(re, "status")); Assert.Equal(1, re.GetProperty("reopenCount").GetInt32());
        Assert.Equal(JsonValueKind.Null, re.GetProperty("resolutionCode").ValueKind);                                                // the resolution is cleared; the timeline keeps it
        Assert.Contains(re.GetProperty("timeline").EnumerateArray(), e => Str(e, "to") == "resolved" && Str(e, "body").StartsWith("fixed:"));
        Assert.Contains("start", Allowed(await Ok(await w.Agent.Get($"/api/tickets/{id}"))));                                        // the assigned agent can pick it up again

        // resolve and close again, then age the closing date
        await Act(w.Agent, id, "start"); await Act(w.Agent, id, "resolve", new { resolutionCode = "fixed", resolutionNote = "Cleaned the sensor" });
        Assert.Equal("closed", Str(await Ok(await Act(w.Requester, id, "confirm")), "status"));
        await _seed.Db(async db => await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE tickets SET closed_at = now() - interval '8 days' WHERE id = {id}"));
        Assert.Equal(HttpStatusCode.Forbidden, (await Act(w.Requester, id, "reopen", new { reason = "too late" })).StatusCode);        // the window has passed
        Assert.Equal(HttpStatusCode.OK, (await Act(w.Supervisor, id, "reopen", new { reason = "Customer escalated" })).StatusCode);   // staff are not bound by it
    }

    private async Task<Guid> Resolve(World w)
    {
        var id = IdOf(await Ok(await Raise(w.Requester, w.ServiceId)));
        await Ok(await w.Supervisor.Post($"/api/tickets/{id}/assign", new { assigneeId = w.AgentUser.Id, teamId = w.TeamId }));
        await Ok(await Act(w.Agent, id, "start"));
        await Ok(await Act(w.Agent, id, "resolve", new { resolutionCode = "fixed", resolutionNote = "Replaced the roller" }));
        return id;
    }

    [Fact]
    public async Task An_unconfirmed_resolution_closes_by_itself_and_a_confirmed_or_fresh_one_does_not()
    {
        var w = await Build();
        var stale = await Resolve(w); var fresh = await Resolve(w);
        await _seed.Db(async db => await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE tickets SET resolved_at = now() - interval '4 days' WHERE id = {stale}"));
        int n;
        using (var scope = fx.Factory.Services.CreateScope()) n = await scope.ServiceProvider.GetRequiredService<TicketService>().AutoCloseDueAsync();
        Assert.True(n >= 1);

        var closed = await Ok(await w.Requester.Get($"/api/tickets/{stale}"));
        Assert.Equal("closed", Str(closed, "status"));
        Assert.Contains(closed.GetProperty("timeline").EnumerateArray(), e => Str(e, "actor") == "System" && Str(e, "body").StartsWith("Closed automatically"));
        Assert.Equal("resolved", Str(await Ok(await w.Requester.Get($"/api/tickets/{fresh}")), "status"));                           // not old enough
        Assert.Equal(HttpStatusCode.OK, (await Act(w.Requester, stale, "reopen", new { reason = "Not actually fixed" })).StatusCode);   // the window still applies after automatic closing
        Assert.Contains("ticket.auto_close", await _seed.Db(db => db.AuditEvents.Where(a => a.TargetId == stale.ToString()).Select(a => a.Action).ToListAsync()));
    }

    // ============================================================ assignment
    [Fact]
    public async Task Assigning_needs_the_permission_a_person_who_works_tickets_and_an_open_ticket()
    {
        var w = await Build(); var (plain, _) = await Person("user");
        var id = IdOf(await Ok(await Raise(w.Requester, w.ServiceId)));
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Agent.Post($"/api/tickets/{id}/assign", new { assigneeId = w.AgentUser.Id })).StatusCode);        // agents don't assign
        Assert.Equal(Invalid, (await w.Supervisor.Post($"/api/tickets/{id}/assign", new { assigneeId = plain.Id })).StatusCode);                          // a requester can't be given work
        Assert.Equal(Invalid, (await w.Supervisor.Post($"/api/tickets/{id}/assign", new { assigneeId = Guid.NewGuid() })).StatusCode);
        Assert.Equal(Invalid, (await w.Supervisor.Post($"/api/tickets/{id}/assign", new { })).StatusCode);                                                // somebody or some team
        Assert.Equal(Invalid, (await w.Supervisor.Post($"/api/tickets/{id}/assign", new { teamId = Guid.NewGuid() })).StatusCode);

        var a1 = await Ok(await w.Supervisor.Post($"/api/tickets/{id}/assign", new { assigneeId = w.AgentUser.Id, teamId = w.TeamId }));
        var a2 = await Ok(await w.Supervisor.Post($"/api/tickets/{id}/assign", new { assigneeId = w.AgentUser.Id, teamId = w.TeamId }));
        Assert.Equal(Ver(a1), Ver(a2)); Assert.Equal(a1.GetProperty("timeline").GetArrayLength(), a2.GetProperty("timeline").GetArrayLength());       // assigning the same thing again changes nothing

        var back = await Ok(await w.Supervisor.Post($"/api/tickets/{id}/assign", new { teamId = w.TeamId }));                                              // back to the team's queue
        Assert.Equal("submitted", Str(back, "status")); Assert.Equal(JsonValueKind.Null, back.GetProperty("assigneeId").ValueKind);

        var people = await Ok(await w.Supervisor.Get($"/api/tickets/assignees?teamId={w.TeamId}"));
        Assert.Equal([w.AgentUser.Id], people.EnumerateArray().Select(IdOf).ToArray());                                                                    // only that team's workers
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Agent.Get("/api/tickets/assignees")).StatusCode);
    }

    // ============================================================ comments
    [Fact]
    public async Task Internal_notes_never_reach_the_requester_and_closed_tickets_take_no_comments()
    {
        var w = await Build();
        var id = IdOf(await Ok(await Raise(w.Requester, w.ServiceId)));
        await Ok(await w.Requester.Post($"/api/tickets/{id}/comments", new { body = "It started yesterday." }));
        await Ok(await w.Agent.Post($"/api/tickets/{id}/comments", new { body = "Can you send a photo?" }));
        await Ok(await w.Agent.Post($"/api/tickets/{id}/comments", new { body = "Probably the roller again — check stock.", @internal = true }));

        Assert.Equal(HttpStatusCode.Forbidden, (await w.Requester.Post($"/api/tickets/{id}/comments", new { body = "secret", @internal = true })).StatusCode);
        Assert.Equal(Invalid, (await w.Requester.Post($"/api/tickets/{id}/comments", new { body = "  " })).StatusCode);
        Assert.Equal(Invalid, (await w.Requester.Post($"/api/tickets/{id}/comments", new { body = new string('x', 4001) })).StatusCode);

        var forRequester = (await Ok(await w.Requester.Get($"/api/tickets/{id}"))).GetProperty("timeline").EnumerateArray().Where(e => Str(e, "kind") == "comment").ToList();
        Assert.Equal(["It started yesterday.", "Can you send a photo?"], forRequester.Select(e => Str(e, "body")).ToArray());
        var forAgent = await Ok(await w.Agent.Get($"/api/tickets/{id}"));
        Assert.Equal(3, forAgent.GetProperty("timeline").EnumerateArray().Count(e => Str(e, "kind") == "comment"));
        Assert.True(forAgent.GetProperty("canCommentInternal").GetBoolean()); Assert.False((await Ok(await w.Requester.Get($"/api/tickets/{id}"))).GetProperty("canCommentInternal").GetBoolean());
        Assert.Contains("ticket.note", await _seed.Db(db => db.AuditEvents.Where(a => a.TargetId == id.ToString()).Select(a => a.Action).ToListAsync()));

        // a stranger can't comment on what they can't see
        var (_, stranger) = await Person("user");
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Post($"/api/tickets/{id}/comments", new { body = "hi" })).StatusCode);
    }

    // ============================================================ editing
    [Fact]
    public async Task Staff_can_change_priority_subject_and_location_and_each_change_is_recorded()
    {
        var w = await Build();
        var site = IdOf(await Ok(await w.Admin.Post("/api/locations", new { type = "site", code = "T-" + Uniq(), name = "Tower T" })));
        var t = await Ok(await Raise(w.Requester, w.ServiceId)); var id = IdOf(t);

        Assert.Equal(HttpStatusCode.Forbidden, (await w.Requester.SendAsync(Patch, $"/api/tickets/{id}", new { subject = "New subject", priority = "low", version = Ver(t) })).StatusCode);
        var u = await Ok(await w.Agent.SendAsync(Patch, $"/api/tickets/{id}", new { subject = "Printer is smoking", priority = "urgent", locationId = site, version = Ver(t) }));
        Assert.Equal("urgent", Str(u, "priority")); Assert.Equal("Tower T", Str(u, "locationName")); Assert.Equal("Printer is smoking", Str(u, "subject"));
        var line = u.GetProperty("timeline").EnumerateArray().Last();
        Assert.Equal("updated", Str(line, "kind")); Assert.Contains("Priority changed from high to urgent.", Str(line, "body"));
        Assert.Equal(HttpStatusCode.Conflict, (await w.Agent.SendAsync(Patch, $"/api/tickets/{id}", new { subject = "stale", priority = "low", version = Ver(t) })).StatusCode);
        Assert.Equal(Invalid, (await w.Agent.SendAsync(Patch, $"/api/tickets/{id}", new { subject = "ab", priority = "low", version = Ver(u) })).StatusCode);
        Assert.Equal(Invalid, (await w.Agent.SendAsync(Patch, $"/api/tickets/{id}", new { subject = "Fine", priority = "asap", version = Ver(u) })).StatusCode);
        Assert.Equal(Invalid, (await w.Agent.SendAsync(Patch, $"/api/tickets/{id}", new { subject = "Fine", priority = "low", locationId = Guid.NewGuid(), version = Ver(u) })).StatusCode);
        var same = await Ok(await w.Agent.SendAsync(Patch, $"/api/tickets/{id}", new { subject = "Printer is smoking", priority = "urgent", locationId = site, version = Ver(u) }));
        Assert.Equal(Ver(u), Ver(same));                                                                                                                       // no change, no new version
        Assert.Contains("ticket.update", await _seed.Db(db => db.AuditEvents.Where(a => a.TargetId == id.ToString()).Select(a => a.Action).ToListAsync()));

        await Act(w.Requester, id, "cancel", new { reason = "no longer needed" });
        Assert.Equal(HttpStatusCode.Conflict, (await w.Agent.SendAsync(Patch, $"/api/tickets/{id}", new { subject = "Late edit", priority = "low" })).StatusCode);
    }

    // ============================================================ the queue
    [Fact]
    public async Task The_queue_filters_by_status_text_team_and_assignment_and_pages()
    {
        var w = await Build();
        var ids = new List<Guid>();
        for (var i = 0; i < 3; i++) ids.Add(IdOf(await Ok(await Raise(w.Requester, w.ServiceId, $"Queue test {i} {Uniq()}"))));
        await Ok(await w.Supervisor.Post($"/api/tickets/{ids[0]}/assign", new { assigneeId = w.AgentUser.Id, teamId = w.TeamId }));
        await Ok(await Act(w.Requester, ids[2], "cancel", new { reason = "no longer needed" }));
        var q = $"teamId={w.TeamId}";

        async Task<List<Guid>> Query(string extra) => [.. (await Ok(await w.Supervisor.Get($"/api/tickets?{q}&{extra}"))).GetProperty("data").EnumerateArray().Select(IdOf)];
        Assert.Equal(3, (await Query("")).Count);
        Assert.Equal(2, (await Query("status=open")).Count); Assert.Equal([ids[2]], await Query("status=cancelled")); Assert.Equal([ids[0]], await Query("status=assigned"));
        Assert.Equal([ids[1]], await Query("status=open&unassigned=true")); Assert.Equal([ids[0]], await Query($"assigneeId={w.AgentUser.Id}"));
        Assert.Empty(await Query("priority=low")); Assert.Equal(3, (await Query("priority=high")).Count);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await w.Supervisor.Get($"/api/tickets?status=nonsense")).StatusCode);

        var number = Str(await Ok(await w.Supervisor.Get($"/api/tickets/{ids[1]}")), "number");
        Assert.Equal([ids[1]], await Query($"q={number.ToLowerInvariant()}"));                                                                              // by number, case-insensitively
        Assert.Equal(3, (await Query($"q={Uri.EscapeDataString(w.RequesterUser.Name)}")).Count);                                                           // by requester name

        var page = await Ok(await w.Supervisor.Get($"/api/tickets?{q}&pageSize=2&page=2"));
        Assert.Equal(1, page.GetProperty("data").GetArrayLength()); Assert.Equal(3, page.GetProperty("total").GetInt32());
    }

    // ============================================================ tenants
    [Fact]
    public async Task One_organisations_tickets_are_invisible_and_untouchable_for_another()
    {
        var (_, a) = await NewTenant(); var (_, b) = await NewTenant();
        var sa = await PublishedService(a);
        var t = await Ok(await Raise(a, sa)); var id = IdOf(t);

        Assert.Equal(HttpStatusCode.NotFound, (await b.Get($"/api/tickets/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Act(b, id, "cancel", new { reason = "not yours" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.Post($"/api/tickets/{id}/comments", new { body = "hi" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.Post($"/api/tickets/{id}/assign", new { teamId = Guid.NewGuid() })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Raise(b, sa)).StatusCode);                                                                              // nor can they raise one from its service
        Assert.Empty((await Ok(await b.Get("/api/tickets"))).GetProperty("data").EnumerateArray());
        Assert.Empty((await Ok(await b.Get("/api/tickets/mine"))).GetProperty("data").EnumerateArray());
    }

    // ============================================================ helpers for isolated organisations
    private async Task<(Guid TenantId, TestBrowser Admin)> NewTenant()
    {
        var pu = await _seed.UserAsync(role: "super_admin"); var p = TestBrowser.Create(fx.Factory); await p.LoginAsync(pu.Email, Seed.Password);
        await p.Post("/api/auth/reauth", new { password = Seed.Password });
        var slug = "t" + Uniq(); var email = $"{slug}.admin@test.local";
        var res = await p.Post("/api/tenants", new { name = $"Org {slug}", slug, adminEmail = email });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var id = (await res.Body()).GetProperty("id").GetGuid();
        var token = InMemoryEmailSender.TokenIn(await _seed.Mail.WaitFor(email, "invited"));
        Assert.Equal(HttpStatusCode.OK, (await TestBrowser.Create(fx.Factory).Post("/api/invitations/accept", new { token, name = "Org Admin", password = StrongPw }, csrf: false)).StatusCode);
        var b = TestBrowser.Create(fx.Factory); Assert.Equal(HttpStatusCode.OK, (await b.LoginAsync(email, StrongPw)).StatusCode);
        return (id, b);
    }

    /// <summary>A published service with one required text question; the organisation admin raises tickets from it (staff may use any service).</summary>
    private async Task<Guid> PublishedService(TestBrowser admin)
    {
        var cat = IdOf(await Ok(await admin.Post("/api/catalog/categories", new { name = $"Cat {Uniq()}" })));
        var svc = IdOf(await Ok(await admin.Post("/api/catalog/services", new { name = $"Svc {Uniq()}", categoryId = cat })));
        Assert.Equal(HttpStatusCode.OK, (await admin.SendAsync(HttpMethod.Put, $"/api/catalog/services/{svc}/form", new { fields = new object[] { new { key = "what", type = "text", label = "What?", required = true }, new { key = "kind", type = "text", label = "Kind", required = false } } })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.Post($"/api/catalog/services/{svc}/publish")).StatusCode);
        return svc;
    }
}
