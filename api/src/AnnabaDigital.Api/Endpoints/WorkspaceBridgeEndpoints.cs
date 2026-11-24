using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using AnnabaDigital.Api.Http;
using AnnabaDigital.Application.Platform;

namespace AnnabaDigital.Api.Endpoints;

/// <summary>SPIKE H00. The two calls that let Annaba Workspace open this application: Core's signed entitlement, and the launch exchange made by this application's own web server.</summary>
public static class WorkspaceBridgeEndpoints
{
    public sealed record EntitlementBody(string? WorkspaceKey, string? AppKey, int Revision, string? OperationId);
    public sealed record LaunchBody(string? Code, string? State = null);

    public static void MapWorkspaceBridge(this IEndpointRouteBuilder app)
    {
        // Signed by Core exactly as it signs for every application: X-Signature = HMAC-SHA256(adapter secret, "<X-Timestamp>.<body>"), five minutes of tolerance.
        app.MapPost("/platform/v1/entitlements", async (HttpContext ctx, WorkspaceBridge bridge, IOptions<CoreOptions> opt) =>
        {
            var o = opt.Value; if (!o.Configured) return Results.NotFound();
            using var reader = new StreamReader(ctx.Request.Body, Encoding.UTF8); var raw = await reader.ReadToEndAsync(ctx.RequestAborted);
            var ts = ctx.Request.Headers["X-Timestamp"].ToString(); var sig = ctx.Request.Headers["X-Signature"].ToString();
            var expected = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(o.AdapterSecret), Encoding.UTF8.GetBytes(ts + "." + raw))).ToLowerInvariant();
            var fresh = long.TryParse(ts, out var unix) && Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - unix) <= 300;
            if (!fresh || sig.Length != expected.Length || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(sig.ToLowerInvariant()), Encoding.ASCII.GetBytes(expected))) return Results.Unauthorized();
            var b = System.Text.Json.JsonSerializer.Deserialize<EntitlementBody>(raw, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
            if (b is null || b.AppKey != o.AppKey) return Results.UnprocessableEntity();
            var applied = await bridge.ApplyEntitlementAsync(b.WorkspaceKey ?? "", b.Revision, ctx.RequestAborted);
            return Results.Ok(new { appliedRevision = applied });
        }).AllowAnonymous().WithMetadata(new MachineEndpoint());

        // Called by this application's web server (behind the proxy key), never by a browser: it spends the code and sets the session cookie.
        app.MapPost("/api/launch", async (LaunchBody b, HttpContext ctx, WorkspaceBridge bridge, IOptions<ApiOptions> api) =>
        {
            var (token, reason) = await bridge.LaunchAsync(b.Code, b.State, ctx.Client(), ctx.RequestAborted);
            if (token is null) return Results.Json(new { error = new { code = reason, message = "This link cannot be used. Open the application again from Workspace." } }, statusCode: reason == "not_provisioned" ? 409 : 403);
            CookieHelper.Set(ctx, api.Value, token, remember: false);
            return Results.Ok(new { ok = true });
        }).AllowAnonymous().WithMetadata(new MachineEndpoint());
    }
}
