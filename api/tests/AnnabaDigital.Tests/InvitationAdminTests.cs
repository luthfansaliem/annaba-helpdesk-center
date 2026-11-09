using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Domain.Security;
using AnnabaDigital.Tests.Infra;
using Xunit;

namespace AnnabaDigital.Tests;

[Collection("db")]
public sealed class InvitationAdminTests(PostgresFixture fx)
{
    private readonly Seed _seed = new(fx.Factory, fx.Clock);
    private const string StrongPw = "Juniper-Canyon-Pebble-31";
    private static string Mail() => $"i{Guid.NewGuid():N}@test.local";
    private static async Task<JsonElement> J(HttpResponseMessage r) => await r.Body();

    private async Task<TestBrowser> Actor(string role)
    {
        var u = await _seed.UserAsync(role: role); var b = TestBrowser.Create(fx.Factory); await b.LoginAsync(u.Email, Seed.Password); return b;
    }
    private async Task<HttpResponseMessage> Invite(TestBrowser b, string email, params string[] roles)
    {
        var ids = new List<Guid>(); foreach (var r in roles.Length == 0 ? ["user"] : roles) ids.Add(await _seed.RoleId(r));
        return await b.Post("/api/invitations", new { email, roleIds = ids, message = "Welcome!" });
    }
    private async Task<JsonElement?> Find(TestBrowser b, string email, string? status = null)
    {
        var list = await J(await b.Get("/api/invitations" + (status is null ? "" : $"?status={status}")));
        foreach (var x in list.EnumerateArray()) if (x.GetProperty("email").GetString() == email) return x;
        return null;
    }
    private static string Tok(InMemoryEmailSender m, string to) => m.Sent.Where(x => x.To == to).Select(InMemoryEmailSender.TokenIn).Last();

    [Fact]
    public async Task Listing_needs_invitations_read_derives_expiry_and_filters_by_status()
    {
        var mgr = await Actor("user_admin"); var email = Mail(); await Invite(mgr, email);
        var expiredMail = Mail(); var revokedMail = Mail();
        var admin = await _seed.UserAsync(role: "user_admin");
        await _seed.Db(async db =>
        {
            db.Invitations.Add(new Invitation { Email = expiredMail, TokenHash = "h" + Guid.NewGuid(), InvitedBy = admin.Id, Status = InvitationStatus.Pending, CreatedAt = fx.Clock.UtcNow.AddDays(-9), ExpiresAt = fx.Clock.UtcNow.AddDays(-2) });
            db.Invitations.Add(new Invitation { Email = revokedMail, TokenHash = "h" + Guid.NewGuid(), InvitedBy = admin.Id, Status = InvitationStatus.Revoked, CreatedAt = fx.Clock.UtcNow, ExpiresAt = fx.Clock.UtcNow.AddDays(5) });
            return await db.SaveChangesAsync();
        });
        var item = (await Find(mgr, email))!.Value;
        Assert.Equal("pending", item.GetProperty("status").GetString()); Assert.Equal("User", item.GetProperty("roles")[0].GetProperty("name").GetString()); Assert.False(string.IsNullOrEmpty(item.GetProperty("invitedBy").GetString()));
        Assert.Equal("expired", (await Find(mgr, expiredMail))!.Value.GetProperty("status").GetString()); // "Pending" rows past their date read as expired
        Assert.NotNull(await Find(mgr, expiredMail, "expired")); Assert.Null(await Find(mgr, email, "expired")); Assert.NotNull(await Find(mgr, revokedMail, "revoked"));
        Assert.Equal((HttpStatusCode)422, (await mgr.Get("/api/invitations?status=banana")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await Actor("user")).Get("/api/invitations")).StatusCode);
        var text = (await J(await mgr.Get("/api/invitations"))).GetRawText(); Assert.DoesNotContain("tokenHash", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Creating_an_invitation_emails_a_hashed_expiring_link_using_the_configured_lifetime()
    {
        var b = await Actor("user_admin"); var email = Mail();
        await using var ttl = await _seed.SettingAsync("invitationExpiryDays", "3");
        var res = await Invite(b, email.ToUpperInvariant(), "user");
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var mail = await _seed.Mail.WaitFor(email, "invited"); var raw = InMemoryEmailSender.TokenIn(mail);
        Assert.Contains("expires in 3 days", mail.Text);
        var inv = await _seed.Db(db => db.Invitations.SingleAsync(i => i.Email == email));
        Assert.NotEqual(raw, inv.TokenHash); Assert.Equal(64, inv.TokenHash.Length); Assert.Equal(3, Math.Round((inv.ExpiresAt - inv.CreatedAt).TotalDays)); Assert.Equal("Welcome!", inv.Message);
        var lookup = await J(await TestBrowser.Create(fx.Factory).Get($"/api/invitations/lookup?token={raw}"));
        Assert.Equal("valid", lookup.GetProperty("state").GetString()); Assert.Equal(email, lookup.GetProperty("email").GetString());
    }

    [Fact]
    public async Task Invitations_cannot_carry_roles_above_the_inviters_privileges_and_conflicts_are_rejected()
    {
        var b = await Actor("user_admin"); var email = Mail();
        foreach (var role in new[] { "super_admin", "security_auditor" })
        {
            var r = await Invite(b, email, role);
            Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode); Assert.Equal("denied", await r.ErrorCode());
        }
        Assert.False(await _seed.Db(db => db.Invitations.AnyAsync(i => i.Email == email)));
        Assert.True(await _seed.Db(db => db.AuditEvents.AnyAsync(a => a.Action == "invitation.create" && a.Outcome == Outcome.Denied)));

        Assert.Equal(HttpStatusCode.Created, (await Invite(b, email)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Invite(b, email.ToUpperInvariant())).StatusCode); // pending invitation exists
        var existing = await _seed.UserAsync();
        Assert.Equal(HttpStatusCode.Conflict, (await Invite(b, existing.Email)).StatusCode);

        var bad = await b.Post("/api/invitations", new { email = "nope", roleIds = Array.Empty<Guid>(), message = new string('x', 600) });
        Assert.Equal((HttpStatusCode)422, bad.StatusCode);
        Assert.Equal((HttpStatusCode)422, (await b.Post("/api/invitations", new { email = Mail(), roleIds = new[] { Guid.NewGuid() } })).StatusCode);
    }

