using System.Net;
using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Email;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Tests.Infra;
using Xunit;

namespace AnnabaDigital.Tests;

[Collection("db")]
public sealed class EmailLanguageTests(PostgresFixture fx)
{
    private readonly Seed _seed = new(fx.Factory, fx.Clock);

    private async Task SetLanguage(Guid userId, string language) => await _seed.Db(async db =>
    {
        var p = await db.Profiles.FirstOrDefaultAsync(x => x.UserId == userId);
        if (p is null) db.Profiles.Add(new UserProfile { UserId = userId, Language = language }); else p.Language = language;
        await db.SaveChangesAsync(); return 0;
    });

    [Fact]
    public async Task Mail_to_a_person_is_written_in_their_own_profile_language()
    {
        var indo = await _seed.UserAsync(); await SetLanguage(indo.Id, "id");
        var english = await _seed.UserAsync(); await SetLanguage(english.Id, "en");
        var plain = await _seed.UserAsync(); // no profile row at all

        foreach (var u in new[] { indo, english, plain })
            Assert.Equal(HttpStatusCode.Accepted, (await TestBrowser.Create(fx.Factory).Post("/api/auth/forgot-password", new { email = u.Email }, csrf: false)).StatusCode);

        var id = await _seed.Mail.WaitFor(indo.Email, "Atur ulang password Anda");
        Assert.StartsWith("Halo", id.Text); Assert.Contains("kedaluwarsa dalam 30 menit", id.Text); Assert.Contains("/reset-password?token=", id.Text);
        Assert.Contains("Atau salin tautan ini:", id.Html); Assert.Contains("Pilih password baru", id.Html); // the button label too
        var en = await _seed.Mail.WaitFor(english.Email, "Reset your password"); Assert.StartsWith("Hi ", en.Text); Assert.Contains("expires in 30 minutes", en.Text);
        await _seed.Mail.WaitFor(plain.Email, "Reset your password"); // no language known: English
    }

    [Fact]
    public async Task An_invitation_is_written_in_the_language_of_whoever_sends_it()
    {
        async Task<TestBrowser> Admin(string language)
        {
            var a = await _seed.UserAsync(role: "super_admin"); await SetLanguage(a.Id, language);
            var b = TestBrowser.Create(fx.Factory); await b.LoginAsync(a.Email, Seed.Password); await b.Post("/api/auth/reauth", new { password = Seed.Password }); return b;
        }
        async Task<string> Invite(TestBrowser b)
        {
            var email = $"invitee{Guid.NewGuid():N}@test.local";
            var res = await b.Post("/api/invitations", new { email, roleIds = new[] { await _seed.RoleId("user") } });
            Assert.Equal(HttpStatusCode.Created, res.StatusCode); return email;
        }

        var indo = await Invite(await Admin("id"));
        var mail = await _seed.Mail.WaitFor(indo, "Anda diundang"); Assert.StartsWith("Halo,", mail.Text); Assert.Contains("Terima undangan", mail.Text);
        var en = await Invite(await Admin("en"));
        var mail2 = await _seed.Mail.WaitFor(en, "You're invited"); Assert.StartsWith("Hi there,", mail2.Text);
    }

    [Fact]
    public void Every_template_has_an_Indonesian_version_and_the_English_wording_is_unchanged()
    {
        var all = new[]
        {
            EmailTemplates.VerifyEmail("a@b.c", "A", "https://x/y"), EmailTemplates.AlreadyRegistered("a@b.c", "A", "https://x/y"), EmailTemplates.ResetPassword("a@b.c", "A", "https://x/y"),
            EmailTemplates.PasswordChanged("a@b.c", "A"), EmailTemplates.MfaEnabled("a@b.c", "A"), EmailTemplates.MfaDisabled("a@b.c", "A"), EmailTemplates.RecoveryCodeUsed("a@b.c", "A", 3),
            EmailTemplates.ConfirmEmailChange("a@b.c", "A", "https://x/y"), EmailTemplates.EmailChangeRequested("a@b.c", "A", "n@b.c"), EmailTemplates.EmailChanged("a@b.c", "A", "n@b.c"),
            EmailTemplates.AccessRequestSubmitted("a@b.c", "A", "R", "Role", "Because", "https://x/y"), EmailTemplates.AccessRequestDecided("a@b.c", "A", true, "Role", "ok", "https://x/y"),
            EmailTemplates.AccessReviewStarted("a@b.c", "A", "Q4", DateTimeOffset.UtcNow, "https://x/y"), EmailTemplates.ClosureRequested("a@b.c", "A", "P", "https://x/y"),
            EmailTemplates.ClosureApproved("a@b.c", "A", DateTimeOffset.UtcNow.AddDays(14), "https://x/y"), EmailTemplates.ClosureRejected("a@b.c", "A", null), EmailTemplates.AccountClosed("a@b.c", "A"),
            EmailTemplates.NewSignIn("a@b.c", "A", "Mac", "Chrome", "1.2.3.4", "now", "https://x/y"), EmailTemplates.MfaResetByAdmin("a@b.c", "A", "Admin"),
            EmailTemplates.PasswordResetRequiredByAdmin("a@b.c", "A", "Admin", "https://x/y"), EmailTemplates.Invitation("a@b.c", "Admin", "https://x/y", 7),
        };
        foreach (var m in all)
        {
            Assert.NotNull(m.Localize); var id = m.Localize!("id");
            Assert.NotNull(id); Assert.NotEqual(m.Subject, id!.Subject); Assert.NotEqual(m.Text, id.Text); Assert.Contains("Halo", id.Text); Assert.Contains("Annaba Digital", id.Text);
            Assert.Null(m.Localize("en")); // English is the message itself
            Assert.StartsWith("Hi ", m.Text);
            if (m.Text.Contains("https://x/y")) Assert.Contains("https://x/y", id.Text); // links survive
        }
    }
}
