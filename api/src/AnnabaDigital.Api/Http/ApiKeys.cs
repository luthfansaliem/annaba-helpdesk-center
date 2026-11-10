using System.Collections.Concurrent;
using AnnabaDigital.Application.Integrations;
using AnnabaDigital.Domain;

namespace AnnabaDigital.Api.Http;

/// <summary>Endpoint metadata: part of the public API; usable with an API key that has this scope.</summary>
public sealed record ApiScope(string Scope);

public static class ApiKeyDefaults
{
    public const string ItemKey = "annabadigital.apikey";
    public const string PathPrefix = "/api/v1";
    public static ApiKeyIdentity? ApiKey(this HttpContext ctx) => ctx.Items.TryGetValue(ItemKey, out var k) ? k as ApiKeyIdentity : null;
}

/// <summary>
/// INT-001. Keeps API keys inside the public API: a request made with a key must hit an endpoint marked with <see cref="ApiScope"/> whose scope the key holds, and the
/// public API only takes keys (never a browser session). Each key may make 120 requests a minute (per instance).
/// </summary>
public sealed class ApiKeyGateMiddleware(RequestDelegate next)
{
    private static readonly ConcurrentDictionary<Guid, (long Minute, int Count)> Windows = new();

    public async Task InvokeAsync(HttpContext ctx)
    {
        var key = ctx.ApiKey();
        var isV1 = ctx.Request.Path.StartsWithSegments(ApiKeyDefaults.PathPrefix);
        if (key is null)
        {
            if (isV1 && ctx.Principal() is not null) { await ErrorWriter.Write(ctx, 401, ErrorCodes.Unauthenticated, "The public API takes an API key (Authorization: Bearer adk_…), not a browser session."); return; }
            await next(ctx); return;
        }
        var scope = ctx.GetEndpoint()?.Metadata.GetMetadata<ApiScope>();
        if (scope is null) { await ErrorWriter.Write(ctx, 403, ErrorCodes.Denied, "API keys can only be used on the public API (/api/v1)."); return; }
        if (!key.Scopes.Contains(scope.Scope)) { await ErrorWriter.Write(ctx, 403, ErrorCodes.Denied, $"This key lacks the scope '{scope.Scope}'."); return; }
        var minute = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60;
        var w = Windows.AddOrUpdate(key.KeyId, _ => (minute, 1), (_, old) => old.Minute == minute ? (minute, old.Count + 1) : (minute, 1));
        if (w.Count > IntegrationRules.RequestsPerMinute)
        {
            ctx.Response.Headers.RetryAfter = (60 - DateTimeOffset.UtcNow.ToUnixTimeSeconds() % 60).ToString();
            await ErrorWriter.Write(ctx, 429, ErrorCodes.RateLimited, "Too many requests for this key; slow down."); return;
        }
        await next(ctx);
    }
}
