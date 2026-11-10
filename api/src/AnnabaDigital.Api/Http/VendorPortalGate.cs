using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Domain;

namespace AnnabaDigital.Api.Http;

/// <summary>
/// R2-11. A vendor's person reaches only their sign-in, profile, notifications and their company's work orders — an allow-list, so no other part of the organisation
/// (requests, tickets, people, places, catalog, knowledge base, reports) is reachable, whatever their role says. The link is looked up per person (cached 30 seconds).
/// </summary>
public sealed class VendorPortalGateMiddleware(RequestDelegate next, IMemoryCache cache)
{
    private static readonly string[] Allowed = ["/api/auth", "/api/me", "/api/sessions/me", "/api/notifications", "/api/work-orders", "/api/settings/public", "/api/avatars"];

    public async Task InvokeAsync(HttpContext ctx, IAppDb db)
    {
        var p = ctx.Principal();
        var path = ctx.Request.Path.Value ?? "";
        if (p is null || ctx.ApiKey() is not null || !path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) || Allowed.Any(a => path.Equals(a, StringComparison.OrdinalIgnoreCase) || path.StartsWith(a + "/", StringComparison.OrdinalIgnoreCase)))
        { await next(ctx); return; }
        var isVendor = await cache.GetOrCreateAsync("vendor-user:" + p.UserId, async e =>
        {
            e.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30);
            return await db.VendorUsers.AsNoTracking().AnyAsync(v => v.UserId == p.UserId, ctx.RequestAborted);
        });
        if (isVendor) { await ErrorWriter.Write(ctx, 403, ErrorCodes.Denied, "Vendor accounts can only see their company's work orders."); return; }
        await next(ctx);
    }
}
