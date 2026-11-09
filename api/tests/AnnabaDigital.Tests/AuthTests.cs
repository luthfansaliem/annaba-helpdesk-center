using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Tests.Infra;
using Xunit;

namespace AnnabaDigital.Tests;

[Collection("db")]
public sealed class AuthTests(PostgresFixture fx)
{
    private readonly Seed _seed = new(fx.Factory, fx.Clock);
    private TestBrowser Browser() => TestBrowser.Create(fx.Factory);

    [Fact]
    public async Task Login_success_sets_hardened_cookie_and_stores_only_a_hash()
    {
        var u = await _seed.UserAsync();
        var b = Browser();
        var res = await b.LoginAsync(u.Email, Seed.Password);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Body();
        Assert.Equal("ok", body.GetProperty("status").GetString());
        Assert.Equal("/dashboard", body.GetProperty("next").GetString());

        Assert.NotNull(b.CookieValue);
        var cookie = b.CookieHeader!.ToLowerInvariant();
        Assert.Contains("httponly", cookie); Assert.Contains("samesite=lax", cookie); Assert.Contains("path=/", cookie);
        Assert.DoesNotContain("max-age", cookie); // non-remember ⇒ session cookie

        var rows = await _seed.Db(db => db.Sessions.Where(s => s.UserId == u.Id).ToListAsync());
        var s = Assert.Single(rows);
        Assert.NotEqual(b.CookieValue, s.TokenHash);
        Assert.Equal(64, s.TokenHash.Length); // SHA-256 hex; the raw token is never stored
        Assert.True(b.CookieValue!.Length >= 43); // 256-bit token, base64url

        var hash = await _seed.Db(db => db.Credentials.Where(c => c.UserId == u.Id).Select(c => c.PasswordHash).SingleAsync());
        Assert.StartsWith("$argon2id$v=19$m=19456,t=2,p=1$", hash);
        Assert.DoesNotContain(Seed.Password, hash);
    }

    [Fact]
    public async Task Wrong_password_and_unknown_email_are_indistinguishable()
    {
        var u = await _seed.UserAsync();
        var b1 = Browser(); var b2 = Browser();
        var wrong = await b1.LoginAsync(u.Email, "not-the-password");
        var unknown = await b2.LoginAsync($"ghost{Guid.NewGuid():N}@test.local", "not-the-password");
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(wrong.StatusCode, unknown.StatusCode);
        var a = (await wrong.Body()).GetProperty("error"); var c = (await unknown.Body()).GetProperty("error");
        Assert.Equal(a.GetProperty("code").GetString(), c.GetProperty("code").GetString());
        Assert.Equal(a.GetProperty("message").GetString(), c.GetProperty("message").GetString());
        Assert.Null(b1.CookieValue); Assert.Null(b2.CookieValue);
    }

    [Fact]
    public async Task Failed_logins_are_audited_without_the_password()
    {
        var u = await _seed.UserAsync();
        var secret = "Sup3r-Secret-Attempt!" + Guid.NewGuid().ToString("N");
        await Browser().LoginAsync(u.Email, secret);
        var rows = await _seed.Db(db => db.AuditEvents.Where(a => a.Action == "auth.login.failure" && a.ActorId == u.Id).ToListAsync());
        Assert.NotEmpty(rows);
        var everything = (await _seed.Db(db => db.AuditEvents.ToListAsync())).Select(a => (a.MetaJson ?? "") + (a.DiffJson ?? "") + a.ActorLabel + a.TargetId).ToList();
        Assert.DoesNotContain(everything, t => t.Contains(secret));
        var sec = await _seed.Db(db => db.SecurityEvents.Where(e => e.UserId == u.Id && e.Type == "login.failed").CountAsync());
        Assert.True(sec >= 1);
    }

