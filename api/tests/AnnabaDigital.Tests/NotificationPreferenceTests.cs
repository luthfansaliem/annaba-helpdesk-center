using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Tests.Infra;
using Xunit;

namespace AnnabaDigital.Tests;

/// <summary>NTF-002: each person chooses, per kind of notification, whether it appears in the app and whether it is also e-mailed. Defaults hold until changed; locked kinds cannot be switched off.</summary>
[Collection("db")]
public sealed class NotificationPreferenceTests(PostgresFixture fx)
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

    private sealed record World(TestBrowser Admin, User RequesterUser, TestBrowser Requester, User AgentUser, TestBrowser Agent, TestBrowser Supervisor, Guid ServiceId);

    private async Task<World> Build()
    {
        var (_, admin) = await Person("super_admin"); var (ru, requester) = await Person("user"); var (au, agent) = await Person("ga_agent"); var (_, sup) = await Person("ga_supervisor");
        var team = IdOf(await Ok(await admin.Post("/api/teams", new { name = $"Team {Uniq()}" })));
        await Ok(await admin.SendAsync(HttpMethod.Put, $"/api/teams/{team}/members", new { members = new[] { new { userId = au.Id, isLead = false } } }));
        var cat = IdOf(await Ok(await admin.Post("/api/catalog/categories", new { name = $"Cat {Uniq()}" })));
        var svc = IdOf(await Ok(await admin.Post("/api/catalog/services", new { name = $"Svc {Uniq()}", categoryId = cat, owningTeamId = team })));
        await Ok(await admin.SendAsync(HttpMethod.Put, $"/api/catalog/services/{svc}/form", new { fields = new object[] { new { key = "what", type = "text", label = "What?", required = true } } }));
        await Ok(await admin.Post($"/api/catalog/services/{svc}/publish"));
        return new World(admin, ru, requester, au, agent, sup, svc);
    }
    private static async Task<Guid> Raise(World w) => IdOf(await Ok(await w.Requester.Post($"/api/portal/services/{w.ServiceId}/requests", new { subject = "Printer " + Uniq(), answers = new { what = "x" } })));
    private static Task<HttpResponseMessage> Act(TestBrowser b, Guid id, string action, object? extra = null)
    {
        var body = new Dictionary<string, object?> { ["action"] = action };
        if (extra is not null) foreach (var p in extra.GetType().GetProperties()) body[p.Name] = p.GetValue(extra);
        return b.Post($"/api/tickets/{id}/transition", body);
    }
    private static async Task<List<JsonElement>> Notes(TestBrowser b) => (await Ok(await b.Get("/api/notifications?pageSize=50"))).GetProperty("data").EnumerateArray().ToList();
    private static bool Got(IEnumerable<JsonElement> notes, string kind, Guid ticket) => notes.Any(n => Str(n, "kind") == kind && n.GetProperty("ticketId").GetGuid() == ticket);
    private static Task<HttpResponseMessage> Put(TestBrowser b, params object[] items) => b.SendAsync(HttpMethod.Put, "/api/notifications/preferences", new { items });
    private static object Choice(string kind, bool inApp, bool email) => new { kind, inApp, email };
    private static JsonElement Item(JsonElement prefs, string kind) => prefs.GetProperty("items").EnumerateArray().Single(i => Str(i, "kind") == kind);
    private bool MailTo(string to, string subjectPart) => _seed.Mail.Sent.Any(x => x.To.Equals(to, StringComparison.OrdinalIgnoreCase) && x.Subject.Contains(subjectPart, StringComparison.OrdinalIgnoreCase));

    // ============================================================ the catalog and the defaults
    [Fact]
    public async Task Everyone_starts_with_the_usual_choices_and_the_locked_kind_is_marked()
    {
        var (_, b) = await Person("user");
        var prefs = await Ok(await b.Get("/api/notifications/preferences"));
        Assert.True(prefs.GetProperty("emailMaster").GetBoolean());
        Assert.Equal(NotificationCatalog.All.Count, prefs.GetProperty("items").GetArrayLength());
        foreach (var k in NotificationCatalog.All)
        {
            var i = Item(prefs, k.Key);
            Assert.True(i.GetProperty("inApp").GetBoolean(), k.Key); Assert.Equal(k.DefaultEmail, i.GetProperty("email").GetBoolean()); Assert.Equal(k.DefaultEmail, i.GetProperty("defaultEmail").GetBoolean());
            Assert.False(i.GetProperty("custom").GetBoolean(), k.Key); Assert.Equal(k.Locked, i.GetProperty("locked").GetBoolean());
        }
        Assert.True(Item(prefs, "approval.needed").GetProperty("locked").GetBoolean());
        Assert.False(Item(prefs, "ticket.started").GetProperty("email").GetBoolean()); Assert.True(Item(prefs, "ticket.assigned").GetProperty("email").GetBoolean());
        Assert.Equal(HttpStatusCode.Unauthorized, (await TestBrowser.Create(fx.Factory).Get("/api/notifications/preferences")).StatusCode);
    }

    // ============================================================ in the app
    [Fact]
    public async Task A_kind_switched_off_in_the_app_is_not_shown_to_that_person_only()
    {
        var w = await Build(); var (_, other) = await Person("user");
        var prefs = await Ok(await Put(w.Requester, Choice("ticket.started", false, false)));
        var started = Item(prefs, "ticket.started"); Assert.False(started.GetProperty("inApp").GetBoolean()); Assert.True(started.GetProperty("custom").GetBoolean());

        var t = await Raise(w);
        Assert.Equal(HttpStatusCode.OK, (await w.Supervisor.Post($"/api/tickets/{t}/assign", new { assigneeId = w.AgentUser.Id })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Act(w.Agent, t, "start")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Act(w.Agent, t, "resolve", new { resolutionCode = "fixed", resolutionNote = "Done" })).StatusCode);
        var mine = await Notes(w.Requester);
        Assert.False(Got(mine, "ticket.started", t)); Assert.True(Got(mine, "ticket.resolved", t));                       // only the chosen kind is hidden
        Assert.True(Got(await Notes(w.Agent), "ticket.assigned", t));                                                       // others are not affected

        // somebody who changed nothing still hears about it
        var t2 = IdOf(await Ok(await other.Post($"/api/portal/services/{w.ServiceId}/requests", new { subject = "Other " + Uniq(), answers = new { what = "x" } })));
        await w.Supervisor.Post($"/api/tickets/{t2}/assign", new { assigneeId = w.AgentUser.Id }); await Act(w.Agent, t2, "start");
        Assert.True(Got(await Notes(other), "ticket.started", t2));
        Assert.False((Item(await Ok(await other.Get("/api/notifications/preferences")), "ticket.started")).GetProperty("custom").GetBoolean());
    }

    // ============================================================ by e-mail
    [Fact]
    public async Task A_person_can_ask_for_e_mail_where_there_is_none_and_turn_it_off_where_there_is()
    {
        var w = await Build();
        var t = await Raise(w); await w.Supervisor.Post($"/api/tickets/{t}/assign", new { assigneeId = w.AgentUser.Id });
        await Act(w.Agent, t, "start");
        await _seed.Mail.WaitFor(w.AgentUser.Email, "assigned");                                                              // the agent's usual mail proves the outbox ran
        await _seed.Mail.AssertNone(w.RequesterUser.Email);                                                                  // by default "work started" is in the app only

        // opt in for "started", opt out of "resolved"
        await Ok(await Put(w.Requester, Choice("ticket.started", true, true), Choice("ticket.resolved", true, false)));
        var t2 = await Raise(w); await w.Supervisor.Post($"/api/tickets/{t2}/assign", new { assigneeId = w.AgentUser.Id });
        await Act(w.Agent, t2, "start");
        var mail = await _seed.Mail.WaitFor(w.RequesterUser.Email, "Work has started");                                      // a plain message with the notification's own words
        Assert.Contains("You chose to receive this by e-mail", mail.Text);

        await Act(w.Agent, t2, "resolve", new { resolutionCode = "fixed", resolutionNote = "Done" });
        Assert.True(Got(await Notes(w.Requester), "ticket.resolved", t2));                                                    // still in the app
        await w.Agent.Post($"/api/tickets/{t2}/comments", new { body = "Anything else?", @internal = false });
        await _seed.Mail.WaitFor(w.RequesterUser.Email, "New reply");                                                           // a later mail did arrive, so "resolved" would have too
        Assert.False(MailTo(w.RequesterUser.Email, "was resolved"));
    }

    [Fact]
    public async Task The_e_mail_switch_in_the_profile_still_silences_everything_except_what_work_waits_for()
    {
        var w = await Build();
        Assert.Equal(HttpStatusCode.OK, (await w.Agent.SendAsync(HttpMethod.Patch, "/api/me/preferences", new { notifyEmail = false })).StatusCode);
        Assert.False((await Ok(await w.Agent.Get("/api/notifications/preferences"))).GetProperty("emailMaster").GetBoolean());
        await Ok(await Put(w.Agent, Choice("ticket.started", true, true)));                                                    // an explicit opt-in does not beat the master switch

        var t = await Raise(w); await w.Supervisor.Post($"/api/tickets/{t}/assign", new { assigneeId = w.AgentUser.Id });
        Assert.True(Got(await Notes(w.Agent), "ticket.assigned", t));                                                          // in the app as usual
        // an approval request is still mailed
        await Ok(await w.Admin.SendAsync(HttpMethod.Put, $"/api/approvals/policies/{w.ServiceId}", new { steps = new[] { new { name = "OK", rule = "any", userIds = new[] { w.AgentUser.Id }, roleIds = Array.Empty<Guid>() } } }));
        await Raise(w);
        await _seed.Mail.WaitFor(w.AgentUser.Email, "Approval needed");
        Assert.False(MailTo(w.AgentUser.Email, "was assigned"));
    }

    // ============================================================ what cannot change, and starting over
    [Fact]
    public async Task Locked_and_unknown_choices_are_refused_defaults_are_not_stored_and_reset_clears_everything()
    {
        var (u, b) = await Person("user");
        async Task<string> Refused(params object[] items) { var r = await Put(b, items); Assert.Equal(Invalid, r.StatusCode); return (await r.Body()).GetProperty("error").GetProperty("fields").EnumerateObject().First().Value.GetString()!; }
        Assert.Contains("cannot be switched off", await Refused(Choice("approval.needed", true, false)));
        Assert.Contains("cannot be switched off", await Refused(Choice("approval.needed", false, true)));
        Assert.Contains("Unknown", await Refused(Choice("ticket.dance", true, true)));
        Assert.Contains("Listed twice", await Refused(Choice("ticket.note", true, true), Choice("ticket.note", false, false)));
        Assert.Equal(Invalid, (await Put(b)).StatusCode);                                                                      // nothing to save
        Assert.Equal(Invalid, (await Put(b, Enumerable.Range(0, 51).Select(i => Choice("ticket.note", true, true)).ToArray())).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Put(b, Choice("approval.needed", true, true))).StatusCode);                    // saying "as it is" is fine and stores nothing

        // choices equal to the defaults are not stored, so a later change of default still reaches this person
        await Ok(await Put(b, Choice("ticket.assigned", true, true), Choice("ticket.started", true, false), Choice("ticket.note", true, true)));
        Assert.Equal(1, await _seed.Db(db => db.NotificationPreferences.CountAsync(p => p.UserId == u.Id)));                    // only "note by e-mail" differs
        var prefs = await Ok(await Put(b, Choice("ticket.note", false, true), Choice("sla.breached", true, false)));
        Assert.True(Item(prefs, "ticket.note").GetProperty("custom").GetBoolean()); Assert.False(Item(prefs, "sla.breached").GetProperty("email").GetBoolean());
        Assert.Equal(2, await _seed.Db(db => db.NotificationPreferences.CountAsync(p => p.UserId == u.Id)));
        var audited = await _seed.Db(db => db.AuditEvents.Where(a => a.Action == "notification.prefs" && a.ActorId == u.Id).Select(a => a.MetaJson).ToListAsync());
        Assert.Contains(audited, m => m != null && m.Contains("changed") && m.Contains("2"));                                    // each real change is audited

        var reset = await Ok(await b.Post("/api/notifications/preferences/reset"));
        Assert.All(reset.GetProperty("items").EnumerateArray(), i => Assert.False(i.GetProperty("custom").GetBoolean()));
        Assert.Equal(0, await _seed.Db(db => db.NotificationPreferences.CountAsync(p => p.UserId == u.Id)));
        Assert.True(Item(reset, "sla.breached").GetProperty("email").GetBoolean());
    }
}
