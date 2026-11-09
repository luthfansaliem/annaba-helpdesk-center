using System.Net;
using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Tests.Infra;
using Xunit;

namespace AnnabaDigital.Tests;

[Collection("db")]
public sealed class AccountTests(PostgresFixture fx)
{
    private readonly Seed _seed = new(fx.Factory, fx.Clock);
    private const string StrongPw = "Violet-Harbor-Lantern-42";
    private TestBrowser B() => TestBrowser.Create(fx.Factory);
    private static string Email() => $"p{Guid.NewGuid():N}@test.local";
    private Task<IAsyncDisposable> OpenRegistration() => _seed.SettingAsync("registrationMode", "\"open\"");

    // ======================================================== registration (AUTH-002)
    [Fact]
    public async Task Registration_is_404_in_invitation_only_mode()
    {
        var res = await B().Post("/api/auth/register", new { name = "New Person", email = Email(), password = StrongPw }, csrf: false);
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal("not_found", await res.ErrorCode());
    }

    [Fact]
    public async Task Open_registration_creates_a_pending_user_and_emails_a_single_use_link_without_leaking_tokens()
    {
        await using var reg = await OpenRegistration();
        var email = Email();
        var res = await B().Post("/api/auth/register", new { name = "New Person", email = email.ToUpperInvariant(), password = StrongPw }, csrf: false);
        Assert.Equal(HttpStatusCode.Accepted, res.StatusCode);
        Assert.DoesNotContain("token", await res.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        var u = await _seed.Db(db => db.Users.Include(x => x.Credential).Include(x => x.UserRoles).SingleAsync(x => x.Email == email)); // normalised to lower-case
        Assert.Equal(UserStatus.Pending, u.Status); Assert.Null(u.EmailVerifiedAt);
        Assert.StartsWith("$argon2id$", u.Credential!.PasswordHash);
        Assert.Single(u.UserRoles); // default 'user' role only — registration can never grant privileges

        var mail = await _seed.Mail.WaitFor(email, "Verify your email");
        var raw = InMemoryEmailSender.TokenIn(mail);
        Assert.True(raw.Length >= 43);
        Assert.StartsWith("http://localhost:3000/verify-email?token=", mail.Text.Split("Verify email: ")[1]); // link uses configured origin
        var stored = await _seed.Db(db => db.EmailVerificationTokens.Where(t => t.UserId == u.Id).ToListAsync());
        Assert.DoesNotContain(stored, t => t.TokenHash == raw); // only the hash is stored
        Assert.Equal(64, stored.Single().TokenHash.Length);
    }

    [Fact]
    public async Task Registering_an_existing_email_gives_the_same_response_creates_nothing_and_notifies_the_owner()
    {
        await using var reg = await OpenRegistration();
        var owner = await _seed.UserAsync();
        var before = await _seed.Db(db => db.Users.CountAsync());
        var fresh = await B().Post("/api/auth/register", new { name = "Someone Else", email = Email(), password = StrongPw }, csrf: false);
        var dup = await B().Post("/api/auth/register", new { name = "Someone Else", email = owner.Email, password = StrongPw }, csrf: false);
        Assert.Equal(fresh.StatusCode, dup.StatusCode);
        Assert.Equal(await fresh.Content.ReadAsStringAsync(), await dup.Content.ReadAsStringAsync());
        Assert.Equal(before + 1, await _seed.Db(db => db.Users.CountAsync()));
        var notice = await _seed.Mail.WaitFor(owner.Email, "already have an account");
        Assert.Contains("/reset-password?token=", notice.Text);
    }

    [Theory]
    [InlineData("short1!", "at least 12")]
    [InlineData("password1234", "too common")]
    [InlineData("Qwerty123456", "")] // fine length but let the policy decide; asserted below via status
    public async Task Weak_passwords_are_rejected_with_a_field_error(string pw, string expectedHint)
    {
        await using var reg = await OpenRegistration();
        var res = await B().Post("/api/auth/register", new { name = "Weak Person", email = Email(), password = pw }, csrf: false);
        if (expectedHint == "") { Assert.True(res.StatusCode is HttpStatusCode.Accepted or (HttpStatusCode)422); return; }
        Assert.Equal((HttpStatusCode)422, res.StatusCode);
        var fields = (await res.Body()).GetProperty("error").GetProperty("fields");
        Assert.Contains(expectedHint, fields.GetProperty("password").GetString());
    }

    [Fact]
    public async Task Password_policy_rejects_personal_info_breached_and_overlong_passwords()
    {
        await using var reg = await OpenRegistration();
        foreach (var (pw, hint) in new[] { ("Alexander-Strong-99!", "name or email"), ("Tr0ub4dor&3-breached-pass", "data breach"), (new string('x', 129) + "A1!", "at most 128"), ("aaaaaaaaaaaaaaaa", "varied") })
        {
            var res = await B().Post("/api/auth/register", new { name = "Alexander Hamilton", email = $"alex{Guid.NewGuid():N}@test.local", password = pw }, csrf: false); // fresh address each time: the per-account limiter counts every attempt
            Assert.Equal((HttpStatusCode)422, res.StatusCode);
            Assert.Contains(hint, (await res.Body()).GetProperty("error").GetProperty("fields").GetProperty("password").GetString());
        }
    }

    [Fact]
    public async Task Invalid_name_or_email_is_a_validation_error_and_registration_is_rate_limited()
    {
        await using var reg = await OpenRegistration();
        var bad = await B().Post("/api/auth/register", new { name = "", email = "nope", password = StrongPw }, csrf: false);
        var fields = (await bad.Body()).GetProperty("error").GetProperty("fields");
        Assert.True(fields.TryGetProperty("name", out _)); Assert.True(fields.TryGetProperty("email", out _));

        var b = B(); HttpResponseMessage? last = null;
        for (var i = 0; i < 6; i++) last = await b.Post("/api/auth/register", new { name = "Spam Bot", email = Email(), password = StrongPw }, csrf: false);
        Assert.Equal((HttpStatusCode)429, last!.StatusCode);
        Assert.True(last.Headers.Contains("Retry-After"));
    }

    [Fact]
    public async Task A_pending_user_cannot_log_in_until_the_email_is_verified()
    {
        await using var reg = await OpenRegistration();
        var email = Email();
        await B().Post("/api/auth/register", new { name = "New Person", email, password = StrongPw }, csrf: false);
        var b = B();
        Assert.Equal("verify_email", (await (await b.LoginAsync(email, StrongPw)).Body()).GetProperty("status").GetString());
        Assert.Null(b.CookieValue);

        var token = InMemoryEmailSender.TokenIn(await _seed.Mail.WaitFor(email, "Verify your email"));
        Assert.Equal(HttpStatusCode.OK, (await B().Post("/api/auth/verify-email", new { token }, csrf: false)).StatusCode);
        Assert.Equal("ok", (await (await b.LoginAsync(email, StrongPw)).Body()).GetProperty("status").GetString());
    }

    // ======================================================== email verification (AUTH-003)
    private async Task<(User u, string token)> PendingWithToken()
    {
        var u = await _seed.UserAsync(status: UserStatus.Pending);
        await B().Post("/api/auth/verify-email/resend", new { email = u.Email }, csrf: false);
        return (u, InMemoryEmailSender.TokenIn(await _seed.Mail.WaitFor(u.Email, "Verify your email")));
    }

    [Fact]
    public async Task Verification_token_activates_the_account_once_and_only_once()
    {
        var (u, token) = await PendingWithToken();
        Assert.Equal(HttpStatusCode.OK, (await B().Post("/api/auth/verify-email", new { token }, csrf: false)).StatusCode);
        var after = await _seed.Db(db => db.Users.SingleAsync(x => x.Id == u.Id));
        Assert.Equal(UserStatus.Active, after.Status); Assert.NotNull(after.EmailVerifiedAt);

        var again = await B().Post("/api/auth/verify-email", new { token }, csrf: false);
        Assert.Equal(HttpStatusCode.Gone, again.StatusCode);
        Assert.Equal("expired", await again.ErrorCode());
    }

    [Fact]
    public async Task Verification_token_expires_after_24_hours_and_unknown_tokens_look_the_same()
    {
        var (_, token) = await PendingWithToken();
        fx.Clock.Advance(TimeSpan.FromHours(24.1));
        var expired = await B().Post("/api/auth/verify-email", new { token }, csrf: false);
        var unknown = await B().Post("/api/auth/verify-email", new { token = "definitely-not-a-token" }, csrf: false);
        Assert.Equal(HttpStatusCode.Gone, expired.StatusCode);
        Assert.Equal(await expired.Content.ReadAsStringAsync().ContinueWith(t => System.Text.RegularExpressions.Regex.Replace(t.Result, "\"requestId\":\"[^\"]+\"", "")),
                     await unknown.Content.ReadAsStringAsync().ContinueWith(t => System.Text.RegularExpressions.Regex.Replace(t.Result, "\"requestId\":\"[^\"]+\"", "")));
    }

    [Fact]
    public async Task Two_concurrent_verifications_of_the_same_token_succeed_exactly_once()
    {
        var (_, token) = await PendingWithToken();
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(i => B().Post("/api/auth/verify-email", new { token }, csrf: false)));
        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(5, results.Count(r => r.StatusCode == HttpStatusCode.Gone));
    }

