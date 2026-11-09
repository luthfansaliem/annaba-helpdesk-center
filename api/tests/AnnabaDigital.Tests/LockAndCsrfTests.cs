using System.Net;
using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Tests.Infra;
using Xunit;

namespace AnnabaDigital.Tests;

[Collection("db")]
public sealed class LockAndCsrfTests(PostgresFixture fx)
{
    private readonly Seed _seed = new(fx.Factory, fx.Clock);
    private async Task<(TestBrowser b, AnnabaDigital.Domain.Entities.User u)> SignedIn(string role = "user")
    {
        var u = await _seed.UserAsync(role: role); var b = TestBrowser.Create(fx.Factory); await b.LoginAsync(u.Email, Seed.Password); return (b, u);
    }

    // ---------- lock screen (AUTH-006): enforced by the server, not an overlay ----------
    [Fact]
    public async Task Locked_session_is_rejected_by_the_server_for_everything_except_unlock_logout_me_csrf()
    {
        var (b, _) = await SignedIn();
        Assert.Equal(HttpStatusCode.NoContent, (await b.Post("/api/auth/lock")).StatusCode);

        var blocked = await b.Get("/api/_probe/open");
        Assert.Equal((HttpStatusCode)423, blocked.StatusCode);
        Assert.Equal("locked", await blocked.ErrorCode());
        Assert.Equal((HttpStatusCode)423, (await b.Post("/api/_probe/mutate")).StatusCode);
        Assert.Equal((HttpStatusCode)423, (await b.Post("/api/auth/reauth", new { password = Seed.Password })).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await b.Get("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await b.Get("/api/auth/csrf")).StatusCode);
    }

    [Fact]
    public async Task Unlock_requires_the_password_and_restores_access()
    {
        var (b, _) = await SignedIn();
        await b.Post("/api/auth/lock");
        var bad = await b.Post("/api/auth/unlock", new { password = "wrong-password" });
        Assert.Equal(HttpStatusCode.Unauthorized, bad.StatusCode);
        Assert.Equal((HttpStatusCode)423, (await b.Get("/api/_probe/open")).StatusCode); // still locked

        Assert.Equal(HttpStatusCode.NoContent, (await b.Post("/api/auth/unlock", new { password = Seed.Password })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await b.Get("/api/_probe/open")).StatusCode);
    }

    [Fact]
    public async Task Five_failed_unlocks_revoke_the_session()
    {
        var (b, _) = await SignedIn();
        await b.Post("/api/auth/lock");
        for (var i = 0; i < 5; i++) Assert.Equal(HttpStatusCode.Unauthorized, (await b.Post("/api/auth/unlock", new { password = "nope-" + i })).StatusCode);
        var sixth = await b.Post("/api/auth/unlock", new { password = Seed.Password }); // even the right password is now too late
        Assert.Equal(HttpStatusCode.Unauthorized, sixth.StatusCode);
        Assert.Equal("unauthenticated", await sixth.ErrorCode());
        var replay = TestBrowser.Create(fx.Factory); replay.CookieValue = b.CookieValue ?? "";
        Assert.Equal(HttpStatusCode.Unauthorized, (await replay.Get("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Reauth_stamps_the_session_and_rejects_a_wrong_password()
    {
        var (b, u) = await SignedIn();
        Assert.Equal(HttpStatusCode.Unauthorized, (await b.Post("/api/auth/reauth", new { password = "wrong-password" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await b.Post("/api/auth/reauth", new { password = Seed.Password })).StatusCode);
        var s = await _seed.Db(db => db.Sessions.SingleAsync(x => x.UserId == u.Id));
        Assert.NotNull(s.ReauthAt);
    }

    // ---------- CSRF / origin ----------
    [Fact]
    public async Task Mutations_without_origin_or_with_foreign_origin_are_rejected_even_when_unauthenticated()
    {
        var b = TestBrowser.Create(fx.Factory);
        b.Origin = null;
        Assert.Equal(HttpStatusCode.Forbidden, (await b.Post("/api/auth/login", new { email = "a@b.co", password = "x" }, csrf: false)).StatusCode);
        b.Origin = "https://evil.example";
        Assert.Equal(HttpStatusCode.Forbidden, (await b.Post("/api/auth/login", new { email = "a@b.co", password = "x" }, csrf: false)).StatusCode);
    }

    [Fact]
    public async Task Authenticated_mutations_need_the_per_session_csrf_token()
    {
        var (b, _) = await SignedIn();
        var good = b.CsrfToken;
        b.CsrfToken = null;
        Assert.Equal(HttpStatusCode.Forbidden, (await b.Post("/api/_probe/mutate")).StatusCode);
        b.CsrfToken = "forged-token";
        Assert.Equal(HttpStatusCode.Forbidden, (await b.Post("/api/_probe/mutate")).StatusCode);
        b.CsrfToken = good;
        Assert.Equal(HttpStatusCode.NoContent, (await b.Post("/api/_probe/mutate")).StatusCode);

        // a token from another session is not valid for this one
        var (other, _) = await SignedIn();
        b.CsrfToken = other.CsrfToken;
        Assert.Equal(HttpStatusCode.Forbidden, (await b.Post("/api/_probe/mutate")).StatusCode);
    }

    // ---------- proxy gate & headers ----------
    [Fact]
    public async Task Requests_without_the_proxy_key_cannot_reach_the_api_but_health_is_open()
    {
        var b = TestBrowser.Create(fx.Factory); b.SendProxyKey = false;
        Assert.Equal(HttpStatusCode.NotFound, (await b.Get("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await b.Get("/health")).StatusCode);
    }

    [Fact]
    public async Task Responses_carry_security_headers_and_api_responses_are_not_cacheable()
    {
        var (b, _) = await SignedIn();
        var res = await b.Get("/api/auth/me");
        Assert.Equal("nosniff", res.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", res.Headers.GetValues("X-Frame-Options").Single());
        Assert.Contains("frame-ancestors 'none'", res.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("no-store", res.Headers.GetValues("Cache-Control").Single());
        Assert.Equal("no-referrer", res.Headers.GetValues("Referrer-Policy").Single());
    }

    [Fact]
    public async Task Errors_use_the_envelope_and_never_leak_internals()
    {
        var res = await TestBrowser.Create(fx.Factory).Get("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        var e = (await res.Body()).GetProperty("error");
        Assert.Equal("unauthenticated", e.GetProperty("code").GetString());
        Assert.False(string.IsNullOrEmpty(e.GetProperty("requestId").GetString()));
        Assert.DoesNotContain("Exception", e.GetRawText());
    }
}
