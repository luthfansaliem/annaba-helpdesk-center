using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Mfa;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Tests.Infra;
using Xunit;

namespace AnnabaDigital.Tests;

[Collection("db")]
public sealed class MfaTests(PostgresFixture fx)
{
    private readonly Seed _seed = new(fx.Factory, fx.Clock);
    private TestBrowser B() => TestBrowser.Create(fx.Factory);
    private string CodeFor(byte[] secret, int offset = 0) => Totp.Compute(secret, Totp.StepAt(fx.Clock.UtcNow) + offset);
    private void NextStep() => fx.Clock.Advance(TimeSpan.FromSeconds(31));

    private sealed record Enrolled(TestBrowser Browser, User User, byte[] Secret, string[] RecoveryCodes);

    private async Task<TestBrowser> SignedIn(User u) { var b = B(); await b.LoginAsync(u.Email, Seed.Password); return b; }

    /// <summary>Signs in, re-authenticates, enrols and confirms through the real HTTP API.</summary>
    private async Task<Enrolled> Enroll(string role = "user")
    {
        var u = await _seed.UserAsync(role: role);
        var b = await SignedIn(u);
        Assert.Equal(HttpStatusCode.NoContent, (await b.Post("/api/auth/reauth", new { password = Seed.Password })).StatusCode);
        var start = await (await b.Post("/api/me/mfa/enroll")).Body();
        var secret = Base32.Decode(start.GetProperty("secret").GetString()!);
        var confirm = await b.Post("/api/me/mfa/confirm", new { code = CodeFor(secret) });
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
        var codes = (await confirm.Body()).GetProperty("recoveryCodes").EnumerateArray().Select(c => c.GetString()!).ToArray();
        NextStep(); // the enrolment code's step is spent; the next sign-in needs a fresh one
        return new Enrolled(b, u, secret, codes);
    }

    /// <summary>Fresh browser that has passed the password step and now holds a pre-auth (MFA-pending) session.</summary>
    private async Task<TestBrowser> PendingLogin(Enrolled e)
    {
        var b = B();
        var res = await b.LoginAsync(e.User.Email, Seed.Password);
        Assert.Equal("mfa_required", (await res.Body()).GetProperty("status").GetString());
        return b;
    }

    private async Task<string?> Csrf(TestBrowser b) => b.CsrfToken = (await (await b.Get("/api/auth/csrf")).Body()).GetProperty("token").GetString();

    // ============================================================ enrolment
    [Fact]
    public async Task Enrolment_needs_a_recent_reauthentication_and_the_seed_is_stored_only_encrypted()
    {
        var u = await _seed.UserAsync(); var b = await SignedIn(u);
        var denied = await b.Post("/api/me/mfa/enroll");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode); Assert.Equal("reauth_required", await denied.ErrorCode());

        await b.Post("/api/auth/reauth", new { password = Seed.Password });
        var start = await (await b.Post("/api/me/mfa/enroll")).Body();
        var b32 = start.GetProperty("secret").GetString()!;
        Assert.StartsWith("otpauth://totp/Annaba%20Digital:", start.GetProperty("otpAuthUri").GetString()); Assert.Contains(b32, start.GetProperty("otpAuthUri").GetString());

        var f = await _seed.Db(db => db.MfaFactors.SingleAsync(x => x.UserId == u.Id));
        var raw = Base32.Decode(b32);
        Assert.Null(f.ConfirmedAt); Assert.Equal(1, f.KeyVersion); Assert.Equal(12, f.SecretNonce.Length);
        Assert.False(f.SecretCiphertext.AsSpan().IndexOf(raw) >= 0); Assert.DoesNotContain(b32, Convert.ToBase64String(f.SecretCiphertext));