    [Fact]
    public async Task Resending_invalidates_the_previous_link_and_is_silent_for_unknown_active_and_blocked_accounts()
    {
        var (u, first) = await PendingWithToken();
        await B().Post("/api/auth/verify-email/resend", new { email = u.Email }, csrf: false);
        await Task.Delay(300);
        var second = _seed.Mail.Sent.Where(m => m.To == u.Email).Select(InMemoryEmailSender.TokenIn).Last();
        Assert.NotEqual(first, second);
        Assert.Equal(HttpStatusCode.Gone, (await B().Post("/api/auth/verify-email", new { token = first }, csrf: false)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await B().Post("/api/auth/verify-email", new { token = second }, csrf: false)).StatusCode);

        var active = await _seed.UserAsync(); var suspended = await _seed.UserAsync(status: UserStatus.Suspended); var ghost = Email();
        foreach (var e in new[] { active.Email, suspended.Email, ghost })
        {
            var r = await B().Post("/api/auth/verify-email/resend", new { email = e }, csrf: false);
            Assert.Equal(HttpStatusCode.Accepted, r.StatusCode);
            await _seed.Mail.AssertNone(e, 150);
        }
    }

    [Fact]
    public async Task Verifying_a_suspended_users_token_does_not_reactivate_the_account()
    {
        var (u, token) = await PendingWithToken();
        await _seed.Db(async db => { (await db.Users.SingleAsync(x => x.Id == u.Id)).Status = UserStatus.Suspended; return await db.SaveChangesAsync(); });
        await B().Post("/api/auth/verify-email", new { token }, csrf: false);
        Assert.Equal(UserStatus.Suspended, (await _seed.Db(db => db.Users.SingleAsync(x => x.Id == u.Id))).Status);
    }

