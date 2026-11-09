using AnnabaDigital.Api.Http;
using AnnabaDigital.Application.Users;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Api.Endpoints;

public static class AccessRequestEndpoints
{
    public static void MapAccessRequests(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/access-requests");
        // self-service: any signed-in user
        g.MapGet("/roles", async (AccessRequestService svc, HttpContext ctx) => Results.Ok(await svc.RequestableRolesAsync(ctx.RequirePrincipal(), ctx.RequestAborted))).RequireAuthorization();
        g.MapGet("/mine", async (AccessRequestService svc, HttpContext ctx) => Results.Ok(await svc.MineAsync(ctx.RequirePrincipal(), ctx.RequestAborted))).RequireAuthorization();
        g.MapPost("/", async (CreateAccessRequest body, AccessRequestService svc, HttpContext ctx)
            => Results.Created("/api/access-requests/mine", await svc.CreateAsync(body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequireAuthorization();
        g.MapPost("/{id:guid}/cancel", async (Guid id, AccessRequestService svc, HttpContext ctx) =>
        {
            await svc.CancelAsync(id, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return Results.NoContent();
        }).RequireAuthorization();
        // deciding: those who may assign roles
        g.MapGet("/pending-count", async (ApprovalService svc, HttpContext ctx) => Results.Ok(await svc.PendingAsync(ctx.RequirePrincipal(), ctx.RequestAborted))).RequirePermission(Permissions.UsersAssignRole);
        g.MapGet("/", async (string? status, int? page, int? pageSize, AccessRequestService svc, HttpContext ctx)
            => Results.Ok(await svc.ListAsync(status, page ?? 1, pageSize ?? 25, ctx.RequestAborted))).RequirePermission(Permissions.UsersAssignRole);
        g.MapPost("/{id:guid}/approve", async (Guid id, DecideAccessRequest? body, AccessRequestService svc, HttpContext ctx) =>
        {
            await svc.DecideAsync(id, true, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return Results.NoContent();
        }).RequirePermission(Permissions.UsersAssignRole);
        g.MapPost("/{id:guid}/reject", async (Guid id, DecideAccessRequest? body, AccessRequestService svc, HttpContext ctx) =>
        {
            await svc.DecideAsync(id, false, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return Results.NoContent();
        }).RequirePermission(Permissions.UsersAssignRole);
    }
}
