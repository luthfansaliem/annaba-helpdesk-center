using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using AnnabaDigital.Application.Helpdesk;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Tests.Infra;
using Xunit;

namespace AnnabaDigital.Tests;

/// <summary>Approvals (APR-001/003/004/006) and notifications (NTF-001/002): policy admin, sequential and parallel steps, rejection, self-approval, the one-time e-mail link, and who is told what.</summary>
[Collection("db")]
public sealed class ApprovalAndNotificationTests(PostgresFixture fx)
{
    private readonly Seed _seed = new(fx.Factory, fx.Clock);
    private static string Uniq() => Guid.NewGuid().ToString("N")[..8];
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
    private DateTimeOffset Ago(int minutes) => fx.Clock.UtcNow.AddMinutes(-minutes);
    private Task Sql(FormattableString sql) => _seed.Db(async db => await db.Database.ExecuteSqlInterpolatedAsync(sql));

    private sealed record World(TestBrowser Admin, User AdminUser, TestBrowser Requester, User RequesterUser, User A1, TestBrowser A1B, User A2, TestBrowser A2B, TestBrowser Supervisor, User SupervisorUser, TestBrowser Agent, User AgentUser, Guid ServiceId, Guid TeamId);

    private async Task<World> Build()
    {
        var (au, admin) = await Person("super_admin"); var (ru, requester) = await Person("user"); var (a1, a1b) = await Person("user"); var (a2, a2b) = await Person("user");
        var (supu, sup) = await Person("ga_supervisor"); var (agu, agent) = await Person("ga_agent");
        var team = IdOf(await Ok(await admin.Post("/api/teams", new { name = $"Team {Uniq()}" })));
        await Ok(await admin.SendAsync(HttpMethod.Put, $"/api/teams/{team}/members", new { members = new[] { new { userId = agu.Id, isLead = false } } }));
        var cat = IdOf(await Ok(await admin.Post("/api/catalog/categories", new { name = $"Cat {Uniq()}" })));
        var svc = IdOf(await Ok(await admin.Post("/api/catalog/services", new { name = $"Svc {Uniq()}", categoryId = cat, owningTeamId = team, defaultPriority = "medium" })));
        await Ok(await admin.SendAsync(HttpMethod.Put, $"/api/catalog/services/{svc}/form", new { fields = new object[] { new { key = "what", type = "text", label = "What?", required = true } } }));
        await Ok(await admin.Post($"/api/catalog/services/{svc}/publish"));
        return new World(admin, au, requester, ru, a1, a1b, a2, a2b, sup, supu, agent, agu, svc, team);
    }

    private static object Step(string name, string rule, Guid[]? users = null, Guid[]? roles = null) => new { name, rule, userIds = users ?? [], roleIds = roles ?? [] };
    private static Task<HttpResponseMessage> SetPolicy(TestBrowser admin, Guid svc, object[] steps, Guid? fallback = null, int? version = null)
        => admin.SendAsync(HttpMethod.Put, $"/api/approvals/policies/{svc}", new { steps, fallbackUserId = fallback, version });
    private static Task<HttpResponseMessage> Raise(TestBrowser b, Guid svc, string subject = "Please approve my request") => b.Post($"/api/portal/services/{svc}/requests", new { subject, answers = new { what = "paper" } });

    private static async Task<Guid> ApprovalOf(TestBrowser approver, Guid ticket)
        => IdOf((await List(await approver.Get("/api/approvals/mine"))).First(a => a.GetProperty("ticketId").GetGuid() == ticket && Str(a, "status") == "pending"));
    private static Task<HttpResponseMessage> Decide(TestBrowser approver, Guid approval, bool approve, string? comment = null) => approver.Post($"/api/approvals/{approval}/decide", new { approve, comment });
    private async Task<JsonElement> Ticket(TestBrowser b, Guid id) => await Ok(await b.Get($"/api/tickets/{id}"));
    private static string[] Statuses(JsonElement ticket) => [.. ticket.GetProperty("approvals").EnumerateArray().Select(a => $"{a.GetProperty("stepNo").GetInt32()}:{Str(a, "status")}")];

    private static async Task<List<JsonElement>> Notes(TestBrowser b, bool unread = false) => (await Ok(await b.Get($"/api/notifications?unread={unread.ToString().ToLowerInvariant()}&pageSize=50"))).GetProperty("data").EnumerateArray().ToList();
    private static bool Has(IEnumerable<JsonElement> notes, string kind, Guid? ticket = null) => notes.Any(n => Str(n, "kind") == kind && (ticket is null || n.GetProperty("ticketId").GetGuid() == ticket));