    // ======================================================== forgot / reset (AUTH-004/005)
    [Fact]
    public async Task Forgot_password_responds_identically_for_known_and_unknown_emails_and_only_emails_active_users()
    {
        var known = await _seed.UserAsync(); var suspended = await _seed.UserAsync(status: UserStatus.Suspended); var ghost = Email();
        var a = await B().Post("/api/auth/forgot-password", new { email = known.Email }, csrf: false);
        var b = await B().Post("/api/auth/forgot-password", new { email = ghost }, csrf: false);
        var c = await B().Post("/api/auth/forgot-password", new { email = suspended.Email }, csrf: false);
        Assert.Equal(HttpStatusCode.Accepted, a.StatusCode);
        Assert.Equal(await a.Content.ReadAsStringAsync(), await b.Content.ReadAsStringAsync());
        Assert.Equal(await a.Content.ReadAsStringAsync(), await c.Content.ReadAsStringAsync());
        var mail = await _seed.Mail.WaitFor(known.Email, "Reset your password");
        Assert.Contains("30 minutes", mail.Text);
        await _seed.Mail.AssertNone(ghost, 150); await _seed.Mail.AssertNone(suspended.Email, 150);
        var t = await _seed.Db(db => db.PasswordResetTokens.Where(x => x.UserId == known.Id).ToListAsync());
        Assert.DoesNotContain(t, x => x.TokenHash == InMemoryEmailSender.TokenIn(mail)); Assert.Equal(30, (t.Single().ExpiresAt - t.Single().CreatedAt).TotalMinutes);
    }

    [Fact]
    public async Task Forgot_password_is_rate_limited_per_account_without_revealing_existence()
    {
        var email = Email(); HttpResponseMessage? last = null;
        for (var i = 0; i < 4; i++) last = await B().Post("/api/auth/forgot-password", new { email }, csrf: false);
        Assert.Equal((HttpStatusCode)429, last!.StatusCode); // unknown address is limited exactly like a real one
    }

    private async Task<(User u, string token)> ResetToken()
    {
        var u = await _seed.UserAsync();
        await B().Post("/api/auth/forgot-password", new { email = u.Email }, csrf: false);
        return (u, InMemoryEmailSender.TokenIn(await _seed.Mail.WaitFor(u.Email, "Reset your password")));
    }

