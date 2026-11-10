using Microsoft.AspNetCore.Mvc;
using AnnabaDigital.Api.Http;
using AnnabaDigital.Application.Helpdesk;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Api.Endpoints;

/// <summary>TKT-004 by e-mail. The mail service's webhook (raw MIME, secret URL) and the mailbox settings (<c>settings.manage</c>).</summary>
public static class InboundEmailEndpoints
{
    public static void MapInboundEmail(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/inbound/email/{token}", async (string token, EmailIntakeService svc, HttpContext ctx) =>
        {
            if (ctx.Request.ContentLength is > EmailIntakeRules.MaxBytes) throw new DomainException(ErrorCodes.Validation, "The message is larger than 10 MB.", 413);
            using var buffer = new MemoryStream();
            var chunk = new byte[81920]; int read;
            while ((read = await ctx.Request.Body.ReadAsync(chunk, ctx.RequestAborted)) > 0)
            {
                if (buffer.Length + read > EmailIntakeRules.MaxBytes) throw new DomainException(ErrorCodes.Validation, "The message is larger than 10 MB.", 413);
                buffer.Write(chunk, 0, read);
            }
            buffer.Position = 0;
            return Results.Ok(await svc.ReceiveAsync(token, buffer, buffer.Length, ctx.RequestAborted));
        }).AllowAnonymous().WithMetadata(new MachineEndpoint()).WithMetadata(new RequestSizeLimitAttribute(11 * 1024 * 1024)).DisableAntiforgery();

        var g = app.MapGroup("/api/settings/inbound-email");
        g.MapGet("/", async (EmailIntakeService svc, HttpContext ctx) => Results.Ok(await svc.GetAsync(ctx.RequestAborted))).RequirePermission(Permissions.SettingsManage);
        g.MapPut("/", async (SaveMailboxRequest body, EmailIntakeService svc, HttpContext ctx) => Results.Ok(await svc.SaveAsync(body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.SettingsManage);
        g.MapPost("/token", async (EmailIntakeService svc, HttpContext ctx) => Results.Ok(await svc.RotateTokenAsync(ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.SettingsManage);
    }
}
