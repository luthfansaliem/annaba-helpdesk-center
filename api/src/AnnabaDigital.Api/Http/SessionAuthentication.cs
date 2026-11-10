using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using AnnabaDigital.Application.Auth;

namespace AnnabaDigital.Api.Http;

public static class SessionDefaults { public const string Scheme = "Session"; public const string ItemKey = "annabadigital.principal"; }

/// <summary>Authenticates the opaque session cookie against the database on every request (no JWT, no cached claims).</summary>
public sealed class SessionAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder,
    SessionService sessions, IOptions<ApiOptions> api, AnnabaDigital.Application.Abstractions.ITenantContext tenant)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // INT-001: the public API takes "Authorization: Bearer adk_…" (an API key acting as the person who made it, within its scopes); nothing else does.
        if (Request.Path.StartsWithSegments(ApiKeyDefaults.PathPrefix) && Request.Headers.Authorization.ToString() is { Length: > 7 } auth && auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var key = await Context.RequestServices.GetRequiredService<AnnabaDigital.Application.Integrations.IntegrationService>().ValidateAsync(auth[7..].Trim(), Context.RequestAborted);
            if (key is null) return AuthenticateResult.Fail("Invalid, revoked or expired API key.");
            Context.Items[ApiKeyDefaults.ItemKey] = key;
            Context.Items[SessionDefaults.ItemKey] = new SessionPrincipal(key.KeyId, key.UserId, key.Email, key.Name, false, false, null, TenantId: key.TenantId);
            tenant.Use(key.TenantId, false);
            return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, key.UserId.ToString()), new Claim("api_key", key.KeyId.ToString())], SessionDefaults.Scheme)), SessionDefaults.Scheme));
        }
        if (!Request.Cookies.TryGetValue(api.Value.CookieName, out var raw)) return AuthenticateResult.NoResult();
        var principal = await sessions.ValidateAsync(raw, Context.RequestAborted);
        if (principal is null) return AuthenticateResult.Fail("Invalid or expired session.");

        Context.Items[SessionDefaults.ItemKey] = principal;
        // From here on every query is limited to one organisation: the person's own, or - in platform support mode, and only for the admin screens - the one they are supporting.
        if (principal.ActingTenantId is { } acting && ActingScope.Covers(Request)) tenant.Use(acting, false, support: true);
        else tenant.Use(principal.TenantId, principal.IsPlatform);
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, principal.UserId.ToString()), new Claim("sid", principal.SessionId.ToString()),
            new Claim(ClaimTypes.Email, principal.Email), new Claim(ClaimTypes.Name, principal.Name),
        ], SessionDefaults.Scheme);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SessionDefaults.Scheme));
    }

    // Unauthenticated requests get our JSON envelope from the authorization result handler, not a redirect.
    protected override Task HandleChallengeAsync(AuthenticationProperties properties) => Task.CompletedTask;
    protected override Task HandleForbiddenAsync(AuthenticationProperties properties) => Task.CompletedTask;
}

/// <summary>
/// Which requests run inside the supported organisation in support mode. Everything about the signed-in person themself (sign-in state, profile, MFA, own sessions,
/// own requests) and platform-wide pages (tenants, system settings) stay in their own organisation, so identity never breaks and platform powers never leak into a tenant.
/// An allow-list, so a new endpoint is not silently moved into someone else's organisation.
/// </summary>
public static class ActingScope
{
    private static readonly string[] Prefixes =
    [
        "/api/users", "/api/invitations", "/api/roles", "/api/permissions", "/api/access-matrix", "/api/audit", "/api/security-events", "/api/access-reviews",
        "/api/dashboard/admin", "/api/sessions/all", "/api/email-deliveries", "/api/avatars", "/api/settings/tenant", "/api/account-closures",
    ];

    public static bool Covers(HttpRequest r)
    {
        var p = r.Path.Value ?? "";
        if (Prefixes.Any(x => p.Equals(x, StringComparison.OrdinalIgnoreCase) || p.StartsWith(x + "/", StringComparison.OrdinalIgnoreCase))) return true;
        // access requests: the decision queue is the organisation's; asking for a role for yourself is not
        if (p.StartsWith("/api/access-requests", StringComparison.OrdinalIgnoreCase))
            return p.EndsWith("/approve", StringComparison.OrdinalIgnoreCase) || p.EndsWith("/reject", StringComparison.OrdinalIgnoreCase) || p.EndsWith("/pending-count", StringComparison.OrdinalIgnoreCase)
                || (HttpMethods.IsGet(r.Method) && p.TrimEnd('/').Equals("/api/access-requests", StringComparison.OrdinalIgnoreCase));
        return false;
    }
}

public static class HttpContextExtensions
{
    public static SessionPrincipal? Principal(this HttpContext ctx) => ctx.Items.TryGetValue(SessionDefaults.ItemKey, out var p) ? p as SessionPrincipal : null;
    public static SessionPrincipal RequirePrincipal(this HttpContext ctx) => ctx.Principal() ?? throw AnnabaDigital.Domain.DomainException.Unauthenticated();
    public static AnnabaDigital.Application.Abstractions.ClientInfo Client(this HttpContext ctx)
        => new(ctx.Connection.RemoteIpAddress?.ToString(), ctx.Request.Headers.UserAgent.ToString(), ctx.TraceIdentifier);
}
