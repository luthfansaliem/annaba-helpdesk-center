using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Tests.Infra;
using Xunit;

namespace AnnabaDigital.Tests;

/// <summary>Approvers who are away or have a manager (APR-002/004): the reporting line, "the requester's manager" as a step, out-of-office delegation, handing an approval over.</summary>
[Collection("db")]
public sealed class DelegationTests(PostgresFixture fx)
{
    private readonly Seed _seed = new(fx.Factory, fx.Clock);
    private static string Uniq() => Guid.NewGuid().ToString("N")[..8];
    private const HttpStatusCode Invalid = (HttpStatusCode)422;
    private DateTimeOffset Now => fx.Clock.UtcNow;

    private async Task<(User User, TestBrowser B)> Person(string role)
    {
        var u = await _seed.UserAsync(role: role); var b = TestBrowser.Create(fx.Factory); await b.LoginAsync(u.Email, Seed.Password); return (u, b);
    }
    private static async Task<JsonElement> Ok(HttpResponseMessage r) { Assert.True(r.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK, await r.Content.ReadAsStringAsync()); return await r.Body(); }
    private static async Task<List<JsonElement>> List(HttpResponseMessage r) => (await Ok(r)).EnumerateArray().ToList();
    private static Guid IdOf(JsonElement e) => e.GetProperty("id").GetGuid();
    private static string Str(JsonElement e, string p) => e.GetProperty(p).GetString()!;
    private static async Task<string> Field(HttpResponseMessage r, string field) { Assert.Equal(Invalid, r.StatusCode); return (await r.Body()).GetProperty("error").GetProperty("fields").GetProperty(field).GetString()!; }

    private sealed record World(TestBrowser Admin, TestBrowser Requester, User RequesterUser, User A1, TestBrowser A1B, User A2, TestBrowser A2B, Guid ServiceId);

    private async Task<World> Build()
    {
        var (_, admin) = await Person("super_admin"); var (ru, requester) = await Person("user"); var (a1, a1b) = await Person("user"); var (a2, a2b) = await Person("user");
        var (agu, _) = await Person("ga_agent");
        var team = IdOf(await Ok(await admin.Post("/api/teams", new { name = $"Team {Uniq()}" })));
        await Ok(await admin.SendAsync(HttpMethod.Put, $"/api/teams/{team}/members", new { members = new[] { new { userId = agu.Id, isLead = false } } }));
        var cat = IdOf(await Ok(await admin.Post("/api/catalog/categories", new { name = $"Cat {Uniq()}" })));
        var svc = IdOf(await Ok(await admin.Post("/api/catalog/services", new { name = $"Svc {Uniq()}", categoryId = cat, owningTeamId = team })));
        await Ok(await admin.SendAsync(HttpMethod.Put, $"/api/catalog/services/{svc}/form", new { fields = new object[] { new { key = "what", type = "text", label = "What?", required = true } } }));
        await Ok(await admin.Post($"/api/catalog/services/{svc}/publish"));
        return new World(admin, requester, ru, a1, a1b, a2, a2b, svc);
    }

    private static Task<HttpResponseMessage> SetPolicy(TestBrowser admin, Guid svc, object[] steps, Guid? fallback = null) => admin.SendAsync(HttpMethod.Put, $"/api/approvals/policies/{svc}", new { steps, fallbackUserId = fallback });
    private static object Step(string rule, Guid[]? users = null, bool manager = false) => new { name = "Approval", rule, userIds = users ?? [], roleIds = Array.Empty<Guid>(), requesterManager = manager };
    private static Task<HttpResponseMessage> Raise(TestBrowser b, Guid svc) => b.Post($"/api/portal/services/{svc}/requests", new { subject = "Please approve " + Uniq(), answers = new { what = "paper" } });
    private static async Task<Guid> RaiseOk(TestBrowser b, Guid svc) => IdOf(await Ok(await Raise(b, svc)));
    private static async Task<List<JsonElement>> Pending(TestBrowser b, Guid? ticket = null) => (await List(await b.Get("/api/approvals/mine"))).Where(a => Str(a, "status") == "pending" && (ticket is null || a.GetProperty("ticketId").GetGuid() == ticket)).ToList();
    private static Task<HttpResponseMessage> Decide(TestBrowser b, Guid approval, bool approve) => b.Post($"/api/approvals/{approval}/decide", new { approve, comment = approve ? null : "No thanks" });
    private static async Task<JsonElement> Ticket(TestBrowser b, Guid id) => await Ok(await b.Get($"/api/tickets/{id}"));
    private static Task<HttpResponseMessage> Delegate(TestBrowser b, Guid to, DateTimeOffset? from, DateTimeOffset? until, string? note = null) => b.Post("/api/approvals/delegations", new { delegateId = to, startsAt = from, endsAt = until, note });
    private static async Task<List<JsonElement>> Notes(TestBrowser b) => (await Ok(await b.Get("/api/notifications?pageSize=50"))).GetProperty("data").EnumerateArray().ToList();