    [Fact]
    public async Task A_delegated_inviter_can_only_invite_into_roles_that_are_no_more_powerful_than_their_own()
    {
        await _seed.RoleAsync("inv_manager", Permissions.InvitationsManage, Permissions.InvitationsRead);
        var b = await Actor("inv_manager");
        Assert.Equal(HttpStatusCode.Created, (await Invite(b, Mail(), "user")).StatusCode); // "user" holds no permissions → always grantable
        Assert.Equal(HttpStatusCode.Forbidden, (await Invite(b, Mail(), "user_admin")).StatusCode);
    }

    [Fact]
    public async Task An_expired_invitation_can_be_replaced_by_a_new_one_for_the_same_email()
    {
        var b = await Actor("user_admin"); var admin = await _seed.UserAsync(role: "user_admin"); var email = Mail();
        await _seed.Db(async db => { db.Invitations.Add(new Invitation { Email = email, TokenHash = "old" + Guid.NewGuid(), InvitedBy = admin.Id, Status = InvitationStatus.Pending, CreatedAt = fx.Clock.UtcNow.AddDays(-9), ExpiresAt = fx.Clock.UtcNow.AddDays(-2) }); return await db.SaveChangesAsync(); });
        Assert.Equal(HttpStatusCode.Created, (await Invite(b, email)).StatusCode);
        Assert.Equal(1, await _seed.Db(db => db.Invitations.CountAsync(i => i.Email == email && i.Status == InvitationStatus.Pending)));
    }

    [Fact]
    public async Task Resending_rotates_the_token_extends_expiry_and_revives_revoked_invitations()
    {
        var b = await Actor("user_admin"); var email = Mail(); await Invite(b, email);
        var first = InMemoryEmailSender.TokenIn(await _seed.Mail.WaitFor(email, "invited"));
        var id = (await Find(b, email))!.Value.GetProperty("id").GetGuid();
        fx.Clock.Advance(TimeSpan.FromDays(2));
        b = await Actor("user_admin"); // the old admin session idled out (30 min) — sign in again
        Assert.Equal(HttpStatusCode.OK, (await b.Post($"/api/invitations/{id}/resend")).StatusCode);
        await Task.Delay(300); var second = Tok(_seed.Mail, email);
        Assert.NotEqual(first, second);
        Assert.Equal("invalid", (await J(await TestBrowser.Create(fx.Factory).Get($"/api/invitations/lookup?token={first}"))).GetProperty("state").GetString()); // old link is gone entirely
        var inv = await _seed.Db(db => db.Invitations.SingleAsync(i => i.Id == id));
        Assert.True(inv.ExpiresAt > fx.Clock.UtcNow.AddDays(6)); // a fresh 7-day window

        Assert.Equal(HttpStatusCode.NoContent, (await b.Post($"/api/invitations/{id}/revoke")).StatusCode);
        Assert.Equal("revoked", (await J(await TestBrowser.Create(fx.Factory).Get($"/api/invitations/lookup?token={second}"))).GetProperty("state").GetString());
        Assert.Equal(HttpStatusCode.OK, (await b.Post($"/api/invitations/{id}/resend")).StatusCode); // revoked → pending again
        await Task.Delay(300); var third = Tok(_seed.Mail, email);
        Assert.Equal(HttpStatusCode.OK, (await TestBrowser.Create(fx.Factory).Post("/api/invitations/accept", new { token = third, name = "Revived Person", password = StrongPw }, csrf: false)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await b.Post($"/api/invitations/{id}/resend")).StatusCode); // accepted: nothing to resend
        Assert.Equal(HttpStatusCode.NotFound, (await b.Post($"/api/invitations/{Guid.NewGuid()}/resend")).StatusCode);
    }

