using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Tests.Infra;
using Xunit;

namespace AnnabaDigital.Tests;

/// <summary>Queue productivity (TKT-011, TKT-015): the extra filters, bulk actions and saved views. Each test uses its own service, so exact counts hold in the shared database.</summary>
[Collection("db")]
public sealed class QueueTests(PostgresFixture fx)
{
    private readonly Seed _seed = new(fx.Factory, fx.Clock);
    private static string Uniq() => Guid.NewGuid().ToString("N")[..8];
    private const HttpStatusCode Invalid = (HttpStatusCode)422;

    private async Task<(User User, TestBrowser B)> Person(string role)
    {
        var u = await _seed.UserAsync(role: role); var b = TestBrowser.Create(fx.Factory); await b.LoginAsync(u.Email, Seed.Password); return (u, b);
    }
    private static async Task<JsonElement> Ok(HttpResponseMessage r) { Assert.True(r.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK, await r.Content.ReadAsStringAsync()); return await r.Body(); }
    private static Guid IdOf(JsonElement e) => e.GetProperty("id").GetGuid();
    private static string Str(JsonElement e, string p) => e.GetProperty(p).GetString()!;

    private sealed record World(TestBrowser Admin, TestBrowser Requester, TestBrowser Agent, User AgentUser, TestBrowser Supervisor, TestBrowser Outsider, Guid ServiceId, Guid TeamId);

    private async Task<World> Build()
    {
        var (_, admin) = await Person("super_admin"); var (_, requester) = await Person("user"); var (au, agent) = await Person("ga_agent"); var (_, sup) = await Person("ga_supervisor"); var (_, outsider) = await Person("ga_agent");
        var team = IdOf(await Ok(await admin.Post("/api/teams", new { name = $"Team {Uniq()}" })));
        Assert.Equal(HttpStatusCode.OK, (await admin.SendAsync(HttpMethod.Put, $"/api/teams/{team}/members", new { members = new[] { new { userId = au.Id, isLead = false } } })).StatusCode);
        var cat = IdOf(await Ok(await admin.Post("/api/catalog/categories", new { name = $"Cat {Uniq()}" })));
        var svc = IdOf(await Ok(await admin.Post("/api/catalog/services", new { name = $"Svc {Uniq()}", categoryId = cat, owningTeamId = team, defaultPriority = "medium" })));
        Assert.Equal(HttpStatusCode.OK, (await admin.SendAsync(HttpMethod.Put, $"/api/catalog/services/{svc}/form", new { fields = new object[] { new { key = "what", type = "text", label = "What?", required = true } } })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.Post($"/api/catalog/services/{svc}/publish")).StatusCode);
        return new World(admin, requester, agent, au, sup, outsider, svc, team);
    }

    private static async Task<Guid> Raise(World w, string subject, string? description = null, Guid? location = null)
        => IdOf(await Ok(await w.Requester.Post($"/api/portal/services/{w.ServiceId}/requests", new { subject, description = description ?? "Please help", answers = new { what = "x" }, locationId = location })));
    private static async Task<JsonElement> Ticket(TestBrowser b, Guid id) => await Ok(await b.Get($"/api/tickets/{id}"));
    private static async Task<string[]> Queue(TestBrowser b, World w, string extra = "")
    {
        var body = await Ok(await b.Get($"/api/tickets?serviceId={w.ServiceId}&status=&pageSize=100{extra}"));
        return [.. body.GetProperty("data").EnumerateArray().Select(t => Str(t, "subject")).Order()];
    }
    private static async Task<Guid> Place(TestBrowser admin, string type, string code, string name, Guid? parent = null)
        => IdOf(await Ok(await admin.Post("/api/locations", new { type, code = code + "-" + Uniq(), name, parentId = parent })));

