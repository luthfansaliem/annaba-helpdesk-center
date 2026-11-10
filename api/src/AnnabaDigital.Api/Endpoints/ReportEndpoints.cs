using AnnabaDigital.Api.Http;
using AnnabaDigital.Application.Helpdesk;

namespace AnnabaDigital.Api.Endpoints;

/// <summary>Operations and SLA reports (RPT-001/002). Any signed-in session may call them; the service shows only the tickets the person may see and refuses anyone without a ticket read permission (403).</summary>
public static class ReportEndpoints
{
    public static void MapReports(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/reports");
        g.MapGet("/operations", async (int? days, ReportService svc, HttpContext ctx) => Results.Ok(await svc.OperationsAsync(days ?? 30, ctx.RequirePrincipal(), ctx.RequestAborted))).RequireAuthorization();
        g.MapGet("/csat", async (int? days, string? by, RatingService svc, HttpContext ctx) => Results.Ok(await svc.ReportAsync(days ?? 30, by, ctx.RequirePrincipal(), ctx.RequestAborted))).RequireAuthorization();
        app.MapPut("/api/tickets/{id:guid}/rating", async (Guid id, RateRequest body, RatingService svc, HttpContext ctx) => Results.Ok(await svc.RateAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequireAuthorization();
        g.MapGet("/sla", async (int? days, ReportService svc, HttpContext ctx) => Results.Ok(await svc.SlaAsync(days ?? 30, ctx.RequirePrincipal(), ctx.RequestAborted))).RequireAuthorization();
    }
}
