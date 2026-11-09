using Microsoft.Extensions.Options;

namespace AnnabaDigital.Api.Http;

public static class CookieHelper
{
    public static void Set(HttpContext ctx, ApiOptions o, string token, bool remember)
        => ctx.Response.Cookies.Append(o.CookieName, token, new CookieOptions
        {
            HttpOnly = true, Secure = o.SecureCookies, SameSite = SameSiteMode.Lax, Path = "/",
            MaxAge = remember ? TimeSpan.FromDays(30) : null, // session cookie otherwise; server-side expiry is authoritative either way
            IsEssential = true,
        });

    public static void Clear(HttpContext ctx, ApiOptions o)
        => ctx.Response.Cookies.Delete(o.CookieName, new CookieOptions { HttpOnly = true, Secure = o.SecureCookies, SameSite = SameSiteMode.Lax, Path = "/" });
}