    // ============================================================ policy administration
    [Fact]
    public async Task Approval_policies_need_their_permissions_and_are_validated()
    {
        var w = await Build(); var (_, manager) = await Person("ga_manager"); var (_, plain) = await Person("user");
        Assert.Equal(HttpStatusCode.Forbidden, (await plain.Get("/api/approvals/policies")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await w.Supervisor.Get("/api/approvals/policies")).StatusCode);                                                    // supervisors can read
        Assert.Equal(HttpStatusCode.Forbidden, (await SetPolicy(w.Supervisor, w.ServiceId, [Step("Boss", "any", [w.A1.Id])])).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SetPolicy(TestBrowser.Create(fx.Factory), w.ServiceId, [Step("Boss", "any", [w.A1.Id])])).StatusCode);

        var before = (await List(await w.Supervisor.Get("/api/approvals/policies"))).Single(p => p.GetProperty("serviceId").GetGuid() == w.ServiceId);
        Assert.False(before.GetProperty("hasPolicy").GetBoolean());
        Assert.Equal(Invalid, (await SetPolicy(manager, w.ServiceId, [])).StatusCode);
        Assert.Equal(Invalid, (await SetPolicy(manager, w.ServiceId, [Step(" ", "any", [w.A1.Id])])).StatusCode);
        Assert.Equal(Invalid, (await SetPolicy(manager, w.ServiceId, [Step("Boss", "most", [w.A1.Id])])).StatusCode);
        Assert.Equal(Invalid, (await SetPolicy(manager, w.ServiceId, [Step("Nobody", "any")])).StatusCode);
        Assert.Equal(Invalid, (await SetPolicy(manager, w.ServiceId, [Step("Ghost", "any", [Guid.NewGuid()])])).StatusCode);                              // unknown person
        Assert.Equal(Invalid, (await SetPolicy(manager, w.ServiceId, [Step("Ghost role", "any", roles: [Guid.NewGuid()])])).StatusCode);
        Assert.Equal(Invalid, (await SetPolicy(manager, w.ServiceId, [Step("Boss", "any", [w.A1.Id])], fallback: Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SetPolicy(manager, Guid.NewGuid(), [Step("Boss", "any", [w.A1.Id])])).StatusCode);

        var made = await Ok(await SetPolicy(manager, w.ServiceId, [Step("Line manager", "any", [w.A1.Id, w.A2.Id]), Step("Finance", "all", [w.A1.Id])], w.A2.Id));      // a manager manages them
        Assert.True(made.GetProperty("hasPolicy").GetBoolean()); Assert.Equal(2, made.GetProperty("steps").GetArrayLength()); Assert.Equal(w.A2.Id, made.GetProperty("fallbackUserId").GetGuid());
        var again = await Ok(await SetPolicy(manager, w.ServiceId, [Step("Line manager", "any", [w.A1.Id, w.A2.Id]), Step("Finance", "all", [w.A1.Id])], w.A2.Id, Ver(made)));
        Assert.Equal(Ver(made), Ver(again));                                                                                                                 // nothing changed: no new version
        var changed = await Ok(await SetPolicy(manager, w.ServiceId, [Step("Line manager", "all", [w.A1.Id, w.A2.Id])], null, Ver(made)));
        Assert.Equal(Ver(made) + 1, Ver(changed));
        Assert.Equal(HttpStatusCode.Conflict, (await SetPolicy(manager, w.ServiceId, [Step("Boss", "any", [w.A1.Id])], null, Ver(made))).StatusCode);          // stale copy
        Assert.Contains("approval.policy.set", await _seed.Db(db => db.AuditEvents.Where(a => a.TargetId == w.ServiceId.ToString()).Select(a => a.Action).ToListAsync()));

        Assert.False((await Ok(await manager.Post($"/api/approvals/policies/{w.ServiceId}/remove"))).GetProperty("hasPolicy").GetBoolean());
        Assert.Equal(HttpStatusCode.NotFound, (await manager.Post($"/api/approvals/policies/{w.ServiceId}/remove")).StatusCode);
    }

