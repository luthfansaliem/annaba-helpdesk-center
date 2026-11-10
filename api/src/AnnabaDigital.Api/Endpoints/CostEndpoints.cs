using System.Text;
using AnnabaDigital.Api.Http;
using AnnabaDigital.Application.Helpdesk;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Api.Endpoints;

/// <summary>CST-001/002/003, RPT-003. Cost report and export (<c>cost.read</c>), budgets and cost centres (<c>budgets.read</c> / <c>budgets.manage</c>), currency and labour rate.</summary>
public static class CostEndpoints
{
    public static void MapCosts(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/costs");
        g.MapGet("/settings", async (CostService svc, HttpContext ctx) => Results.Ok(await svc.SettingsAsync(ctx.RequestAborted))).RequireAuthorization();
        g.MapPut("/settings", async (CostSettingsDto body, CostService svc, HttpContext ctx) => Results.Ok(await svc.SaveSettingsAsync(body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.BudgetsManage);

        g.MapGet("/report", async (DateOnly? from, DateOnly? to, string? by, CostService svc, HttpContext ctx) => Results.Ok(await svc.ReportAsync(from, to, by, ctx.RequestAborted))).RequirePermission(Permissions.CostRead);
        g.MapGet("/report.csv", async (DateOnly? from, DateOnly? to, string? by, CostService svc, HttpContext ctx) =>
        {
            var csv = await svc.ReportCsvAsync(from, to, by, ctx.RequestAborted);
            ctx.Response.Headers.ContentDisposition = $"attachment; filename=\"costs-{by ?? "month"}.csv\"";
            return Results.Text("﻿" + csv, "text/csv", Encoding.UTF8);
        }).RequirePermission(Permissions.CostRead);

        g.MapGet("/centers", async (CostService svc, HttpContext ctx) => Results.Ok(await svc.CentersAsync(ctx.RequestAborted))).RequirePermission(Permissions.BudgetsRead);
        g.MapPost("/centers", async (SaveCostCenterRequest body, CostService svc, HttpContext ctx) => Results.Ok(await svc.SaveCenterAsync(null, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.BudgetsManage);
        g.MapPut("/centers/{id:guid}", async (Guid id, SaveCostCenterRequest body, CostService svc, HttpContext ctx) => Results.Ok(await svc.SaveCenterAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.BudgetsManage);
        g.MapPost("/centers/{id:guid}/archive", async (Guid id, CostService svc, HttpContext ctx) => { await svc.SetCenterArchivedAsync(id, true, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return Results.NoContent(); }).RequirePermission(Permissions.BudgetsManage);
        g.MapPost("/centers/{id:guid}/restore", async (Guid id, CostService svc, HttpContext ctx) => { await svc.SetCenterArchivedAsync(id, false, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return Results.NoContent(); }).RequirePermission(Permissions.BudgetsManage);

        g.MapGet("/budgets", async (int? year, CostService svc, HttpContext ctx) => Results.Ok(await svc.BudgetsAsync(year, ctx.RequirePrincipal(), ctx.RequestAborted))).RequirePermission(Permissions.BudgetsRead);
        g.MapPost("/budgets", async (SaveBudgetRequest body, CostService svc, HttpContext ctx) => Results.Ok(await svc.SaveBudgetAsync(null, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.BudgetsManage);
        g.MapPut("/budgets/{id:guid}", async (Guid id, SaveBudgetRequest body, CostService svc, HttpContext ctx) => Results.Ok(await svc.SaveBudgetAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.BudgetsManage);
        g.MapDelete("/budgets/{id:guid}", async (Guid id, CostService svc, HttpContext ctx) => { await svc.DeleteBudgetAsync(id, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return Results.NoContent(); }).RequirePermission(Permissions.BudgetsManage);
    }
}
