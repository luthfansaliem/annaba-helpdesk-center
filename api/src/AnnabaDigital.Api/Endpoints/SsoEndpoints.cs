using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using AnnabaDigital.Api.Http;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Infrastructure.Security;

namespace AnnabaDigital.Api.Endpoints;

/// <summary>Sign in through an OpenID Connect provider. Browser-navigation endpoints (GET + redirects), so they live outside the JSON API conventions.</summary>
public static class SsoEndpoints
{
    private sealed record Flow(string State, string Nonce, string Verifier, string ReturnTo, DateTimeOffset Expires);
    private static string FlowCookie(ApiOptions o) => o.SecureCookies ? "__Host-sso" : "sso";
    private static string B64(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static readonly byte[] FlowAad = Encoding.UTF8.GetBytes("sso-flow");

    /// <summary>
    /// The sign-in state lives in a short-lived cookie, sealed with the application's own encryption key (the same one that protects MFA secrets, kept in the
    /// environment), so it survives redeploys and works across instances without a separate key store.
    /// </summary>
    private static string Seal(ISecretProtector p, string json)
    {
        var s = p.Protect(Encoding.UTF8.GetBytes(json), FlowAad);
        return $"{s.KeyVersion}.{B64(s.Nonce)}.{B64(s.Ciphertext)}";
    }

    private static string Open(ISecretProtector p, string sealedValue)
    {
        var parts = sealedValue.Split('.');
        if (parts.Length != 3 || !int.TryParse(parts[0], out var version)) throw new FormatException("Malformed flow cookie.");
        return Encoding.UTF8.GetString(p.Unprotect(new ProtectedSecret(Unb64(parts[2]), Unb64(parts[1]), version), FlowAad));
    }

    private static byte[] Unb64(string s) => Convert.FromBase64String(s.Replace('-', '+').Replace('_', '/').PadRight((s.Length + 3) / 4 * 4, '='));

    private static string Rand(int bytes) => B64(RandomNumberGenerator.GetBytes(bytes));

    public static void MapSso(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/auth/sso");

        g.MapGet("/start", async (string? returnTo, HttpContext ctx, IOidcClient oidc, ISecretProtector sealer, IOptions<ApiOptions> api) =>
        {
            if (!oidc.Enabled) return Results.NotFound();
            var o = api.Value;
            var flow = new Flow(Rand(32), Rand(32), Rand(32), Redirects.Safe(returnTo), DateTimeOffset.UtcNow.AddMinutes(10));
            var challenge = B64(SHA256.HashData(Encoding.ASCII.GetBytes(flow.Verifier)));
            string url;
            try { url = await oidc.BuildAuthorizeUrlAsync($"{o.Origin.TrimEnd('/')}/api/auth/sso/callback", flow.State, flow.Nonce, challenge, ctx.RequestAborted); }
            catch (OidcException) { return Results.Redirect("/login?sso=failed"); }
            ctx.Response.Cookies.Append(FlowCookie(o), Seal(sealer, JsonSerializer.Serialize(flow)),
                new CookieOptions { HttpOnly = true, Secure = o.SecureCookies, SameSite = SameSiteMode.Lax, Path = "/", MaxAge = TimeSpan.FromMinutes(10), IsEssential = true });
            ctx.Response.Headers.CacheControl = "no-store";
            return Results.Redirect(url);
        }).AllowAnonymous();

        g.MapGet("/callback", async (string? code, string? state, string? error, HttpContext ctx, IOidcClient oidc, AuthService auth, ISecretProtector sealer, IOptions<ApiOptions> api, ILoggerFactory logs) =>
        {
            var o = api.Value; ctx.Response.Headers.CacheControl = "no-store";
            ctx.Request.Cookies.TryGetValue(FlowCookie(o), out var raw);
            ctx.Response.Cookies.Delete(FlowCookie(o), new CookieOptions { HttpOnly = true, Secure = o.SecureCookies, SameSite = SameSiteMode.Lax, Path = "/" }); // single use
            if (!oidc.Enabled) return Results.NotFound();

            Flow? flow = null;
            try { if (!string.IsNullOrEmpty(raw)) flow = JsonSerializer.Deserialize<Flow>(Open(sealer, raw)); } catch (Exception e) when (e is CryptographicException or JsonException or FormatException or ArgumentException) { } // an unreadable cookie is simply "no flow"
            if (flow is null || flow.Expires < DateTimeOffset.UtcNow || string.IsNullOrEmpty(state) || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(state), Encoding.UTF8.GetBytes(flow.State)))
                return Results.Redirect("/login?sso=failed");
            if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(code)) return Results.Redirect("/login?sso=failed");

            OidcIdentity identity;
            try { identity = await oidc.ExchangeAsync($"{o.Origin.TrimEnd('/')}/api/auth/sso/callback", code, flow.Verifier, flow.Nonce, ctx.RequestAborted); }
            catch (OidcException e) { logs.CreateLogger("Sso").LogWarning(e, "Single sign-on token exchange failed"); return Results.Redirect("/login?sso=failed"); }

            ctx.Request.Cookies.TryGetValue(o.CookieName, out var existing);
            var sso = ctx.RequestServices.GetRequiredService<IOptions<SsoOptions>>().Value;
            var r = await auth.SsoLoginAsync(identity, sso.RequireVerifiedEmail, ctx.Client(), existing, ctx.RequestAborted);
            if (r.RawToken is not null) CookieHelper.Set(ctx, o, r.RawToken, r.Remember);
            return r.Status switch
            {
                LoginStatus.Ok => Results.Redirect(flow.ReturnTo),
                LoginStatus.MfaRequired => Results.Redirect($"/mfa?returnTo={Uri.EscapeDataString(flow.ReturnTo)}"),
                LoginStatus.MfaEnrollmentRequired => Results.Redirect("/profile?tab=security"),
                LoginStatus.Suspended => Results.Redirect("/login?sso=suspended"),
                LoginStatus.Locked => Results.Redirect("/login?sso=locked"),
                _ => Results.Redirect("/login?sso=denied"),
            };
        }).AllowAnonymous();
    }
}
