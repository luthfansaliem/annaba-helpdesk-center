using AnnabaDigital.Api.Http;
using AnnabaDigital.Application.Helpdesk;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Api.Endpoints;

/// <summary>VIS-001/002/005. Anyone signed in registers their own visitors (vendor accounts never reach these routes); reception checks them in and out
/// (<c>visitors.checkin</c>); the block list and settings need <c>visitors.manage</c>. Who sees which visits is decided in the service.</summary>
public static class VisitEndpoints
{
    public static void MapVisits(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/visits");
        g.MapGet("/", async (string? scope, DateOnly? date, Guid? locationId, string? status, string? q, int? page, int? pageSize, VisitService svc, HttpContext ctx)
            => Results.Ok(await svc.ListAsync(scope, date, locationId, status, q, page ?? 1, pageSize ?? 50, ctx.RequirePrincipal(), ctx.RequestAborted))).RequireAuthorization();
        g.MapGet("/summary", async (VisitService svc, HttpContext ctx) => Results.Ok(await svc.SummaryAsync(ctx.RequirePrincipal(), ctx.RequestAborted))).RequireAuthorization();
        g.MapGet("/{id:guid}", async (Guid id, VisitService svc, HttpContext ctx) => Results.Ok(await svc.GetAsync(id, ctx.RequirePrincipal(), ctx.RequestAborted))).RequireAuthorization();
        g.MapPost("/", async (SaveVisitRequest body, VisitService svc, HttpContext ctx) =>
        {
            var v = await svc.SaveAsync(null, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted);
            return Results.Created($"/api/visits/{v.Id}", v);
        }).RequireAuthorization();
        g.MapPut("/{id:guid}", async (Guid id, SaveVisitRequest body, VisitService svc, HttpContext ctx) => Results.Ok(await svc.SaveAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequireAuthorization();
        g.MapPost("/{id:guid}/cancel", async (Guid id, VisitService svc, HttpContext ctx) => Results.Ok(await svc.CancelAsync(id, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequireAuthorization();
        g.MapPost("/{id:guid}/check-in", async (Guid id, CheckInVisitRequest body, VisitService svc, HttpContext ctx) => Results.Ok(await svc.CheckInAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.VisitorsCheckIn);
        g.MapPost("/{id:guid}/check-out", async (Guid id, VisitService svc, HttpContext ctx) => Results.Ok(await svc.CheckOutAsync(id, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.VisitorsCheckIn);

        var s = app.MapGroup("/api/visitors");
        s.MapGet("/settings", async (VisitService svc, HttpContext ctx) => Results.Ok(await svc.SettingsAsync(ctx.RequestAborted))).RequireAuthorization();
        s.MapPut("/settings", async (VisitorSettingsDto body, VisitService svc, HttpContext ctx) => Results.Ok(await svc.SaveSettingsAsync(body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.VisitorsManage);
        s.MapGet("/blocks", async (VisitService svc, HttpContext ctx) => Results.Ok(await svc.BlocksAsync(ctx.RequestAborted))).RequirePermission(Permissions.VisitorsManage);
        s.MapPost("/blocks", async (SaveVisitorBlockRequest body, VisitService svc, HttpContext ctx) => Results.Ok(await svc.AddBlockAsync(body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.VisitorsManage);
        s.MapDelete("/blocks/{id:guid}", async (Guid id, VisitService svc, HttpContext ctx) => Results.Ok(await svc.RemoveBlockAsync(id, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.VisitorsManage);
    }
}