    [Fact]
    public async Task Resend_is_refused_when_it_would_create_a_second_pending_invitation_or_exceed_the_senders_privileges()
    {
        var b = await Actor("user_admin"); var email = Mail(); await Invite(b, email);
        var id = (await Find(b, email))!.Value.GetProperty("id").GetGuid();
        await b.Post($"/api/invitations/{id}/revoke");
        Assert.Equal(HttpStatusCode.Created, (await Invite(b, email)).StatusCode); // a newer pending one
        Assert.Equal(HttpStatusCode.Conflict, (await b.Post($"/api/invitations/{id}/resend")).StatusCode);

        // an invitation created by a super admin for a powerful role cannot be resent by a weaker admin
        var boss = await Actor("super_admin"); var target = Mail(); await Invite(boss, target, "super_admin");
        var bid = (await Find(boss, target))!.Value.GetProperty("id").GetGuid();
        var r = await b.Post($"/api/invitations/{bid}/resend");
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task Revoking_only_works_on_pending_invitations_and_invalidates_the_link()
    {
        var b = await Actor("user_admin"); var email = Mail(); await Invite(b, email);
        var raw = InMemoryEmailSender.TokenIn(await _seed.Mail.WaitFor(email, "invited"));
        var id = (await Find(b, email))!.Value.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await b.Post($"/api/invitations/{id}/revoke")).StatusCode);
        Assert.Equal(HttpStatusCode.Gone, (await TestBrowser.Create(fx.Factory).Post("/api/invitations/accept", new { token = raw, name = "Too Late", password = StrongPw }, csrf: false)).StatusCode);
        var again = await b.Post($"/api/invitations/{id}/revoke"); Assert.Equal(HttpStatusCode.Conflict, again.StatusCode); Assert.Equal("invariant", await again.ErrorCode());
        Assert.Equal(HttpStatusCode.NotFound, (await b.Post($"/api/invitations/{Guid.NewGuid()}/revoke")).StatusCode);
    }

    [Fact]
    public async Task Revoke_and_accept_racing_each_other_have_exactly_one_consistent_winner()
    {
        for (var round = 0; round < 3; round++)
        {
            var b = await Actor("user_admin"); var email = Mail(); await Invite(b, email);
            var raw = InMemoryEmailSender.TokenIn(await _seed.Mail.WaitFor(email, "invited"));
            var id = (await Find(b, email))!.Value.GetProperty("id").GetGuid();
            var revoke = b.Post($"/api/invitations/{id}/revoke");
            var accept = TestBrowser.Create(fx.Factory).Post("/api/invitations/accept", new { token = raw, name = "Racing Person", password = StrongPw + round }, csrf: false);
            var (rv, ac) = (await revoke, await accept);
            var final = (await _seed.Db(db => db.Invitations.SingleAsync(i => i.Id == id))).Status;
            Assert.True((rv.StatusCode == HttpStatusCode.NoContent && ac.StatusCode == HttpStatusCode.Gone && final == InvitationStatus.Revoked) ||
                        (ac.StatusCode == HttpStatusCode.OK && rv.StatusCode == HttpStatusCode.Conflict && final == InvitationStatus.Accepted), $"revoke={rv.StatusCode} accept={ac.StatusCode} final={final}");
            Assert.Equal(final == InvitationStatus.Accepted ? 1 : 0, await _seed.Db(db => db.Users.CountAsync(u => u.Email == email && u.Status == UserStatus.Active)));
        }
    }
}