    // ============================================================ the flow
    [Fact]
    public async Task A_request_waits_for_approval_then_goes_to_the_team_when_one_of_two_approvers_says_yes()
    {
        var w = await Build();
        await Ok(await SetPolicy(w.Admin, w.ServiceId, [Step("Line manager", "any", [w.A1.Id, w.A2.Id])]));
        var t = await Ok(await Raise(w.Requester, w.ServiceId)); var id = IdOf(t);

        Assert.Equal("pendingapproval", Str(t, "status")); Assert.Equal(JsonValueKind.Null, t.GetProperty("assigneeId").ValueKind);
        Assert.Equal(["cancel"], t.GetProperty("allowed").EnumerateArray().Select(a => a.GetString()!).ToArray());                                             // the requester can only withdraw
        Assert.Equal(["1:pending", "1:pending"], Statuses(t));

        foreach (var (user, b) in new[] { (w.A1, w.A1B), (w.A2, w.A2B) })
        {
            Assert.True(Has(await Notes(b, unread: true), "approval.needed", id));                                                                               // in the app…
            Assert.Contains($"{Str(t, "number")}", (await _seed.Mail.WaitFor(user.Email, "Approval needed")).Subject);                                          // …and by e-mail, always
            Assert.Equal(HttpStatusCode.OK, (await b.Get($"/api/tickets/{id}")).StatusCode);                                                                      // an approver can open what they decide
        }
        Assert.Equal(1, (await Ok(await w.A1B.Get("/api/approvals/mine/count"))).GetProperty("pending").GetInt32());
        Assert.Equal("paused", Str((await Ticket(w.Supervisor, id)).GetProperty("sla"), "state"));                                                                  // its service clock is stopped while it waits

        Assert.Equal(HttpStatusCode.NoContent, (await Decide(w.A1B, await ApprovalOf(w.A1B, id), true, "Fine by me")).StatusCode);
        var after = await Ticket(w.Requester, id);
        Assert.Equal("submitted", Str(after, "status")); Assert.Equal(w.TeamId, after.GetProperty("teamId").GetGuid());                                          // released to the service's team
        Assert.Equal(["1:approved", "1:skipped"], Statuses(after).Order().ToArray());                                                                           // the other approver is no longer needed
        Assert.Contains(after.GetProperty("timeline").EnumerateArray(), e => Str(e, "kind") == "approval" && Str(e, "body").StartsWith("Approved: Fine by me"));
        Assert.Equal(0, (await Ok(await w.A2B.Get("/api/approvals/mine/count"))).GetProperty("pending").GetInt32());
        Assert.Equal(HttpStatusCode.Conflict, (await Decide(w.A2B, RowOf(after, w.A2.Id), true)).StatusCode);                                                    // no longer needed

        var notes = await Notes(w.Requester); Assert.True(Has(notes, "approval.approved", id));
        Assert.Contains("approved", (await _seed.Mail.WaitFor(w.RequesterUser.Email, "was approved")).Subject);
        Assert.Contains("approval.approve", await _seed.Db(db => db.AuditEvents.Where(a => a.TargetId == id.ToString()).Select(a => a.Action).ToListAsync()));
    }

    [Fact]
    public async Task Steps_run_in_order_and_an_all_step_needs_everyone()
    {
        var w = await Build(); var (third, thirdB) = await Person("user");
        await Ok(await SetPolicy(w.Admin, w.ServiceId, [Step("Line manager", "any", [w.A1.Id]), Step("Finance", "all", [w.A2.Id, third.Id])]));
        var id = IdOf(await Ok(await Raise(w.Requester, w.ServiceId)));

        Assert.False(Has(await Notes(w.A2B), "approval.needed", id));                                                                                            // step 2 is not told yet
        Assert.Equal(0, (await Ok(await w.A2B.Get("/api/approvals/mine/count"))).GetProperty("pending").GetInt32());
        Assert.Equal(HttpStatusCode.NoContent, (await Decide(w.A1B, await ApprovalOf(w.A1B, id), true)).StatusCode);
        var mid = await Ticket(w.Requester, id);
        Assert.Equal("pendingapproval", Str(mid, "status")); Assert.Equal(["1:approved", "2:pending", "2:pending"], Statuses(mid));
        Assert.True(Has(await Notes(w.A2B), "approval.needed", id)); Assert.True(Has(await Notes(thirdB), "approval.needed", id));
        await _seed.Mail.WaitFor(third.Email, "Approval needed");

        Assert.Equal(HttpStatusCode.NoContent, (await Decide(w.A2B, await ApprovalOf(w.A2B, id), true)).StatusCode);
        Assert.Equal("pendingapproval", Str(await Ticket(w.Requester, id), "status"));                                                                            // all of them are needed
        Assert.Equal(HttpStatusCode.NoContent, (await Decide(thirdB, await ApprovalOf(thirdB, id), true)).StatusCode);
        var done = await Ticket(w.Requester, id);
        Assert.Equal("submitted", Str(done, "status")); Assert.Equal(["1:approved", "2:approved", "2:approved"], Statuses(done));
    }