        var status = await (await b.Get("/api/me/mfa")).Body();
        Assert.False(status.GetProperty("enrolled").GetBoolean());
        Assert.DoesNotContain(b32, status.GetRawText()); // the seed is never returned again
    }

    [Fact]
    public async Task Confirming_with_a_wrong_code_does_not_enable_mfa_and_the_right_code_returns_ten_hashed_recovery_codes()
    {
        var u = await _seed.UserAsync(); var b = await SignedIn(u);
        await b.Post("/api/auth/reauth", new { password = Seed.Password });
        var secret = Base32.Decode((await (await b.Post("/api/me/mfa/enroll")).Body()).GetProperty("secret").GetString()!);

        var bad = await b.Post("/api/me/mfa/confirm", new { code = "000000" });
        Assert.Equal(HttpStatusCode.Unauthorized, bad.StatusCode); Assert.Equal("invalid_code", await bad.ErrorCode());
        Assert.False((await (await b.Get("/api/me/mfa")).Body()).GetProperty("enrolled").GetBoolean());

        var ok = await b.Post("/api/me/mfa/confirm", new { code = CodeFor(secret) });
        var codes = (await ok.Body()).GetProperty("recoveryCodes").EnumerateArray().Select(c => c.GetString()!).ToArray();
        Assert.Equal(10, codes.Length); Assert.Equal(10, codes.Distinct().Count()); Assert.All(codes, c => Assert.True(RecoveryCodes.LooksLikeCode(c)));

        var rows = await _seed.Db(db => db.MfaRecoveryCodes.Where(c => c.UserId == u.Id).ToListAsync());
        Assert.Equal(10, rows.Count); Assert.All(rows, r => { Assert.Equal(64, r.CodeHash.Length); Assert.DoesNotContain(codes, c => RecoveryCodes.Normalize(c) == r.CodeHash); Assert.Null(r.UsedAt); });
        var st = await (await b.Get("/api/me/mfa")).Body();
        Assert.True(st.GetProperty("enrolled").GetBoolean()); Assert.Equal(10, st.GetProperty("recoveryCodesRemaining").GetInt32());
        await _seed.Mail.WaitFor(u.Email, "Two-step verification turned on");
        Assert.NotEmpty(await _seed.Db(db => db.SecurityEvents.Where(e => e.UserId == u.Id && e.Type == "mfa.enrolled").ToListAsync()));

        var again = await b.Post("/api/me/mfa/enroll"); // already enabled
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task Restarting_enrolment_replaces_the_unconfirmed_seed()
    {
        var u = await _seed.UserAsync(); var b = await SignedIn(u);
        await b.Post("/api/auth/reauth", new { password = Seed.Password });
        var first = Base32.Decode((await (await b.Post("/api/me/mfa/enroll")).Body()).GetProperty("secret").GetString()!);
        var second = Base32.Decode((await (await b.Post("/api/me/mfa/enroll")).Body()).GetProperty("secret").GetString()!);
        Assert.NotEqual(first, second);
        Assert.Equal(1, await _seed.Db(db => db.MfaFactors.CountAsync(f => f.UserId == u.Id)));
        Assert.Equal(HttpStatusCode.Unauthorized, (await b.Post("/api/me/mfa/confirm", new { code = CodeFor(first) })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await b.Post("/api/me/mfa/confirm", new { code = CodeFor(second) })).StatusCode);
    }

    [Fact]
    public async Task Confirm_without_starting_enrolment_is_rejected_and_attempts_are_rate_limited()
    {
        var u = await _seed.UserAsync(); var b = await SignedIn(u);
        Assert.Equal(HttpStatusCode.Conflict, (await b.Post("/api/me/mfa/confirm", new { code = "123456" })).StatusCode);
        await b.Post("/api/auth/reauth", new { password = Seed.Password }); await b.Post("/api/me/mfa/enroll");
        HttpResponseMessage? last = null;
        for (var i = 0; i < 6; i++) last = await b.Post("/api/me/mfa/confirm", new { code = "00000" + i });
        Assert.Equal((HttpStatusCode)429, last!.StatusCode);
    }

    // ============================================================ sign-in second step
    [Fact]
    public async Task A_pre_auth_session_can_do_nothing_but_verify_fetch_csrf_or_log_out_then_verification_rotates_the_session()
    {
        var e = await Enroll(); var b = await PendingLogin(e);
        var pre = b.CookieValue!;
        foreach (var path in new[] { "/api/_probe/open", "/api/auth/me", "/api/me/mfa" })
        {
            var r = await b.Get(path);
            Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode); Assert.Equal("mfa_required", await r.ErrorCode());
        }
        Assert.Equal(HttpStatusCode.OK, (await b.Get("/api/auth/csrf")).StatusCode);

        var wrong = await b.Post("/api/auth/mfa/verify", new { code = "000000" });
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode); Assert.Equal("invalid_code", await wrong.ErrorCode());
        Assert.Equal(HttpStatusCode.Forbidden, (await b.Get("/api/_probe/open")).StatusCode); // still pending

        var ok = await b.Post("/api/auth/mfa/verify", new { code = CodeFor(e.Secret), returnTo = "/users/all" });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var body = await ok.Body();
        Assert.Equal("/users/all", body.GetProperty("next").GetString()); Assert.Equal(e.User.Email, body.GetProperty("user").GetProperty("email").GetString());
        Assert.NotEqual(pre, b.CookieValue); // new session id after MFA

        var old = B(); old.CookieValue = pre;
        Assert.Equal(HttpStatusCode.Unauthorized, (await old.Get("/api/auth/me")).StatusCode); // pre-auth session is dead
        await Csrf(b);
        Assert.Equal(HttpStatusCode.OK, (await b.Get("/api/_probe/open")).StatusCode);
        var s = await _seed.Db(db => db.Sessions.Where(x => x.UserId == e.User.Id && x.RevokedAt == null).OrderByDescending(x => x.CreatedAt).FirstAsync());
        Assert.NotNull(s.MfaVerifiedAt); Assert.False(s.MfaRequired);
    }

    [Fact]
    public async Task A_totp_code_cannot_be_replayed_and_the_next_step_works()
    {
        var e = await Enroll(); var code = CodeFor(e.Secret);
        var b1 = await PendingLogin(e);
        Assert.Equal(HttpStatusCode.OK, (await b1.Post("/api/auth/mfa/verify", new { code })).StatusCode);
        var b2 = await PendingLogin(e);
        var replay = await b2.Post("/api/auth/mfa/verify", new { code });
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode); Assert.Equal("invalid_code", await replay.ErrorCode());
        NextStep();
        var b3 = await PendingLogin(e);
        Assert.Equal(HttpStatusCode.OK, (await b3.Post("/api/auth/mfa/verify", new { code = CodeFor(e.Secret) })).StatusCode);
    }

    [Fact]
    public async Task One_code_used_concurrently_by_two_sessions_succeeds_exactly_once()
    {
        var e = await Enroll(); var code = CodeFor(e.Secret);
        var a = await PendingLogin(e); var b = await PendingLogin(e);
        var res = await Task.WhenAll(a.Post("/api/auth/mfa/verify", new { code }), b.Post("/api/auth/mfa/verify", new { code }));
        Assert.Equal(1, res.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, res.Count(r => r.StatusCode == HttpStatusCode.Unauthorized));
    }

    [Fact]
    public async Task Clock_skew_of_one_step_is_tolerated_but_two_is_not()
    {
        var e = await Enroll();
        var b1 = await PendingLogin(e);
        Assert.Equal(HttpStatusCode.OK, (await b1.Post("/api/auth/mfa/verify", new { code = CodeFor(e.Secret, +1) })).StatusCode);
        NextStep(); NextStep(); NextStep();
        var b2 = await PendingLogin(e);
        Assert.Equal(HttpStatusCode.Unauthorized, (await b2.Post("/api/auth/mfa/verify", new { code = CodeFor(e.Secret, -2) })).StatusCode);
    }

    [Fact]
    public async Task Repeated_wrong_codes_revoke_the_session_and_lock_the_account()
    {
        var e = await Enroll(); var b = await PendingLogin(e);
        for (var i = 0; i < 5; i++)
        {
            var r = await b.Post("/api/auth/mfa/verify", new { code = "11111" + i });
            Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await b.Post("/api/auth/mfa/verify", new { code = CodeFor(e.Secret) })).StatusCode); // session gone, even the right code is too late
        var u = await _seed.Db(db => db.Users.SingleAsync(x => x.Id == e.User.Id));
        Assert.NotNull(u.LockedUntil);
        var login = await B().LoginAsync(e.User.Email, Seed.Password);
        Assert.Equal("locked", (await login.Body()).GetProperty("status").GetString());
        Assert.NotEmpty(await _seed.Db(db => db.SecurityEvents.Where(x => x.UserId == e.User.Id && x.Type == "account.locked").ToListAsync()));
    }

    [Fact]
    public async Task Malformed_codes_are_rejected_without_server_errors()
    {
        var e = await Enroll(); var b = await PendingLogin(e);
        foreach (var bad in new[] { "", "abc", "12345", "1234567", "12 456", "<script>", new string('9', 500) })
        {
            var r = await b.Post("/api/auth/mfa/verify", new { code = bad });
            Assert.True(r.StatusCode is HttpStatusCode.Unauthorized or (HttpStatusCode)422, $"{bad.Length}: {r.StatusCode}");
        }
    }

    // ============================================================ recovery codes
    [Fact]
    public async Task A_recovery_code_signs_in_once_is_forgiving_about_format_and_triggers_a_security_alert()
    {
        var e = await Enroll(); var code = e.RecoveryCodes[0];
        var b = await PendingLogin(e);
        var ok = await b.Post("/api/auth/mfa/verify", new { recoveryCode = code.ToLowerInvariant().Replace("-", " ") });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        await Csrf(b);
        Assert.Equal(9, (await (await b.Get("/api/me/mfa")).Body()).GetProperty("recoveryCodesRemaining").GetInt32());

        var again = await (await PendingLogin(e)).Post("/api/auth/mfa/verify", new { recoveryCode = code });
        Assert.Equal(HttpStatusCode.Unauthorized, again.StatusCode); Assert.Equal("invalid_code", await again.ErrorCode());
        Assert.NotEmpty(await _seed.Db(db => db.SecurityEvents.Where(x => x.UserId == e.User.Id && x.Type == "mfa.recovery_used" && x.Severity == Severity.Medium).ToListAsync()));
        await _seed.Mail.WaitFor(e.User.Email, "recovery code was used");
    }

    [Fact]
    public async Task A_recovery_code_of_another_user_or_a_guess_does_not_work()
    {
        var a = await Enroll(); var other = await Enroll();
        var b = await PendingLogin(a);
        Assert.Equal(HttpStatusCode.Unauthorized, (await b.Post("/api/auth/mfa/verify", new { recoveryCode = other.RecoveryCodes[0] })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await b.Post("/api/auth/mfa/verify", new { recoveryCode = RecoveryCodes.New() })).StatusCode);
    }

    [Fact]
    public async Task Regenerating_recovery_codes_needs_fresh_reauth_and_invalidates_the_old_set()
    {
        var e = await Enroll();
        var b = await PendingLogin(e);
        await b.Post("/api/auth/mfa/verify", new { code = CodeFor(e.Secret) });
        await Csrf(b);

        var denied = await b.Post("/api/me/mfa/recovery-codes");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode); Assert.Equal("reauth_required", await denied.ErrorCode());

        NextStep();
        var noCode = await b.Post("/api/auth/reauth", new { password = Seed.Password }); // MFA users must also give a code
        Assert.Equal((HttpStatusCode)422, noCode.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await b.Post("/api/auth/reauth", new { password = Seed.Password, code = CodeFor(e.Secret) })).StatusCode);
        var fresh = (await (await b.Post("/api/me/mfa/recovery-codes")).Body()).GetProperty("recoveryCodes").EnumerateArray().Select(x => x.GetString()!).ToArray();
        Assert.Equal(10, fresh.Length); Assert.Empty(fresh.Intersect(e.RecoveryCodes));

        Assert.Equal(HttpStatusCode.Unauthorized, (await (await PendingLogin(e)).Post("/api/auth/mfa/verify", new { recoveryCode = e.RecoveryCodes[1] })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await (await PendingLogin(e)).Post("/api/auth/mfa/verify", new { recoveryCode = fresh[0] })).StatusCode);

        fx.Clock.Advance(TimeSpan.FromMinutes(6)); // reauth window (5 min) elapsed
        var stale = await b.Post("/api/me/mfa/recovery-codes");
        Assert.Equal(HttpStatusCode.Forbidden, stale.StatusCode); Assert.Equal("reauth_required", await stale.ErrorCode());
    }

    // ============================================================ lock screen & reauth with MFA
    [Fact]
    public async Task Unlocking_an_mfa_account_needs_the_password_and_a_valid_second_factor()
    {
        var e = await Enroll(); var b = await PendingLogin(e);
        await b.Post("/api/auth/mfa/verify", new { code = CodeFor(e.Secret) });
        await Csrf(b);
        await b.Post("/api/auth/lock");
        Assert.Equal((HttpStatusCode)423, (await b.Get("/api/_probe/open")).StatusCode);

        Assert.Equal((HttpStatusCode)422, (await b.Post("/api/auth/unlock", new { password = Seed.Password })).StatusCode); // password alone is not enough
        NextStep();
        Assert.Equal(HttpStatusCode.Unauthorized, (await b.Post("/api/auth/unlock", new { password = Seed.Password, code = "000000" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await b.Post("/api/auth/unlock", new { password = "wrong-password", code = CodeFor(e.Secret) })).StatusCode);
        Assert.Equal((HttpStatusCode)423, (await b.Get("/api/_probe/open")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await b.Post("/api/auth/unlock", new { password = Seed.Password, code = CodeFor(e.Secret) })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await b.Get("/api/_probe/open")).StatusCode);

        await b.Post("/api/auth/lock");
        Assert.Equal(HttpStatusCode.NoContent, (await b.Post("/api/auth/unlock", new { password = Seed.Password, recoveryCode = e.RecoveryCodes[2] })).StatusCode); // recovery code works too
    }

    // ============================================================ disable
    private async Task<TestBrowser> SignedInViaMfa(Enrolled e)
    {
        NextStep(); var b = await PendingLogin(e);
        Assert.Equal(HttpStatusCode.OK, (await b.Post("/api/auth/mfa/verify", new { code = CodeFor(e.Secret) })).StatusCode);
        await Csrf(b);
        return b;
    }

    [Fact]
    public async Task Disabling_mfa_needs_password_and_a_code_removes_every_secret_revokes_other_sessions_and_notifies()
    {
        var e = await Enroll();
        var b = await SignedInViaMfa(e);
        var other = await SignedInViaMfa(e); // a second live session

        NextStep();
        Assert.Equal(HttpStatusCode.Unauthorized, (await b.Post("/api/me/mfa/disable", new { password = "nope", code = CodeFor(e.Secret) })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await b.Post("/api/me/mfa/disable", new { password = Seed.Password, code = "000000" })).StatusCode);
        Assert.True((await (await b.Get("/api/me/mfa")).Body()).GetProperty("enrolled").GetBoolean());

        Assert.Equal(HttpStatusCode.NoContent, (await b.Post("/api/me/mfa/disable", new { password = Seed.Password, code = CodeFor(e.Secret) })).StatusCode);
        Assert.False((await (await b.Get("/api/me/mfa")).Body()).GetProperty("enrolled").GetBoolean());
        Assert.Empty(await _seed.Db(db => db.MfaFactors.Where(f => f.UserId == e.User.Id).ToListAsync()));
        Assert.Empty(await _seed.Db(db => db.MfaRecoveryCodes.Where(c => c.UserId == e.User.Id).ToListAsync()));
        Assert.Equal(HttpStatusCode.Unauthorized, (await other.Get("/api/auth/me")).StatusCode); // other sessions signed out
        await _seed.Mail.WaitFor(e.User.Email, "Two-step verification turned off");
        Assert.NotEmpty(await _seed.Db(db => db.SecurityEvents.Where(x => x.UserId == e.User.Id && x.Type == "mfa.removed").ToListAsync()));

        var plain = await B().LoginAsync(e.User.Email, Seed.Password); // no second step any more
        Assert.Equal("ok", (await plain.Body()).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Secrets_and_codes_never_reach_the_audit_log_or_security_events()
    {
        var e = await Enroll(); var b = await PendingLogin(e);
        await b.Post("/api/auth/mfa/verify", new { code = CodeFor(e.Secret) });
        await (await PendingLogin(e)).Post("/api/auth/mfa/verify", new { recoveryCode = e.RecoveryCodes[3] });
        var b32 = Base32.Encode(e.Secret);
        var audit = (await _seed.Db(db => db.AuditEvents.Where(a => a.ActorId == e.User.Id).ToListAsync())).Select(a => (a.MetaJson ?? "") + (a.DiffJson ?? "") + a.TargetId + a.ActorLabel);
        var sec = (await _seed.Db(db => db.SecurityEvents.Where(a => a.UserId == e.User.Id).ToListAsync())).Select(s => s.Detail + (s.MetaJson ?? ""));
        foreach (var text in audit.Concat(sec))
        {
            Assert.DoesNotContain(b32, text); Assert.DoesNotContain(e.RecoveryCodes[3], text); Assert.DoesNotContain(RecoveryCodes.Normalize(e.RecoveryCodes[3]), text);
            Assert.DoesNotContain(Seed.Password, text);
        }
    }

    [Fact]
    public async Task A_ciphertext_moved_to_another_users_row_cannot_be_decrypted()
    {
        var a = await Enroll(); var victim = await Enroll();
        await _seed.Db(async db =>
        {
            var fa = await db.MfaFactors.SingleAsync(f => f.UserId == a.User.Id); var fv = await db.MfaFactors.SingleAsync(f => f.UserId == victim.User.Id);
            fv.SecretCiphertext = fa.SecretCiphertext; fv.SecretNonce = fa.SecretNonce; // attacker with DB write access swaps seeds
            return await db.SaveChangesAsync();
        });
        var b = await PendingLogin(victim);
        var res = await b.Post("/api/auth/mfa/verify", new { code = CodeFor(a.Secret) }); // the attacker's own valid code
        Assert.NotEqual(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("internal_error", await res.ErrorCode());
        Assert.DoesNotContain("Cryptographic", await res.Content.ReadAsStringAsync());
    }

    // ============================================================ organisation policy
    [Fact]
    public async Task When_the_org_requires_mfa_users_without_it_are_confined_to_enrolment_until_they_enrol()
    {
        await using var policy = await _seed.SettingAsync("mfaPolicy", "\"required\"");
        var u = await _seed.UserAsync(); var b = B();
        var login = await b.LoginAsync(u.Email, Seed.Password);
        Assert.Equal("mfa_enrollment_required", (await login.Body()).GetProperty("status").GetString());

        var blocked = await b.Get("/api/_probe/open");
        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode); Assert.Equal("mfa_enrollment_required", await blocked.ErrorCode());
        Assert.Equal(HttpStatusCode.OK, (await b.Get("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await b.Get("/api/me/mfa")).StatusCode);
        Assert.True((await (await b.Get("/api/me/mfa")).Body()).GetProperty("required").GetBoolean());

        await b.Post("/api/auth/reauth", new { password = Seed.Password });
        var secret = Base32.Decode((await (await b.Post("/api/me/mfa/enroll")).Body()).GetProperty("secret").GetString()!);
        Assert.Equal(HttpStatusCode.OK, (await b.Post("/api/me/mfa/confirm", new { code = CodeFor(secret) })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await b.Get("/api/_probe/open")).StatusCode); // released

        NextStep();
        var disable = await b.Post("/api/me/mfa/disable", new { password = Seed.Password, code = CodeFor(secret) });
        Assert.Equal(HttpStatusCode.Conflict, disable.StatusCode); Assert.Equal("invariant", await disable.ErrorCode()); // cannot opt out while required
    }

    [Fact]
    public async Task Mfa_endpoints_require_authentication_and_csrf()
    {
        var anon = B();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.Get("/api/me/mfa")).StatusCode);
        foreach (var path in new[] { "/api/me/mfa/enroll", "/api/me/mfa/confirm", "/api/me/mfa/recovery-codes", "/api/me/mfa/disable", "/api/auth/mfa/verify" })
            Assert.Equal(HttpStatusCode.Unauthorized, (await anon.Post(path, csrf: false)).StatusCode);

        var u = await _seed.UserAsync(); var b = await SignedIn(u); b.CsrfToken = null;
        Assert.Equal(HttpStatusCode.Forbidden, (await b.Post("/api/me/mfa/enroll")).StatusCode); // no CSRF token
    }
}
