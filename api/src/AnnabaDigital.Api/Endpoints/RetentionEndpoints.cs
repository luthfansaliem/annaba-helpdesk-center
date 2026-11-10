using AnnabaDigital.Api.Http;
using AnnabaDigital.Application.Helpdesk;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Api.Endpoints;

/// <summary>ADM-006. Retention policy, legal holds and the clean-up. Everything needs <c>retention.manage</c> except the notice, which any signed-in person may read (what their organisation keeps and for how long).</summary>
public static class RetentionEndpoints
{
    public static void MapRetention(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/retention");
        g.MapGet("/notice", async (RetentionService svc, HttpContext ctx) => Results.Ok(await svc.NoticeAsync(ctx.RequestAborted))).RequireAuthorization();
        g.MapGet("/", async (RetentionService svc, HttpContext ctx) => Results.Ok(await svc.GetAsync(ctx.RequestAborted))).RequirePermission(Permissions.RetentionManage);
        g.MapPut("/", async (SaveRetentionRequest body, RetentionService svc, HttpContext ctx) => Results.Ok(await svc.UpdateAsync(body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.RetentionManage);
        g.MapPost("/preview", async (SaveRetentionRequest body, RetentionService svc, HttpContext ctx) => Results.Ok(await svc.PreviewAsync(body, ctx.RequestAborted))).RequirePermission(Permissions.RetentionManage);
        g.MapPost("/run", async (RetentionService svc, HttpContext ctx) => Results.Ok(await svc.RunNowAsync(ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.RetentionManage);
        g.MapGet("/holds", async (RetentionService svc, HttpContext ctx, int? page, int? pageSize) => Results.Ok(await svc.HoldsAsync(page ?? 1, pageSize ?? 25, ctx.RequestAborted))).RequirePermission(Permissions.RetentionManage);

        g.MapGet("/files", async (FileCheckService svc, HttpContext ctx) => Results.Ok(await svc.CheckAsync(ctx.RequestAborted))).RequirePermission(Permissions.RetentionManage);
        g.MapPost("/files/clean", async (FileCheckService svc, HttpContext ctx) => Results.Ok(await svc.CleanAsync(ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.RetentionManage);

        app.MapPut("/api/tickets/{id:guid}/legal-hold", async (Guid id, PlaceHoldRequest body, RetentionService svc, HttpContext ctx) =>
        {
            await svc.PlaceHoldAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted);
            return Results.NoContent();
        }).RequirePermission(Permissions.RetentionManage);
        app.MapDelete("/api/tickets/{id:guid}/legal-hold", async (Guid id, RetentionService svc, HttpContext ctx) =>
        {
            await svc.ReleaseHoldAsync(id, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted);
            return Results.NoContent();
        }).RequirePermission(Permissions.RetentionManage);
    }
}