    [Fact]
    public async Task A_rejection_needs_a_reason_ends_the_request_and_tells_the_requester()
    {
        var w = await Build(); var (third, thirdB) = await Person("user");
        await Ok(await SetPolicy(w.Admin, w.ServiceId, [Step("Panel", "all", [w.A1.Id, w.A2.Id, third.Id])]));
        var id = IdOf(await Ok(await Raise(w.Requester, w.ServiceId)));
        var mine = await ApprovalOf(w.A1B, id);

        var noReason = await Decide(w.A1B, mine, false);
        Assert.Equal(Invalid, noReason.StatusCode); Assert.True((await noReason.Body()).GetProperty("error").GetProperty("fields").TryGetProperty("comment", out _));
        Assert.Equal(Invalid, (await Decide(w.A1B, mine, false, "no")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Decide(w.A1B, mine, false, "Not in the budget this quarter")).StatusCode);

        var t = await Ticket(w.Requester, id);
        Assert.Equal("rejected", Str(t, "status")); Assert.Equal("Not in the budget this quarter", Str(t, "cancelReason")); Assert.Empty(t.GetProperty("allowed").EnumerateArray());
        Assert.Equal(["1:cancelled", "1:cancelled", "1:rejected"], Statuses(t).Order().ToArray());                                                              // the others are released from deciding
        Assert.Equal(HttpStatusCode.Conflict, (await Decide(w.A2B, RowOf(t, w.A2.Id), true)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await w.Requester.Post($"/api/tickets/{id}/comments", new { body = "hello?" })).StatusCode);                     // a rejected request takes no comments
        Assert.True(Has(await Notes(w.Requester), "approval.rejected", id));
        var mail = await _seed.Mail.WaitFor(w.RequesterUser.Email, "was rejected"); Assert.Contains("Not in the budget", mail.Text);
        Assert.Equal("none", Str((await Ticket(w.Supervisor, id)).GetProperty("sla"), "state"));                                                                  // no clock on a closed matter
    }

    /// <summary>The approval row of one approver, read from the ticket (the "mine" list only shows pending and decided rows).</summary>
    private static Guid RowOf(JsonElement ticket, Guid approver) => IdOf(ticket.GetProperty("approvals").EnumerateArray().First(a => a.GetProperty("approverId").GetGuid() == approver));

    [Fact]
    public async Task Nobody_approves_their_own_request_and_a_fallback_steps_in_when_that_leaves_nobody()
    {
        var w = await Build();
        // the requester is named as the only approver: excluded, so the request cannot be made without a fallback
        await Ok(await SetPolicy(w.Admin, w.ServiceId, [Step("Self", "any", [w.RequesterUser.Id])]));
        var refused = await Raise(w.Requester, w.ServiceId, "I approve myself");
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal(0, await _seed.Db(db => db.Tickets.CountAsync(x => x.RequesterId == w.RequesterUser.Id)));                                                 // nothing was created
        // a fallback that is the requester doesn't help either
        await Ok(await SetPolicy(w.Admin, w.ServiceId, [Step("Self", "any", [w.RequesterUser.Id])], fallback: w.RequesterUser.Id));
        Assert.Equal(HttpStatusCode.Conflict, (await Raise(w.Requester, w.ServiceId)).StatusCode);
        // a real fallback decides
        await Ok(await SetPolicy(w.Admin, w.ServiceId, [Step("Self", "any", [w.RequesterUser.Id])], fallback: w.A1.Id));
        var t = await Ok(await Raise(w.Requester, w.ServiceId));
        Assert.Equal(["1:pending"], Statuses(t)); Assert.Equal(w.A1.Id, t.GetProperty("approvals")[0].GetProperty("approverId").GetGuid());
        Assert.Equal(HttpStatusCode.NotFound, (await w.Requester.Post($"/api/approvals/{IdOf(t.GetProperty("approvals")[0])}/decide", new { approve = true })).StatusCode);   // it isn't theirs to decide

        // someone filing on the requester's behalf can't approve either
        await Ok(await SetPolicy(w.Admin, w.ServiceId, [Step("Staff", "any", [w.AgentUser.Id, w.A2.Id])]));
        var onBehalf = await Ok(await w.Agent.Post($"/api/portal/services/{w.ServiceId}/requests", new { subject = "For a colleague", answers = new { what = "x" }, onBehalfOfUserId = w.RequesterUser.Id }));
        Assert.Equal([w.A2.Id], onBehalf.GetProperty("approvals").EnumerateArray().Select(a => a.GetProperty("approverId").GetGuid()).ToArray());              // the creator is left out
    }

    [Fact]
    public async Task Roles_can_approve_and_the_requesters_own_role_membership_does_not_let_them_approve()
    {
        var w = await Build(); var role = await _seed.RoleAsync("approver_" + Uniq());
        var (m1, m1b) = await Person(role.Key); var (m2, _) = await Person(role.Key); var (rm, rmb) = await Person(role.Key);
        await Ok(await SetPolicy(w.Admin, w.ServiceId, [Step("Role", "any", roles: [role.Id])]));
        var t = await Ok(await Raise(rmb, w.ServiceId)); var id = IdOf(t);                                                                                          // the requester holds the role too
        var approvers = t.GetProperty("approvals").EnumerateArray().Select(a => a.GetProperty("approverId").GetGuid()).ToList();
        Assert.DoesNotContain(rm.Id, approvers); Assert.Contains(m1.Id, approvers); Assert.Contains(m2.Id, approvers);
        Assert.Equal(HttpStatusCode.NoContent, (await Decide(m1b, await ApprovalOf(m1b, id), true)).StatusCode);
        Assert.Equal("submitted", Str(await Ticket(rmb, id), "status"));
    }