    // ============================================================ filters and search
    [Fact]
    public async Task The_queue_filters_by_place_including_what_is_inside_it_by_date_by_me_and_searches_descriptions()
    {
        var w = await Build();
        var site = await Place(w.Admin, "site", "S", "North site"); var building = await Place(w.Admin, "building", "B", "Block A", site); var floor = await Place(w.Admin, "floor", "F", "Level 2", building);
        var otherSite = await Place(w.Admin, "site", "S", "South site");
        var onFloor = await Raise(w, "Leak on level 2", location: floor); var inSite = await Raise(w, "Light in north lobby", "A quokka lives in the ceiling", site);
        await Raise(w, "Noise in south", location: otherSite); await Raise(w, "No place given");

        Assert.Equal(["Leak on level 2", "Light in north lobby", "No place given", "Noise in south"], await Queue(w.Supervisor, w));
        Assert.Equal(["Leak on level 2"], await Queue(w.Supervisor, w, $"&locationId={floor}"));
        Assert.Equal(["Leak on level 2"], await Queue(w.Supervisor, w, $"&locationId={building}"));                             // a building stands for its floors
        Assert.Equal(["Leak on level 2", "Light in north lobby"], await Queue(w.Supervisor, w, $"&locationId={site}"));           // and a site for everything inside
        Assert.Equal(["Noise in south"], await Queue(w.Supervisor, w, $"&locationId={otherSite}"));
        Assert.Empty(await Queue(w.Supervisor, w, $"&locationId={Guid.NewGuid()}"));

        Assert.Equal(["Light in north lobby"], await Queue(w.Supervisor, w, "&q=quokka"));                                        // the description is searched too
        Assert.Equal(["Noise in south"], await Queue(w.Supervisor, w, "&q=south"));

        var tomorrow = Uri.EscapeDataString(fx.Clock.UtcNow.AddDays(1).ToString("o")); var yesterday = Uri.EscapeDataString(fx.Clock.UtcNow.AddDays(-1).ToString("o"));
        Assert.Equal(4, (await Queue(w.Supervisor, w, $"&from={yesterday}&to={tomorrow}")).Length);
        Assert.Empty(await Queue(w.Supervisor, w, $"&from={tomorrow}"));                                                         // nothing was created in the future
        Assert.Empty(await Queue(w.Supervisor, w, $"&to={yesterday}"));

        Assert.Empty(await Queue(w.Agent, w, "&assignedToMe=true"));
        Assert.Equal(HttpStatusCode.OK, (await w.Supervisor.Post($"/api/tickets/{onFloor}/assign", new { assigneeId = w.AgentUser.Id })).StatusCode);
        Assert.Equal(["Leak on level 2"], await Queue(w.Agent, w, "&assignedToMe=true"));
        Assert.Equal(["Light in north lobby", "No place given", "Noise in south"], await Queue(w.Supervisor, w, "&unassigned=true"));
        Assert.Equal(["Light in north lobby"], await Queue(w.Supervisor, w, $"&unassigned=true&locationId={site}"));               // filters combine
        _ = inSite;
    }

    // ============================================================ search is literal, and the totals are right
    [Fact]
    public async Task Search_treats_percent_and_underscore_as_ordinary_characters_and_the_totals_are_exact()
    {
        var w = await Build();
        await Raise(w, "Fifty%off sale"); await Raise(w, "under_score issue"); await Raise(w, "plain issue");
        Assert.Equal(["Fifty%off sale"], await Queue(w.Supervisor, w, "&q=" + Uri.EscapeDataString("%")));                       // a percent sign is not "anything"
        Assert.Equal(["under_score issue"], await Queue(w.Supervisor, w, "&q=" + Uri.EscapeDataString("r_s")));                   // nor is an underscore "any one character"
        Assert.Empty(await Queue(w.Supervisor, w, "&q=rxs"));
        Assert.Equal(["plain issue", "under_score issue"], await Queue(w.Supervisor, w, "&q=ISSUE"));                          // case does not matter
        Assert.Empty(await Queue(w.Supervisor, w, "&q=%5C"));                                                                     // a backslash is literal too, and matches nothing here

        // the total is right on a short page, on a full page with more behind it, and on a later page
        async Task<(int Total, int Rows)> Page(int page, int size) { var b = await Ok(await w.Supervisor.Get($"/api/tickets?serviceId={w.ServiceId}&status=&pageSize={size}&page={page}")); return (b.GetProperty("total").GetInt32(), b.GetProperty("data").GetArrayLength()); }
        Assert.Equal((3, 3), await Page(1, 10)); Assert.Equal((3, 2), await Page(1, 2)); Assert.Equal((3, 1), await Page(2, 2)); Assert.Equal((3, 0), await Page(3, 2));
    }

