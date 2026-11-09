using Microsoft.Extensions.Options;
using AnnabaDigital.Api.Http;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;

namespace AnnabaDigital.Api.Endpoints;

public sealed record LoginBody(string? Email, string? Password, bool Remember, string? ReturnTo);
public sealed record PasswordBody(string? Password, string? Code = null, string? RecoveryCode = null);

public static class AuthEndpoints
{
    public static void MapAuth(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/auth");

        // PUBLIC: credentials are the authentication. Rate-limited and enumeration-safe inside AuthService.
        g.MapPost("/login", async (LoginBody body, HttpContext ctx, AuthService auth, IOptions<ApiOptions> api) =>
        {
            ctx.Request.Cookies.TryGetValue(api.Value.CookieName, out var existing);
            var r = await auth.LoginAsync(new LoginRequest(body.Email, body.Password, body.Remember), ctx.Client(), existing, ctx.RequestAborted);
            if (r.RawToken is not null) CookieHelper.Set(ctx, api.Value, r.RawToken, r.Remember);
            return Results.Ok(new { status = r.Status, next = r.Status == LoginStatus.Ok ? Redirects.Safe(body.ReturnTo) : null, user = r.User });
        }).AllowAnonymous();

        g.MapPost("/logout", async (HttpContext ctx, AuthService auth, IOptions<ApiOptions> api) =>
        {
            await auth.LogoutAsync(ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted);
            CookieHelper.Clear(ctx, api.Value);
            return Results.NoContent();
        }).RequireAuthorization().WithMetadata(new AllowLocked(), new AllowMfaPending(), new AllowMfaEnrollment());

        g.MapGet("/me", async (HttpContext ctx, AuthService auth) => Results.Ok(await auth.MeAsync(ctx.RequirePrincipal(), ctx.RequestAborted)))
            .RequireAuthorization().WithMetadata(new AllowLocked(), new AllowMfaEnrollment());

        g.MapGet("/csrf", (HttpContext ctx, ITokenService tokens) => Results.Ok(new { token = tokens.CsrfToken(ctx.RequirePrincipal().SessionId) }))
            .RequireAuthorization().WithMetadata(new AllowLocked(), new AllowMfaPending(), new AllowMfaEnrollment());

        g.MapPost("/lock", async (HttpContext ctx, AuthService auth) =>
        {
            await auth.LockAsync(ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted);
            return Results.NoContent();
        }).RequireAuthorization();

        g.MapPost("/unlock", async (PasswordBody body, HttpContext ctx, AuthService auth, IOptions<ApiOptions> api) =>
        {
            try { await auth.UnlockAsync(ctx.RequirePrincipal(), body.Password, body.Code, body.RecoveryCode, ctx.Client(), ctx.RequestAborted); }
            catch (AnnabaDigital.Domain.DomainException e) when (e.Code == AnnabaDigital.Domain.ErrorCodes.Unauthenticated) { CookieHelper.Clear(ctx, api.Value); throw; }
            return Results.NoContent();
        }).RequireAuthorization().WithMetadata(new AllowLocked());

        g.MapPost("/reauth", async (PasswordBody body, HttpContext ctx, AuthService auth) =>
        {
            await auth.ReauthAsync(ctx.RequirePrincipal(), body.Password, body.Code, body.RecoveryCode, ctx.Client(), ctx.RequestAborted);
            return Results.NoContent();
        }).RequireAuthorization().WithMetadata(new AllowMfaEnrollment());
    }


}