    [Fact]
    public async Task Withdrawing_a_waiting_request_stops_the_approvals_and_voids_the_links()
    {
        var w = await Build();
        await Ok(await SetPolicy(w.Admin, w.ServiceId, [Step("Boss", "any", [w.A1.Id])]));
        var id = IdOf(await Ok(await Raise(w.Requester, w.ServiceId)));
        var token = InMemoryEmailSender.TokenIn(await _seed.Mail.WaitFor(w.A1.Email, "Approval needed"));
        var cancelled = await Ok(await w.Requester.Post($"/api/tickets/{id}/transition", new { action = "cancel", reason = "No longer needed" }));
        Assert.Equal("cancelled", Str(cancelled, "status")); Assert.Equal(["1:cancelled"], Statuses(cancelled));
        Assert.Equal(HttpStatusCode.Gone, (await TestBrowser.Create(fx.Factory).Get($"/api/approvals/link/{token}")).StatusCode);                                  // the e-mail link no longer works
        Assert.Equal(HttpStatusCode.Conflict, (await Decide(w.A1B, RowOf(cancelled, w.A1.Id), true)).StatusCode);
        Assert.Equal(0, (await Ok(await w.A1B.Get("/api/approvals/mine/count"))).GetProperty("pending").GetInt32());
    }

    // ============================================================ the one-time e-mail link
    [Fact]
    public async Task The_e_mail_link_decides_without_signing_in_works_once_and_only_after_the_person_chooses()
    {
        var w = await Build();
        await Ok(await SetPolicy(w.Admin, w.ServiceId, [Step("Boss", "any", [w.A1.Id])]));
        var t = await Ok(await Raise(w.Requester, w.ServiceId)); var id = IdOf(t);
        var mail = await _seed.Mail.WaitFor(w.A1.Email, "Approval needed"); var token = InMemoryEmailSender.TokenIn(mail);
        Assert.Contains("/approve?token=", mail.Text);
        var anon = TestBrowser.Create(fx.Factory);

        var info = await Ok(await anon.Get($"/api/approvals/link/{token}"));                                                                                       // looking decides nothing
        Assert.Equal(Str(t, "number"), Str(info, "ticketNumber")); Assert.Equal("Boss", Str(info, "stepName")); Assert.Equal(w.RequesterUser.Name, Str(info, "requesterName"));
        Assert.Equal("pendingapproval", Str(await Ticket(w.Requester, id), "status"));
        Assert.Equal(HttpStatusCode.Gone, (await anon.Get("/api/approvals/link/not-a-real-token")).StatusCode);

        Assert.Equal(Invalid, (await anon.Post($"/api/approvals/link/{token}", new { approve = false }, csrf: false)).StatusCode);                                  // a rejection still needs its reason
        var done = await Ok(await anon.Post($"/api/approvals/link/{token}", new { approve = true, comment = "From my phone" }, csrf: false));
        Assert.Equal("approved", Str(done, "decision"));
        var after = await Ticket(w.Requester, id);
        Assert.Equal("submitted", Str(after, "status"));
        var trail = after.GetProperty("approvals")[0]; Assert.Equal("email", Str(trail, "decidedVia")); Assert.Equal("From my phone", Str(trail, "comment"));      // the trail records how it was decided

        Assert.Equal(HttpStatusCode.Gone, (await anon.Post($"/api/approvals/link/{token}", new { approve = true }, csrf: false)).StatusCode);                      // single use
        Assert.Equal(HttpStatusCode.Gone, (await anon.Get($"/api/approvals/link/{token}")).StatusCode);
        Assert.Contains("approval.approve", await _seed.Db(db => db.AuditEvents.Where(a => a.TargetId == id.ToString() && a.ActorId == w.A1.Id).Select(a => a.Action).ToListAsync()));
        var stored = await _seed.Db(db => db.TicketApprovals.IgnoreQueryFilters().Where(a => a.TicketId == id).Select(a => new { a.TokenHash, a.TokenExpiresAt }).ToListAsync());
        Assert.All(stored, s => { Assert.Null(s.TokenHash); Assert.Null(s.TokenExpiresAt); });                                                                      // nothing usable is left behind
    }

