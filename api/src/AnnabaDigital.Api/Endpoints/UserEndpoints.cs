using AnnabaDigital.Api.Http;
using AnnabaDigital.Application.Users;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Api.Endpoints;

public sealed record ReasonBody(string? Reason = null);
public sealed record RolesBody(Guid[]? RoleIds);
public sealed record BulkBody(Guid[]? Ids, string? Action);

public static class UserEndpoints
{
    /// <summary>Several permissions on one endpoint are ANDed (each is its own authorization policy).</summary>
    private static TBuilder RequireAll<TBuilder>(this TBuilder b, params string[] permissions) where TBuilder : IEndpointConventionBuilder
    {
        foreach (var p in permissions) if (!Permissions.All.Contains(p)) throw new ArgumentException($"Unknown permission '{p}'.");
        return b.RequireAuthorization([.. permissions.Select(PermissionPolicy.Name)]);
    }

    public static void MapUsers(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/users");

        g.MapGet("/", async (string? q, string? status, string? role, bool? verified, string? sort, string? dir, int? page, int? pageSize, UserAdminService svc, HttpContext ctx)
            => Results.Ok(await svc.ListAsync(new UserQuery(q, status, role, verified, sort, dir, page ?? 1, pageSize ?? 10), ctx.RequirePrincipal(), ctx.RequestAborted))).RequirePermission(Permissions.UsersRead);

        g.MapGet("/stats", async (UserAdminService svc, HttpContext ctx) => Results.Ok(await svc.StatsAsync(ctx.RequirePrincipal(), ctx.RequestAborted))).RequirePermission(Permissions.UsersRead);

        g.MapPost("/", async (CreateUserRequest body, UserAdminService svc, HttpContext ctx) =>
        {
            var u = await svc.CreateAsync(body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted);
            return Results.Created($"/api/users/{u.Id}", u);
        }).RequireAll(Permissions.UsersCreate, Permissions.UsersAssignRole);

        g.MapPost("/import", async (ImportUsersRequest body, UserImportService svc, HttpContext ctx)
            => Results.Ok(await svc.ImportAsync(body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequireAll(Permissions.UsersCreate, Permissions.UsersAssignRole);

        g.MapGet("/{id:guid}", async (Guid id, UserAdminService svc, HttpContext ctx) => Results.Ok(await svc.GetAsync(id, ctx.RequestAborted))).RequirePermission(Permissions.UsersRead);

        g.MapPut("/{id:guid}/manager", async (Guid id, SetManagerRequest body, UserAdminService svc, HttpContext ctx)
            => Results.Ok(await svc.SetManagerAsync(id, body.ManagerId, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.UsersUpdate);
        g.MapPatch("/{id:guid}", async (Guid id, UpdateUserRequest body, UserAdminService svc, HttpContext ctx)
            => Results.Ok(await svc.UpdateAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.UsersUpdate);

        foreach (var action in new[] { "suspend", "deactivate", "activate" })
            g.MapPost($"/{{id:guid}}/{action}", async (Guid id, ReasonBody? body, UserAdminService svc, HttpContext ctx)
                => Results.Ok(await svc.SetStatusAsync(id, action, body?.Reason, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.UsersSuspend);

        g.MapPost("/{id:guid}/reset-mfa", async (Guid id, ReasonBody? body, UserAdminService svc, HttpContext ctx) =>
        {
            await svc.ResetMfaAsync(id, body?.Reason, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return Results.NoContent();
        }).RequirePermission(Permissions.UsersResetCredentials);
        g.MapPost("/{id:guid}/require-password-reset", async (Guid id, ReasonBody? body, UserAdminService svc, HttpContext ctx) =>
        {
            await svc.RequirePasswordResetAsync(id, body?.Reason, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return Results.NoContent();
        }).RequirePermission(Permissions.UsersResetCredentials);
        g.MapPost("/{id:guid}/unlock", async (Guid id, UserAdminService svc, HttpContext ctx) => Results.Ok(await svc.UnlockAsync(id, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted)))
            .RequirePermission(Permissions.UsersSuspend);

        g.MapPost("/{id:guid}/resend-verification", async (Guid id, UserAdminService svc, HttpContext ctx) =>
        {
            await svc.ResendVerificationAsync(id, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted);
            return Results.Accepted();
        }).RequirePermission(Permissions.UsersUpdate);

        g.MapPut("/{id:guid}/roles", async (Guid id, RolesBody body, UserAdminService svc, HttpContext ctx)
            => Results.Ok(await svc.SetRolesAsync(id, body.RoleIds ?? [], ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.UsersAssignRole);

        g.MapGet("/{id:guid}/sessions", async (Guid id, UserAdminService svc, HttpContext ctx) => Results.Ok(await svc.SessionsAsync(id, ctx.RequestAborted))).RequirePermission(Permissions.SessionsReadAll);

        g.MapDelete("/{id:guid}/sessions", async (Guid id, Guid? sessionId, UserAdminService svc, HttpContext ctx)
            => Results.Ok(new { revoked = await svc.RevokeSessionsAsync(id, sessionId, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted) })).RequirePermission(Permissions.SessionsRevoke);

        g.MapGet("/{id:guid}/activity", async (Guid id, UserAdminService svc, HttpContext ctx) => Results.Ok(await svc.ActivityAsync(id, ctx.RequestAborted))).RequirePermission(Permissions.AuditRead);

        // Base permission only; the service checks the per-action permission (suspend ⇒ users.suspend, resend ⇒ users.update).
        g.MapPost("/bulk", async (BulkBody body, UserAdminService svc, HttpContext ctx)
            => Results.Ok(new { results = await svc.BulkAsync(body.Ids ?? [], body.Action, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted) })).RequirePermission(Permissions.UsersRead);

        var inv = app.MapGroup("/api/invitations");
        inv.MapGet("/", async (string? status, InvitationAdminService svc, HttpContext ctx) => Results.Ok(await svc.ListAsync(status, ctx.RequestAborted))).RequirePermission(Permissions.InvitationsRead);
        inv.MapPost("/", async (CreateInvitationRequest body, InvitationAdminService svc, HttpContext ctx) =>
        {
            var i = await svc.CreateAsync(body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted);
            return Results.Created($"/api/invitations/{i.Id}", i);
        }).RequirePermission(Permissions.InvitationsManage);
        inv.MapPost("/{id:guid}/resend", async (Guid id, InvitationAdminService svc, HttpContext ctx) => Results.Ok(await svc.ResendAsync(id, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted)))
            .RequirePermission(Permissions.InvitationsManage);
        inv.MapPost("/{id:guid}/revoke", async (Guid id, InvitationAdminService svc, HttpContext ctx) =>
        {
            await svc.RevokeAsync(id, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted);
            return Results.NoContent();
        }).RequirePermission(Permissions.InvitationsManage);
    }
}