    // ============================================================ reporting line
    [Fact]
    public async Task A_manager_is_set_by_people_who_manage_users_and_cannot_loop_back()
    {
        var (_, userAdmin) = await Person("user_admin"); var (_, plain) = await Person("user");
        var (a, _) = await Person("user"); var (b, _) = await Person("user"); var (c, _) = await Person("user");
        Task<HttpResponseMessage> Set(TestBrowser who, Guid user, Guid? mgr) => who.SendAsync(HttpMethod.Put, $"/api/users/{user}/manager", new { managerId = mgr });

        Assert.Equal(HttpStatusCode.Forbidden, (await Set(plain, a.Id, b.Id)).StatusCode);
        var d = await Ok(await Set(userAdmin, a.Id, b.Id));
        Assert.Equal(b.Id, d.GetProperty("managerId").GetGuid()); Assert.Equal(b.Name, Str(d, "managerName"));
        Assert.Equal(b.Id, (await Ok(await userAdmin.Get($"/api/users/{a.Id}"))).GetProperty("managerId").GetGuid());

        Assert.Contains("own manager", await Field(await Set(userAdmin, a.Id, a.Id), "managerId"));
        Assert.Contains("active person", await Field(await Set(userAdmin, a.Id, Guid.NewGuid()), "managerId"));
        Assert.Equal(HttpStatusCode.OK, (await Set(userAdmin, b.Id, c.Id)).StatusCode);
        Assert.Contains("loop", await Field(await Set(userAdmin, c.Id, a.Id), "managerId"));                          // a → b → c → a would be a circle
        Assert.Equal(HttpStatusCode.OK, (await Set(userAdmin, a.Id, null)).StatusCode);                               // clearing is allowed
        Assert.Equal(JsonValueKind.Null, (await Ok(await userAdmin.Get($"/api/users/{a.Id}"))).GetProperty("managerId").ValueKind);
        Assert.Contains("user.manager", await _seed.Db(db => db.AuditEvents.Select(x => x.Action).Distinct().ToListAsync()));
    }

    [Fact]
    public async Task A_step_can_ask_for_the_requesters_manager_and_falls_back_when_there_is_none()
    {
        var w = await Build(); var (_, userAdmin) = await Person("user_admin"); var (fb, fbB) = await Person("user");
        // a step must still name someone or the manager
        Assert.Equal(Invalid, (await SetPolicy(w.Admin, w.ServiceId, [Step("any")])).StatusCode);
        var set = await Ok(await SetPolicy(w.Admin, w.ServiceId, [Step("any", manager: true)], fb.Id));
        Assert.True(set.GetProperty("steps")[0].GetProperty("requesterManager").GetBoolean());

        // no manager yet: the fallback person decides
        var first = await RaiseOk(w.Requester, w.ServiceId);
        Assert.Single(await Pending(fbB, first)); Assert.Empty(await Pending(w.A1B, first));

        // with a manager, the manager decides and the fallback is not asked
        Assert.Equal(HttpStatusCode.OK, (await userAdmin.SendAsync(HttpMethod.Put, $"/api/users/{w.RequesterUser.Id}/manager", new { managerId = w.A1.Id })).StatusCode);
        var second = await RaiseOk(w.Requester, w.ServiceId);
        var mine = Assert.Single(await Pending(w.A1B, second)); Assert.Empty(await Pending(fbB, second));
        Assert.Equal(HttpStatusCode.NoContent, (await Decide(w.A1B, IdOf(mine), true)).StatusCode);
        Assert.Equal("submitted", Str(await Ticket(w.Requester, second), "status"));                                      // released to the team

        // without a manager and without a fallback the request cannot be made, and says why
        await Ok(await SetPolicy(w.Admin, w.ServiceId, [Step("any", manager: true)]));
        await userAdmin.SendAsync(HttpMethod.Put, $"/api/users/{w.RequesterUser.Id}/manager", new { managerId = (Guid?)null });
        var refused = await Raise(w.Requester, w.ServiceId); Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("No approver", (await refused.Body()).GetProperty("error").GetProperty("message").GetString());
    }

