using AnnabaDigital.Api.Http;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Application.Users;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Api.Endpoints;

public static class AccessReviewEndpoints
{
    public static void MapAccessReviews(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/access-reviews");
        g.MapGet("/", async (AccessReviewService svc, HttpContext ctx) => Results.Ok(await svc.ListAsync(ctx.RequestAborted))).RequirePermission(Permissions.UsersAssignRole);
        g.MapGet("/schedule", async (AccessReviewService svc, HttpContext ctx) => Results.Ok(await svc.GetScheduleAsync(ctx.RequestAborted))).RequirePermission(Permissions.UsersAssignRole);
        g.MapPut("/schedule", async (SaveReviewSchedule body, AccessReviewService svc, HttpContext ctx)
            => Results.Ok(await svc.SaveScheduleAsync(body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.UsersAssignRole);
        g.MapPost("/", async (StartAccessReview body, AccessReviewService svc, AuditWriter audit, HttpContext ctx)
            => Results.Created("/api/access-reviews", await svc.StartAsync(body, ctx.RequirePrincipal(), ctx.Client(), audit, ctx.RequestAborted))).RequirePermission(Permissions.UsersAssignRole);
        g.MapGet("/{id:guid}/items", async (Guid id, string? decision, int? page, int? pageSize, AccessReviewService svc, HttpContext ctx)
            => Results.Ok(await svc.ItemsAsync(id, decision, page ?? 1, pageSize ?? 25, ctx.RequestAborted))).RequirePermission(Permissions.UsersAssignRole);
        g.MapPost("/{id:guid}/items/{itemId:guid}/decision", async (Guid id, Guid itemId, ReviewDecisionRequest body, AccessReviewService svc, AuditWriter audit, HttpContext ctx) =>
        {
            await svc.DecideAsync(id, itemId, body, ctx.RequirePrincipal(), ctx.Client(), audit, ctx.RequestAborted); return Results.NoContent();
        }).RequirePermission(Permissions.UsersAssignRole);
        g.MapPost("/{id:guid}/close", async (Guid id, AccessReviewService svc, AuditWriter audit, HttpContext ctx) =>
        {
            await svc.CloseAsync(id, ctx.RequirePrincipal(), ctx.Client(), audit, ctx.RequestAborted); return Results.NoContent();
        }).RequirePermission(Permissions.UsersAssignRole);
    }
}
