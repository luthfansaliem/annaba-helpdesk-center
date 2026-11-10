using AnnabaDigital.Api.Http;
using AnnabaDigital.Application.Helpdesk;

namespace AnnabaDigital.Api.Endpoints;

/// <summary>Go-live checklist and quick start (ADM-001). Signed-in only; the service checks that the person configures the helpdesk (status) or holds every permission the quick start uses.</summary>
public static class SetupEndpoints
{
    public static void MapSetup(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/setup");
        g.MapGet("/status", async (SetupService svc, HttpContext ctx) => Results.Ok(await svc.StatusAsync(ctx.RequirePrincipal(), ctx.RequestAborted))).RequireAuthorization();
        g.MapPost("/quick-start", async (QuickStartRequest body, SetupService svc, HttpContext ctx) => Results.Ok(await svc.QuickStartAsync(body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequireAuthorization();
    }
}