    [Fact]
    public async Task An_expired_link_and_a_rejection_by_link_are_handled()
    {
        var w = await Build(); var anon = TestBrowser.Create(fx.Factory);
        await Ok(await SetPolicy(w.Admin, w.ServiceId, [Step("Boss", "any", [w.A1.Id])]));
        var id = IdOf(await Ok(await Raise(w.Requester, w.ServiceId)));
        var token = InMemoryEmailSender.TokenIn(await _seed.Mail.WaitFor(w.A1.Email, "Approval needed"));
        await Sql($"UPDATE ticket_approvals SET token_expires_at = {Ago(5)} WHERE ticket_id = {id}");
        Assert.Equal(HttpStatusCode.Gone, (await anon.Get($"/api/approvals/link/{token}")).StatusCode);
        Assert.Equal(HttpStatusCode.Gone, (await anon.Post($"/api/approvals/link/{token}", new { approve = true }, csrf: false)).StatusCode);
        Assert.Equal("pendingapproval", Str(await Ticket(w.Requester, id), "status"));                                                                             // an expired link decides nothing

        // the person can still decide in the app, and a link for a later step only exists once that step starts
        Assert.Equal(HttpStatusCode.NoContent, (await Decide(w.A1B, await ApprovalOf(w.A1B, id), false, "Not this time")).StatusCode);
        Assert.Equal("rejected", Str(await Ticket(w.Requester, id), "status"));
    }

    // ============================================================ the clock waits with the request
    [Fact]
    public async Task The_service_clock_is_stopped_while_waiting_and_restarts_on_approval_with_the_wait_not_counted()
    {
        var w = await Build();
        var cal = IdOf(await Ok(await w.Admin.Post("/api/calendars", new { name = $"24/7 {Uniq()}", timeZone = "Asia/Jakarta", isAlwaysOpen = true, periods = Array.Empty<object>(), holidays = Array.Empty<object>() })));
        var pol = IdOf(await Ok(await w.Admin.Post("/api/sla/policies", new { name = $"Waits {Uniq()}", serviceId = w.ServiceId, calendarId = cal, firstResponseMinutes = 30, resolutionMinutes = 240 })));
        var ids = (await List(await w.Admin.Get("/api/sla/policies"))).Select(IdOf).ToList(); ids.Remove(pol); ids.Insert(0, pol);
        await Ok(await w.Admin.Post("/api/sla/policies/reorder", new { ids }));
        await Ok(await SetPolicy(w.Admin, w.ServiceId, [Step("Boss", "any", [w.A1.Id])]));

        var id = IdOf(await Ok(await Raise(w.Requester, w.ServiceId)));
        var waiting = (await Ticket(w.Supervisor, id)).GetProperty("sla"); Assert.Equal("paused", Str(waiting, "state"));
        await Sql($"UPDATE tickets SET sla_paused_at = {Ago(120)} WHERE id = {id}");                                                                                  // pretend it waited two hours
        Assert.Equal(HttpStatusCode.NoContent, (await Decide(w.A1B, await ApprovalOf(w.A1B, id), true)).StatusCode);
        var sla = (await Ticket(w.Supervisor, id)).GetProperty("sla");
        Assert.Equal("ontrack", Str(sla, "state")); Assert.Equal(JsonValueKind.Null, sla.GetProperty("pausedAt").ValueKind);
        var created = (await Ticket(w.Supervisor, id)).GetProperty("createdAt").GetDateTimeOffset();
        Assert.InRange((sla.GetProperty("resolutionDueAt").GetDateTimeOffset() - created).TotalMinutes, 240 + 119, 240 + 121);                                      // 240 minutes of work plus the 120 spent waiting
    }

    // ============================================================ notifications
    [Fact]
    public async Task The_bell_lists_counts_and_marks_only_your_own_notifications()
    {
        var w = await Build(); var (_, other) = await Person("user");
        var id = IdOf(await Ok(await w.Requester.Post($"/api/portal/services/{w.ServiceId}/requests", new { subject = "Bell test", answers = new { what = "x" } })));
        await Ok(await w.Supervisor.Post($"/api/tickets/{id}/assign", new { assigneeId = w.AgentUser.Id, teamId = w.TeamId }));                                      // the agent is told

        var unread = (await Ok(await w.Agent.Get("/api/notifications/unread-count"))).GetProperty("unread").GetInt32();
        Assert.True(unread >= 1);
        var mine = await Notes(w.Agent, unread: true); var note = mine.First(n => Str(n, "kind") == "ticket.assigned" && n.GetProperty("ticketId").GetGuid() == id);
        Assert.False(note.GetProperty("read").GetBoolean()); Assert.Equal($"/tickets/{id}", Str(note, "link"));
        Assert.Equal(HttpStatusCode.NotFound, (await other.Post($"/api/notifications/{IdOf(note)}/read")).StatusCode);                                            // someone else's id looks like it doesn't exist
        Assert.Equal(HttpStatusCode.NoContent, (await w.Agent.Post($"/api/notifications/{IdOf(note)}/read")).StatusCode);
        Assert.Equal(unread - 1, (await Ok(await w.Agent.Get("/api/notifications/unread-count"))).GetProperty("unread").GetInt32());
        Assert.False(Has(await Notes(w.Agent, unread: true), "ticket.assigned", id));
        Assert.Equal(HttpStatusCode.OK, (await w.Agent.Post("/api/notifications/read-all")).StatusCode);
        Assert.Equal(0, (await Ok(await w.Agent.Get("/api/notifications/unread-count"))).GetProperty("unread").GetInt32());
        Assert.Equal(HttpStatusCode.Unauthorized, (await TestBrowser.Create(fx.Factory).Get("/api/notifications")).StatusCode);
        Assert.Contains("assigned to you", (await _seed.Mail.WaitFor(w.AgentUser.Email, "was assigned to you")).Subject);
    }

