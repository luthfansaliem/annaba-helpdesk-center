using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;
using AnnabaDigital.Api.Http;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Monitoring;
using AnnabaDigital.Application.Profile;
using AnnabaDigital.Application.Settings;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Api.Endpoints;

public sealed record NewPasswordBody(string? NewPassword);
public sealed record NewEmailBody(string? NewEmail);
public sealed record SecretBody(string? Value);

/// <summary>Sessions, monitoring, dashboards, profile and settings.</summary>
public static class SelfServiceEndpoints
{
    public static void MapSelfService(this IEndpointRouteBuilder app)
    {
        // ------------------------------------------------------------ my sessions (any signed-in user)
        var s = app.MapGroup("/api/sessions");
        s.MapGet("/me", async (SessionsService svc, HttpContext ctx) => Results.Ok(await svc.MineAsync(ctx.RequirePrincipal(), ctx.RequestAborted))).RequireAuthorization();
        s.MapDelete("/me/{id:guid}", async (Guid id, SessionsService svc, HttpContext ctx, IOptions<ApiOptions> api) =>
        {
            var current = await svc.RevokeOneAsync(ctx.RequirePrincipal(), id, ctx.Client(), ctx.RequestAborted);
            if (current) CookieHelper.Clear(ctx, api.Value);
            return Results.Ok(new { revoked = 1, signedOut = current });
        }).RequireAuthorization();
        // ?except=current → revoke the others; otherwise sign out everywhere (including this browser)
        s.MapDelete("/me", async (string? except, SessionsService svc, HttpContext ctx, IOptions<ApiOptions> api) =>
        {
            var who = ctx.RequirePrincipal();
            if (string.Equals(except, "current", StringComparison.OrdinalIgnoreCase)) return Results.Ok(new { revoked = await svc.RevokeOthersAsync(who, ctx.Client(), ctx.RequestAborted), signedOut = false });
            var n = await svc.SignOutEverywhereAsync(who, ctx.Client(), ctx.RequestAborted); CookieHelper.Clear(ctx, api.Value);
            return Results.Ok(new { revoked = n, signedOut = true });
        }).RequireAuthorization();
        s.MapGet("/", async (Guid? userId, int? page, int? pageSize, SessionsService svc, HttpContext ctx)
            => Results.Ok(await svc.AllAsync(new SessionQuery(userId, page ?? 1, pageSize ?? 25), ctx.RequestAborted))).RequirePermission(Permissions.SessionsReadAll);

        // ------------------------------------------------------------ monitoring
        app.MapGet("/api/audit", async (string? q, string? outcome, string? category, Guid? actorId, DateTimeOffset? from, DateTimeOffset? to, int? page, int? pageSize, MonitoringService svc, HttpContext ctx)
            => Results.Ok(await svc.AuditAsync(new AuditQuery(q, outcome, category, actorId, from, to, page ?? 1, pageSize ?? 25), ctx.RequestAborted))).RequirePermission(Permissions.AuditRead);
        app.MapGet("/api/audit/export", async (string? q, string? outcome, string? category, Guid? actorId, DateTimeOffset? from, DateTimeOffset? to, string? format, MonitoringService svc, HttpContext ctx) =>
        {
            var r = await svc.ExportAsync(new AuditQuery(q, outcome, category, actorId, from, to), format, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted);
            ctx.Response.Headers["X-Export-Rows"] = r.Count.ToString(); ctx.Response.Headers["X-Export-Truncated"] = r.Truncated ? "true" : "false";
            ctx.Response.Headers.CacheControl = "no-store";
            return Results.File(System.Text.Encoding.UTF8.GetBytes(r.Content), r.ContentType + "; charset=utf-8", r.FileName);
        }).RequirePermission(Permissions.AuditRead);
        app.MapGet("/api/audit/{id:long}", async (long id, MonitoringService svc, HttpContext ctx) => Results.Ok(await svc.AuditDetailAsync(id, ctx.RequestAborted))).RequirePermission(Permissions.AuditRead);
        app.MapGet("/api/security-events", async (string? severity, bool? acknowledged, int? page, int? pageSize, MonitoringService svc, HttpContext ctx)
            => Results.Ok(await svc.SecurityAsync(new SecurityQuery(severity, acknowledged, page ?? 1, pageSize ?? 25), ctx.RequestAborted))).RequirePermission(Permissions.SecurityRead);
        app.MapPost("/api/security-events/{id:guid}/ack", async (Guid id, MonitoringService svc, HttpContext ctx) =>
        {
            await svc.AcknowledgeAsync(id, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted);
            return Results.NoContent();
        }).RequirePermission(Permissions.SecurityRead);

        // ------------------------------------------------------------ dashboards (sections are filtered by permission inside the service)
        app.MapGet("/api/dashboard/admin", async (DashboardService svc, HttpContext ctx) => Results.Ok(await svc.AdminAsync(ctx.RequirePrincipal(), ctx.RequestAborted))).RequireAuthorization();
        app.MapGet("/api/dashboard/me", async (DashboardService svc, HttpContext ctx) => Results.Ok(await svc.MeAsync(ctx.RequirePrincipal(), ctx.RequestAborted))).RequireAuthorization();

        // ------------------------------------------------------------ profile
        var me = app.MapGroup("/api/me");
        me.MapGet("/profile", async (ProfileService svc, HttpContext ctx) => Results.Ok(await svc.GetAsync(ctx.RequirePrincipal().UserId, ctx.RequestAborted))).RequireAuthorization();
        me.MapPatch("/profile", async (UpdateProfileRequest body, ProfileService svc, HttpContext ctx) => Results.Ok(await svc.UpdateAsync(ctx.RequirePrincipal(), body, ctx.Client(), ctx.RequestAborted))).RequireAuthorization();

        me.MapPut("/avatar", async (HttpContext ctx, ProfileService svc) =>
        {
            // Hard cap at the socket (Kestrel) before anything is buffered; the loop below enforces the same limit on every host.
            if (ctx.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } cap) cap.MaxRequestBodySize = ImageInspector.MaxBytes + 1;
            using var ms = new MemoryStream(); var buf = new byte[16 * 1024]; int n;
            while ((n = await ctx.Request.Body.ReadAsync(buf, ctx.RequestAborted)) > 0)
            {
                if (ms.Length + n > ImageInspector.MaxBytes) throw DomainException.Validation("The image must be 256 KB or smaller.", new Dictionary<string, string> { ["file"] = "The image must be 256 KB or smaller." });
                ms.Write(buf, 0, n);
            }
            return Results.Ok(await svc.SetAvatarAsync(ctx.RequirePrincipal(), ms.ToArray(), ctx.Client(), ctx.RequestAborted));
        }).RequireAuthorization();
        me.MapDelete("/avatar", async (ProfileService svc, HttpContext ctx) => { await svc.RemoveAvatarAsync(ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted); return Results.NoContent(); }).RequireAuthorization();

