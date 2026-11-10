using AnnabaDigital.Api.Http;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Helpdesk;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Api.Endpoints;

/// <summary>
/// Approvals and notifications. Policy administration carries its own permissions; deciding and reading one's own approvals and notifications need only a session
/// (the service checks the person is the approver / the owner). The two <c>link</c> routes are public by design: the unguessable one-time token is the credential,
/// reading never decides, and an unknown, used or expired token all answer the same 410.
/// </summary>
public static class ApprovalEndpoints
{
    public static void MapApprovals(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/approvals");
        g.MapGet("/policies", async (ApprovalPolicyService svc, HttpContext ctx) => Results.Ok(await svc.ListAsync(ctx.RequestAborted))).RequirePermission(Permissions.ApprovalsRead);
        g.MapPut("/policies/{serviceId:guid}", async (Guid serviceId, SaveApprovalPolicyRequest body, ApprovalPolicyService svc, HttpContext ctx)
            => Results.Ok(await svc.SetAsync(serviceId, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.ApprovalsManage);
        g.MapPost("/policies/{serviceId:guid}/remove", async (Guid serviceId, ApprovalPolicyService svc, HttpContext ctx)
            => Results.Ok(await svc.RemoveAsync(serviceId, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.ApprovalsManage);

        g.MapGet("/mine", async (TicketApprovalService svc, HttpContext ctx) => Results.Ok(await svc.MineAsync(ctx.RequirePrincipal().UserId, ctx.RequestAborted))).RequireAuthorization();
        g.MapGet("/mine/count", async (TicketApprovalService svc, HttpContext ctx) => Results.Ok(new { pending = await svc.PendingCountAsync(ctx.RequirePrincipal().UserId, ctx.RequestAborted) })).RequireAuthorization();
        g.MapPost("/{id:guid}/decide", async (Guid id, DecideRequest body, TicketApprovalService svc, HttpContext ctx) =>
        {
            await svc.DecideAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted);
            return Results.NoContent();
        }).RequireAuthorization();

        // reassigning: the approver hands their own waiting approval over; holders of approvals.manage may move anyone's (the service decides)
        g.MapPost("/{id:guid}/reassign", async (Guid id, ReassignRequest body, TicketApprovalService svc, AuthorizationService authz, HttpContext ctx) =>
        {
            var who = ctx.RequirePrincipal();
            var canManage = (await authz.GetEffectivePermissionsAsync(who.UserId, ctx.RequestAborted)).Contains(Permissions.ApprovalsManage);
            await svc.ReassignAsync(id, body, who, canManage, ctx.Client(), ctx.RequestAborted);
            return Results.NoContent();
        }).RequireAuthorization();

        // out-of-office cover (APR-004): a person manages only their own
        g.MapGet("/delegations", async (ApprovalDelegationService svc, HttpContext ctx) => Results.Ok(await svc.MineAsync(ctx.RequirePrincipal().UserId, ctx.RequestAborted))).RequireAuthorization();
        g.MapPost("/delegations", async (SaveDelegationRequest body, ApprovalDelegationService svc, HttpContext ctx) =>
        {
            var r = await svc.CreateAsync(body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted);
            return Results.Created($"/api/approvals/delegations/{r.Delegation.Id}", r);
        }).RequireAuthorization();
        g.MapPost("/delegations/{id:guid}/end", async (Guid id, ApprovalDelegationService svc, HttpContext ctx) => Results.Ok(await svc.RevokeAsync(id, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequireAuthorization();

        g.MapGet("/link/{token}", async (string token, TicketApprovalService svc, HttpContext ctx) => Results.Ok(await svc.LookupAsync(token, ctx.Client(), ctx.RequestAborted))).AllowAnonymous();
        g.MapPost("/link/{token}", async (string token, DecideRequest body, TicketApprovalService svc, HttpContext ctx) => Results.Ok(await svc.DecideByLinkAsync(token, body, ctx.Client(), ctx.RequestAborted))).AllowAnonymous();

        var n = app.MapGroup("/api/notifications");
        n.MapGet("/", async (bool? unread, int? page, int? pageSize, NotificationService svc, HttpContext ctx)
            => Results.Ok(await svc.ListAsync(ctx.RequirePrincipal().UserId, unread ?? false, page ?? 1, pageSize ?? 20, ctx.RequestAborted))).RequireAuthorization();
        // what each person wants to be told, and how (NTF-002); only their own
        n.MapGet("/preferences", async (NotificationService svc, HttpContext ctx) => Results.Ok(await svc.PreferencesAsync(ctx.RequirePrincipal().UserId, ctx.RequestAborted))).RequireAuthorization();
        n.MapPut("/preferences", async (SaveNotificationPrefsRequest body, NotificationService svc, HttpContext ctx) => { var who = ctx.RequirePrincipal(); return Results.Ok(await svc.SetPreferencesAsync(who.UserId, who.Email, body, ctx.Client(), ctx.RequestAborted)); }).RequireAuthorization();
        n.MapPost("/preferences/reset", async (NotificationService svc, HttpContext ctx) => { var who = ctx.RequirePrincipal(); return Results.Ok(await svc.ResetPreferencesAsync(who.UserId, who.Email, ctx.Client(), ctx.RequestAborted)); }).RequireAuthorization();
        n.MapGet("/unread-count", async (NotificationService svc, HttpContext ctx) => Results.Ok(await svc.UnreadAsync(ctx.RequirePrincipal().UserId, ctx.RequestAborted))).RequireAuthorization();
        n.MapPost("/{id:guid}/read", async (Guid id, NotificationService svc, HttpContext ctx) =>
        {
            await svc.MarkReadAsync(ctx.RequirePrincipal().UserId, id, ctx.RequestAborted);
            return Results.NoContent();
        }).RequireAuthorization();
        n.MapPost("/read-all", async (NotificationService svc, HttpContext ctx) => Results.Ok(new { marked = await svc.MarkAllReadAsync(ctx.RequirePrincipal().UserId, ctx.RequestAborted) })).RequireAuthorization();
    }
}
