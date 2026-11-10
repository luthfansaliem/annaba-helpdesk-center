using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Helpdesk;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Tests.Infra;
using Xunit;

namespace AnnabaDigital.Tests;

/// <summary>
/// Data retention (ADM-006): who may set it, that nothing is removed until it is switched on, what anonymising and deleting do, legal holds, files and notifications,
/// the preview, other organisations being left alone, and the personal data export. Each test has its own database: a run applies to a whole organisation.
/// </summary>
[Collection("db")]
public sealed class RetentionTests(PostgresFixture fx)
{
    private const string StrongPw = "Cobalt-Meadow-Harvest-58";
    private static string Uniq() => Guid.NewGuid().ToString("N")[..8];
    private const HttpStatusCode Invalid = (HttpStatusCode)422;
    private static async Task<JsonElement> Ok(HttpResponseMessage r) { Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync()); return r.StatusCode == HttpStatusCode.NoContent ? default : await r.Body(); }
    private static Guid IdOf(JsonElement e) => e.GetProperty("id").GetGuid();
    private static string Str(JsonElement e, string p) => e.GetProperty(p).GetString()!;
    private static byte[] Png() { var b = new byte[64]; new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(b, 0); Random.Shared.NextBytes(b.AsSpan(8)); return b; }

    private sealed record World(ApiFactory F, TestClock Clock, Seed Seed, TestBrowser Admin, TestBrowser Requester, User RequesterUser, TestBrowser Agent, TestBrowser Supervisor, Guid ServiceId, Guid TeamId);

    private async Task<World> Build()
    {
        var iso = await fx.NewIsolatedAsync(); var seed = new Seed(iso.Factory, iso.Clock);
        async Task<(User, TestBrowser)> Person(string role) { var u = await seed.UserAsync(role: role); var b = TestBrowser.Create(iso.Factory); await b.LoginAsync(u.Email, Seed.Password); return (u, b); }
        var (_, admin) = await Person("super_admin"); var (ru, requester) = await Person("user"); var (au, agent) = await Person("ga_agent"); var (_, sup) = await Person("ga_supervisor");
        await admin.Post("/api/auth/reauth", new { password = Seed.Password });
        var team = IdOf(await Ok(await admin.Post("/api/teams", new { name = $"Team {Uniq()}" })));
        await Ok(await admin.SendAsync(HttpMethod.Put, $"/api/teams/{team}/members", new { members = new[] { new { userId = au.Id, isLead = false } } }));
        var cat = IdOf(await Ok(await admin.Post("/api/catalog/categories", new { name = $"Cat {Uniq()}" })));
        var svc = IdOf(await Ok(await admin.Post("/api/catalog/services", new { name = $"Svc {Uniq()}", categoryId = cat, owningTeamId = team })));
        await Ok(await admin.SendAsync(HttpMethod.Put, $"/api/catalog/services/{svc}/form", new { fields = new object[] { new { key = "what", type = "text", label = "What?", required = true } } }));
        await Ok(await admin.Post($"/api/catalog/services/{svc}/publish"));
        return new World(iso.Factory, iso.Clock, seed, admin, requester, ru, agent, sup, svc, team);
    }

    private static async Task<Guid> Raise(World w, string subject = "Printer is broken", string description = "My badge number is 12345")
        => IdOf(await Ok(await w.Requester.Post($"/api/portal/services/{w.ServiceId}/requests", new { subject, description, answers = new { what = "secret answer" } })));
    private static async Task<JsonElement> Ticket(TestBrowser b, Guid id) => await Ok(await b.Get($"/api/tickets/{id}"));
    private static Task Sql(World w, FormattableString sql) => w.Seed.Db(async db => await db.Database.ExecuteSqlInterpolatedAsync(sql));
    /// <summary>Makes a ticket look as if it finished this many days ago.</summary>
    private static Task Finish(World w, Guid id, int daysAgo, string status = "Closed") { var at = w.Clock.UtcNow.AddDays(-daysAgo); return Sql(w, $"UPDATE tickets SET status = {status}, closed_at = {at}, updated_at = {at} WHERE id = {id}"); }
    private static Task<HttpResponseMessage> Save(World w, bool enabled, int? tickets, string action = "anonymise", int? files = null, int? notifications = null)
        => w.Admin.SendAsync(HttpMethod.Put, "/api/retention", new { enabled, finishedTicketDays = tickets, ticketAction = action, attachmentDays = files, notificationDays = notifications });
    private static async Task<JsonElement> Run(World w) => await Ok(await w.Admin.Post("/api/retention/run"));
    private static async Task<Guid> Attach(World w, Guid ticket, string name = "photo.png")
        => IdOf(await Ok(await w.Requester.UploadAsync($"/api/tickets/{ticket}/attachments", name, Png(), "image/png")));
    private static async Task<string> KeyOf(World w, Guid file) => await w.Seed.Db(db => db.Attachments.Where(a => a.Id == file).Select(a => a.StorageKey).SingleAsync());
    private static async Task<bool> Exists(World w, string key) { await using var s = await w.F.Services.GetRequiredService<IFileStorage>().OpenAsync(key); return s is not null; }

    /// <summary>A volume that refuses to delete anything (a locked file, a read-only mount).</summary>
    private sealed class RefusingStorage : IFileStorage
    {
        public Task SaveAsync(string key, Stream content, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Stream?> OpenAsync(string key, CancellationToken ct = default) => Task.FromResult<Stream?>(null);
        public Task DeleteAsync(string key, CancellationToken ct = default) => throw new IOException("The volume is read-only.");
        public async IAsyncEnumerable<StoredFile> ListAsync(string prefix, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default) { await Task.CompletedTask; yield break; }
    }

    // ============================================================ who, and off by default
    [Fact]
    public async Task Only_people_with_the_permission_manage_retention_and_it_starts_switched_off()
    {
        var w = await Build(); var id = await Raise(w);
        foreach (var b in new[] { w.Supervisor, w.Agent, w.Requester })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await b.Get("/api/retention")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await b.SendAsync(HttpMethod.Put, "/api/retention", new { enabled = false, finishedTicketDays = 90, ticketAction = "anonymise" })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await b.Post("/api/retention/preview", new { enabled = false, finishedTicketDays = 90, ticketAction = "anonymise" })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await b.Post("/api/retention/run")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await b.Get("/api/retention/holds")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await b.SendAsync(HttpMethod.Put, $"/api/tickets/{id}/legal-hold", new { reason = "dispute" })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await b.SendAsync(HttpMethod.Delete, $"/api/tickets/{id}/legal-hold")).StatusCode);
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await TestBrowser.Create(w.F).Get("/api/retention/notice")).StatusCode);

        var v = await Ok(await w.Admin.Get("/api/retention"));
        Assert.False(v.GetProperty("enabled").GetBoolean()); Assert.Equal("anonymise", Str(v, "ticketAction")); Assert.Equal(JsonValueKind.Null, v.GetProperty("finishedTicketDays").ValueKind);
        Assert.Equal(365, v.GetProperty("auditDays").GetInt32()); Assert.Equal(0, v.GetProperty("holds").GetInt32());
        var notice = await Ok(await w.Requester.Get("/api/retention/notice")); Assert.False(notice.GetProperty("enabled").GetBoolean());   // anyone signed in may read what is kept
        Assert.Equal(HttpStatusCode.Conflict, (await w.Admin.Post("/api/retention/run")).StatusCode);                                         // no policy: nothing to run
    }

    [Fact]
    public async Task A_policy_is_validated_needs_a_fresh_password_check_and_every_change_is_audited()
    {
        var w = await Build();
        async Task<JsonElement> Refused(HttpResponseMessage r) { Assert.Equal(Invalid, r.StatusCode); return (await r.Body()).GetProperty("error").GetProperty("fields"); }
        Assert.True((await Refused(await Save(w, true, null))).TryGetProperty("enabled", out _));                       // on, with nothing to apply
        Assert.True((await Refused(await Save(w, false, 10))).TryGetProperty("finishedTicketDays", out _));              // too short
        Assert.True((await Refused(await Save(w, false, 90, files: 3))).TryGetProperty("attachmentDays", out _));
        Assert.True((await Refused(await Save(w, false, 90, files: 120))).TryGetProperty("attachmentDays", out _));     // files can't outlive their ticket
        Assert.True((await Refused(await Save(w, false, null, notifications: 99999))).TryGetProperty("notificationDays", out _));
        Assert.True((await Refused(await Save(w, false, 90, "shred"))).TryGetProperty("ticketAction", out _));
        Assert.Equal(Invalid, (await w.Admin.Post("/api/retention/preview", new { enabled = false, finishedTicketDays = 5, ticketAction = "anonymise" })).StatusCode);

        var v = await Ok(await Save(w, true, 365, "delete", 90, 60));
        Assert.True(v.GetProperty("enabled").GetBoolean()); Assert.Equal(365, v.GetProperty("finishedTicketDays").GetInt32()); Assert.Equal("delete", Str(v, "ticketAction"));
        Assert.Equal(90, v.GetProperty("attachmentDays").GetInt32()); Assert.Equal(60, v.GetProperty("notificationDays").GetInt32());
        var notice = await Ok(await w.Requester.Get("/api/retention/notice")); Assert.True(notice.GetProperty("enabled").GetBoolean()); Assert.Equal(365, notice.GetProperty("finishedTicketDays").GetInt32());

        var audit = await w.Seed.Db(db => db.AuditEvents.Where(a => a.Action == "retention.policy.update").ToListAsync());
        Assert.Single(audit); Assert.Contains("delete after 365 days", audit[0].DiffJson);
        Assert.True(await w.Seed.Db(db => db.SecurityEvents.AnyAsync(e => e.Type == "retention.change")));

        // without a recent password check it is refused (a person who has not confirmed theirs)
        var other = await w.Seed.UserAsync(role: "super_admin"); var b = TestBrowser.Create(w.F); await b.LoginAsync(other.Email, Seed.Password);
        var denied = await b.SendAsync(HttpMethod.Put, "/api/retention", new { enabled = false, finishedTicketDays = 90, ticketAction = "anonymise" });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode); Assert.Equal("reauth_required", await denied.ErrorCode());
        Assert.Equal("reauth_required", await (await b.Post("/api/retention/run")).ErrorCode());
        Assert.Equal("reauth_required", await (await b.SendAsync(HttpMethod.Put, $"/api/tickets/{Guid.NewGuid()}/legal-hold", new { reason = "dispute" })).ErrorCode());
    }

    [Fact]
    public async Task Nothing_is_removed_while_the_policy_is_off_or_for_tickets_that_are_not_finished_or_not_old_enough()
    {
        var w = await Build();
        var old = await Raise(w); var recent = await Raise(w); var open = await Raise(w); var cancelled = await Raise(w);
        await Finish(w, old, 400); await Finish(w, recent, 10); await Finish(w, cancelled, 400, "Cancelled");
        await Sql(w, $"UPDATE tickets SET updated_at = {w.Clock.UtcNow.AddDays(-400)} WHERE id = {open}");               // old, but still open

        Assert.Equal(0, (await w.F.Services.CreateScope().ServiceProvider.GetRequiredService<RetentionService>().RunDueAsync()).Tickets);   // no policy at all
        await Ok(await Save(w, false, 90));                                                                                     // a policy that is saved but switched off
        Assert.Equal(0, (await w.F.Services.CreateScope().ServiceProvider.GetRequiredService<RetentionService>().RunDueAsync()).Tickets);
        Assert.Equal(HttpStatusCode.Conflict, (await w.Admin.Post("/api/retention/run")).StatusCode);
        Assert.Equal("Printer is broken", Str(await Ticket(w.Admin, old), "subject"));

        await Ok(await Save(w, true, 90));
        var r = await Run(w);
        Assert.Equal(2, r.GetProperty("tickets").GetInt32());                                                                   // the old closed one and the old cancelled one, nothing else
        Assert.Equal(RetentionRules.RemovedSubject, Str(await Ticket(w.Admin, old), "subject"));
        Assert.Equal(RetentionRules.RemovedSubject, Str(await Ticket(w.Admin, cancelled), "subject"));
        Assert.Equal("Printer is broken", Str(await Ticket(w.Admin, recent), "subject"));                                       // finished 10 days ago: too young
        var o = await Ticket(w.Admin, open); Assert.Equal("Printer is broken", Str(o, "subject")); Assert.Equal(JsonValueKind.Null, o.GetProperty("anonymisedAt").ValueKind);   // open tickets are never aged
    }

    // ============================================================ anonymise and delete
    [Fact]
    public async Task Anonymising_removes_what_people_wrote_and_files_and_notifications_but_keeps_the_ticket_as_a_statistic()
    {
        var w = await Build(); var id = await Raise(w);
        await Ok(await w.Requester.Post($"/api/tickets/{id}/comments", new { body = "Call me on 0812-secret", @internal = false }));
        var file = await Attach(w, id); var key = await KeyOf(w, file);
        Assert.True(await Exists(w, key));
        await w.Seed.Db(async db => { db.Notifications.Add(new Notification { UserId = w.RequesterUser.Id, Kind = "ticket.reply", Title = "New reply on GA-1", TicketId = id, CreatedAt = w.Clock.UtcNow }); return await db.SaveChangesAsync(); });
        var before = await Ticket(w.Admin, id);
        await Finish(w, id, 200);

        await Ok(await Save(w, true, 180, "anonymise"));
        var r = await Run(w);
        Assert.Equal(1, r.GetProperty("tickets").GetInt32()); Assert.Equal(1, r.GetProperty("files").GetInt32());

        foreach (var b in new[] { w.Admin, w.Requester })
        {
            var t = await Ticket(b, id);
            Assert.Equal(RetentionRules.RemovedSubject, Str(t, "subject")); Assert.Equal(JsonValueKind.Null, t.GetProperty("description").ValueKind);
            Assert.Empty(t.GetProperty("answers").EnumerateArray()); Assert.Empty(t.GetProperty("attachments").EnumerateArray());
            Assert.NotEqual(JsonValueKind.Null, t.GetProperty("anonymisedAt").ValueKind);
            Assert.All(t.GetProperty("timeline").EnumerateArray(), e => { Assert.Equal(JsonValueKind.Null, e.GetProperty("body").ValueKind); Assert.Contains(Str(e, "actor"), new[] { "System", RetentionRules.RemovedActor }); });
            Assert.Equal(Str(before, "number"), Str(t, "number")); Assert.Equal(Str(before, "serviceName"), Str(t, "serviceName"));   // the statistic stays
            Assert.Equal("closed", Str(t, "status")); Assert.Equal(before.GetProperty("createdAt").GetDateTimeOffset(), t.GetProperty("createdAt").GetDateTimeOffset());
        }
        Assert.False(await Exists(w, key));                                                                                       // the bytes are gone, not just hidden
        Assert.NotNull(await w.Seed.Db(db => db.Attachments.Where(a => a.Id == file).Select(a => a.DeletedAt).SingleAsync()));
        Assert.False(await w.Seed.Db(db => db.Notifications.AnyAsync(n => n.TicketId == id)));
        Assert.Equal(HttpStatusCode.NotFound, (await w.Requester.Get($"/api/tickets/{id}/attachments/{file}")).StatusCode);

        // it stays in the numbers and cannot be brought back
        var reopen = await w.Requester.Post($"/api/tickets/{id}/transition", new { action = "reopen", reason = "still broken" });
        Assert.Equal(HttpStatusCode.Conflict, reopen.StatusCode); Assert.Contains("retention", (await reopen.Body()).GetProperty("error").GetProperty("message").GetString());
        Assert.Equal(0, (await Run(w)).GetProperty("tickets").GetInt32());                                                      // a second run finds nothing new

        var run = await w.Seed.Db(db => db.AuditEvents.Where(a => a.Action == "retention.run").OrderBy(a => a.Id).ToListAsync());
        Assert.Equal(2, run.Count); Assert.Equal("1", JsonDocument.Parse(run[0].MetaJson!).RootElement.GetProperty("tickets").GetString()); Assert.Equal("0", JsonDocument.Parse(run[1].MetaJson!).RootElement.GetProperty("tickets").GetString());   // every manual run is on record
        Assert.All(run, r => Assert.DoesNotContain("12345", r.MetaJson));                                                       // the log carries counts, never content
        var last = (await Ok(await w.Admin.Get("/api/retention"))).GetProperty("lastRun"); Assert.Equal(0, last.GetProperty("tickets").GetInt32());   // the most recent run was the empty second one
    }

    [Fact]
    public async Task Deleting_removes_the_ticket_its_timeline_approvals_notifications_and_files()
    {
        var w = await Build(); var id = await Raise(w); var keep = await Raise(w);
        await Ok(await w.Requester.Post($"/api/tickets/{id}/comments", new { body = "hello", @internal = false }));
        var file = await Attach(w, id); var key = await KeyOf(w, file);
        await w.Seed.Db(async db => { db.Notifications.Add(new Notification { UserId = w.RequesterUser.Id, Kind = "ticket.reply", Title = "x", TicketId = id, CreatedAt = w.Clock.UtcNow }); return await db.SaveChangesAsync(); });
        await Finish(w, id, 400);

        await Ok(await Save(w, true, 365, "delete"));
        Assert.Equal(1, (await Run(w)).GetProperty("tickets").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, (await w.Admin.Get($"/api/tickets/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await w.Admin.Get($"/api/tickets/{keep}")).StatusCode);
        await w.Seed.Db(async db =>
        {
            Assert.False(await db.TicketEvents.AnyAsync(e => e.TicketId == id)); Assert.False(await db.Attachments.AnyAsync(a => a.TicketId == id)); Assert.False(await db.Notifications.AnyAsync(n => n.TicketId == id));
            return 0;
        });
        Assert.False(await Exists(w, key));
    }

    // ============================================================ legal hold
    [Fact]
    public async Task A_ticket_under_legal_hold_is_never_touched_until_the_hold_is_released()
    {
        var w = await Build(); var held = await Raise(w, "Disputed invoice"); var other = await Raise(w);
        var file = await Attach(w, held); var key = await KeyOf(w, file);
        await Finish(w, held, 400); await Finish(w, other, 400);

        Assert.Equal(Invalid, (await w.Admin.SendAsync(HttpMethod.Put, $"/api/tickets/{held}/legal-hold", new { reason = "ab" })).StatusCode);                  // a reason is required
        Assert.Equal(HttpStatusCode.NotFound, (await w.Admin.SendAsync(HttpMethod.Put, $"/api/tickets/{Guid.NewGuid()}/legal-hold", new { reason = "dispute" })).StatusCode);
        await Ok(await w.Admin.SendAsync(HttpMethod.Put, $"/api/tickets/{held}/legal-hold", new { reason = "Court order 2026/17" }));
        Assert.Equal(HttpStatusCode.Conflict, (await w.Admin.SendAsync(HttpMethod.Put, $"/api/tickets/{held}/legal-hold", new { reason = "again" })).StatusCode);

        // only people who manage retention see that a hold exists, and why
        var forAdmin = await Ticket(w.Admin, held); Assert.True(forAdmin.GetProperty("canHold").GetBoolean()); Assert.Equal("Court order 2026/17", Str(forAdmin.GetProperty("legalHold"), "reason"));
        foreach (var b in new[] { w.Requester, w.Supervisor }) { var t = await Ticket(b, held); Assert.False(t.GetProperty("canHold").GetBoolean()); Assert.Equal(JsonValueKind.Null, t.GetProperty("legalHold").ValueKind); }
        var list = await Ok(await w.Admin.Get("/api/retention/holds")); Assert.Equal(1, list.GetProperty("total").GetInt32());
        Assert.Equal("Disputed invoice", Str(list.GetProperty("data")[0], "subject")); Assert.Equal(1, (await Ok(await w.Admin.Get("/api/retention"))).GetProperty("holds").GetInt32());

        await Ok(await Save(w, true, 90, "anonymise", 30));
        var preview = await Ok(await w.Admin.Post("/api/retention/preview", new { enabled = true, finishedTicketDays = 90, ticketAction = "anonymise", attachmentDays = 30 }));
        Assert.Equal(1, preview.GetProperty("tickets").GetInt32()); Assert.Equal(1, preview.GetProperty("heldTickets").GetInt32()); Assert.Equal(0, preview.GetProperty("files").GetInt32());
        Assert.Equal(1, (await Run(w)).GetProperty("tickets").GetInt32());
        var t2 = await Ticket(w.Admin, held); Assert.Equal("Disputed invoice", Str(t2, "subject")); Assert.Single(t2.GetProperty("attachments").EnumerateArray()); Assert.True(await Exists(w, key));
        Assert.Equal(RetentionRules.RemovedSubject, Str(await Ticket(w.Admin, other), "subject"));

        // a held ticket's files cannot be removed by hand either (the ticket is open here, so it could otherwise be done)
        var open = await Raise(w); var f2 = await Attach(w, open); await Ok(await w.Admin.SendAsync(HttpMethod.Put, $"/api/tickets/{open}/legal-hold", new { reason = "investigation" }));
        var del = await w.Requester.SendAsync(HttpMethod.Delete, $"/api/tickets/{open}/attachments/{f2}"); Assert.Equal(HttpStatusCode.Conflict, del.StatusCode);

        await Ok(await w.Admin.SendAsync(HttpMethod.Delete, $"/api/tickets/{held}/legal-hold"));
        Assert.Equal(HttpStatusCode.Conflict, (await w.Admin.SendAsync(HttpMethod.Delete, $"/api/tickets/{held}/legal-hold")).StatusCode);
        Assert.Equal(1, (await Run(w)).GetProperty("tickets").GetInt32());                                                      // released: now it goes
        Assert.Equal(RetentionRules.RemovedSubject, Str(await Ticket(w.Admin, held), "subject")); Assert.False(await Exists(w, key));
        var actions = await w.Seed.Db(db => db.AuditEvents.Where(a => a.Action.StartsWith("retention.hold")).Select(a => a.Action).ToListAsync());
        Assert.Equal(2, actions.Count(a => a == "retention.hold.place")); Assert.Single(actions, a => a == "retention.hold.release");
    }

    // ============================================================ files and notifications
    [Fact]
    public async Task Files_can_go_earlier_than_the_ticket_and_notifications_age_on_their_own()
    {
        var w = await Build(); var a = await Raise(w, "Older"); var b = await Raise(w, "Younger");
        var fa = await Attach(w, a); var fb = await Attach(w, b); var ka = await KeyOf(w, fa); var kb = await KeyOf(w, fb);
        await Finish(w, a, 100); await Finish(w, b, 20);
        await w.Seed.Db(async db =>
        {
            db.Notifications.Add(new Notification { UserId = w.RequesterUser.Id, Kind = "ticket.reply", Title = "old one", CreatedAt = w.Clock.UtcNow.AddDays(-80) });
            db.Notifications.Add(new Notification { UserId = w.RequesterUser.Id, Kind = "ticket.reply", Title = "new one", CreatedAt = w.Clock.UtcNow.AddDays(-5) });
            return await db.SaveChangesAsync();
        });

        await Ok(await Save(w, true, null, files: 30, notifications: 60));                                                      // tickets are kept for good
        var preview = await Ok(await w.Admin.Post("/api/retention/preview", new { enabled = true, ticketAction = "anonymise", attachmentDays = 30, notificationDays = 60 }));
        Assert.Equal(0, preview.GetProperty("tickets").GetInt32()); Assert.Equal(1, preview.GetProperty("files").GetInt32()); Assert.True(preview.GetProperty("fileBytes").GetInt64() > 0); Assert.Equal(1, preview.GetProperty("notifications").GetInt32());
        Assert.True(await Exists(w, ka));                                                                                     // a preview changes nothing

        var r = await Run(w);
        Assert.Equal(0, r.GetProperty("tickets").GetInt32()); Assert.Equal(1, r.GetProperty("files").GetInt32()); Assert.Equal(1, r.GetProperty("notifications").GetInt32());
        Assert.False(await Exists(w, ka)); Assert.True(await Exists(w, kb));                                                    // the ticket finished 20 days ago keeps its file
        Assert.Equal("Older", Str(await Ticket(w.Admin, a), "subject"));                                                        // the ticket itself is untouched
        Assert.Empty((await Ticket(w.Admin, a)).GetProperty("attachments").EnumerateArray()); Assert.Single((await Ticket(w.Admin, b)).GetProperty("attachments").EnumerateArray());
        var titles = await w.Seed.Db(db => db.Notifications.Where(n => n.TicketId == null).Select(n => n.Title).ToListAsync());
        Assert.DoesNotContain("old one", titles); Assert.Contains("new one", titles);
    }

    [Fact]
    public async Task A_file_that_cannot_be_removed_is_reported_and_retried_and_its_ticket_waits_for_it()
    {
        var w = await Build(); var id = await Raise(w, "Waits for its file");
        var file = await Attach(w, id); var key = await KeyOf(w, file);
        await Finish(w, id, 200); await Ok(await Save(w, true, 90, "anonymise"));

        using var refusing = w.F.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<IFileStorage, RefusingStorage>()));
        var r = await refusing.Services.CreateScope().ServiceProvider.GetRequiredService<RetentionService>().RunDueAsync();
        Assert.Equal(0, r.Files); Assert.Equal(1, r.FilesFailed); Assert.Equal(0, r.Tickets);                                  // not reported as done
        Assert.Equal("Waits for its file", Str(await Ticket(w.Admin, id), "subject"));                                          // the ticket keeps its content while its file is still there
        Assert.Null(await w.Seed.Db(db => db.Attachments.Where(a => a.Id == file).Select(a => a.DeletedAt).SingleAsync()));
        Assert.True(await Exists(w, key));
        Assert.True(await w.Seed.Db(db => db.SecurityEvents.AnyAsync(e => e.Type == "retention.files_failed")));
        Assert.Equal(1, (await Ok(await w.Admin.Get("/api/retention"))).GetProperty("lastRun").GetProperty("filesFailed").GetInt32());

        var again = await Run(w);                                                                                               // the volume is fine now
        Assert.Equal(1, again.GetProperty("files").GetInt32()); Assert.Equal(1, again.GetProperty("tickets").GetInt32());
        Assert.False(await Exists(w, key)); Assert.Equal(RetentionRules.RemovedSubject, Str(await Ticket(w.Admin, id), "subject"));
    }

    // ============================================================ organisations
    [Fact]
    public async Task The_scheduled_run_applies_each_organisations_own_policy_and_leaves_the_others_alone()
    {
        var w = await Build(); var mine = await Raise(w);
        await Finish(w, mine, 400); await Ok(await Save(w, true, 90, "delete"));

        // a second organisation with an old finished ticket and no policy
        var slug = "r" + Uniq(); var email = $"{slug}.admin@test.local";
        Assert.Equal(HttpStatusCode.Created, (await w.Admin.Post("/api/tenants", new { name = $"Org {slug}", slug, adminEmail = email })).StatusCode);
        var token = InMemoryEmailSender.TokenIn(await w.Seed.Mail.WaitFor(email, "invited"));
        Assert.Equal(HttpStatusCode.OK, (await TestBrowser.Create(w.F).Post("/api/invitations/accept", new { token, name = "Org Admin", password = StrongPw }, csrf: false)).StatusCode);
        var other = TestBrowser.Create(w.F); await other.LoginAsync(email, StrongPw);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.SendAsync(HttpMethod.Put, "/api/retention", new { enabled = true, finishedTicketDays = 30, ticketAction = "delete" })).StatusCode);   // reauth first
        await other.Post("/api/auth/reauth", new { password = StrongPw });
        var cat = IdOf(await Ok(await other.Post("/api/catalog/categories", new { name = "Cat" })));
        var team = IdOf(await Ok(await other.Post("/api/teams", new { name = "Team" })));
        var svc = IdOf(await Ok(await other.Post("/api/catalog/services", new { name = "Svc", categoryId = cat, owningTeamId = team })));
        await Ok(await other.Post($"/api/catalog/services/{svc}/publish"));
        var theirs = IdOf(await Ok(await other.Post($"/api/portal/services/{svc}/requests", new { subject = "Theirs", answers = new { } })));
        await Sql(w, $"UPDATE tickets SET status = 'Closed', updated_at = {w.Clock.UtcNow.AddDays(-400)} WHERE id = {theirs}");

        var r = await w.F.Services.CreateScope().ServiceProvider.GetRequiredService<RetentionService>().RunDueAsync();
        Assert.Equal(1, r.Tickets);
        Assert.Equal(HttpStatusCode.NotFound, (await w.Admin.Get($"/api/tickets/{mine}")).StatusCode);                          // mine is gone
        Assert.Equal("Theirs", Str(await Ticket(other, theirs), "subject"));                                                    // theirs, with no policy, is intact
        Assert.Equal(HttpStatusCode.NotFound, (await other.Get($"/api/tickets/{mine}")).StatusCode);
        var theirView = await Ok(await other.Get("/api/retention")); Assert.False(theirView.GetProperty("enabled").GetBoolean());   // each organisation has its own policy
        Assert.Equal(1, await w.Seed.Db(db => db.RetentionPolicies.IgnoreQueryFilters().CountAsync(p => p.Enabled)));
    }

    // ============================================================ the person's own data
    [Fact]
    public async Task The_personal_data_export_contains_the_persons_requests_comments_and_files_and_nobody_elses()
    {
        var w = await Build(); var id = await Raise(w, "My laptop", "Serial ABC-123");
        await Ok(await w.Requester.Post($"/api/tickets/{id}/comments", new { body = "It is the grey one", @internal = false }));
        await Ok(await w.Agent.Post($"/api/tickets/{id}/comments", new { body = "Agent reply, not theirs", @internal = false }));
        await Attach(w, id, "invoice.png");
        var someone = await w.Seed.UserAsync(); var sb = TestBrowser.Create(w.F); await sb.LoginAsync(someone.Email, Seed.Password);
        await Ok(await sb.Post($"/api/portal/services/{w.ServiceId}/requests", new { subject = "Someone else's request", answers = new { what = "x" } }));
        await w.Requester.Post("/api/auth/reauth", new { password = Seed.Password });

        var res = await w.Requester.Get("/api/me/export"); Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var text = await res.Content.ReadAsStringAsync(); var doc = JsonDocument.Parse(text).RootElement.GetProperty("helpdesk");
        Assert.Equal(1, doc.GetProperty("requestsTotal").GetInt32());
        var req = doc.GetProperty("requests")[0]; Assert.Equal("My laptop", Str(req, "subject")); Assert.Equal("Serial ABC-123", Str(req, "description")); Assert.Equal("secret answer", req.GetProperty("answers").GetProperty("what").GetString());
        Assert.Equal(["It is the grey one"], doc.GetProperty("comments").EnumerateArray().Select(c => Str(c, "body")).ToArray());        // their own words only
        Assert.Equal(["invoice.png"], doc.GetProperty("files").EnumerateArray().Select(f => Str(f, "fileName")).ToArray());
        Assert.DoesNotContain("Someone else's request", text); Assert.DoesNotContain("Agent reply", text);
    }
}