    // ============================================================ bulk actions
    [Fact]
    public async Task A_bulk_change_goes_through_the_single_ticket_rules_and_reports_every_outcome()
    {
        var w = await Build();
        var ids = new List<Guid>(); for (var i = 0; i < 4; i++) ids.Add(await Raise(w, $"Bulk {i}"));
        var cancelled = await Raise(w, "Bulk cancelled");
        Assert.Equal(HttpStatusCode.OK, (await w.Requester.Post($"/api/tickets/{cancelled}/transition", new { action = "cancel", reason = "No longer needed" })).StatusCode);

        // assign: everyone who can be assigned goes through; the cancelled one is refused and says why
        var all = ids.Append(cancelled).Append(Guid.NewGuid()).ToArray();
        var r = await Ok(await w.Supervisor.Post("/api/tickets/bulk", new { ids = all, action = "assign", assigneeId = w.AgentUser.Id }));
        Assert.Equal(6, r.GetProperty("requested").GetInt32()); Assert.Equal(4, r.GetProperty("succeeded").GetInt32());
        var items = r.GetProperty("items").EnumerateArray().ToDictionary(i => IdOf(i));
        Assert.All(ids, id => Assert.True(items[id].GetProperty("ok").GetBoolean()));
        Assert.False(items[cancelled].GetProperty("ok").GetBoolean()); Assert.False(string.IsNullOrWhiteSpace(Str(items[cancelled], "error")));
        Assert.Equal("Ticket not found.", Str(items[all[^1]], "error"));                                                          // an unknown id is just "not found"
        foreach (var id in ids) { var t = await Ticket(w.Supervisor, id); Assert.Equal("assigned", Str(t, "status")); Assert.Equal(w.AgentUser.Id, t.GetProperty("assigneeId").GetGuid()); }

        // priority: changed on all, with a timeline entry and a new SLA calculation, no-op where already that priority
        var p = await Ok(await w.Supervisor.Post("/api/tickets/bulk", new { ids = ids.ToArray(), action = "priority", priority = "urgent" }));
        Assert.Equal(4, p.GetProperty("succeeded").GetInt32());
        foreach (var id in ids) Assert.Equal("urgent", Str(await Ticket(w.Supervisor, id), "priority"));
        Assert.Equal(4, (await Ok(await w.Supervisor.Post("/api/tickets/bulk", new { ids = ids.ToArray(), action = "priority", priority = "urgent" }))).GetProperty("succeeded").GetInt32());

        // transitions: start works on assigned tickets; a second start is refused ticket by ticket, not as a whole
        var s1 = await Ok(await w.Agent.Post("/api/tickets/bulk", new { ids = ids.Take(2).ToArray(), action = "start" }));
        Assert.Equal(2, s1.GetProperty("succeeded").GetInt32());
        var s2 = await Ok(await w.Agent.Post("/api/tickets/bulk", new { ids = ids.ToArray(), action = "start" }));
        Assert.Equal(2, s2.GetProperty("succeeded").GetInt32()); Assert.Equal(2, s2.GetProperty("items").EnumerateArray().Count(i => !i.GetProperty("ok").GetBoolean()));
        foreach (var id in ids) Assert.Equal("inprogress", Str(await Ticket(w.Supervisor, id), "status"));
        var hold = await Ok(await w.Agent.Post("/api/tickets/bulk", new { ids = ids.Take(1).ToArray(), action = "hold", reason = "Waiting for parts" }));
        Assert.Equal(1, hold.GetProperty("succeeded").GetInt32()); Assert.Equal("onhold", Str(await Ticket(w.Supervisor, ids[0]), "status"));

        // the single-ticket trail is there for each, plus one summary entry for the batch
        var timeline = (await Ticket(w.Supervisor, ids[1])).GetProperty("timeline").EnumerateArray().Select(e => Str(e, "kind")).ToArray();
        Assert.Contains("assigned", timeline); Assert.Contains("updated", timeline); Assert.Contains("status", timeline);
        var audit = await _seed.Db(db => db.AuditEvents.Where(a => a.Action == "ticket.bulk").OrderByDescending(a => a.Id).Take(20).Select(a => a.MetaJson).ToListAsync());
        Assert.Contains(audit, m => m != null && m.Contains("assign") && m.Contains("\"succeeded\"") && m.Contains("4"));
    }

