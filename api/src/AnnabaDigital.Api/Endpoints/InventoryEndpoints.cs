using AnnabaDigital.Api.Http;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Helpdesk;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Api.Endpoints;

/// <summary>INV-001/003/004. Reading stock needs <c>inventory.read</c>; items, receipts, issues and transfers need <c>inventory.manage</c>; adjustments and
/// stock-takes need <c>inventory.adjust</c> (the moves route checks which one the kind of movement needs).</summary>
public static class InventoryEndpoints
{
    public static void MapInventory(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/inventory");
        g.MapGet("/items", async (string? q, string? category, Guid? locationId, bool? low, bool? archived, InventoryService svc, HttpContext ctx)
            => Results.Ok(await svc.ListAsync(q, category, locationId, low ?? false, archived ?? false, ctx.RequestAborted))).RequirePermission(Permissions.InventoryRead);
        g.MapGet("/items/{id:guid}", async (Guid id, InventoryService svc, HttpContext ctx) => Results.Ok(await svc.GetAsync(id, ctx.RequestAborted))).RequirePermission(Permissions.InventoryRead);
        g.MapPost("/items", async (SaveInventoryItemRequest body, InventoryService svc, HttpContext ctx) =>
        {
            var i = await svc.SaveAsync(null, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted);
            return Results.Created($"/api/inventory/items/{i.Id}", i);
        }).RequirePermission(Permissions.InventoryManage);
        g.MapPut("/items/{id:guid}", async (Guid id, SaveInventoryItemRequest body, InventoryService svc, HttpContext ctx) => Results.Ok(await svc.SaveAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.InventoryManage);
        g.MapPost("/items/{id:guid}/archive", async (Guid id, InventoryService svc, HttpContext ctx) => Results.Ok(await svc.ArchiveAsync(id, true, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.InventoryManage);
        g.MapPost("/items/{id:guid}/restore", async (Guid id, InventoryService svc, HttpContext ctx) => Results.Ok(await svc.ArchiveAsync(id, false, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.InventoryManage);
        g.MapPost("/items/{id:guid}/moves", async (Guid id, MoveStockRequest body, InventoryService svc, AuthorizationService authz, HttpContext ctx) =>
        {
            var p = ctx.RequirePrincipal();
            var perms = await authz.GetEffectivePermissionsAsync(p.UserId, ctx.RequestAborted);
            return Results.Ok(await svc.MoveAsync(id, body, perms.Contains(Permissions.InventoryManage), perms.Contains(Permissions.InventoryAdjust), p, ctx.Client(), ctx.RequestAborted));
        }).RequirePermission(Permissions.InventoryRead);
        g.MapGet("/movements", async (Guid? itemId, Guid? locationId, int? page, int? pageSize, InventoryService svc, HttpContext ctx) => Results.Ok(await svc.MovementsAsync(itemId, locationId, page ?? 1, pageSize ?? 50, ctx.RequestAborted))).RequirePermission(Permissions.InventoryRead);
        g.MapGet("/reorder", async (InventoryService svc, HttpContext ctx) => Results.Ok(await svc.ReorderAsync(ctx.RequestAborted))).RequirePermission(Permissions.InventoryRead);
        g.MapGet("/summary", async (InventoryService svc, HttpContext ctx) => Results.Ok(await svc.SummaryAsync(ctx.RequestAborted))).RequirePermission(Permissions.InventoryRead);

        g.MapGet("/stock-takes", async (InventoryService svc, HttpContext ctx) => Results.Ok(await svc.TakesAsync(ctx.RequestAborted))).RequirePermission(Permissions.InventoryRead);
        g.MapGet("/stock-takes/{id:guid}", async (Guid id, InventoryService svc, HttpContext ctx) => Results.Ok(await svc.TakeAsync(id, ctx.RequestAborted))).RequirePermission(Permissions.InventoryRead);
        g.MapPost("/stock-takes", async (StartStockTakeRequest body, InventoryService svc, HttpContext ctx) => Results.Ok(await svc.StartTakeAsync(body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.InventoryAdjust);
        g.MapPut("/stock-takes/{id:guid}/counts", async (Guid id, CountRequest body, InventoryService svc, HttpContext ctx) => Results.Ok(await svc.CountAsync(id, body, ctx.RequestAborted))).RequirePermission(Permissions.InventoryAdjust);
        g.MapPost("/stock-takes/{id:guid}/close", async (Guid id, InventoryService svc, HttpContext ctx) => Results.Ok(await svc.CloseTakeAsync(id, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.InventoryAdjust);
    }
}
