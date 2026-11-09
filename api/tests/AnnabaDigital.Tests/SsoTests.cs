using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Infrastructure.Security;
using AnnabaDigital.Tests.Infra;
using Xunit;

namespace AnnabaDigital.Tests;

/// <summary>A stand-in identity provider for the endpoint tests: it checks PKCE like a real one would and hands out whatever identity the test registered for a code.</summary>
public sealed class FakeOidc : IOidcClient
{
    public Dictionary<string, OidcIdentity> Codes { get; } = [];
    public bool Enabled => true;
    public string DisplayName => "Acme SSO";
    private string? _challenge; private string? _nonce;
    public Task<string> BuildAuthorizeUrlAsync(string redirectUri, string state, string nonce, string codeChallenge, CancellationToken ct = default)
    {
        _challenge = codeChallenge; _nonce = nonce;
        return Task.FromResult($"https://idp.test/authorize?state={Uri.EscapeDataString(state)}&nonce={Uri.EscapeDataString(nonce)}&code_challenge={Uri.EscapeDataString(codeChallenge)}&redirect_uri={Uri.EscapeDataString(redirectUri)}");
    }
    public Task<OidcIdentity> ExchangeAsync(string redirectUri, string code, string codeVerifier, string nonce, CancellationToken ct = default)
    {
        var expected = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        if (!Codes.TryGetValue(code, out var id) || expected != _challenge || nonce != _nonce) throw new OidcException("PKCE or nonce mismatch.");
        return Task.FromResult(id);
    }
}

[Collection("db")]
public sealed class SsoTests(PostgresFixture fx)
{
    private const string Issuer = "https://idp.test";
    private readonly Seed _seed = new(fx.Factory, fx.Clock);
    private static string Sub() => "sub-" + Guid.NewGuid().ToString("N");

