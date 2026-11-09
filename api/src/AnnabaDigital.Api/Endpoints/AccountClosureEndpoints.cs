using AnnabaDigital.Api.Http;
using AnnabaDigital.Application.Profile;
using AnnabaDigital.Application.Users;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Api.Endpoints;

/// <summary>Download my data, close my account (self-service), and the administrators' queue for closure requests.</summary>
public static class AccountClosureEndpoints
{
    public static void MapAccountClosures(this IEndpointRouteBuilder app)
    {
        var me = app.MapGroup("/api/me");
        me.MapGet("/export", async (AccountExportService svc, HttpContext ctx) =>
        {
            var data = await svc.ExportAsync(ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted);
            ctx.Response.Headers.CacheControl = "no-store";
            var bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(data, new System.Text.Json.JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
            return Results.File(bytes, "application/json", $"annaba-digital-my-data-{DateTime.UtcNow:yyyyMMdd}.json");
        }).RequireAuthorization();
        me.MapGet("/closure", async (AccountClosureService svc, HttpContext ctx) =>
        {
            var r = await svc.MineAsync(ctx.RequirePrincipal(), ctx.RequestAborted);
            return r is null ? Results.NoContent() : Results.Ok(r);
        }).RequireAuthorization();
        me.MapPost("/closure", async (RequestClosure body, AccountClosureService svc, HttpContext ctx)
            => Results.Created("/api/me/closure", await svc.RequestAsync(body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequireAuthorization();
        me.MapPost("/closure/cancel", async (AccountClosureService svc, HttpContext ctx) =>
        {
            await svc.CancelAsync(ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return Results.NoContent();
        }).RequireAuthorization();

        var g = app.MapGroup("/api/account-closures");
        g.MapGet("/", async (string? status, int? page, int? pageSize, AccountClosureService svc, HttpContext ctx)
            => Results.Ok(await svc.ListAsync(status, page ?? 1, pageSize ?? 25, ctx.RequestAborted))).RequirePermission(Permissions.UsersSuspend);
        g.MapPost("/{id:guid}/approve", async (Guid id, DecideClosure? body, AccountClosureService svc, HttpContext ctx) =>
        {
            await svc.DecideAsync(id, true, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return Results.NoContent();
        }).RequirePermission(Permissions.UsersSuspend);
        g.MapPost("/{id:guid}/reject", async (Guid id, DecideClosure? body, AccountClosureService svc, HttpContext ctx) =>
        {
            await svc.DecideAsync(id, false, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return Results.NoContent();
        }).RequirePermission(Permissions.UsersSuspend);
    }
}
