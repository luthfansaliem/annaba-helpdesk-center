using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Helpdesk;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Tests.Infra;
using Xunit;

namespace AnnabaDigital.Tests;

/// <summary>Attachments (TKT-005): what is accepted, who may see and remove files, how they are served, the file question on request forms, and unclaimed uploads.</summary>
[Collection("db")]
public sealed class AttachmentTests(PostgresFixture fx)
{
    private readonly Seed _seed = new(fx.Factory, fx.Clock);
    private const string StrongPw = "Cobalt-Meadow-Harvest-58";
    private static string Uniq() => Guid.NewGuid().ToString("N")[..8];
    private const HttpStatusCode Invalid = (HttpStatusCode)422;

    private async Task<(User User, TestBrowser B)> Person(string role)
    {
        var u = await _seed.UserAsync(role: role); var b = TestBrowser.Create(fx.Factory); await b.LoginAsync(u.Email, Seed.Password); return (u, b);
    }
    private static async Task<JsonElement> Ok(HttpResponseMessage r) { Assert.True(r.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK, await r.Content.ReadAsStringAsync()); return await r.Body(); }
    private static Guid IdOf(JsonElement e) => e.GetProperty("id").GetGuid();
    private static string Str(JsonElement e, string p) => e.GetProperty(p).GetString()!;
    private Task Sql(FormattableString sql) => _seed.Db(async db => await db.Database.ExecuteSqlInterpolatedAsync(sql));
    private DateTimeOffset Ago(int minutes) => fx.Clock.UtcNow.AddMinutes(-minutes);

    private static byte[] Png(int size = 64) { var b = new byte[size]; new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(b, 0); Random.Shared.NextBytes(b.AsSpan(8)); return b; }
    private static byte[] Pdf() => Encoding.ASCII.GetBytes("%PDF-1.7\n1 0 obj\n<<>>\nendobj\n%%EOF");

    private sealed record World(TestBrowser Admin, TestBrowser Requester, User RequesterUser, TestBrowser Agent, User AgentUser, TestBrowser Supervisor, Guid ServiceId, Guid TeamId);

    private async Task<World> Build(int fileMax = 2)
    {
        var (_, admin) = await Person("super_admin"); var (ru, requester) = await Person("user"); var (au, agent) = await Person("ga_agent"); var (_, sup) = await Person("ga_supervisor");
        var team = (await Ok(await admin.Post("/api/teams", new { name = $"Team {Uniq()}" }))).GetProperty("id").GetGuid();
        await Ok(await admin.SendAsync(HttpMethod.Put, $"/api/teams/{team}/members", new { members = new[] { new { userId = au.Id, isLead = false } } }));
        var cat = IdOf(await Ok(await admin.Post("/api/catalog/categories", new { name = $"Cat {Uniq()}" })));
        var svc = IdOf(await Ok(await admin.Post("/api/catalog/services", new { name = $"Svc {Uniq()}", categoryId = cat, owningTeamId = team })));
        await Ok(await admin.SendAsync(HttpMethod.Put, $"/api/catalog/services/{svc}/form", new
        {
            fields = new object[] { new { key = "what", type = "text", label = "What?", required = true }, new { key = "photos", type = "file", label = "Photos", required = false, max = fileMax } },
        }));
        await Ok(await admin.Post($"/api/catalog/services/{svc}/publish"));
        return new World(admin, requester, ru, agent, au, sup, svc, team);
    }

    private static async Task<Guid> Raise(TestBrowser b, Guid svc, object? photos = null)
        => IdOf(await Ok(await b.Post($"/api/portal/services/{svc}/requests", new { subject = "Printer is broken", answers = photos is null ? (object)new { what = "x" } : new { what = "x", photos } })));
    private static async Task<JsonElement> Ticket(TestBrowser b, Guid id) => await Ok(await b.Get($"/api/tickets/{id}"));
    private static string[] Names(JsonElement ticket) => [.. ticket.GetProperty("attachments").EnumerateArray().Select(a => Str(a, "fileName"))];

