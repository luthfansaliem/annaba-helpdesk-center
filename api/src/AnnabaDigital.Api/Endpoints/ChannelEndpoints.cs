using AnnabaDigital.Api.Http;
using AnnabaDigital.Application.Helpdesk;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Api.Endpoints;

/// <summary>NTF-003. The organisation's channel set-up and delivery log (<c>integrations.manage</c>), and each person's own channels (signed in).</summary>
public static class ChannelEndpoints
{
    public static void MapChannels(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/channels");
        g.MapGet("/settings", async (ChannelService svc, HttpContext ctx) => Results.Ok(await svc.SettingsAsync(ctx.RequestAborted))).RequirePermission(Permissions.IntegrationsManage);
        g.MapPut("/settings/{kind}", async (string kind, SaveChannelSettingRequest body, ChannelService svc, HttpContext ctx) => Results.Ok(await svc.SaveSettingAsync(kind, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.IntegrationsManage);
        g.MapGet("/deliveries", async (string? kind, ChannelService svc, HttpContext ctx) => Results.Ok(await svc.DeliveriesAsync(kind, ctx.RequestAborted))).RequirePermission(Permissions.IntegrationsManage);

        var me = app.MapGroup("/api/me/channels").RequireAuthorization();
        me.MapGet("/", async (ChannelService svc, HttpContext ctx) => Results.Ok(await svc.MineAsync(ctx.RequirePrincipal().UserId, ctx.RequestAborted)));
        me.MapPost("/{kind}", async (string kind, ConnectChannelRequest body, ChannelService svc, HttpContext ctx) => Results.Ok(await svc.ConnectAsync(kind, body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted)));
        me.MapPost("/whatsapp/verify", async (VerifyChannelRequest body, ChannelService svc, HttpContext ctx) => Results.Ok(await svc.VerifyAsync(body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted)));
        me.MapPut("/{kind}", async (string kind, UpdateMyChannelRequest body, ChannelService svc, HttpContext ctx) => Results.Ok(await svc.UpdateAsync(kind, body, ctx.RequirePrincipal(), ctx.RequestAborted)));
        me.MapDelete("/{kind}", async (string kind, ChannelService svc, HttpContext ctx) => Results.Ok(await svc.RemoveAsync(kind, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted)));
        me.MapPost("/{kind}/test", async (string kind, ChannelService svc, HttpContext ctx) => { await svc.TestAsync(kind, ctx.RequirePrincipal(), ctx.RequestAborted); return Results.NoContent(); });
    }
}
