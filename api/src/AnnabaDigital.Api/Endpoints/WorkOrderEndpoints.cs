using Microsoft.AspNetCore.Mvc;
using AnnabaDigital.Api.Http;
using AnnabaDigital.Application.Helpdesk;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Api.Endpoints;

/// <summary>TKT-006/007/014. Work orders, their checklist, time, costs and photos, and checklist templates. Everything needs <c>workorders.read</c>; the service checks the rest per work order.</summary>
public static class WorkOrderEndpoints
{
    public static void MapWorkOrders(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/tickets/{id:guid}/work-orders", async (Guid id, WorkOrderService svc, HttpContext ctx) => Results.Ok(await svc.ForTicketAsync(id, ctx.RequirePrincipal(), ctx.RequestAborted))).RequirePermission(Permissions.WorkOrdersRead);
        app.MapPost("/api/tickets/{id:guid}/work-orders", async (Guid id, SaveWorkOrderRequest body, WorkOrderService svc, HttpContext ctx) =>
        {
            var w = await svc.CreateAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted);
            return Results.Created($"/api/work-orders/{w.Id}", w);
        }).RequirePermission(Permissions.WorkOrdersManage);
        app.MapPut("/api/tickets/{id:guid}/work-orders/settings", async (Guid id, ResolveSettingRequest body, WorkOrderService svc, HttpContext ctx)
            => Results.Ok(await svc.SetResolveAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.WorkOrdersManage);

        var g = app.MapGroup("/api/work-orders");
        g.MapGet("/", async (bool? mine, string? status, int? page, int? pageSize, WorkOrderService svc, HttpContext ctx)
            => Results.Ok(await svc.ListAsync(mine ?? false, status, page ?? 1, pageSize ?? 25, ctx.RequirePrincipal(), ctx.RequestAborted))).RequirePermission(Permissions.WorkOrdersRead);
        g.MapGet("/{id:guid}", async (Guid id, WorkOrderService svc, HttpContext ctx) => Results.Ok(await svc.GetAsync(id, ctx.RequirePrincipal(), ctx.RequestAborted))).RequirePermission(Permissions.WorkOrdersRead);
        g.MapPut("/{id:guid}", async (Guid id, SaveWorkOrderRequest body, WorkOrderService svc, HttpContext ctx) => Results.Ok(await svc.UpdateAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.WorkOrdersManage);
        g.MapPost("/{id:guid}/action", async (Guid id, WorkOrderActionRequest body, WorkOrderService svc, HttpContext ctx) => Results.Ok(await svc.ActAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.WorkOrdersRead);
        g.MapPost("/{id:guid}/tasks/{taskId:guid}", async (Guid id, Guid taskId, TickRequest body, WorkOrderService svc, HttpContext ctx) => Results.Ok(await svc.TickAsync(id, taskId, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.WorkOrdersComplete);
        g.MapPost("/{id:guid}/time", async (Guid id, LogTimeRequest body, WorkOrderService svc, HttpContext ctx) => Results.Ok(await svc.LogTimeAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.WorkOrdersComplete);
        g.MapDelete("/{id:guid}/time/{timeId:guid}", async (Guid id, Guid timeId, WorkOrderService svc, HttpContext ctx) => Results.Ok(await svc.DeleteTimeAsync(id, timeId, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.WorkOrdersRead);
        g.MapPost("/{id:guid}/costs", async (Guid id, AddCostRequest body, WorkOrderService svc, HttpContext ctx) => Results.Ok(await svc.AddCostAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.WorkOrdersCosts);
        g.MapDelete("/{id:guid}/costs/{costId:guid}", async (Guid id, Guid costId, WorkOrderService svc, HttpContext ctx) => Results.Ok(await svc.DeleteCostAsync(id, costId, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.WorkOrdersCosts);
        g.MapPost("/{id:guid}/files", async (Guid id, string? stage, IFormFile file, WorkOrderService svc, HttpContext ctx) =>
        {
            await using var s = file.OpenReadStream();
            return Results.Ok(await svc.AddFileAsync(id, stage, file.FileName, s, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted));
        }).RequirePermission(Permissions.WorkOrdersComplete).DisableAntiforgery().WithMetadata(new RequestSizeLimitAttribute(10 * 1024 * 1024));
        g.MapGet("/{id:guid}/files/{fileId:guid}", async (Guid id, Guid fileId, WorkOrderService svc, HttpContext ctx) =>
        {
            var d = await svc.OpenFileAsync(id, fileId, ctx.RequirePrincipal(), ctx.RequestAborted);
            ctx.Response.Headers.ContentSecurityPolicy = "default-src 'none'; sandbox";
            ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
            ctx.Response.Headers.ContentDisposition = new Microsoft.Net.Http.Headers.ContentDispositionHeaderValue(d.Inline ? "inline" : "attachment") { FileNameStar = d.FileName }.ToString();
            return Results.Stream(d.Content, d.ContentType);
        }).RequirePermission(Permissions.WorkOrdersRead);

        var tpl = app.MapGroup("/api/checklist-templates");
        tpl.MapGet("/", async (bool? includeArchived, WorkOrderService svc, HttpContext ctx) => Results.Ok(await svc.TemplatesAsync(includeArchived ?? false, ctx.RequestAborted))).RequirePermission(Permissions.WorkOrdersRead);
        tpl.MapPost("/", async (SaveTemplateRequest body, WorkOrderService svc, HttpContext ctx) =>
        {
            var t = await svc.SaveTemplateAsync(null, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted);
            return Results.Created($"/api/checklist-templates/{t.Id}", t);
        }).RequirePermission(Permissions.WorkOrdersManage);
        tpl.MapPut("/{id:guid}", async (Guid id, SaveTemplateRequest body, WorkOrderService svc, HttpContext ctx) => Results.Ok(await svc.SaveTemplateAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.WorkOrdersManage);
        tpl.MapPost("/{id:guid}/archive", async (Guid id, WorkOrderService svc, HttpContext ctx) => { await svc.SetTemplateArchivedAsync(id, true, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return Results.NoContent(); }).RequirePermission(Permissions.WorkOrdersManage);
        tpl.MapPost("/{id:guid}/restore", async (Guid id, WorkOrderService svc, HttpContext ctx) => { await svc.SetTemplateArchivedAsync(id, false, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return Results.NoContent(); }).RequirePermission(Permissions.WorkOrdersManage);
    }
}
