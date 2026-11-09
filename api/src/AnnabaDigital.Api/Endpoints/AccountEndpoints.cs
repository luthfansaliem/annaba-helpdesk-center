using AnnabaDigital.Api.Http;
using AnnabaDigital.Application.Auth;

namespace AnnabaDigital.Api.Endpoints;

public sealed record RegisterBody(string? Name, string? Email, string? Password);
public sealed record TokenBody(string? Token);
public sealed record EmailBody(string? Email);
public sealed record ResetBody(string? Token, string? Password);
public sealed record AcceptBody(string? Token, string? Name, string? Password);

/// <summary>Public, unauthenticated account-lifecycle endpoints. All are rate-limited and enumeration-safe in the service layer.</summary>
public static class AccountEndpoints
{
    private static readonly object Accepted = new { message = "If the request can be processed you will receive an email shortly." };

    public static void MapAccount(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/auth");

        // 202 with an identical body whether or not the email already has an account.
        g.MapPost("/register", async (RegisterBody b, HttpContext ctx, AccountService svc) =>
        {
            await svc.RegisterAsync(new RegisterRequest(b.Name, b.Email, b.Password), ctx.Client(), ctx.RequestAborted);
            return Results.Accepted(value: Accepted);
        }).AllowAnonymous();

        g.MapPost("/verify-email", async (TokenBody b, HttpContext ctx, AccountService svc) =>
        {
            await svc.VerifyEmailAsync(b.Token, ctx.Client(), ctx.RequestAborted);
            return Results.Ok(new { status = "verified" });
        }).AllowAnonymous();

        g.MapPost("/verify-email/resend", async (EmailBody b, HttpContext ctx, AccountService svc) =>
        {
            await svc.ResendVerificationAsync(b.Email, ctx.Client(), ctx.RequestAborted);
            return Results.Accepted(value: Accepted);
        }).AllowAnonymous();

        g.MapPost("/forgot-password", async (EmailBody b, HttpContext ctx, AccountService svc) =>
        {
            await svc.ForgotPasswordAsync(b.Email, ctx.Client(), ctx.RequestAborted);
            return Results.Accepted(value: Accepted);
        }).AllowAnonymous();

        g.MapPost("/reset-password", async (ResetBody b, HttpContext ctx, AccountService svc) =>
        {
            await svc.ResetPasswordAsync(b.Token, b.Password, ctx.Client(), ctx.RequestAborted);
            return Results.Ok(new { status = "password_updated" });
        }).AllowAnonymous();

        var inv = app.MapGroup("/api/invitations");
        inv.MapGet("/lookup", async (string? token, HttpContext ctx, InvitationAcceptService svc)
            => Results.Ok(await svc.LookupAsync(token, ctx.Client(), ctx.RequestAborted))).AllowAnonymous();
        inv.MapPost("/accept", async (AcceptBody b, HttpContext ctx, InvitationAcceptService svc) =>
        {
            await svc.AcceptAsync(new AcceptInvitationRequest(b.Token, b.Name, b.Password), ctx.Client(), ctx.RequestAborted);
            return Results.Ok(new { status = "accepted" });
        }).AllowAnonymous();
    }
}