    [Fact]
    public async Task A_bulk_change_is_checked_before_anything_changes_and_respects_who_may_see_and_do_what()
    {
        var w = await Build(); var a = await Raise(w, "Guarded A"); var b = await Raise(w, "Guarded B");
        async Task<string> Field(HttpResponseMessage r, string field) { Assert.Equal(Invalid, r.StatusCode); return (await r.Body()).GetProperty("error").GetProperty("fields").GetProperty(field).GetString()!; }
        Assert.Contains("at least one", await Field(await w.Supervisor.Post("/api/tickets/bulk", new { ids = Array.Empty<Guid>(), action = "start" }), "ids"));
        Assert.Contains("at most 100", await Field(await w.Supervisor.Post("/api/tickets/bulk", new { ids = Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()).ToArray(), action = "start" }), "ids"));
        Assert.Contains("Choose", await Field(await w.Supervisor.Post("/api/tickets/bulk", new { ids = new[] { a }, action = "explode" }), "action"));
        Assert.Contains("person or a team", await Field(await w.Supervisor.Post("/api/tickets/bulk", new { ids = new[] { a }, action = "assign" }), "assigneeId"));
        Assert.Contains("low, medium", await Field(await w.Supervisor.Post("/api/tickets/bulk", new { ids = new[] { a }, action = "priority", priority = "huge" }), "priority"));
        Assert.Contains("reason", await Field(await w.Supervisor.Post("/api/tickets/bulk", new { ids = new[] { a }, action = "hold" }), "reason"));
        Assert.Contains("resolution", await Field(await w.Supervisor.Post("/api/tickets/bulk", new { ids = new[] { a }, action = "resolve", resolutionCode = "fixed" }), "resolutionNote"));