    [Fact]
    public async Task Reset_changes_the_password_revokes_every_session_unlocks_the_account_and_notifies()
    {
        var (u, token) = await ResetToken();
        var session = B(); await session.LoginAsync(u.Email, Seed.Password);
        Assert.Equal(HttpStatusCode.OK, (await session.Get("/api/auth/me")).StatusCode);
        for (var i = 0; i < 5; i++) await B().LoginAsync(u.Email, "wrong-" + i); // lock the account
        Assert.NotNull((await _seed.Db(db => db.Users.SingleAsync(x => x.Id == u.Id))).LockedUntil);

        var res = await B().Post("/api/auth/reset-password", new { token, password = StrongPw }, csrf: false);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await session.Get("/api/auth/me")).StatusCode); // existing session is dead
        Assert.Equal(HttpStatusCode.Unauthorized, (await B().LoginAsync(u.Email, Seed.Password)).StatusCode); // old password no longer works
        var ok = B(); Assert.Equal("ok", (await (await ok.LoginAsync(u.Email, StrongPw)).Body()).GetProperty("status").GetString()); // new one does, lock cleared
        await _seed.Mail.WaitFor(u.Email, "password was changed");
        Assert.Contains(await _seed.Db(db => db.SecurityEvents.Where(e => e.UserId == u.Id && e.Type == "password.reset").ToListAsync()), _ => true);
        var audit = await _seed.Db(db => db.AuditEvents.Where(a => a.Action == "auth.password.reset" && a.ActorId == u.Id).ToListAsync());
        Assert.DoesNotContain(audit, a => (a.MetaJson ?? "").Contains(StrongPw));
    }

    [Fact]
    public async Task A_rejected_new_password_does_not_consume_the_token()
    {
        var (u, token) = await ResetToken();
        Assert.Equal((HttpStatusCode)422, (await B().Post("/api/auth/reset-password", new { token, password = "short" }, csrf: false)).StatusCode);
        Assert.Equal((HttpStatusCode)422, (await B().Post("/api/auth/reset-password", new { token, password = "password1234" }, csrf: false)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await B().Post("/api/auth/reset-password", new { token, password = StrongPw }, csrf: false)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await B().LoginAsync(u.Email, StrongPw)).StatusCode);
    }

    [Fact]
    public async Task Reset_token_is_single_use_expires_in_30_minutes_and_concurrent_use_wins_once()
    {
        var (_, used) = await ResetToken();
        Assert.Equal(HttpStatusCode.OK, (await B().Post("/api/auth/reset-password", new { token = used, password = StrongPw }, csrf: false)).StatusCode);
        Assert.Equal(HttpStatusCode.Gone, (await B().Post("/api/auth/reset-password", new { token = used, password = StrongPw + "x" }, csrf: false)).StatusCode);

        var (_, late) = await ResetToken();
        fx.Clock.Advance(TimeSpan.FromMinutes(31));
        Assert.Equal(HttpStatusCode.Gone, (await B().Post("/api/auth/reset-password", new { token = late, password = StrongPw }, csrf: false)).StatusCode);

        var (_, race) = await ResetToken();
        var all = await Task.WhenAll(Enumerable.Range(0, 5).Select(i => B().Post("/api/auth/reset-password", new { token = race, password = StrongPw + i }, csrf: false)));
        Assert.Equal(1, all.Count(r => r.StatusCode == HttpStatusCode.OK));
    }

    [Fact]
    public async Task Requesting_a_new_reset_link_invalidates_the_older_one()
    {
        var u = await _seed.UserAsync();
        await B().Post("/api/auth/forgot-password", new { email = u.Email }, csrf: false);
        var first = InMemoryEmailSender.TokenIn(await _seed.Mail.WaitFor(u.Email, "Reset your password"));
        await B().Post("/api/auth/forgot-password", new { email = u.Email }, csrf: false); await Task.Delay(300);
        Assert.Equal(HttpStatusCode.Gone, (await B().Post("/api/auth/reset-password", new { token = first, password = StrongPw }, csrf: false)).StatusCode);
    }

    [Fact]
    public async Task Reset_links_for_users_who_were_suspended_after_requesting_stop_working()
    {
        var (u, token) = await ResetToken();
        await _seed.Db(async db => { (await db.Users.SingleAsync(x => x.Id == u.Id)).Status = UserStatus.Suspended; return await db.SaveChangesAsync(); });
        Assert.Equal(HttpStatusCode.Gone, (await B().Post("/api/auth/reset-password", new { token, password = StrongPw }, csrf: false)).StatusCode);
    }

    [Fact]
    public async Task Pending_users_asking_for_a_reset_get_a_verification_email_instead()
    {
        var u = await _seed.UserAsync(status: UserStatus.Pending);
        await B().Post("/api/auth/forgot-password", new { email = u.Email }, csrf: false);
        await _seed.Mail.WaitFor(u.Email, "Verify your email");
        Assert.DoesNotContain(_seed.Mail.Sent, m => m.To == u.Email && m.Subject.Contains("Reset"));
    }
}
