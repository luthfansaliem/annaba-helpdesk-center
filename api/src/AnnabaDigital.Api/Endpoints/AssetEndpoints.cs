using AnnabaDigital.Api.Http;
using AnnabaDigital.Application.Helpdesk;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Api.Endpoints;

/// <summary>AST-001/003/004/006. The asset register and asset types. Reading needs <c>assets.read</c>, changing <c>assets.manage</c>, importing <c>assets.import</c>.</summary>
public static class AssetEndpoints
{
    public static void MapAssets(this IEndpointRouteBuilder app)
    {
        var types = app.MapGroup("/api/asset-types");
        types.MapGet("/", async (bool? includeArchived, AssetService svc, HttpContext ctx) => Results.Ok(await svc.TypesAsync(includeArchived ?? false, ctx.RequestAborted))).RequirePermission(Permissions.AssetsRead);
        types.MapPost("/", async (SaveAssetTypeRequest body, AssetService svc, HttpContext ctx) =>
        {
            var t = await svc.CreateTypeAsync(body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted);
            return Results.Created($"/api/asset-types/{t.Id}", t);
        }).RequirePermission(Permissions.AssetsManage);
        types.MapPut("/{id:guid}", async (Guid id, SaveAssetTypeRequest body, AssetService svc, HttpContext ctx) => Results.Ok(await svc.UpdateTypeAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.AssetsManage);
        types.MapPost("/{id:guid}/archive", async (Guid id, AssetService svc, HttpContext ctx) => Results.Ok(await svc.SetTypeArchivedAsync(id, true, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.AssetsManage);
        types.MapPost("/{id:guid}/restore", async (Guid id, AssetService svc, HttpContext ctx) => Results.Ok(await svc.SetTypeArchivedAsync(id, false, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.AssetsManage);

        var g = app.MapGroup("/api/assets");
        g.MapGet("/", async (string? q, Guid? typeId, string? status, Guid? locationId, Guid? assignedToId, int? warrantyWithinDays, bool? frequent, int? page, int? pageSize, AssetService svc, HttpContext ctx)
            => Results.Ok(await svc.ListAsync(new AssetQuery(q, typeId, status, locationId, assignedToId, warrantyWithinDays, page ?? 1, pageSize ?? 25, frequent), ctx.RequestAborted))).RequirePermission(Permissions.AssetsRead);
        g.MapGet("/summary", async (AssetService svc, HttpContext ctx) => Results.Ok(await svc.SummaryAsync(ctx.RequestAborted))).RequirePermission(Permissions.AssetsRead);
        // AST-002: what a scanned label (tag) or a "report a problem" link (id) points to. Anyone signed in to the organisation may read this much.
        g.MapGet("/card", async (string? tag, Guid? id, AssetService svc, HttpContext ctx) => Results.Ok(await svc.CardAsync(tag, id, ctx.RequirePrincipal(), ctx.RequestAborted))).RequireAuthorization();
        g.MapGet("/labels", async (string? ids, string? q, Guid? typeId, string? status, Guid? locationId, AssetService svc, HttpContext ctx) =>
        {
            var list = (ids ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(x => Guid.TryParse(x, out var g) ? g : Guid.Empty).Where(g => g != Guid.Empty).Take(AssetService.MaxLabels).ToList();
            return Results.Ok(await svc.LabelsAsync(new AssetQuery(q, typeId, status, locationId), list, ctx.RequestAborted));
        }).RequirePermission(Permissions.AssetsRead);
        // for the ticket's asset picker: whoever may edit tickets may choose an asset, without needing the register itself
        g.MapGet("/pick", async (string? q, AssetService svc, HttpContext ctx) => Results.Ok(await svc.PickAsync(q, ctx.RequestAborted))).RequirePermission(Permissions.TicketsUpdate);
        g.MapGet("/export", async (string? q, Guid? typeId, string? status, Guid? locationId, Guid? assignedToId, int? warrantyWithinDays, bool? frequent, AssetService svc, HttpContext ctx) =>
        {
            var r = await svc.ExportAsync(new AssetQuery(q, typeId, status, locationId, assignedToId, warrantyWithinDays, Frequent: frequent), ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted);
            ctx.Response.Headers.CacheControl = "no-store";
            ctx.Response.Headers["X-Export-Truncated"] = r.Truncated ? "true" : "false";
            return Results.File(System.Text.Encoding.UTF8.GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(r.Content)).ToArray(), "text/csv; charset=utf-8", r.FileName);
        }).RequirePermission(Permissions.AssetsRead);
        g.MapPost("/import", async (ImportRequest body, AssetService svc, HttpContext ctx) => Results.Ok(await svc.ImportAsync(body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.AssetsImport);
        g.MapGet("/{id:guid}", async (Guid id, AssetService svc, HttpContext ctx) => Results.Ok(await svc.GetAsync(id, ctx.RequirePrincipal(), ctx.RequestAborted))).RequirePermission(Permissions.AssetsRead);
        g.MapPost("/", async (SaveAssetRequest body, AssetService svc, HttpContext ctx) =>
        {
            var a = await svc.CreateAsync(body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted);
            return Results.Created($"/api/assets/{a.Id}", a);
        }).RequirePermission(Permissions.AssetsManage);
        g.MapPut("/{id:guid}", async (Guid id, SaveAssetRequest body, AssetService svc, HttpContext ctx) => Results.Ok(await svc.UpdateAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.AssetsManage);
        g.MapPost("/{id:guid}/retire", async (Guid id, RetireAssetRequest body, AssetService svc, HttpContext ctx) => Results.Ok(await svc.RetireAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.AssetsManage);
        g.MapPost("/{id:guid}/restore", async (Guid id, AssetService svc, HttpContext ctx) => Results.Ok(await svc.RestoreAsync(id, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.AssetsManage);
    }
}