    private (HttpClient http, FakeOidc idp) Client()
    {
        var idp = new FakeOidc();
        var f = fx.Factory.WithWebHostBuilder(b => b.ConfigureTestServices(s => { s.RemoveAll<IOidcClient>(); s.AddSingleton<IOidcClient>(idp); }));
        return (f.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false }), idp);
    }
    private static HttpRequestMessage Req(string url, string? cookie = null)
    {
        var r = new HttpRequestMessage(HttpMethod.Get, url);
        r.Headers.Add("X-Internal-Proxy-Key", ApiFactory.ProxyKey); r.Headers.Add("X-Forwarded-For", TestBrowser.NewIp());
        if (cookie is not null) r.Headers.Add("Cookie", cookie);
        return r;
    }
    private static string Cookie(HttpResponseMessage r, string name) => r.Headers.GetValues("Set-Cookie").Select(c => c.Split(';')[0]).First(c => c.StartsWith(name + "="));
    private static string State(HttpResponseMessage r) => System.Web.HttpUtility.ParseQueryString(r.Headers.Location!.Query)["state"]!;

    /// <summary>Runs start → callback with the identity the provider would return, and gives back the callback response.</summary>
    private async Task<HttpResponseMessage> SignIn(HttpClient http, FakeOidc idp, OidcIdentity id, string returnTo = "/users/all")
    {
        var start = await http.SendAsync(Req($"/api/auth/sso/start?returnTo={Uri.EscapeDataString(returnTo)}"));
        Assert.Equal(HttpStatusCode.Redirect, start.StatusCode); Assert.StartsWith("https://idp.test/authorize", start.Headers.Location!.ToString());
        var code = "code-" + Guid.NewGuid().ToString("N"); idp.Codes[code] = id;
        return await http.SendAsync(Req($"/api/auth/sso/callback?code={code}&state={Uri.EscapeDataString(State(start))}", Cookie(start, "sso")));
    }

    [Fact]
    public async Task Sso_is_off_unless_configured()
    {
        var b = TestBrowser.Create(fx.Factory);
        Assert.Equal(HttpStatusCode.NotFound, (await b.Get("/api/auth/sso/start")).StatusCode);
        var pub = await (await b.Get("/api/settings/public")).Body();
        Assert.False(pub.GetProperty("ssoEnabled").GetBoolean()); Assert.Equal(JsonValueKind.Null, pub.GetProperty("ssoName").ValueKind);
    }

    [Fact]
    public async Task A_verified_email_of_an_active_user_signs_in_links_the_subject_and_then_the_subject_decides()
    {
        var (http, idp) = Client(); var u = await _seed.UserAsync(role: "user_admin"); var sub = Sub();
        var res = await SignIn(http, idp, new OidcIdentity(Issuer, sub, u.Email.ToUpperInvariant(), true, "Ada"));
        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode); Assert.Equal("/users/all", res.Headers.Location!.ToString());
        var sid = Cookie(res, "sid"); Assert.NotEmpty(sid[4..]);
        var me = Req("/api/auth/me", sid); Assert.Equal(HttpStatusCode.OK, (await http.SendAsync(me)).StatusCode); // the cookie is a real session
        Assert.Single(await _seed.Db(db => db.UserIdentities.Where(i => i.UserId == u.Id && i.Issuer == Issuer && i.Subject == sub).ToListAsync()));
        var log = await _seed.Db(db => db.AuditEvents.Where(a => a.ActorId == u.Id && (a.Action == "auth.sso.link" || a.Action == "auth.login.success")).ToListAsync());
        Assert.Contains(log, a => a.Action == "auth.sso.link"); Assert.Contains(log, a => a.Action == "auth.login.success" && a.MetaJson!.Contains("sso"));

        // the e-mail changed at the provider: the same subject still maps to the same person
        var again = await SignIn(http, idp, new OidcIdentity(Issuer, sub, "someone.else@example.com", true, "Ada"));
        Assert.Equal("/users/all", again.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Nobody_is_created_and_unverified_unknown_pending_or_mismatched_identities_are_turned_away()
    {
        var (http, idp) = Client();
        var active = await _seed.UserAsync(); var pending = await _seed.UserAsync(status: UserStatus.Pending); var suspended = await _seed.UserAsync(status: UserStatus.Suspended);

        async Task Denied(OidcIdentity id, string expected)
        {
            var r = await SignIn(http, idp, id);
            Assert.Equal(expected, r.Headers.Location!.ToString());
            Assert.False(r.Headers.TryGetValues("Set-Cookie", out var c) && c.Any(x => x.StartsWith("sid=") && x.Split(';')[0].Length > 4)); // no session
        }
        var unknown = $"nobody{Guid.NewGuid():N}@example.com";
        await Denied(new OidcIdentity(Issuer, Sub(), unknown, true, null), "/login?sso=denied");
        Assert.False(await _seed.Db(db => db.Users.AnyAsync(x => x.Email == unknown))); // no auto-provisioning
        await Denied(new OidcIdentity(Issuer, Sub(), active.Email, false, null), "/login?sso=denied");   // e-mail not verified by the provider
        await Denied(new OidcIdentity(Issuer, Sub(), null, true, null), "/login?sso=denied");            // no e-mail at all
        await Denied(new OidcIdentity(Issuer, Sub(), pending.Email, true, null), "/login?sso=denied");  // must accept the invitation first
        await Denied(new OidcIdentity(Issuer, Sub(), suspended.Email, true, null), "/login?sso=suspended");

        var first = await SignIn(http, idp, new OidcIdentity(Issuer, Sub(), active.Email, true, null)); Assert.Equal("/users/all", first.Headers.Location!.ToString());
        await Denied(new OidcIdentity(Issuer, Sub(), active.Email, true, null), "/login?sso=denied");    // a second, different account claiming the same e-mail
        Assert.NotEmpty(await _seed.Db(db => db.AuditEvents.Where(a => a.Action == "auth.sso.denied").ToListAsync()));
    }

    [Fact]
    public async Task The_state_must_match_and_a_missing_or_tampered_flow_is_rejected()
    {
        var (http, idp) = Client(); var u = await _seed.UserAsync(); idp.Codes["c1"] = new OidcIdentity(Issuer, Sub(), u.Email, true, null);
        var start = await http.SendAsync(Req("/api/auth/sso/start?returnTo=https://evil.example/x"));
        var flowCookie = Cookie(start, "sso");
        Assert.Equal("/login?sso=failed", (await http.SendAsync(Req($"/api/auth/sso/callback?code=c1&state=wrong", flowCookie))).Headers.Location!.ToString());
        Assert.Equal("/login?sso=failed", (await http.SendAsync(Req($"/api/auth/sso/callback?code=c1&state={Uri.EscapeDataString(State(start))}"))).Headers.Location!.ToString()); // no flow cookie
        Assert.Equal("/login?sso=failed", (await http.SendAsync(Req($"/api/auth/sso/callback?code=c1&state={Uri.EscapeDataString(State(start))}", flowCookie[..^3] + "xyz"))).Headers.Location!.ToString());
        foreach (var junk in new[] { "sso=garbage", "sso=1.@@@.@@@", "sso=9.AAAA.AAAA", "sso=1..", "sso=1.AAAA.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" }) // unreadable cookies are "no flow", never a 500
            Assert.Equal("/login?sso=failed", (await http.SendAsync(Req($"/api/auth/sso/callback?code=c1&state={Uri.EscapeDataString(State(start))}", junk))).Headers.Location!.ToString());
        Assert.Equal("/login?sso=failed", (await http.SendAsync(Req($"/api/auth/sso/callback?error=access_denied&state={Uri.EscapeDataString(State(start))}", flowCookie))).Headers.Location!.ToString());
        var ok = await http.SendAsync(Req($"/api/auth/sso/callback?code=c1&state={Uri.EscapeDataString(State(start))}", flowCookie));
        Assert.Equal("/dashboard", ok.Headers.Location!.ToString()); // an off-site returnTo falls back to the dashboard
    }
}