    // ============================================================ delegation
    [Fact]
    public async Task While_someone_is_away_approvals_go_to_their_cover_and_the_trail_names_both()
    {
        var w = await Build(); var (_, other) = await Person("user");
        await Ok(await SetPolicy(w.Admin, w.ServiceId, [Step("any", [w.A1.Id])]));

        var made = await Ok(await Delegate(w.A1B, w.A2.Id, Now, Now.AddDays(7), "On leave"));
        Assert.Equal(0, made.GetProperty("handedOver").GetInt32()); Assert.Equal("active", Str(made.GetProperty("delegation"), "state"));
        Assert.Contains("approval.delegated", (await Notes(w.A2B)).Select(n => Str(n, "kind")));

        var t = await RaiseOk(w.Requester, w.ServiceId);
        Assert.Empty(await Pending(w.A1B, t));                                                                               // the person who is away is not asked
        var item = Assert.Single(await Pending(w.A2B, t)); Assert.Equal(w.A1.Name, Str(item, "onBehalfOfName"));
        var row = (await Ticket(w.Requester, t)).GetProperty("approvals").EnumerateArray().Single();
        Assert.Equal(w.A2.Id, row.GetProperty("approverId").GetGuid()); Assert.Equal(w.A1.Name, Str(row, "onBehalfOfName"));
        Assert.Contains($"(for {w.A1.Name})", Str((await Notes(w.A2B)).First(n => Str(n, "kind") == "approval.needed"), "body"));

        Assert.Equal(HttpStatusCode.NotFound, (await Decide(w.A1B, IdOf(item), true)).StatusCode);                        // the away person cannot decide a row that is not theirs
        Assert.Equal(HttpStatusCode.NoContent, (await Decide(w.A2B, IdOf(item), true)).StatusCode);
        var done = await Ticket(w.Requester, t); Assert.Equal("submitted", Str(done, "status"));
        var audit = await _seed.Db(db => db.AuditEvents.Where(a => a.Action == "approval.approve" && a.TargetId == t.ToString()).Select(a => a.MetaJson).ToListAsync());
        Assert.Contains(audit, m => m != null && m.Contains(w.A1.Id.ToString()));                                            // who it was for is on the audit record too

        // ending the delegation sends the next one back to the original approver
        var id = IdOf(made.GetProperty("delegation"));
        Assert.Equal("revoked", Str(await Ok(await w.A1B.Post($"/api/approvals/delegations/{id}/end")), "state"));
        var later = await RaiseOk(w.Requester, w.ServiceId); Assert.Single(await Pending(w.A1B, later)); Assert.Empty(await Pending(w.A2B, later));
        Assert.Equal(HttpStatusCode.Conflict, (await w.A1B.Post($"/api/approvals/delegations/{id}/end")).StatusCode);       // already ended
        Assert.Equal(HttpStatusCode.NotFound, (await other.Post($"/api/approvals/delegations/{id}/end")).StatusCode);       // only the owner
    }

