using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Tests.Infra;
using Xunit;

namespace AnnabaDigital.Tests;

/// <summary>E-mail to ticket (TKT-004 by e-mail, R2 slice 9): the secret webhook, new requests, replies threaded by ticket number, duplicates, automatic mail, unknown senders and attachments.</summary>
[Collection("db")]
public sealed class EmailIntakeTests(PostgresFixture fx)
{
    private static string Uniq() => Guid.NewGuid().ToString("N")[..8];
    private static async Task<JsonElement> Ok(HttpResponseMessage r) { Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync()); return await r.Body(); }
    private static Guid IdOf(JsonElement e) => e.GetProperty("id").GetGuid();
    private static string Str(JsonElement e, string p) => e.GetProperty(p).GetString()!;
    private const string Png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8DwHwAFBQIAX8jx0gAAAABJRU5ErkJggg==";

    private static string Mail(string from, string subject, string body, string? messageId = null, string extraHeaders = "", string? pngName = null)
    {
        var id = messageId ?? $"<{Guid.NewGuid():N}@mail.test>";
        var head = $"From: Someone <{from}>\r\nTo: help@acme.test\r\nSubject: {subject}\r\nMessage-ID: {id}\r\nDate: Tue, 10 Nov 2026 09:00:00 +0700\r\nMIME-Version: 1.0\r\n{extraHeaders}";
        if (pngName is null) return head + "Content-Type: text/plain; charset=utf-8\r\n\r\n" + body + "\r\n";
        return head + "Content-Type: multipart/mixed; boundary=\"b1\"\r\n\r\n--b1\r\nContent-Type: text/plain; charset=utf-8\r\n\r\n" + body + "\r\n--b1\r\n"
            + $"Content-Type: image/png; name=\"{pngName}\"\r\nContent-Disposition: attachment; filename=\"{pngName}\"\r\nContent-Transfer-Encoding: base64\r\n\r\n{Png}\r\n--b1--\r\n";
    }

    /// <summary>Posts like a mail service: no Origin, no session — only the proxy key the web front adds to everything under /api.</summary>
    private static async Task<HttpResponseMessage> Deliver(ApiFactory f, string path, string raw)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, path) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes(raw)) };
        req.Content.Headers.ContentType = new MediaTypeHeaderValue("message/rfc822");
        req.Headers.Add("X-Internal-Proxy-Key", ApiFactory.ProxyKey);
        return await f.CreateClient().SendAsync(req);
    }

    [Fact]
    public async Task Mail_becomes_requests_and_replies_once_from_known_people_and_everything_else_is_logged_and_left_alone()
    {
        var iso = await fx.NewIsolatedAsync(); var f = iso.Factory; var seed = new Seed(f, iso.Clock);
        async Task<(User, TestBrowser)> Person(string role) { var u = await seed.UserAsync(role: role); var b = TestBrowser.Create(f); await b.LoginAsync(u.Email, Seed.Password); return (u, b); }
        var (_, admin) = await Person("super_admin"); var (ru, req) = await Person("user"); var (au, agent) = await Person("ga_agent"); var (ou, _) = await Person("user");
        var team = IdOf(await Ok(await admin.Post("/api/teams", new { name = $"Team {Uniq()}" })));
        await Ok(await admin.SendAsync(HttpMethod.Put, $"/api/teams/{team}/members", new { members = new[] { new { userId = au.Id, isLead = false } } }));
        var cat = IdOf(await Ok(await admin.Post("/api/catalog/categories", new { name = $"Cat {Uniq()}" })));
        var svc = IdOf(await Ok(await admin.Post("/api/catalog/services", new { name = $"General help {Uniq()}", categoryId = cat, owningTeamId = team })));

        // --- settings: a published service, a token (password re-entry), then on
        Assert.Equal((HttpStatusCode)422, (await admin.SendAsync(HttpMethod.Put, "/api/settings/inbound-email", new { address = "help@acme.test", defaultServiceId = svc, enabled = false })).StatusCode);   // not published yet
        await Ok(await admin.Post($"/api/catalog/services/{svc}/publish"));
        Assert.Equal(HttpStatusCode.Conflict, (await admin.SendAsync(HttpMethod.Put, "/api/settings/inbound-email", new { address = "help@acme.test", defaultServiceId = svc, enabled = true })).StatusCode);   // no token yet
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.Post("/api/settings/inbound-email/token")).StatusCode);                   // re-enter the password first
        await admin.Post("/api/auth/reauth", new { password = Seed.Password });
        var token = await Ok(await admin.Post("/api/settings/inbound-email/token"));
        var path = Str(token, "path");
        Assert.StartsWith("/api/inbound/email/", path);
        Assert.Equal(HttpStatusCode.NotFound, (await Deliver(f, path, Mail(ru.Email, "Too early", "x"))).StatusCode);              // switched off
        await Ok(await admin.SendAsync(HttpMethod.Put, "/api/settings/inbound-email", new { address = "Help@Acme.test", defaultServiceId = svc, enabled = true }));
        Assert.Equal(HttpStatusCode.NotFound, (await Deliver(f, "/api/inbound/email/not-the-token", Mail(ru.Email, "Guess", "x"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await req.Get("/api/settings/inbound-email")).StatusCode);

        // --- a new request, with a picture; the provider's retry is a duplicate
        var first = Mail(ru.Email.ToUpperInvariant(), "Fwd: Projector in room 3", "The projector shows no signal.\r\n\r\nThanks", "<p1@mail.test>", pngName: "screen.png");
        var created = await Ok(await Deliver(f, path, first));
        Assert.Equal("created", Str(created, "outcome"));
        var number = Str(created, "ticketNumber");
        var ticket = await seed.Db(db => db.Tickets.FirstAsync(t => t.Number == number));
        Assert.Equal(ru.Id, ticket.RequesterId); Assert.Equal("Projector in room 3", ticket.Subject); Assert.Contains("no signal", ticket.Description);
        Assert.Equal(team, ticket.TeamId);
        Assert.Equal(1, await seed.Db(db => db.Attachments.CountAsync(a => a.TicketId == ticket.Id && a.FileName == "screen.png")));
        Assert.Equal("duplicate", Str(await Ok(await Deliver(f, path, first)), "outcome"));
        Assert.Equal(1, await seed.Db(db => db.Tickets.CountAsync(t => t.RequesterId == ru.Id)));

        // --- replies: only the new text, by the requester and by staff who can see the ticket
        var reply = Mail(ru.Email, $"Re: New reply on {number}", "It works now, thank you!\r\n\r\nOn Tue, 10 Nov 2026 at 10:00, Helpdesk wrote:\r\n> Please try again.\r\n> Regards", "<p2@mail.test>");
        Assert.Equal("replied", Str(await Ok(await Deliver(f, path, reply)), "outcome"));
        Assert.Equal("replied", Str(await Ok(await Deliver(f, path, Mail(au.Email, $"RE: {number}", "Checked on site — cable replaced.\r\n-- \r\nAgent signature", "<p3@mail.test>"))), "outcome"));
        var timeline = (await Ok(await req.Get($"/api/tickets/{ticket.Id}"))).GetProperty("timeline").EnumerateArray().Where(e => Str(e, "kind") == "comment").Select(e => Str(e, "body")).ToList();
        Assert.Equal(["It works now, thank you!\n\n(by e-mail)", "Checked on site — cable replaced.\n\n(by e-mail)"], timeline);

        // --- someone who can't see the ticket starts their own request instead; nothing leaks into the ticket
        var stranger = await Ok(await Deliver(f, path, Mail(ou.Email, $"Re: {number}", "Is this about my desk?", "<p4@mail.test>")));
        Assert.Equal("created", Str(stranger, "outcome")); Assert.NotEqual(number, Str(stranger, "ticketNumber"));

        // --- left alone: unknown senders, automatic mail, empty replies, finished tickets
        Assert.Equal("rejected", Str(await Ok(await Deliver(f, path, Mail("someone@else.test", "Hello", "Buy now", "<p5@mail.test>"))), "outcome"));
        Assert.Equal("ignored", Str(await Ok(await Deliver(f, path, Mail(ru.Email, $"Out of office: {number}", "I am away.", "<p6@mail.test>", "Auto-Submitted: auto-replied\r\n"))), "outcome"));
        Assert.Equal("ignored", Str(await Ok(await Deliver(f, path, Mail(ru.Email, $"Re: {number}", "> only the quote", "<p7@mail.test>"))), "outcome"));
        await Ok(await req.Post($"/api/tickets/{ticket.Id}/transition", new { action = "cancel", reason = "Fixed itself" }));
        Assert.Equal("rejected", Str(await Ok(await Deliver(f, path, Mail(ru.Email, $"Re: {number}", "One more thing", "<p8@mail.test>"))), "outcome"));
        Assert.True(await seed.Db(db => db.Notifications.AnyAsync(n => n.UserId == ru.Id && n.Title == $"Your e-mail about {number} was not added")));
        Assert.Equal((HttpStatusCode)422, (await Deliver(f, path, "this is not mail")).StatusCode);

        var log = (await Ok(await admin.Get("/api/settings/inbound-email"))).GetProperty("recent").EnumerateArray().Select(m => Str(m, "outcome")).ToList();
        Assert.Equal(new[] { "created", "replied", "replied", "created", "rejected", "ignored", "ignored", "rejected" }.OrderBy(x => x), log.OrderBy(x => x));
        Assert.Equal("help@acme.test", Str(await Ok(await admin.Get("/api/settings/inbound-email")), "address"));
    }

    [Fact]
    public void The_new_text_of_a_reply_the_ticket_number_and_automatic_mail_are_recognised()
    {
        Assert.Equal("Thanks!", EmailIntakeRules.NewText("Thanks!\n\nPada Sel, 10 Nov 2026 pukul 10.00 Helpdesk menulis:\n> lama"));
        Assert.Equal("Done", EmailIntakeRules.NewText("Done\r\n\r\n-----Original Message-----\r\nFrom: x"));
        Assert.Equal("Line 1\nLine 2", EmailIntakeRules.NewText("Line 1\n> quoted\nLine 2\n-- \nsig"));
        Assert.Equal("GA-2026-000123", EmailIntakeRules.TicketNumberIn("Re: [GA-2026-000123] New reply"));
        Assert.Null(EmailIntakeRules.TicketNumberIn("Re: lunch"));
        Assert.Equal("Projector", EmailIntakeRules.CleanSubject("Re: FW: re:  Projector"));
        Assert.True(EmailIntakeRules.IsAutomated("auto-replied", null, false, "a@b.c", false));
        Assert.False(EmailIntakeRules.IsAutomated("no", null, false, "a@b.c", false));
        Assert.True(EmailIntakeRules.IsAutomated(null, "bulk", false, "a@b.c", false));
        Assert.True(EmailIntakeRules.IsAutomated(null, null, false, "MAILER-DAEMON@b.c", false));
        Assert.True(EmailIntakeRules.IsAutomated(null, null, true, "a@b.c", false));
    }
}
