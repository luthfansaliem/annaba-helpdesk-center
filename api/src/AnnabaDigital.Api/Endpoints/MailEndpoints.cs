using AnnabaDigital.Api.Http;
using AnnabaDigital.Application.Helpdesk;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Api.Endpoints;

/// <summary>VIS-004. The mailroom log: everyone sees the mail logged for or by them; logging, collecting, sending and voiding need <c>mail.manage</c>.</summary>
public static class MailEndpoints
{
    public static void MapMail(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/mail");
        g.MapGet("/", async (string? scope, string? direction, string? status, string? q, int? page, int? pageSize, MailService svc, HttpContext ctx)
            => Results.Ok(await svc.ListAsync(scope, direction, status, q, page ?? 1, pageSize ?? 50, ctx.RequirePrincipal(), ctx.RequestAborted))).RequireAuthorization();
        g.MapGet("/summary", async (MailService svc, HttpContext ctx) => Results.Ok(await svc.SummaryAsync(ctx.RequestAborted))).RequirePermission(Permissions.MailManage);
        g.MapPost("/", async (LogMailRequest body, MailService svc, HttpContext ctx) =>
        {
            var m = await svc.LogAsync(body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted);
            return Results.Created($"/api/mail/{m.Id}", m);
        }).RequirePermission(Permissions.MailManage);
        g.MapPost("/{id:guid}/collect", async (Guid id, CollectMailRequest body, MailService svc, HttpContext ctx) => Results.Ok(await svc.CollectAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.MailManage);
        g.MapPost("/{id:guid}/send", async (Guid id, SendMailRequest body, MailService svc, HttpContext ctx) => Results.Ok(await svc.SendAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.MailManage);
        g.MapPost("/{id:guid}/void", async (Guid id, CollectMailRequest body, MailService svc, HttpContext ctx) => Results.Ok(await svc.VoidAsync(id, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.MailManage);
    }
}