    [Fact]
    public async Task Account_locks_after_threshold_blocks_correct_password_and_expires()
    {
        var u = await _seed.UserAsync();
        var b = Browser();
        for (var i = 0; i < 5; i++) Assert.Equal(HttpStatusCode.Unauthorized, (await b.LoginAsync(u.Email, "bad-password-" + i)).StatusCode);

        var locked = await b.LoginAsync(u.Email, Seed.Password);
        Assert.Equal(HttpStatusCode.OK, locked.StatusCode);
        Assert.Equal("locked", (await locked.Body()).GetProperty("status").GetString());
        Assert.Null(b.CookieValue);
        Assert.Contains(await _seed.Db(db => db.SecurityEvents.Where(e => e.UserId == u.Id && e.Type == "account.locked").ToListAsync()), _ => true);

        fx.Clock.Advance(TimeSpan.FromMinutes(16));
        var ok = await Browser().LoginAsync(u.Email, Seed.Password);
        Assert.Equal("ok", (await ok.Body()).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Per_ip_rate_limit_returns_429_with_retry_after_and_other_ips_are_unaffected()
    {
        var b = Browser();
        HttpResponseMessage? last = null;
        for (var i = 0; i < 21; i++) last = await b.LoginAsync($"x{i}{Guid.NewGuid():N}@test.local", "whatever-password");
        Assert.Equal((HttpStatusCode)429, last!.StatusCode);
        Assert.Equal("rate_limited", await last.ErrorCode());
        Assert.True(last.Headers.Contains("Retry-After"));

        var other = Browser();
        var u = await _seed.UserAsync();
        Assert.Equal(HttpStatusCode.OK, (await other.LoginAsync(u.Email, Seed.Password)).StatusCode);
    }

    [Theory]
    [InlineData(UserStatus.Pending, "verify_email")]
    [InlineData(UserStatus.Suspended, "suspended")]
    [InlineData(UserStatus.Deactivated, "deactivated")]
    public async Task Non_active_accounts_cannot_get_a_session(UserStatus status, string expected)
    {
        var u = await _seed.UserAsync(status: status);
        var b = Browser();
        var res = await b.LoginAsync(u.Email, Seed.Password);
        Assert.Equal(expected, (await res.Body()).GetProperty("status").GetString());
        Assert.Null(b.CookieValue);
        Assert.Empty(await _seed.Db(db => db.Sessions.Where(s => s.UserId == u.Id).ToListAsync()));
    }

    [Fact]
    public async Task Non_active_accounts_with_wrong_password_do_not_reveal_their_state()
    {
        var u = await _seed.UserAsync(status: UserStatus.Suspended);
        var res = await Browser().LoginAsync(u.Email, "wrong-password");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        Assert.Equal("invalid_credentials", await res.ErrorCode());
    }

    [Fact]
    public async Task Login_rotates_session_id_so_a_pre_existing_session_cannot_be_fixated()
    {
        var u = await _seed.UserAsync();
        var b = Browser();
        await b.LoginAsync(u.Email, Seed.Password);
        var first = b.CookieValue!;
        await b.LoginAsync(u.Email, Seed.Password); // sends the old cookie
        Assert.NotEqual(first, b.CookieValue);
        var old = Browser(); old.CookieValue = first;
        Assert.Equal(HttpStatusCode.Unauthorized, (await old.Get("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await b.Get("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Logout_revokes_the_session_server_side()
    {
        var u = await _seed.UserAsync();
        var b = Browser(); await b.LoginAsync(u.Email, Seed.Password);
        var stolen = b.CookieValue!;
        Assert.Equal(HttpStatusCode.NoContent, (await b.Post("/api/auth/logout")).StatusCode);
        var replay = Browser(); replay.CookieValue = stolen;
        Assert.Equal(HttpStatusCode.Unauthorized, (await replay.Get("/api/auth/me")).StatusCode);
        var s = await _seed.Db(db => db.Sessions.SingleAsync(x => x.UserId == u.Id));
        Assert.Equal("user", s.RevokedReason);
    }

    [Fact]
    public async Task Me_returns_roles_and_effective_permissions_but_never_secrets()
    {
        var u = await _seed.UserAsync(role: "security_auditor");
        var b = Browser(); await b.LoginAsync(u.Email, Seed.Password);
        var me = await (await b.Get("/api/auth/me")).Body();
        Assert.Equal(u.Email, me.GetProperty("email").GetString());
        var perms = me.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()).ToArray();
        Assert.Contains("audit.read", perms); Assert.DoesNotContain("users.create", perms);
        Assert.DoesNotContain("passwordHash", me.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Sessions_expire_on_idle_and_on_absolute_lifetime_and_activity_extends_idle()
    {
        var u = await _seed.UserAsync();
        var idle = Browser(); await idle.LoginAsync(u.Email, Seed.Password);
        fx.Clock.Advance(TimeSpan.FromMinutes(29)); // within 30-minute idle window
        Assert.Equal(HttpStatusCode.OK, (await idle.Get("/api/auth/me")).StatusCode); // touches ⇒ extends idle
        fx.Clock.Advance(TimeSpan.FromMinutes(29));
        Assert.Equal(HttpStatusCode.OK, (await idle.Get("/api/auth/me")).StatusCode);
        fx.Clock.Advance(TimeSpan.FromMinutes(31));
        Assert.Equal(HttpStatusCode.Unauthorized, (await idle.Get("/api/auth/me")).StatusCode);

        var abs = Browser(); await abs.LoginAsync(u.Email, Seed.Password);
        for (var i = 0; i < 12; i++) { fx.Clock.Advance(TimeSpan.FromMinutes(25)); await abs.Get("/api/auth/me"); } // 5 h of activity
        fx.Clock.Advance(TimeSpan.FromHours(7.5));
        Assert.Equal(HttpStatusCode.Unauthorized, (await abs.Get("/api/auth/me")).StatusCode); // 12.5 h > 12 h absolute
    }

    [Fact]
    public async Task Remember_me_uses_persistent_cookie_and_longer_idle_window()
    {
        var u = await _seed.UserAsync();
        var b = Browser(); await b.LoginAsync(u.Email, Seed.Password, remember: true);
        Assert.Contains("max-age", b.CookieHeader!.ToLowerInvariant());
        fx.Clock.Advance(TimeSpan.FromDays(3));
        Assert.Equal(HttpStatusCode.OK, (await b.Get("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Suspending_the_user_kills_the_session_on_the_very_next_request()
    {
        var u = await _seed.UserAsync();
        var b = Browser(); await b.LoginAsync(u.Email, Seed.Password);
        await _seed.Db(async db => { var x = await db.Users.SingleAsync(z => z.Id == u.Id); x.Status = UserStatus.Suspended; return await db.SaveChangesAsync(); });
        Assert.Equal(HttpStatusCode.Unauthorized, (await b.Get("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Rehash_on_login_when_stored_hash_uses_weaker_parameters()
    {
        var u = await _seed.UserAsync();
        await _seed.Db(async db =>
        {
            var c = await db.Credentials.SingleAsync(x => x.UserId == u.Id);
            var weak = new AnnabaDigital.Infrastructure.Security.Argon2PasswordHasher(Microsoft.Extensions.Options.Options.Create(new AnnabaDigital.Infrastructure.Security.SecurityOptions { Argon2MemoryKiB = 8192, Argon2Iterations = 1 }));
            c.PasswordHash = weak.Hash(Seed.Password); return await db.SaveChangesAsync();
        });
        Assert.Equal(HttpStatusCode.OK, (await Browser().LoginAsync(u.Email, Seed.Password)).StatusCode);
        var now = await _seed.Db(db => db.Credentials.Where(c => c.UserId == u.Id).Select(c => c.PasswordHash).SingleAsync());
        Assert.StartsWith("$argon2id$v=19$m=19456,t=2,p=1$", now);
    }

    [Theory]
    [InlineData("", "x")] [InlineData("a@b.co", "")] [InlineData("   ", "pw")]
    public async Task Empty_credentials_are_rejected_with_validation_error(string email, string password)
    {
        var res = await Browser().LoginAsync(email, password);
        Assert.Equal((HttpStatusCode)422, res.StatusCode);
        Assert.Equal("validation", await res.ErrorCode());
    }

    [Fact]
    public async Task Return_url_is_only_honoured_for_same_origin_paths()
    {
        var u = await _seed.UserAsync();
        foreach (var (input, expected) in new[] { ("/users/all", "/users/all"), ("//evil.com", "/dashboard"), ("https://evil.com", "/dashboard"), ("/\\evil.com", "/dashboard"), ("/login", "/dashboard") })
        {
            var b = Browser();
            var res = await b.Post("/api/auth/login", new { email = u.Email, password = Seed.Password, returnTo = input }, csrf: false);
            Assert.Equal(expected, (await res.Body()).GetProperty("next").GetString());
        }
    }

    [Fact]
    public async Task Mfa_enrolled_users_get_a_session_that_is_unusable_until_the_second_factor_is_verified()
    {
        var u = await _seed.UserAsync();
        await _seed.Db(async db =>
        {
            db.MfaFactors.Add(new MfaFactor { UserId = u.Id, SecretCiphertext = [1], SecretNonce = [1], ConfirmedAt = fx.Clock.UtcNow, CreatedAt = fx.Clock.UtcNow });
            return await db.SaveChangesAsync();
        });
        var b = Browser();
        var res = await b.LoginAsync(u.Email, Seed.Password);
        Assert.Equal("mfa_required", (await res.Body()).GetProperty("status").GetString());
        var me = await b.Get("/api/auth/me");
        Assert.Equal(HttpStatusCode.Forbidden, me.StatusCode);
        Assert.Equal("mfa_required", await me.ErrorCode());
        Assert.Equal(HttpStatusCode.NoContent, (await b.Post("/api/auth/logout")).StatusCode);
    }
}
