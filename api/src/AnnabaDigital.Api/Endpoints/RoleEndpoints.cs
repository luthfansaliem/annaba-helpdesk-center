using AnnabaDigital.Api.Http;
using AnnabaDigital.Application.Roles;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Api.Endpoints;

public static class RoleEndpoints
{
    public static void MapRoles(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/roles");

        g.MapGet("/", async (bool? includeArchived, RoleAdminService svc, HttpContext ctx) => Results.Ok(await svc.ListAsync(includeArchived ?? false, ctx.RequestAborted))).RequirePermission(Permissions.RolesRead);
        g.MapGet("/{id:guid}", async (Guid id, RoleAdminService svc, HttpContext ctx) => Results.Ok(await svc.GetAsync(id, ctx.RequestAborted))).RequirePermission(Permissions.RolesRead);
        g.MapGet("/{id:guid}/users", async (Guid id, int? page, int? pageSize, RoleAdminService svc, HttpContext ctx)
            => Results.Ok(await svc.UsersAsync(id, page ?? 1, pageSize ?? 20, ctx.RequestAborted))).RequirePermission(Permissions.RolesRead);

        // roles.create is the base permission; supplying permissions additionally needs permissions.assign (+ recent reauth) — enforced in the service.
        g.MapPost("/", async (SaveRoleRequest body, RoleAdminService svc, HttpContext ctx) =>
        {
            var r = await svc.CreateAsync(body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted);
            return Results.Created($"/api/roles/{r.Id}", r);
        }).RequirePermission(Permissions.RolesCreate);

        g.MapPatch("/{id:guid}", async (Guid id, SaveRoleRequest body, RoleAdminService svc, HttpContext ctx)
            => Results.Ok(await svc.UpdateAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.RolesUpdate);

        g.MapPost("/{id:guid}/duplicate", async (Guid id, RoleAdminService svc, HttpContext ctx) =>
        {
            var r = await svc.DuplicateAsync(id, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted);
            return Results.Created($"/api/roles/{r.Id}", r);
        }).RequirePermission(Permissions.RolesCreate);

        g.MapPost("/{id:guid}/archive", async (Guid id, RoleAdminService svc, HttpContext ctx) =>
        {
            await svc.ArchiveAsync(id, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted);
            return Results.NoContent();
        }).RequirePermission(Permissions.RolesUpdate);

        app.MapGet("/api/permissions", async (RoleAdminService svc, HttpContext ctx) => Results.Ok(await svc.PermissionsAsync(ctx.RequestAborted))).RequirePermission(Permissions.PermissionsRead);
        app.MapGet("/api/access-matrix", async (RoleAdminService svc, HttpContext ctx) => Results.Ok(await svc.MatrixAsync(ctx.RequirePrincipal(), ctx.RequestAborted))).RequirePermission(Permissions.PermissionsRead);
        app.MapPut("/api/access-matrix", async (SaveMatrixRequest body, RoleAdminService svc, HttpContext ctx)
            => Results.Ok(await svc.SaveMatrixAsync(body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.PermissionsAssign);
    }
}