        Assert.Equal(HttpStatusCode.Forbidden, (await w.Agent.Post("/api/tickets/bulk", new { ids = new[] { a }, action = "assign", assigneeId = w.AgentUser.Id })).StatusCode);   // assigning needs tickets.assign
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Requester.Post("/api/tickets/bulk", new { ids = new[] { a }, action = "priority", priority = "high" })).StatusCode);       // a requester has no queue
        Assert.Equal(HttpStatusCode.Unauthorized, (await TestBrowser.Create(fx.Factory).Post("/api/tickets/bulk", new { ids = new[] { a }, action = "priority", priority = "high" }, csrf: false)).StatusCode);

        // an agent from another team can use the feature but reaches none of these tickets
        var o = await Ok(await w.Outsider.Post("/api/tickets/bulk", new { ids = new[] { a, b }, action = "priority", priority = "urgent" }));
        Assert.Equal(0, o.GetProperty("succeeded").GetInt32()); Assert.All(o.GetProperty("items").EnumerateArray(), i => Assert.Equal("Ticket not found.", Str(i, "error")));
        Assert.Equal("medium", Str(await Ticket(w.Supervisor, a), "priority"));
    }

    // ============================================================ saved views
    [Fact]
    public async Task Views_keep_clean_filters_stay_private_and_can_be_shared_by_people_who_assign()
    {
        var (_, sup) = await Person("ga_supervisor"); var (_, other) = await Person("ga_supervisor"); var (_, agent) = await Person("ga_agent"); var (_, plain) = await Person("user");
        var name = "Late " + Uniq();
        var team = Guid.NewGuid().ToString();
        var v = await Ok(await sup.Post("/api/ticket-views", new { name, filter = new Dictionary<string, string> { ["status"] = "OPEN", ["sla"] = "Breached", ["teamId"] = team, ["unassigned"] = "true", ["assignedToMe"] = "false", ["q"] = "  ", ["from"] = "2026-01-31" } }));
        var f = v.GetProperty("filter"); Assert.Equal("open", Str(f, "status")); Assert.Equal("breached", Str(f, "sla")); Assert.Equal("true", Str(f, "unassigned")); Assert.Equal("2026-01-31", Str(f, "from"));
        Assert.False(f.TryGetProperty("assignedToMe", out _)); Assert.False(f.TryGetProperty("q", out _));                          // false and blank values are dropped
        Assert.True(v.GetProperty("mine").GetBoolean()); Assert.False(v.GetProperty("shared").GetBoolean()); Assert.True(v.GetProperty("canEdit").GetBoolean());
        var id = IdOf(v);

        // private: nobody else sees it or can touch it
        Assert.DoesNotContain((await Ok(await other.Get("/api/ticket-views"))).EnumerateArray().Select(x => IdOf(x)), x => x == id);
        Assert.Equal(HttpStatusCode.NotFound, (await other.SendAsync(HttpMethod.Delete, $"/api/ticket-views/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.SendAsync(HttpMethod.Patch, $"/api/ticket-views/{id}", new { name = "Mine now", filter = new Dictionary<string, string>() })).StatusCode);

        // the people who may not use the queue may not keep views either
        Assert.Equal(HttpStatusCode.Forbidden, (await plain.Get("/api/ticket-views")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await plain.Post("/api/ticket-views", new { name = "Nope", filter = new Dictionary<string, string>() })).StatusCode);

        // sharing needs the right to assign
        Assert.Equal(HttpStatusCode.Forbidden, (await agent.Post("/api/ticket-views", new { name = "Agent shared " + Uniq(), shared = true, filter = new Dictionary<string, string>() })).StatusCode);
        var shared = await Ok(await sup.SendAsync(HttpMethod.Patch, $"/api/ticket-views/{id}", new { name, shared = true, filter = new Dictionary<string, string> { ["status"] = "open" } }));
        Assert.True(shared.GetProperty("shared").GetBoolean()); Assert.Single(shared.GetProperty("filter").EnumerateObject());   // the filter was replaced, not merged
        var seen = (await Ok(await agent.Get("/api/ticket-views"))).EnumerateArray().Single(x => IdOf(x) == id);
        Assert.False(seen.GetProperty("mine").GetBoolean()); Assert.False(seen.GetProperty("canEdit").GetBoolean()); Assert.NotNull(Str(seen, "ownerName"));
        Assert.Equal(HttpStatusCode.Forbidden, (await agent.SendAsync(HttpMethod.Delete, $"/api/ticket-views/{id}")).StatusCode);   // read-only for someone who cannot assign
        Assert.Equal(HttpStatusCode.Forbidden, (await agent.SendAsync(HttpMethod.Patch, $"/api/ticket-views/{id}", new { name = "Hijack", filter = new Dictionary<string, string>() })).StatusCode);
        Assert.True((await Ok(await other.Get("/api/ticket-views"))).EnumerateArray().Single(x => IdOf(x) == id).GetProperty("canEdit").GetBoolean());  // another supervisor may maintain it
        Assert.Equal(HttpStatusCode.NoContent, (await other.SendAsync(HttpMethod.Delete, $"/api/ticket-views/{id}")).StatusCode);
        Assert.DoesNotContain((await Ok(await sup.Get("/api/ticket-views"))).EnumerateArray().Select(x => IdOf(x)), x => x == id);
        Assert.Contains("ticket.view.create", await _seed.Db(db => db.AuditEvents.Select(a => a.Action).Distinct().ToListAsync()));
    }

    [Fact]
    public async Task A_view_must_be_valid_unique_and_within_the_limit()
    {
        var (_, sup) = await Person("ga_supervisor");
        async Task<string> Refused(object body, string field) { var r = await sup.Post("/api/ticket-views", body); Assert.Equal(Invalid, r.StatusCode); return (await r.Body()).GetProperty("error").GetProperty("fields").GetProperty(field).GetString()!; }
        Assert.Contains("2–60", await Refused(new { name = "x", filter = new Dictionary<string, string>() }, "name"));
        Assert.Contains("2–60", await Refused(new { name = new string('x', 61), filter = new Dictionary<string, string>() }, "name"));
        Assert.Contains("Unknown filter", await Refused(new { name = "Bad key", filter = new Dictionary<string, string> { ["tenantId"] = Guid.NewGuid().ToString() } }, "filter"));
        Assert.Contains("Unknown status", await Refused(new { name = "Bad status", filter = new Dictionary<string, string> { ["status"] = "dancing" } }, "filter"));
        Assert.Contains("id", await Refused(new { name = "Bad id", filter = new Dictionary<string, string> { ["teamId"] = "abc" } }, "filter"));
        Assert.Contains("date", await Refused(new { name = "Bad date", filter = new Dictionary<string, string> { ["from"] = "31/01/2026" } }, "filter"));
        Assert.Contains("too long", await Refused(new { name = "Long search", filter = new Dictionary<string, string> { ["q"] = new string('q', 101) } }, "filter"));

        var n = "Same " + Uniq();
        Assert.Equal(HttpStatusCode.Created, (await sup.Post("/api/ticket-views", new { name = n, filter = new Dictionary<string, string>() })).StatusCode);
        Assert.Contains("already have", await Refused(new { name = n.ToUpperInvariant(), filter = new Dictionary<string, string>() }, "name"));       // names are unique per person, ignoring case
        for (var i = 1; i < 30; i++) Assert.Equal(HttpStatusCode.Created, (await sup.Post("/api/ticket-views", new { name = $"View {i} {Uniq()}", filter = new Dictionary<string, string>() })).StatusCode);
        Assert.Contains("at most 30", await Refused(new { name = "One too many", filter = new Dictionary<string, string>() }, "name"));
    }
}