    [Fact]
    public async Task Replies_go_to_the_other_side_notes_to_the_assignee_and_nobody_is_told_about_their_own_action()
    {
        var w = await Build();
        var id = IdOf(await Ok(await Raise(w.Requester, w.ServiceId)));
        await Ok(await w.Supervisor.Post($"/api/tickets/{id}/assign", new { assigneeId = w.AgentUser.Id, teamId = w.TeamId }));
        await Ok(await w.Agent.Post($"/api/tickets/{id}/comments", new { body = "Can you send a photo?" }));
        Assert.True(Has(await Notes(w.Requester), "ticket.reply", id)); Assert.Contains("New reply", (await _seed.Mail.WaitFor(w.RequesterUser.Email, "New reply")).Subject);
        Assert.False(Has(await Notes(w.Agent), "ticket.reply", id));                                                                                              // not told about their own reply

        await Ok(await w.Requester.Post($"/api/tickets/{id}/comments", new { body = "Here it is." }));
        Assert.True(Has(await Notes(w.Agent), "ticket.reply", id));                                                                                               // the assignee hears from the requester

        await Ok(await w.Supervisor.Post($"/api/tickets/{id}/comments", new { body = "Watch the budget on this one.", @internal = true }));
        Assert.True(Has(await Notes(w.Agent), "ticket.note", id)); Assert.False(Has(await Notes(w.Requester), "ticket.note", id));                                // an internal note never reaches the requester

        await Ok(await w.Agent.Post($"/api/tickets/{id}/transition", new { action = "start" }));
        Assert.True(Has(await Notes(w.Requester), "ticket.started", id));
        await Ok(await w.Agent.Post($"/api/tickets/{id}/transition", new { action = "resolve", resolutionCode = "fixed", resolutionNote = "Replaced the roller" }));
        Assert.True(Has(await Notes(w.Requester), "ticket.resolved", id)); await _seed.Mail.WaitFor(w.RequesterUser.Email, "was resolved");
        await Ok(await w.Requester.Post($"/api/tickets/{id}/transition", new { action = "reopen", reason = "Still jams" }));
        Assert.True(Has(await Notes(w.Agent), "ticket.reopened", id));                                                                                            // a reopened ticket goes back to its assignee
        Assert.False(Has(await Notes(w.Requester), "ticket.reopened", id));
    }

    [Fact]
    public async Task Turning_e_mail_off_keeps_the_in_app_notice_but_approval_requests_are_always_mailed()
    {
        var w = await Build();
        await _seed.Db(async db => await db.Profiles.Where(p => p.UserId == w.RequesterUser.Id || p.UserId == w.A1.Id).ExecuteUpdateAsync(s => s.SetProperty(p => p.NotifyEmail, false)));
        await Ok(await SetPolicy(w.Admin, w.ServiceId, [Step("Boss", "any", [w.A1.Id])]));
        var id = IdOf(await Ok(await Raise(w.Requester, w.ServiceId)));
        await _seed.Mail.WaitFor(w.A1.Email, "Approval needed");                                                                                                      // actionable: always sent
        Assert.Equal(HttpStatusCode.NoContent, (await Decide(w.A1B, await ApprovalOf(w.A1B, id), true)).StatusCode);
        Assert.True(Has(await Notes(w.Requester), "approval.approved", id));                                                                                         // still in the app
        await _seed.Mail.AssertNone(w.RequesterUser.Email);                                                                                                           // but no e-mail: they asked for none
    }