    [Fact]
    public async Task A_cover_never_approves_their_own_request_or_twice_in_one_step_and_a_future_delegation_waits()
    {
        var w = await Build();
        await Ok(await SetPolicy(w.Admin, w.ServiceId, [Step("any", [w.A1.Id])]));
        await Ok(await Delegate(w.A1B, w.A2.Id, Now, Now.AddDays(3)));
        // the cover raised this request, so the approval stays with the person it was for
        var own = await RaiseOk(w.A2B, w.ServiceId); Assert.Single(await Pending(w.A1B, own)); Assert.Empty(await Pending(w.A2B, own));

        // a cover who is already a named approver of the step is not asked twice, and the absent person is dropped
        await Ok(await SetPolicy(w.Admin, w.ServiceId, [Step("all", [w.A1.Id, w.A2.Id])]));
        var t = await RaiseOk(w.Requester, w.ServiceId);
        var rows = (await Ticket(w.Requester, t)).GetProperty("approvals").EnumerateArray().ToList();
        Assert.Single(rows); Assert.Equal(w.A2.Id, rows[0].GetProperty("approverId").GetGuid());

        // a delegation that has not started yet changes nothing
        var (c1, c1b) = await Person("user"); var (c2, c2b) = await Person("user");
        await Ok(await SetPolicy(w.Admin, w.ServiceId, [Step("any", [c1.Id])]));
        var up = await Ok(await Delegate(c1b, c2.Id, Now.AddDays(2), Now.AddDays(5))); Assert.Equal("upcoming", Str(up.GetProperty("delegation"), "state"));
        var t2 = await RaiseOk(w.Requester, w.ServiceId); Assert.Single(await Pending(c1b, t2)); Assert.Empty(await Pending(c2b, t2));
    }

    [Fact]
    public async Task A_delegation_that_starts_now_takes_over_what_is_already_waiting_and_voids_the_old_link()
    {
        var w = await Build();
        await Ok(await SetPolicy(w.Admin, w.ServiceId, [Step("any", [w.A1.Id])]));
        var t = await RaiseOk(w.Requester, w.ServiceId); var mail = await _seed.Mail.WaitFor(w.A1.Email, "Approval needed"); var token = InMemoryEmailSender.TokenIn(mail);
        var own = await RaiseOk(w.A2B, w.ServiceId);                                                                          // a request by the cover: it cannot move to them

        var made = await Ok(await Delegate(w.A1B, w.A2.Id, Now, Now.AddDays(5)));
        Assert.Equal(1, made.GetProperty("handedOver").GetInt32()); Assert.Equal(1, made.GetProperty("kept").GetInt32());
        Assert.Empty(await Pending(w.A1B, t)); Assert.Single(await Pending(w.A2B, t)); Assert.Single(await Pending(w.A1B, own));
        Assert.Equal(HttpStatusCode.Gone, (await TestBrowser.Create(fx.Factory).Get($"/api/approvals/link/{token}")).StatusCode);   // the old e-mail link no longer works
        var timeline = (await Ticket(w.Requester, t)).GetProperty("timeline").EnumerateArray().Select(e => e.GetProperty("body").GetString()).ToList();
        Assert.Contains(timeline, b => b != null && b.StartsWith("Approval handed over from"));
    }

