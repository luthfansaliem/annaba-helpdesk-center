using Microsoft.Extensions.Options;
using AnnabaDigital.Api.Http;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Mfa;

namespace AnnabaDigital.Api.Endpoints;

public sealed record CodeBody(string? Code = null, string? RecoveryCode = null, string? ReturnTo = null);
public sealed record DisableMfaBody(string? Password, string? Code = null, string? RecoveryCode = null);

public static class MfaEndpoints
{
    public static void MapMfa(this IEndpointRouteBuilder app)
    {
        // Second step of sign-in: reachable only with a session that is still waiting for its second factor.
        app.MapPost("/api/auth/mfa/verify", async (CodeBody body, HttpContext ctx, MfaService mfa, AuthService auth, IOptions<ApiOptions> api) =>
        {
            var v = await mfa.VerifyLoginAsync(ctx.RequirePrincipal(), body.Code, body.RecoveryCode, ctx.Client(), ctx.RequestAborted);
            CookieHelper.Set(ctx, api.Value, v.RawToken, v.Remember); // brand-new session id (rotation)
            return Results.Ok(new { status = "ok", next = Redirects.Safe(body.ReturnTo), user = await auth.MeByUserIdAsync(v.UserId, ctx.RequestAborted) });
        }).RequireAuthorization().WithMetadata(new AllowMfaPending());

        var g = app.MapGroup("/api/me/mfa");
        g.MapGet("/", async (HttpContext ctx, MfaService mfa) => Results.Ok(await mfa.StatusAsync(ctx.RequirePrincipal().UserId, ctx.RequestAborted)))
            .RequireAuthorization().WithMetadata(new AllowMfaEnrollment());

        g.MapPost("/enroll", async (HttpContext ctx, MfaService mfa) => Results.Ok(await mfa.EnrollAsync(ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted)))
            .RequireAuthorization().WithMetadata(new AllowMfaEnrollment());

        g.MapPost("/confirm", async (CodeBody body, HttpContext ctx, MfaService mfa)
            => Results.Ok(new { recoveryCodes = await mfa.ConfirmAsync(ctx.RequirePrincipal(), body.Code, ctx.Client(), ctx.RequestAborted) }))
            .RequireAuthorization().WithMetadata(new AllowMfaEnrollment());

        g.MapPost("/recovery-codes", async (HttpContext ctx, MfaService mfa)
            => Results.Ok(new { recoveryCodes = await mfa.RegenerateRecoveryCodesAsync(ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted) }))
            .RequireAuthorization();

        // POST (not DELETE) so proxies that drop DELETE bodies cannot break the re-authentication payload.
        g.MapPost("/disable", async (DisableMfaBody body, HttpContext ctx, MfaService mfa) =>
        {
            await mfa.DisableAsync(ctx.RequirePrincipal(), body.Password, body.Code, body.RecoveryCode, ctx.Client(), ctx.RequestAborted);
            return Results.NoContent();
        }).RequireAuthorization();
    }
}