    [Fact]
    public async Task An_SLA_breach_is_announced_to_the_assignee_and_the_team_lead()
    {
        var w = await Build();
        var cal = IdOf(await Ok(await w.Admin.Post("/api/calendars", new { name = $"24/7 {Uniq()}", timeZone = "Asia/Jakarta", isAlwaysOpen = true, periods = Array.Empty<object>(), holidays = Array.Empty<object>() })));
        var pol = IdOf(await Ok(await w.Admin.Post("/api/sla/policies", new { name = $"Tiny {Uniq()}", serviceId = w.ServiceId, calendarId = cal, firstResponseMinutes = 10, resolutionMinutes = 60 })));
        var ids = (await List(await w.Admin.Get("/api/sla/policies"))).Select(IdOf).ToList(); ids.Remove(pol); ids.Insert(0, pol);
        await Ok(await w.Admin.Post("/api/sla/policies/reorder", new { ids }));
        await Ok(await w.Admin.SendAsync(HttpMethod.Put, $"/api/teams/{w.TeamId}/members", new { members = new[] { new { userId = w.AgentUser.Id, isLead = false }, new { userId = w.SupervisorUser.Id, isLead = true } } })); // the team lead is told
        var id = IdOf(await Ok(await Raise(w.Requester, w.ServiceId)));
        await Ok(await w.Supervisor.Post($"/api/tickets/{id}/assign", new { assigneeId = w.AgentUser.Id, teamId = w.TeamId }));
        await Ok(await w.Agent.Post($"/api/tickets/{id}/transition", new { action = "start" }));
        await Sql($"UPDATE tickets SET sla_start_at = {Ago(300)}, created_at = {Ago(300)} WHERE id = {id}");
        using (var scope = fx.Factory.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<SlaService>().RunDueAsync();

        Assert.True(Has(await Notes(w.Agent), "sla.breached", id)); Assert.True(Has(await Notes(w.Supervisor), "sla.breached", id));
        Assert.Contains("SLA breached", (await _seed.Mail.WaitFor(w.AgentUser.Email, "SLA breached")).Subject);
        Assert.False(Has(await Notes(w.Requester), "sla.breached", id));                                                                                             // internal matter
    }

    // ============================================================ tenants
    [Fact]
    public async Task One_organisations_approvals_and_notifications_stay_inside_it()
    {
        var w = await Build();
        await Ok(await SetPolicy(w.Admin, w.ServiceId, [Step("Boss", "any", [w.A1.Id])]));
        var id = IdOf(await Ok(await Raise(w.Requester, w.ServiceId)));
        var approval = await ApprovalOf(w.A1B, id);
        var token = InMemoryEmailSender.TokenIn(await _seed.Mail.WaitFor(w.A1.Email, "Approval needed"));

        var (_, otherAdmin) = await NewTenantAdmin();
        Assert.Equal(HttpStatusCode.NotFound, (await Decide(otherAdmin, approval, true)).StatusCode);
        Assert.Empty(await List(await otherAdmin.Get("/api/approvals/mine")));
        Assert.DoesNotContain(await List(await otherAdmin.Get("/api/approvals/policies")), p => p.GetProperty("serviceId").GetGuid() == w.ServiceId);
        Assert.Equal(HttpStatusCode.NotFound, (await SetPolicy(otherAdmin, w.ServiceId, [Step("Boss", "any", [w.A1.Id])])).StatusCode);
        Assert.Equal(0, (await Notes(otherAdmin)).Count(n => n.GetProperty("ticketId").ValueKind == JsonValueKind.String && n.GetProperty("ticketId").GetGuid() == id));
        Assert.Equal(HttpStatusCode.OK, (await TestBrowser.Create(fx.Factory).Get($"/api/approvals/link/{token}")).StatusCode);                                    // the link itself is the credential, tied to its own approver
    }

    private async Task<(Guid TenantId, TestBrowser Admin)> NewTenantAdmin()
    {
        var pu = await _seed.UserAsync(role: "super_admin"); var p = TestBrowser.Create(fx.Factory); await p.LoginAsync(pu.Email, Seed.Password);
        await p.Post("/api/auth/reauth", new { password = Seed.Password });
        var slug = "n" + Uniq(); var email = $"{slug}.admin@test.local";
        var res = await p.Post("/api/tenants", new { name = $"Org {slug}", slug, adminEmail = email });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var id = (await res.Body()).GetProperty("id").GetGuid();
        var token = InMemoryEmailSender.TokenIn(await _seed.Mail.WaitFor(email, "invited"));
        const string pw = "Cobalt-Meadow-Harvest-58";
        Assert.Equal(HttpStatusCode.OK, (await TestBrowser.Create(fx.Factory).Post("/api/invitations/accept", new { token, name = "Org Admin", password = pw }, csrf: false)).StatusCode);
        var b = TestBrowser.Create(fx.Factory); Assert.Equal(HttpStatusCode.OK, (await b.LoginAsync(email, pw)).StatusCode);
        return (id, b);
    }
}