    // ============================================================ attaching and serving
    [Fact]
    public async Task A_file_is_attached_listed_on_the_timeline_and_served_safely_to_everyone_who_may_see_the_ticket()
    {
        var w = await Build(); var id = await Raise(w.Requester, w.ServiceId);
        var png = Png(); var pdf = Pdf();

        var up = await Ok(await w.Requester.UploadAsync($"/api/tickets/{id}/attachments", @"C:\Users\me\Desktop\Broken printer.png", png, "image/png"));
        Assert.Equal("Broken printer.png", Str(up, "fileName"));                                                       // a path in the name is stripped
        Assert.Equal("image/png", Str(up, "contentType")); Assert.True(up.GetProperty("inline").GetBoolean()); Assert.Equal(png.Length, up.GetProperty("size").GetInt64()); Assert.True(up.GetProperty("canDelete").GetBoolean());
        var doc = await Ok(await w.Agent.UploadAsync($"/api/tickets/{id}/attachments", "quote.pdf", pdf, "text/html"));     // the claimed type is ignored: we decide by extension and content
        Assert.Equal("application/pdf", Str(doc, "contentType")); Assert.False(doc.GetProperty("inline").GetBoolean());

        foreach (var b in new[] { w.Requester, w.Agent, w.Supervisor })
            Assert.Equal(["Broken printer.png", "quote.pdf"], Names(await Ticket(b, id)));
        var t = await Ticket(w.Requester, id);
        Assert.Contains(t.GetProperty("timeline").EnumerateArray(), e => Str(e, "kind") == "updated" && Str(e, "body").StartsWith("Attached Broken printer.png"));
        Assert.True(t.GetProperty("canAttach").GetBoolean());

        var img = await w.Agent.Get($"/api/tickets/{id}/attachments/{IdOf(up)}");
        Assert.Equal(HttpStatusCode.OK, img.StatusCode); Assert.Equal("image/png", img.Content.Headers.ContentType!.MediaType);
        Assert.Equal(png, await img.Content.ReadAsByteArrayAsync());
        Assert.StartsWith("inline", img.Content.Headers.ContentDisposition!.ToString()); Assert.Equal("nosniff", img.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Contains("sandbox", img.Headers.GetValues("Content-Security-Policy").Single());
        var pdfRes = await w.Requester.Get($"/api/tickets/{id}/attachments/{IdOf(doc)}");
        Assert.StartsWith("attachment", pdfRes.Content.Headers.ContentDisposition!.ToString()); Assert.Equal(pdf, await pdfRes.Content.ReadAsByteArrayAsync());

        var audit = await _seed.Db(db => db.AuditEvents.Where(a => a.TargetId == id.ToString() && a.Action == "attachment.add").ToListAsync());
        Assert.Equal(2, audit.Count);
        var sha = Convert.ToHexString(SHA256.HashData(png)).ToLowerInvariant();
        Assert.Contains(audit, a => a.MetaJson.Contains(sha));                                                                  // the content fingerprint is on record
    }

    [Fact]
    public async Task Unsafe_empty_oversized_and_infected_files_are_refused_and_nothing_is_stored()
    {
        var w = await Build(); var id = await Raise(w.Requester, w.ServiceId); var url = $"/api/tickets/{id}/attachments";
        async Task<string> Refused(HttpResponseMessage r) { Assert.Equal(Invalid, r.StatusCode); return (await r.Body()).GetProperty("error").GetProperty("fields").GetProperty("file").GetString()!; }

        Assert.Contains("not accepted", await Refused(await w.Requester.UploadAsync(url, "setup.exe", Png())));                 // not on the list
        Assert.Contains("not accepted", await Refused(await w.Requester.UploadAsync(url, "logo.svg", Encoding.UTF8.GetBytes("<svg onload=alert(1)/>"))));
        Assert.Contains("does not match", await Refused(await w.Requester.UploadAsync(url, "photo.png", Encoding.ASCII.GetBytes("MZ\u0090\u0000program"))));   // a program renamed to .png
        Assert.Contains("empty", await Refused(await w.Requester.UploadAsync(url, "photo.png", [])));
        Assert.Contains("larger", await Refused(await w.Requester.UploadAsync(url, "big.png", Png(8 * 1024 * 1024 + 1))));
        var infected = Encoding.ASCII.GetBytes("plain text with " + MarkerScanner.Marker);
        Assert.Contains("security scan", await Refused(await w.Requester.UploadAsync(url, "notes.txt", infected)));
        Assert.Contains("attachment.rejected", await _seed.Db(db => db.AuditEvents.Where(a => a.TargetId == id.ToString()).Select(a => a.Action).ToListAsync()));

        var tooBig = await w.Requester.UploadAsync(url, "huge.png", Png(11 * 1024 * 1024));                                      // past the request limit: refused before it is read
        Assert.False(tooBig.IsSuccessStatusCode); Assert.NotEqual(HttpStatusCode.InternalServerError, tooBig.StatusCode);
        Assert.Empty(Names(await Ticket(w.Requester, id)));
        Assert.Equal(0, await _seed.Db(db => db.Attachments.CountAsync(a => a.TicketId == id)));
    }

    [Fact]
    public async Task A_ticket_holds_a_limited_number_of_files_and_finished_tickets_take_none()
    {
        var w = await Build(); var id = await Raise(w.Requester, w.ServiceId); var url = $"/api/tickets/{id}/attachments";
        for (var i = 0; i < 20; i++) Assert.Equal(HttpStatusCode.Created, (await w.Requester.UploadAsync(url, $"p{i}.png", Png())).StatusCode);
        Assert.Equal(Invalid, (await w.Requester.UploadAsync(url, "one-too-many.png", Png())).StatusCode);
        var first = IdOf((await Ticket(w.Requester, id)).GetProperty("attachments")[0]);
        Assert.Equal(HttpStatusCode.NoContent, (await w.Requester.SendAsync(HttpMethod.Delete, $"{url}/{first}")).StatusCode);       // removing one makes room again
        Assert.Equal(HttpStatusCode.Created, (await w.Requester.UploadAsync(url, "fits-now.png", Png())).StatusCode);

        var done = await Raise(w.Requester, w.ServiceId);
        await Ok(await w.Requester.Post($"/api/tickets/{done}/transition", new { action = "cancel", reason = "No longer needed" }));
        Assert.Equal(HttpStatusCode.Conflict, (await w.Requester.UploadAsync($"/api/tickets/{done}/attachments", "late.png", Png())).StatusCode);
    }

    // ============================================================ who may see and remove
    [Fact]
    public async Task Only_people_who_can_see_the_ticket_can_add_open_or_remove_its_files()
    {
        var w = await Build(); var (_, stranger) = await Person("user"); var (_, outsider) = await Person("ga_agent");
        var id = await Raise(w.Requester, w.ServiceId);
        var file = await Ok(await w.Requester.UploadAsync($"/api/tickets/{id}/attachments", "a.png", Png()));

        foreach (var b in new[] { stranger, outsider })                                                                          // somebody else's request / another team's agent
        {
            Assert.Equal(HttpStatusCode.NotFound, (await b.UploadAsync($"/api/tickets/{id}/attachments", "x.png", Png())).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await b.Get($"/api/tickets/{id}/attachments/{IdOf(file)}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await b.SendAsync(HttpMethod.Delete, $"/api/tickets/{id}/attachments/{IdOf(file)}")).StatusCode);
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await TestBrowser.Create(fx.Factory).Get($"/api/tickets/{id}/attachments/{IdOf(file)}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await w.Requester.Get($"/api/tickets/{id}/attachments/{Guid.NewGuid()}")).StatusCode);
        var other = await Raise(w.Requester, w.ServiceId);
        Assert.Equal(HttpStatusCode.NotFound, (await w.Requester.Get($"/api/tickets/{other}/attachments/{IdOf(file)}")).StatusCode);   // a file is reachable only through its own ticket

        var staffFile = await Ok(await w.Agent.UploadAsync($"/api/tickets/{id}/attachments", "internal-note.pdf", Pdf()));
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Requester.SendAsync(HttpMethod.Delete, $"/api/tickets/{id}/attachments/{IdOf(staffFile)}")).StatusCode);   // not theirs
        Assert.Equal(HttpStatusCode.NoContent, (await w.Agent.SendAsync(HttpMethod.Delete, $"/api/tickets/{id}/attachments/{IdOf(file)}")).StatusCode);          // staff who manage the ticket can remove any
        Assert.Equal(["internal-note.pdf"], Names(await Ticket(w.Requester, id)));
        Assert.Equal(HttpStatusCode.NotFound, (await w.Requester.Get($"/api/tickets/{id}/attachments/{IdOf(file)}")).StatusCode);

        var fid = IdOf(file);
        var key = await _seed.Db(db => db.Attachments.Where(a => a.Id == fid).Select(a => a.StorageKey).SingleAsync());
        Assert.Null(await fx.Factory.Services.GetRequiredService<IFileStorage>().OpenAsync(key));                                  // the bytes are gone, not just hidden
        Assert.NotNull(await _seed.Db(db => db.Attachments.Where(a => a.Id == fid).Select(a => a.DeletedAt).SingleAsync()));      // the row stays for the trail
        Assert.Contains("attachment.delete", await _seed.Db(db => db.AuditEvents.Where(a => a.TargetId == id.ToString()).Select(a => a.Action).ToListAsync()));
        Assert.Contains((await Ticket(w.Requester, id)).GetProperty("timeline").EnumerateArray(), e => Str(e, "body") == "Removed a.png.");
    }

    // ============================================================ the file question
    [Fact]
    public async Task Files_uploaded_for_a_request_form_are_claimed_by_the_ticket_once_and_only_by_their_owner()
    {
        var w = await Build(fileMax: 2); var (_, other) = await Person("user");
        var a = IdOf(await Ok(await w.Requester.UploadAsync("/api/uploads", "front.png", Png(), "image/png")));
        var b = IdOf(await Ok(await w.Requester.UploadAsync("/api/uploads", "back.jpg", [0xFF, 0xD8, 0xFF, 0xE0, 0, 1, 2, 3])));
        var c = IdOf(await Ok(await w.Requester.UploadAsync("/api/uploads", "side.png", Png())));
        var theirs = IdOf(await Ok(await other.UploadAsync("/api/uploads", "stranger.png", Png())));

        var answers = (object photos) => w.Requester.Post($"/api/portal/services/{w.ServiceId}/requests", new { subject = "With photos", answers = new { what = "x", photos } });
        Assert.Equal(Invalid, (await answers(new[] { a, b, c })).StatusCode);                                                     // the question allows two
        Assert.Equal(Invalid, (await answers(new[] { theirs })).StatusCode);                                                      // someone else's upload
        Assert.Equal(Invalid, (await answers(new[] { Guid.NewGuid() })).StatusCode);                                              // unknown
        var bad = await answers(new[] { theirs }); Assert.True((await bad.Body()).GetProperty("error").GetProperty("fields").TryGetProperty("answers.photos", out _));

        var ok = await Ok(await answers(new[] { a, b }));
        var id = IdOf(ok);
        Assert.Equal(["back.jpg", "front.png"], Names(ok).Order().ToArray());
        var photoAnswer = ok.GetProperty("answers").EnumerateArray().Single(x => Str(x, "key") == "photos");
        Assert.Equal("file", Str(photoAnswer, "type")); Assert.Contains("front.png", Str(photoAnswer, "display"));                 // the answer reads as names, the files are in the list
        Assert.Equal(2, photoAnswer.GetProperty("value").GetArrayLength());
        Assert.Equal(HttpStatusCode.OK, (await w.Agent.Get($"/api/tickets/{id}/attachments/{a}")).StatusCode);                    // staff can open what the requester attached
        Assert.Equal(Invalid, (await answers(new[] { a })).StatusCode);                                                           // already attached to a ticket: claimed once
        var again = await Ok(await answers(new[] { c })); Assert.Equal(["side.png"], Names(again));

        Assert.Equal(HttpStatusCode.Unauthorized, (await TestBrowser.Create(fx.Factory).UploadAsync("/api/uploads", "x.png", Png())).StatusCode);
        Assert.Equal(Invalid, (await w.Requester.UploadAsync("/api/uploads", "x.exe", Png())).StatusCode);                        // the same checks apply to every upload
    }

    [Fact]
    public async Task Unclaimed_uploads_are_removed_after_a_day_and_claimed_files_never_are()
    {
        var w = await Build();
        var stale = IdOf(await Ok(await w.Requester.UploadAsync("/api/uploads", "stale.png", Png())));
        var fresh = IdOf(await Ok(await w.Requester.UploadAsync("/api/uploads", "fresh.png", Png())));
        var kept = IdOf(await Ok(await w.Requester.UploadAsync("/api/uploads", "kept.png", Png())));
        var ticket = await Raise(w.Requester, w.ServiceId, new[] { kept });
        await Sql($"UPDATE attachments SET created_at = {Ago(60 * 25)} WHERE id = {stale} OR id = {kept}");
        var keyOfStale = await _seed.Db(db => db.Attachments.Where(a => a.Id == stale).Select(a => a.StorageKey).SingleAsync());

        int n;
        using (var scope = fx.Factory.Services.CreateScope()) n = await scope.ServiceProvider.GetRequiredService<AttachmentService>().PurgeOrphansAsync();
        Assert.True(n >= 1);
        Assert.False(await _seed.Db(db => db.Attachments.AnyAsync(a => a.Id == stale)));                                          // the abandoned upload is gone, bytes and row
        Assert.Null(await fx.Factory.Services.GetRequiredService<IFileStorage>().OpenAsync(keyOfStale));
        Assert.True(await _seed.Db(db => db.Attachments.AnyAsync(a => a.Id == fresh)));                                           // still within its day
        Assert.True(await _seed.Db(db => db.Attachments.AnyAsync(a => a.Id == kept)));                                            // attached to a ticket: never purged, however old
        Assert.Equal(HttpStatusCode.OK, (await w.Requester.Get($"/api/tickets/{ticket}/attachments/{kept}")).StatusCode);
    }

    [Fact]
    public async Task One_person_cannot_pile_up_unlimited_unclaimed_uploads()
    {
        var w = await Build();
        for (var i = 0; i < AttachmentService.MaxPendingUploads; i++) Assert.Equal(HttpStatusCode.Created, (await w.Requester.UploadAsync("/api/uploads", $"p{i}.png", Png())).StatusCode);
        Assert.Equal(Invalid, (await w.Requester.UploadAsync("/api/uploads", "extra.png", Png())).StatusCode);
    }

    // ============================================================ tenants
    [Fact]
    public async Task One_organisations_files_are_out_of_reach_for_another()
    {
        var w = await Build(); var id = await Raise(w.Requester, w.ServiceId);
        var file = await Ok(await w.Requester.UploadAsync($"/api/tickets/{id}/attachments", "a.png", Png()));
        var (_, otherAdmin) = await NewTenantAdmin();
        Assert.Equal(HttpStatusCode.NotFound, (await otherAdmin.Get($"/api/tickets/{id}/attachments/{IdOf(file)}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await otherAdmin.UploadAsync($"/api/tickets/{id}/attachments", "x.png", Png())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await otherAdmin.SendAsync(HttpMethod.Delete, $"/api/tickets/{id}/attachments/{IdOf(file)}")).StatusCode);
        // the stored file sits under its own organisation's folder
        var fid = IdOf(file);
        var key = await _seed.Db(db => db.Attachments.Where(a => a.Id == fid).Select(a => a.StorageKey).SingleAsync());
        Assert.Matches("^[0-9a-f]{32}/[0-9a-f]{32}$", key);
    }

    private async Task<(Guid TenantId, TestBrowser Admin)> NewTenantAdmin()
    {
        var pu = await _seed.UserAsync(role: "super_admin"); var p = TestBrowser.Create(fx.Factory); await p.LoginAsync(pu.Email, Seed.Password);
        await p.Post("/api/auth/reauth", new { password = Seed.Password });
        var slug = "f" + Uniq(); var email = $"{slug}.admin@test.local";
        var res = await p.Post("/api/tenants", new { name = $"Org {slug}", slug, adminEmail = email });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var id = (await res.Body()).GetProperty("id").GetGuid();
        var token = InMemoryEmailSender.TokenIn(await _seed.Mail.WaitFor(email, "invited"));
        Assert.Equal(HttpStatusCode.OK, (await TestBrowser.Create(fx.Factory).Post("/api/invitations/accept", new { token, name = "Org Admin", password = StrongPw }, csrf: false)).StatusCode);
        var b = TestBrowser.Create(fx.Factory); Assert.Equal(HttpStatusCode.OK, (await b.LoginAsync(email, StrongPw)).StatusCode);
        return (id, b);
    }
}
