using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Email;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Tests.Infra;
using Xunit;

namespace AnnabaDigital.Tests;

/// <summary>
/// An organisation's own e-mail wording (NTF-002): who may set it, what is accepted, that every outgoing mail of that organisation carries it in the recipient's language,
/// that it is escaped, and that other organisations are untouched. Each test has its own database: the wording applies to a whole organisation.
/// </summary>
[Collection("db")]
public sealed class EmailWordingTests(PostgresFixture fx)
{
    private const string StrongPw = "Cobalt-Meadow-Harvest-58";
    private static string Uniq() => Guid.NewGuid().ToString("N")[..8];
    private const HttpStatusCode Invalid = (HttpStatusCode)422;
    private static async Task<JsonElement> Ok(HttpResponseMessage r) { Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync()); return await r.Body(); }
    private static Guid IdOf(JsonElement e) => e.GetProperty("id").GetGuid();

    private sealed record World(ApiFactory F, Seed Seed, TestBrowser Admin, TestBrowser Requester, User RequesterUser, TestBrowser Agent, TestBrowser Supervisor, Guid ServiceId);

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
        await Ok(await admin.Post($"/api/catalog/services/{svc}/publish"));
        return new World(iso.Factory, seed, admin, requester, ru, agent, sup, svc);
    }

    private static Task<HttpResponseMessage> Save(TestBrowser b, string? sender, string? prefix, string? footerEn, string? footerId)
        => b.SendAsync(HttpMethod.Put, "/api/settings/email-wording", new { senderName = sender, subjectPrefix = prefix, footerEn, footerId });

    /// <summary>Raises a request and has the agent resolve it, which mails the requester. Returns that mail.</summary>
    private static async Task<EmailMessage> ResolvedMail(World w, string subjectPart)
    {
        var id = IdOf(await Ok(await w.Requester.Post($"/api/portal/services/{w.ServiceId}/requests", new { subject = "Lamp is broken", answers = new { } })));
        await Ok(await w.Agent.Post($"/api/tickets/{id}/transition", new { action = "start" }));
        await Ok(await w.Agent.Post($"/api/tickets/{id}/transition", new { action = "resolve", resolutionCode = "fixed", resolutionNote = "Replaced it" }));
        var number = (await Ok(await w.Requester.Get($"/api/tickets/{id}"))).GetProperty("number").GetString()!;
        var m = await w.Seed.Mail.WaitFor(w.RequesterUser.Email, number);
        Assert.Contains(subjectPart, m.Subject);
        return m;
    }

    [Fact]
    public async Task Only_settings_managers_read_and_change_the_wording_and_saving_needs_a_fresh_password_check()
    {
        var w = await Build();
        foreach (var b in new[] { w.Supervisor, w.Agent, w.Requester })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await b.Get("/api/settings/email-wording")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Save(b, "X", null, null, null)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await b.Post("/api/settings/email-wording/preview", new { senderName = "X" })).StatusCode);
        }
        var v = await Ok(await w.Admin.Get("/api/settings/email-wording"));
        foreach (var p in new[] { "senderName", "subjectPrefix", "footerEn", "footerId", "updatedAt" }) Assert.Equal(JsonValueKind.Null, v.GetProperty(p).ValueKind);   // nothing of its own yet

        var other = await w.Seed.UserAsync(role: "super_admin"); var fresh = TestBrowser.Create(w.F); await fresh.LoginAsync(other.Email, Seed.Password);
        var denied = await Save(fresh, "Acme GA", null, null, null);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode); Assert.Equal("reauth_required", await denied.ErrorCode());
    }

    [Fact]
    public async Task The_wording_is_validated_trimmed_and_audited_and_an_unchanged_save_is_refused()
    {
        var w = await Build();
        async Task<JsonElement> Refused(HttpResponseMessage r) { Assert.Equal(Invalid, r.StatusCode); return (await r.Body()).GetProperty("error").GetProperty("fields"); }
        Assert.True((await Refused(await Save(w.Admin, new string('a', 61), null, null, null))).TryGetProperty("senderName", out _));
        Assert.True((await Refused(await Save(w.Admin, "Two\nlines", null, null, null))).TryGetProperty("senderName", out _));
        Assert.True((await Refused(await Save(w.Admin, null, new string('p', 31), null, null))).TryGetProperty("subjectPrefix", out _));
        Assert.True((await Refused(await Save(w.Admin, null, null, new string('f', 401), null))).TryGetProperty("footerEn", out _));
        Assert.True((await Refused(await Save(w.Admin, null, null, null, "1\n2\n3\n4\n5\n6\n7"))).TryGetProperty("footerId", out _));
        Assert.True((await Refused(await Save(w.Admin, null, "bell\u0007", null, null))).TryGetProperty("subjectPrefix", out _));          // control characters never reach a mail header
        Assert.Equal(Invalid, (await w.Admin.Post("/api/settings/email-wording/preview", new { senderName = new string('a', 61) })).StatusCode);

        var v = await Ok(await Save(w.Admin, "  Acme GA  ", "[Acme]", "Call 1234.\r\nFloor 3.", "   "));
        Assert.Equal("Acme GA", v.GetProperty("senderName").GetString()); Assert.Equal("Call 1234.\nFloor 3.", v.GetProperty("footerEn").GetString());
        Assert.Equal(JsonValueKind.Null, v.GetProperty("footerId").ValueKind);                                                           // blank means "none"
        Assert.Equal(Invalid, (await Save(w.Admin, "Acme GA", "[Acme]", "Call 1234.\nFloor 3.", null)).StatusCode);                      // nothing changed
        var log = await w.Seed.Db(db => db.AuditEvents.Where(a => a.Action == "settings.email_wording.update").ToListAsync());
        Assert.Single(log); Assert.Contains("Acme GA", log[0].DiffJson); Assert.Contains("(default)", log[0].DiffJson);

        await Ok(await Save(w.Admin, null, null, null, null));                                                                           // back to the product's wording
        var cleared = await Ok(await w.Admin.Get("/api/settings/email-wording")); Assert.Equal(JsonValueKind.Null, cleared.GetProperty("senderName").ValueKind);
    }

    [Fact]
    public async Task Every_mail_of_the_organisation_carries_its_prefix_closing_text_and_name_in_the_recipients_language()
    {
        var w = await Build();
        var plain = await ResolvedMail(w, "was resolved");
        Assert.StartsWith("Your request", plain.Subject); Assert.EndsWith("— Annaba Digital", plain.Text);                               // the product's wording until the organisation sets its own

        await Ok(await Save(w.Admin, "Acme General Affairs", "[Acme GA]", "Urgent? Call ext. 1234 <now> & tell us.", "Mendesak? Hubungi ext. 1234."));
        var en = await ResolvedMail(w, "was resolved");
        Assert.StartsWith("[Acme GA] Your request", en.Subject);
        Assert.Contains("\n\nUrgent? Call ext. 1234 <now> & tell us.\n\n— Acme General Affairs", en.Text); Assert.DoesNotContain("— Annaba Digital", en.Text);
        Assert.Contains("Urgent? Call ext. 1234 &lt;now&gt; &amp; tell us.", en.Html); Assert.DoesNotContain("<now>", en.Html);          // escaped in the HTML part
        Assert.Contains("— Acme General Affairs", en.Html); Assert.EndsWith("</div>", en.Html.TrimEnd());                               // inside the message, not after it
        Assert.Contains("/tickets/", en.Text);                                                                                           // the product's own sentences and link are untouched

        await Ok(await w.Requester.SendAsync(HttpMethod.Patch, "/api/me/preferences", new { language = "id" }));
        var id = await ResolvedMail(w, "telah diselesaikan");
        Assert.StartsWith("[Acme GA] Permintaan", id.Subject); Assert.Contains("Mendesak? Hubungi ext. 1234.", id.Text); Assert.DoesNotContain("Urgent?", id.Text);

        await Ok(await Save(w.Admin, "Acme General Affairs", "[Acme GA]", "Urgent? Call ext. 1234.", null));                             // only one language written: it is used for both
        var fallback = await ResolvedMail(w, "telah diselesaikan");
        Assert.Contains("Urgent? Call ext. 1234.", fallback.Text);

        // account mail (not helpdesk) carries it too: an invitation
        var invitee = $"new.{Uniq()}@test.local";
        Assert.Equal(HttpStatusCode.Created, (await w.Admin.Post("/api/invitations", new { email = invitee, roleIds = new[] { await w.Seed.RoleId("user") } })).StatusCode);
        var inv = await w.Seed.Mail.WaitFor(invitee, "invited");
        Assert.StartsWith("[Acme GA] ", inv.Subject); Assert.Contains("— Acme General Affairs", inv.Text);
        Assert.NotEqual("", InMemoryEmailSender.TokenIn(inv));                                                                           // the one-time link still works
    }

    [Fact]
    public async Task Another_organisation_keeps_its_own_wording()
    {
        var w = await Build();
        await Ok(await Save(w.Admin, "Acme GA", "[Acme]", "Acme footer", null));
        var slug = "w" + Uniq(); var email = $"{slug}.admin@test.local";
        Assert.Equal(HttpStatusCode.Created, (await w.Admin.Post("/api/tenants", new { name = $"Org {slug}", slug, adminEmail = email })).StatusCode);
        var token = InMemoryEmailSender.TokenIn(await w.Seed.Mail.WaitFor(email, "invited"));
        Assert.Equal(HttpStatusCode.OK, (await TestBrowser.Create(w.F).Post("/api/invitations/accept", new { token, name = "Org Admin", password = StrongPw }, csrf: false)).StatusCode);
        var other = TestBrowser.Create(w.F); await other.LoginAsync(email, StrongPw); await other.Post("/api/auth/reauth", new { password = StrongPw });

        var theirs = await Ok(await other.Get("/api/settings/email-wording")); Assert.Equal(JsonValueKind.Null, theirs.GetProperty("senderName").ValueKind);
        var roles = await Ok(await other.Get("/api/roles")); var userRole = roles.EnumerateArray().First(r => r.GetProperty("key").GetString() == "user").GetProperty("id").GetGuid();
        var invitee = $"their.{Uniq()}@test.local";
        Assert.Equal(HttpStatusCode.Created, (await other.Post("/api/invitations", new { email = invitee, roleIds = new[] { userRole } })).StatusCode);
        var plain = await w.Seed.Mail.WaitFor(invitee, "invited");
        Assert.DoesNotContain("[Acme]", plain.Subject); Assert.DoesNotContain("Acme footer", plain.Text); Assert.EndsWith("— Annaba Digital", plain.Text);

        await Ok(await Save(other, "Beta Facilities", null, null, null));                                                                // and can set its own
        var invitee2 = $"their2.{Uniq()}@test.local";
        Assert.Equal(HttpStatusCode.Created, (await other.Post("/api/invitations", new { email = invitee2, roleIds = new[] { userRole } })).StatusCode);
        var own = await w.Seed.Mail.WaitFor(invitee2, "invited");
        Assert.EndsWith("— Beta Facilities", own.Text); Assert.DoesNotContain("[Acme]", own.Subject);
        Assert.Equal("Acme GA", (await Ok(await w.Admin.Get("/api/settings/email-wording"))).GetProperty("senderName").GetString());
    }

    [Fact]
    public async Task The_preview_shows_both_languages_and_changes_nothing()
    {
        var w = await Build();
        var p = await Ok(await w.Admin.Post("/api/settings/email-wording/preview", new { senderName = "Acme GA", subjectPrefix = "[Acme]", footerEn = "Call 1234.", footerId = "Hubungi 1234." }));
        var versions = p.GetProperty("versions").EnumerateArray().ToDictionary(v => v.GetProperty("lang").GetString()!, v => v);
        Assert.StartsWith("[Acme] Your request GA-2026-000123 was resolved", versions["en"].GetProperty("subject").GetString());
        Assert.Contains("Call 1234.\n\n— Acme GA", versions["en"].GetProperty("text").GetString());
        Assert.StartsWith("[Acme] Permintaan GA-2026-000123", versions["id"].GetProperty("subject").GetString());
        Assert.Contains("Hubungi 1234.\n\n— Acme GA", versions["id"].GetProperty("text").GetString());
        Assert.Equal(JsonValueKind.Null, (await Ok(await w.Admin.Get("/api/settings/email-wording"))).GetProperty("senderName").ValueKind);   // nothing was saved
        Assert.Equal(0, await w.Seed.Db(db => db.OutboxEmails.IgnoreQueryFilters().CountAsync(o => o.ToAddress == "sample@example.org")));    // and nothing was sent
    }

    [Fact]
    public void Applying_no_wording_leaves_a_message_as_it_is_and_a_message_without_the_usual_sign_off_still_gets_the_text()
    {
        var m = EmailTemplates.PasswordChanged("a@test.local", "Ann");
        Assert.Same(m, EmailWordingService.Apply(m, null, "en"));
        Assert.Same(m, EmailWordingService.Apply(m, new EmailWording(), "en"));
        var odd = new EmailMessage("a@test.local", "Subject", "Body without sign-off", "<p>Body</p>");
        var r = EmailWordingService.Apply(odd, new EmailWording { SenderName = "Acme", FooterEn = "Footer" }, "en");
        Assert.Equal("Body without sign-off\n\nFooter\n\n— Acme", r.Text); Assert.Contains("Footer", r.Html); Assert.Equal("Subject", r.Subject);
    }
}
