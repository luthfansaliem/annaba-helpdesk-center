using AnnabaDigital.Api.Http;
using AnnabaDigital.Application.Helpdesk;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Api.Endpoints;

/// <summary>SLA policies (<c>/api/sla</c>) and assignment rules (<c>/api/assignment-rules</c>). Every route declares exactly one permission.</summary>
public static class SlaAndRuleEndpoints
{
    public static void MapSlaAndRules(this IEndpointRouteBuilder app)
    {
        var sla = app.MapGroup("/api/sla");
        sla.MapGet("/policies", async (bool? includeArchived, SlaService svc, HttpContext ctx) => Results.Ok(await svc.ListAsync(includeArchived ?? false, ctx.RequestAborted))).RequirePermission(Permissions.SlaRead);
        sla.MapPost("/policies", async (SaveSlaPolicyRequest body, SlaService svc, HttpContext ctx) =>
        {
            var p = await svc.CreateAsync(body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted);
            return Results.Created($"/api/sla/policies/{p.Id}", p);
        }).RequirePermission(Permissions.SlaManage);
        sla.MapPatch("/policies/{id:guid}", async (Guid id, SaveSlaPolicyRequest body, SlaService svc, HttpContext ctx)
            => Results.Ok(await svc.UpdateAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.SlaManage);
        sla.MapPost("/policies/{id:guid}/archive", async (Guid id, SlaService svc, HttpContext ctx)
            => Results.Ok(await svc.ArchiveAsync(id, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.SlaManage);
        sla.MapPost("/policies/{id:guid}/restore", async (Guid id, SlaService svc, HttpContext ctx)
            => Results.Ok(await svc.RestoreAsync(id, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.SlaManage);
        sla.MapPost("/policies/reorder", async (ReorderRequest body, SlaService svc, HttpContext ctx)
            => Results.Ok(await svc.ReorderAsync(body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.SlaManage);
        sla.MapPost("/preview", async (RulePreviewRequest body, SlaService svc, HttpContext ctx) => Results.Ok(await svc.PreviewAsync(body, ctx.RequestAborted))).RequirePermission(Permissions.SlaRead);

        var rules = app.MapGroup("/api/assignment-rules");
        rules.MapGet("/", async (bool? includeArchived, AssignmentService svc, HttpContext ctx) => Results.Ok(await svc.ListAsync(includeArchived ?? false, ctx.RequestAborted))).RequirePermission(Permissions.RulesRead);
        rules.MapPost("/", async (SaveAssignmentRuleRequest body, AssignmentService svc, HttpContext ctx) =>
        {
            var r = await svc.CreateAsync(body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted);
            return Results.Created($"/api/assignment-rules/{r.Id}", r);
        }).RequirePermission(Permissions.RulesManage);
        rules.MapPatch("/{id:guid}", async (Guid id, SaveAssignmentRuleRequest body, AssignmentService svc, HttpContext ctx)
            => Results.Ok(await svc.UpdateAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.RulesManage);
        rules.MapPost("/{id:guid}/archive", async (Guid id, AssignmentService svc, HttpContext ctx)
            => Results.Ok(await svc.ArchiveAsync(id, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.RulesManage);
        rules.MapPost("/{id:guid}/restore", async (Guid id, AssignmentService svc, HttpContext ctx)
            => Results.Ok(await svc.RestoreAsync(id, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.RulesManage);
        rules.MapPost("/reorder", async (ReorderRequest body, AssignmentService svc, HttpContext ctx)
            => Results.Ok(await svc.ReorderAsync(body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.RulesManage);
        rules.MapPost("/preview", async (RulePreviewRequest body, AssignmentService svc, HttpContext ctx) => Results.Ok(await svc.PreviewAsync(body, ctx.RequestAborted))).RequirePermission(Permissions.RulesRead);
    }
}
