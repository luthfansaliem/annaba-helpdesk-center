using AnnabaDigital.Api.Http;
using AnnabaDigital.Application.Helpdesk;

namespace AnnabaDigital.Api.Endpoints;

/// <summary>RPT-007. Report exports and schedules. Signed-in people only; every report checks its own permission against the person it is built for, so
/// the routes need nothing more (someone who can see no report gets an empty list and 403 on export).</summary>
public static class ReportScheduleEndpoints
{
    public static void MapReportSchedules(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/reports/catalog", async (ReportExportService svc, HttpContext ctx) => Results.Ok(await svc.AvailableAsync(ctx.RequirePrincipal().UserId, ctx.RequestAborted))).RequireAuthorization();
        app.MapGet("/api/reports/export", async (string? report, int? days, string? format, ReportExportService svc, HttpContext ctx) =>
        {
            var f = await svc.ExportAsync(report, days, format, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted);
            ctx.Response.Headers["X-Report-Rows"] = f.Rows.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (f.Truncated) ctx.Response.Headers["X-Report-Truncated"] = "true";
            ctx.Response.Headers.CacheControl = "no-store";
            return Results.File(f.Content, f.ContentType, f.FileName);
        }).RequireAuthorization();

        var g = app.MapGroup("/api/report-schedules").RequireAuthorization();
        g.MapGet("/", async (ReportExportService svc, HttpContext ctx) => Results.Ok(await svc.ListAsync(ctx.RequirePrincipal(), ctx.RequestAborted)));
        g.MapPost("/", async (SaveReportScheduleRequest body, ReportExportService svc, HttpContext ctx) =>
        {
            var s = await svc.SaveAsync(null, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted);
            return Results.Created($"/api/report-schedules/{s.Id}", s);
        });
        g.MapPut("/{id:guid}", async (Guid id, SaveReportScheduleRequest body, ReportExportService svc, HttpContext ctx) => Results.Ok(await svc.SaveAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted)));
        g.MapDelete("/{id:guid}", async (Guid id, ReportExportService svc, HttpContext ctx) => { await svc.DeleteAsync(id, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return Results.NoContent(); });
        g.MapPost("/{id:guid}/send", async (Guid id, ReportExportService svc, HttpContext ctx) => Results.Ok(await svc.SendNowAsync(id, ctx.RequirePrincipal(), ctx.RequestAborted)));
    }
}
