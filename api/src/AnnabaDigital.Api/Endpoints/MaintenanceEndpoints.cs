using AnnabaDigital.Api.Http;
using AnnabaDigital.Application.Helpdesk;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Api.Endpoints;

/// <summary>PM-001/002/003. Preventive-maintenance plans and the compliance report. Reading needs <c>pm.read</c>, changing <c>pm.manage</c>.</summary>
public static class MaintenanceEndpoints
{
    public static void MapMaintenance(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/maintenance");
        g.MapGet("/plans", async (string? q, string? status, Guid? assetId, int? page, int? pageSize, MaintenanceService svc, HttpContext ctx)
            => Results.Ok(await svc.ListAsync(q, status, assetId, page ?? 1, pageSize ?? 25, ctx.RequestAborted))).RequirePermission(Permissions.PmRead);
        g.MapGet("/summary", async (MaintenanceService svc, HttpContext ctx) => Results.Ok(await svc.SummaryAsync(ctx.RequestAborted))).RequirePermission(Permissions.PmRead);
        g.MapGet("/plans/{id:guid}", async (Guid id, MaintenanceService svc, HttpContext ctx) => Results.Ok(await svc.GetAsync(id, ctx.RequirePrincipal(), ctx.RequestAborted))).RequirePermission(Permissions.PmRead);
        g.MapPost("/plans", async (SavePlanRequest body, MaintenanceService svc, HttpContext ctx) =>
        {
            var p = await svc.SaveAsync(null, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted);
            return Results.Created($"/api/maintenance/plans/{p.Id}", p);
        }).RequirePermission(Permissions.PmManage);
        g.MapPut("/plans/{id:guid}", async (Guid id, SavePlanRequest body, MaintenanceService svc, HttpContext ctx) => Results.Ok(await svc.SaveAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.PmManage);
        g.MapPost("/plans/{id:guid}/pause", async (Guid id, MaintenanceService svc, HttpContext ctx) => Results.Ok(await svc.SetPausedAsync(id, true, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.PmManage);
        g.MapPost("/plans/{id:guid}/resume", async (Guid id, MaintenanceService svc, HttpContext ctx) => Results.Ok(await svc.SetPausedAsync(id, false, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.PmManage);
        g.MapDelete("/plans/{id:guid}", async (Guid id, MaintenanceService svc, HttpContext ctx) => { await svc.DeleteAsync(id, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return Results.NoContent(); }).RequirePermission(Permissions.PmManage);
        g.MapGet("/compliance", async (DateOnly? from, DateOnly? to, string? by, MaintenanceService svc, HttpContext ctx) => Results.Ok(await svc.ComplianceAsync(from, to, by, ctx.RequestAborted))).RequirePermission(Permissions.PmRead);
    }
}
