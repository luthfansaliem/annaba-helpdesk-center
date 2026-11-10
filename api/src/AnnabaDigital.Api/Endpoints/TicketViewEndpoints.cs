using AnnabaDigital.Api.Http;
using AnnabaDigital.Application.Helpdesk;

namespace AnnabaDigital.Api.Endpoints;

/// <summary>Saved queue filters (TKT-011). Signed-in only; the service requires queue access, keeps personal views private and lets staff who assign tickets share one.</summary>
public static class TicketViewEndpoints
{
    public static void MapTicketViews(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/ticket-views");
        g.MapGet("/", async (TicketViewService svc, HttpContext ctx) => Results.Ok(await svc.ListAsync(ctx.RequirePrincipal(), ctx.RequestAborted))).RequireAuthorization();
        g.MapPost("/", async (SaveViewRequest body, TicketViewService svc, HttpContext ctx) =>
        {
            var v = await svc.CreateAsync(body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted);
            return Results.Created($"/api/ticket-views/{v.Id}", v);
        }).RequireAuthorization();
        g.MapPatch("/{id:guid}", async (Guid id, SaveViewRequest body, TicketViewService svc, HttpContext ctx) => Results.Ok(await svc.UpdateAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequireAuthorization();
        g.MapDelete("/{id:guid}", async (Guid id, TicketViewService svc, HttpContext ctx) => { await svc.DeleteAsync(id, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return Results.NoContent(); }).RequireAuthorization();
    }
}