    [Fact]
    public async Task A_delegation_is_validated_and_belongs_to_one_person()
    {
        var (a, ab) = await Person("user"); var (b, _) = await Person("user"); var (_, anon) = (0, TestBrowser.Create(fx.Factory));
        var suspended = await _seed.UserAsync(role: "user"); await _seed.Db(async db => { var u = await db.Users.FirstAsync(x => x.Id == suspended.Id); u.Status = UserStatus.Suspended; await db.SaveChangesAsync(); return 0; });

        Assert.Equal(HttpStatusCode.Unauthorized, (await Delegate(anon, b.Id, Now, Now.AddDays(1))).StatusCode);
        Assert.Contains("Choose who", await Field(await ab.Post("/api/approvals/delegations", new { endsAt = Now.AddDays(1) }), "delegateId"));
        Assert.Contains("yourself", await Field(await Delegate(ab, a.Id, Now, Now.AddDays(1)), "delegateId"));
        Assert.Contains("active person", await Field(await Delegate(ab, suspended.Id, Now, Now.AddDays(1)), "delegateId"));
        Assert.Contains("when you are back", await Field(await Delegate(ab, b.Id, Now, null), "endsAt"));
        Assert.Contains("after the start", await Field(await Delegate(ab, b.Id, Now.AddDays(2), Now.AddDays(1)), "endsAt"));
        Assert.Contains("future", await Field(await Delegate(ab, b.Id, Now.AddDays(-3), Now.AddHours(-1)), "endsAt"));
        Assert.Contains("180 days", await Field(await Delegate(ab, b.Id, Now, Now.AddDays(181)), "endsAt"));
        Assert.Contains("past", await Field(await Delegate(ab, b.Id, Now.AddDays(-3), Now.AddDays(1)), "startsAt"));
        Assert.Contains("300", await Field(await Delegate(ab, b.Id, Now, Now.AddDays(1), new string('x', 301)), "note"));

        Assert.Equal(HttpStatusCode.Created, (await Delegate(ab, b.Id, Now.AddDays(1), Now.AddDays(4))).StatusCode);
        Assert.Contains("already have", await Field(await Delegate(ab, b.Id, Now.AddDays(3), Now.AddDays(6)), "startsAt"));   // periods may not overlap
        Assert.Equal(HttpStatusCode.Created, (await Delegate(ab, b.Id, Now.AddDays(4), Now.AddDays(6))).StatusCode);          // back to back is fine
        var mine = await List(await ab.Get("/api/approvals/delegations")); Assert.Equal(2, mine.Count); Assert.All(mine, d => Assert.Equal("upcoming", Str(d, "state")));
        Assert.Empty(await List(await (await Person("user")).B.Get("/api/approvals/delegations")));                           // nobody else's
    }

    // ============================================================ handing one over
    [Fact]
    public async Task An_approver_can_hand_a_waiting_approval_to_someone_else_and_administrators_can_do_it_for_them()
    {
        var w = await Build(); var (c, cb) = await Person("user"); var (_, stranger) = await Person("user");
        await Ok(await SetPolicy(w.Admin, w.ServiceId, [Step("any", [w.A1.Id, w.A2.Id])]));
        var t = await RaiseOk(w.Requester, w.ServiceId); var mine = IdOf(Assert.Single(await Pending(w.A1B, t)));
        Task<HttpResponseMessage> Reassign(TestBrowser who, Guid approval, Guid? to) => who.Post($"/api/approvals/{approval}/reassign", new { toUserId = to });

        Assert.Equal(HttpStatusCode.NotFound, (await Reassign(stranger, mine, c.Id)).StatusCode);                              // not theirs
        Assert.Contains("Choose a person", await Field(await Reassign(w.A1B, mine, null), "toUserId"));
        Assert.Contains("made the request", await Field(await Reassign(w.A1B, mine, w.RequesterUser.Id), "toUserId"));         // never the requester
        Assert.Contains("already an approver", await Field(await Reassign(w.A1B, mine, w.A2.Id), "toUserId"));                // a2 is in this step already

        Assert.Equal(HttpStatusCode.NoContent, (await Reassign(w.A1B, mine, c.Id)).StatusCode);
        Assert.Empty(await Pending(w.A1B, t)); var theirs = Assert.Single(await Pending(cb, t)); Assert.Equal(w.A1.Name, Str(theirs, "onBehalfOfName"));
        Assert.Contains("approval.needed", (await Notes(cb)).Select(n => Str(n, "kind")));
        Assert.Contains((await Ticket(w.Requester, t)).GetProperty("timeline").EnumerateArray(), e => e.GetProperty("body").GetString()?.StartsWith("Approval handed over from") == true);

        // an administrator can move it again; the trail still names the person it was first for
        var (d, db) = await Person("user");
        Assert.Equal(HttpStatusCode.NoContent, (await Reassign(w.Admin, IdOf(theirs), d.Id)).StatusCode);
        Assert.Equal(w.A1.Name, Str(Assert.Single(await Pending(db, t)), "onBehalfOfName"));

        // once decided it can no longer move
        Assert.Equal(HttpStatusCode.NoContent, (await Decide(db, IdOf((await Pending(db, t))[0]), true)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Reassign(w.A1B, mine, c.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Reassign(w.Admin, mine, c.Id)).StatusCode);
        Assert.Contains("approval.reassign", await _seed.Db(x => x.AuditEvents.Select(a => a.Action).Distinct().ToListAsync()));
    }
}
