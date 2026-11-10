using AnnabaDigital.Api.Http;
using AnnabaDigital.Application.Email;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Api.Endpoints;

/// <summary>NTF-002. An organisation's own e-mail wording. Needs <c>settings.manage</c>; saving also needs a fresh password check.</summary>
public static class EmailWordingEndpoints
{
    public static void MapEmailWording(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/settings/email-wording");
        g.MapGet("/", async (EmailWordingService svc, HttpContext ctx) => Results.Ok(await svc.GetAsync(ctx.RequestAborted))).RequirePermission(Permissions.SettingsManage);
        g.MapPut("/", async (SaveEmailWording body, EmailWordingService svc, HttpContext ctx) => Results.Ok(await svc.UpdateAsync(body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.SettingsManage);
        g.MapPost("/preview", (SaveEmailWording body, EmailWordingService svc) => Results.Ok(svc.Preview(body))).RequirePermission(Permissions.SettingsManage);
    }
}
