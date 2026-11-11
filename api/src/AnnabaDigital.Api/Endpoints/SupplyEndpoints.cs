using AnnabaDigital.Api.Http;
using AnnabaDigital.Application.Helpdesk;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Api.Endpoints;

/// <summary>INV-002. Anyone signed in asks for supplies and sees their own requests (no permission, like raising a ticket; vendor accounts are kept out by the
/// vendor portal gate). The requester's manager decides requests that need approval. <c>supplies.fulfil</c> sees the desk, issues stock and closes back-orders;
/// <c>inventory.manage</c> sets the approval amount and the default place.</summary>
public static class SupplyEndpoints
{
    public sealed record DecideSupply(bool Approve, string? Note);
    public sealed record CloseSupply(string? Note);

    public static void MapSupplies(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/supplies").RequireAuthorization();
        g.MapGet("/catalog", async (Guid? locationId, string? q, SupplyService svc, HttpContext ctx) => Results.Ok(await svc.CatalogAsync(locationId, q, ctx.RequestAborted)));
        g.MapGet("/requests", async (string? scope, string? status, int? page, int? pageSize, SupplyService svc, HttpContext ctx)
            => Results.Ok(await svc.ListAsync(scope, status, page ?? 1, pageSize ?? 25, ctx.RequirePrincipal(), ctx.RequestAborted)));
        g.MapGet("/requests/{id:guid}", async (Guid id, SupplyService svc, HttpContext ctx) => Results.Ok(await svc.GetAsync(id, ctx.RequirePrincipal(), ctx.RequestAborted)));
        g.MapPost("/requests", async (CreateSupplyRequest body, SupplyService svc, HttpContext ctx) =>
        {
            var r = await svc.CreateAsync(body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted);
            return Results.Created($"/api/supplies/requests/{r.Id}", r);
        });
        g.MapPost("/requests/{id:guid}/cancel", async (Guid id, SupplyService svc, HttpContext ctx) => Results.Ok(await svc.CancelAsync(id, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted)));
        g.MapPost("/requests/{id:guid}/decide", async (Guid id, DecideSupply body, SupplyService svc, HttpContext ctx) => Results.Ok(await svc.DecideAsync(id, body.Approve, body.Note, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted)));
        g.MapPost("/requests/{id:guid}/fulfil", async (Guid id, FulfilSupplyRequest body, SupplyService svc, HttpContext ctx) => Results.Ok(await svc.FulfilAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.SuppliesFulfil);
        g.MapPost("/requests/{id:guid}/close", async (Guid id, CloseSupply body, SupplyService svc, HttpContext ctx) => Results.Ok(await svc.CloseAsync(id, body.Note, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.SuppliesFulfil);
        g.MapGet("/summary", async (SupplyService svc, HttpContext ctx) => Results.Ok(await svc.SummaryAsync(ctx.RequestAborted))).RequirePermission(Permissions.SuppliesFulfil);
        g.MapGet("/settings", async (SupplyService svc, HttpContext ctx) => Results.Ok(await svc.SettingsAsync(ctx.RequestAborted)));
        g.MapPut("/settings", async (SupplySettingsDto body, SupplyService svc, HttpContext ctx) => Results.Ok(await svc.SaveSettingsAsync(body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.InventoryManage);
    }
}