public sealed class OidcClientTests
{
    private const string Issuer = "https://idp.example";
    private readonly TestClock _clock = new();
    private readonly RsaSecurityKey _key = new(RSA.Create(2048)) { KeyId = "k1" };

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> f) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(f(request));
    }
    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private string Token(Action<SecurityTokenDescriptor>? tweak = null, SecurityKey? signWith = null, string nonce = "n1", string aud = "client-1", string iss = Issuer)
    {
        var d = new SecurityTokenDescriptor
        {
            Issuer = iss, Audience = aud, NotBefore = _clock.UtcNow.UtcDateTime.AddMinutes(-1), Expires = _clock.UtcNow.UtcDateTime.AddMinutes(10),
            Claims = new Dictionary<string, object> { ["sub"] = "abc", ["email"] = "ada@example.com", ["email_verified"] = true, ["name"] = "Ada", ["nonce"] = nonce },
            SigningCredentials = new SigningCredentials(signWith ?? _key, SecurityAlgorithms.RsaSha256),
        };
        tweak?.Invoke(d);
        return new JsonWebTokenHandler().CreateToken(d);
    }

    private OidcClient Client(string idToken, string discoveryIssuer = Issuer, string authEndpoint = $"{Issuer}/auth")
    {
        var jwks = JsonSerializer.Serialize(new { keys = new[] { JsonWebKeyConverter.ConvertFromRSASecurityKey(new RsaSecurityKey(_key.Rsa.ExportParameters(false)) { KeyId = "k1" }) } });
        var http = new HttpClient(new Handler(r => r.RequestUri!.AbsolutePath switch
        {
            "/.well-known/openid-configuration" => Json(JsonSerializer.Serialize(new { issuer = discoveryIssuer, authorization_endpoint = authEndpoint, token_endpoint = $"{Issuer}/token", jwks_uri = $"{Issuer}/jwks" })),
            "/jwks" => Json(jwks),
            "/token" => Json(JsonSerializer.Serialize(new { id_token = idToken, access_token = "x" })),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        }));
        return new OidcClient(http, Options.Create(new SsoOptions { Issuer = Issuer, ClientId = "client-1", ClientSecret = "secret" }), _clock);
    }

    [Fact]
    public async Task A_correct_id_token_yields_the_identity_and_the_authorize_url_carries_pkce_and_state()
    {
        var c = Client(Token());
        var url = await c.BuildAuthorizeUrlAsync("https://app/cb", "st", "n1", "chal");
        Assert.StartsWith($"{Issuer}/auth?", url); Assert.Contains("code_challenge=chal", url); Assert.Contains("code_challenge_method=S256", url); Assert.Contains("state=st", url); Assert.Contains("response_type=code", url);
        var id = await c.ExchangeAsync("https://app/cb", "code", "verifier", "n1");
        Assert.Equal(new OidcIdentity(Issuer, "abc", "ada@example.com", true, "Ada"), id);
    }

    [Fact]
    public async Task Tokens_with_the_wrong_audience_issuer_nonce_key_or_lifetime_are_refused()
    {
        async Task Refused(string token, string nonce = "n1") => await Assert.ThrowsAsync<OidcException>(() => Client(token).ExchangeAsync("https://app/cb", "code", "v", nonce));
        await Refused(Token(aud: "someone-else"));
        await Refused(Token(iss: "https://evil.example"));
        await Refused(Token(), nonce: "other-nonce");
        await Refused(Token(signWith: new RsaSecurityKey(RSA.Create(2048)) { KeyId = "k1" })); // signed by a key the provider doesn't publish
        await Refused(Token(d => { d.NotBefore = _clock.UtcNow.UtcDateTime.AddHours(-3); d.Expires = _clock.UtcNow.UtcDateTime.AddHours(-2); })); // expired
        await Refused(new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor { Issuer = Issuer, Audience = "client-1", Expires = _clock.UtcNow.UtcDateTime.AddMinutes(5), NotBefore = _clock.UtcNow.UtcDateTime.AddMinutes(-1), Claims = new Dictionary<string, object> { ["sub"] = "abc", ["nonce"] = "n1" } })); // unsigned
    }

    [Fact]
    public async Task Discovery_must_name_the_configured_issuer_and_use_https()
    {
        await Assert.ThrowsAsync<OidcException>(() => Client(Token(), discoveryIssuer: "https://other.example").BuildAuthorizeUrlAsync("https://app/cb", "s", "n", "c"));
        await Assert.ThrowsAsync<OidcException>(() => Client(Token(), authEndpoint: "http://idp.example/auth").BuildAuthorizeUrlAsync("https://app/cb", "s", "n", "c"));
    }
}