        // Avatars are served to their owner and to people who may list users; always as an exact image type, never sniffed or rendered as a document.
        app.MapGet("/api/avatars/{userId:guid}", async (Guid userId, ProfileService svc, AuthorizationService authz, HttpContext ctx) =>
        {
            var who = ctx.RequirePrincipal();
            if (who.UserId != userId && !(await authz.GetEffectivePermissionsAsync(who.UserId, ctx.RequestAborted)).Contains(Permissions.UsersRead)) return Results.NotFound();
            var f = await svc.AvatarAsync(userId, ctx.RequestAborted, own: who.UserId == userId); // your own picture is yours in any organisation (support mode too)
            if (f is null) return Results.NotFound();
            ctx.Response.Headers["Content-Security-Policy"] = "default-src 'none'; sandbox"; ctx.Response.Headers.ETag = f.ETag;
            return Results.File(f.Bytes, f.ContentType);
        }).RequireAuthorization();

        me.MapPost("/password", async (NewPasswordBody body, ProfileService svc, HttpContext ctx) => { await svc.ChangePasswordAsync(ctx.RequirePrincipal(), body.NewPassword, ctx.Client(), ctx.RequestAborted); return Results.NoContent(); }).RequireAuthorization();
        me.MapPost("/email", async (NewEmailBody body, ProfileService svc, HttpContext ctx) => { await svc.RequestEmailChangeAsync(ctx.RequirePrincipal(), body.NewEmail, ctx.Client(), ctx.RequestAborted); return Results.Accepted(value: new { message = "If the address can be used, a confirmation link was sent to it." }); }).RequireAuthorization();
        me.MapGet("/preferences", async (ProfileService svc, HttpContext ctx) => Results.Ok(await svc.PreferencesAsync(ctx.RequirePrincipal().UserId, ctx.RequestAborted))).RequireAuthorization();
        me.MapPatch("/preferences", async (UpdatePreferencesRequest body, ProfileService svc, HttpContext ctx) => Results.Ok(await svc.UpdatePreferencesAsync(ctx.RequirePrincipal(), body, ctx.Client(), ctx.RequestAborted))).RequireAuthorization();

        // ------------------------------------------------------------ system settings
        app.MapGet("/api/settings/public", async (SettingsService svc, HttpContext ctx) => Results.Ok(await svc.PublicAsync(ctx.RequestAborted))).AllowAnonymous();
        app.MapGet("/api/settings", async (SettingsService svc, HttpContext ctx) => Results.Ok(await svc.GetAsync(ctx.RequestAborted))).RequirePermission(Permissions.SettingsManage);
        app.MapGet("/api/email-deliveries", async (string? status, int? page, int? pageSize, AnnabaDigital.Application.Email.EmailDeliveryService svc, HttpContext ctx) => Results.Ok(await svc.ListAsync(status, page ?? 1, pageSize ?? 25, ctx.RequestAborted))).RequirePermission(Permissions.SettingsManage);
        // an organisation's own (stricter) security policy; the platform organisation uses the system-wide settings above
        app.MapGet("/api/settings/tenant", async (SettingsService svc, HttpContext ctx) => Results.Ok(await svc.GetTenantPolicyAsync(ctx.RequestAborted))).RequirePermission(Permissions.SettingsManage);
        app.MapPatch("/api/settings/tenant", async (SaveTenantPolicy body, SettingsService svc, HttpContext ctx)
            => Results.Ok(await svc.UpdateTenantPolicyAsync(body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.SettingsManage);
        app.MapPatch("/api/settings", async (UpdateSettingsRequest body, SettingsService svc, HttpContext ctx) => Results.Ok(await svc.UpdateAsync(body, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.SettingsManage);
        app.MapPut("/api/settings/email-api-key", async (SecretBody body, SettingsService svc, HttpContext ctx) => Results.Ok(await svc.SetEmailApiKeyAsync(body.Value, ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.SettingsManage);
        app.MapDelete("/api/settings/email-api-key", async (SettingsService svc, HttpContext ctx) => Results.Ok(await svc.ClearEmailApiKeyAsync(ctx.RequirePrincipal(), ctx.Client(), ctx.RequestAborted))).RequirePermission(Permissions.SettingsManage);
    }
}
