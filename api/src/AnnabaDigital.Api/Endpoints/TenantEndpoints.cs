using AnnabaDigital.Api.Http;
using AnnabaDigital.Application.Users;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Api.Endpoints;

public static class TenantEndpoints
{
    public static void MapTenants(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/tenants");
        g.MapGet("/", async (TenantService svc, HttpContext ctx) => Results.Ok(await svc.ListAsync(ctx.RequirePrincipal(), ctx.RequestAborted))).RequirePermission(Permissions.TenantsManage);
        g.MapPost("/", async (CreateTenantRequest body, TenantService svc, HttpContext ctx)
            => Results.Created("/api/tenants", await svc.CreateAsync(body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.TenantsManage);
        g.MapPost("/{id:guid}/suspend", async (Guid id, TenantService svc, HttpContext ctx) =>
        {
            await svc.SetStatusAsync(id, true, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return Results.NoContent();
        }).RequirePermission(Permissions.TenantsManage);
        g.MapPost("/{id:guid}/support", async (Guid id, TenantService svc, HttpContext ctx) =>
        {
            await svc.EnterSupportAsync(id, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return Results.NoContent();
        }).RequirePermission(Permissions.TenantsManage);
        g.MapPost("/support/exit", async (TenantService svc, HttpContext ctx) =>
        {
            await svc.ExitSupportAsync(ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return Results.NoContent();
        }).RequirePermission(Permissions.TenantsManage);
        g.MapPost("/{id:guid}/activate", async (Guid id, TenantService svc, HttpContext ctx) =>
        {
            await svc.SetStatusAsync(id, false, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return Results.NoContent();
        }).RequirePermission(Permissions.TenantsManage);
    }
}
