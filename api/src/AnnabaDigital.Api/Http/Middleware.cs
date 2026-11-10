using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Domain;

namespace AnnabaDigital.Api.Http;

public sealed record ErrorBody(string Code, string Message, IDictionary<string, string>? Fields, string RequestId);

public static class ErrorWriter
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static Task Write(HttpContext ctx, int status, string code, string message, IDictionary<string, string>? fields = null, TimeSpan? retryAfter = null)
    {
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json";
        if (retryAfter is { } r) ctx.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(r.TotalSeconds)).ToString();
        return ctx.Response.WriteAsJsonAsync(new { error = new ErrorBody(code, message, fields, ctx.TraceIdentifier) }, Json);
    }
}

/// <summary>Maps exceptions to the stable error envelope. Never leaks stack traces, SQL or internal messages.</summary>
public sealed class ExceptionEnvelopeMiddleware(RequestDelegate next, ILogger<ExceptionEnvelopeMiddleware> log)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        try { await next(ctx); }
        catch (DomainException e) when (!ctx.Response.HasStarted) { await ErrorWriter.Write(ctx, e.Status, e.Code, e.Message, e.Fields, e.RetryAfter); }
        catch (OperationCanceledException) when (ctx.RequestAborted.IsCancellationRequested) { ctx.Response.StatusCode = 499; }
        catch (Exception e) when (!ctx.Response.HasStarted)
        {
            log.LogError(e, "Unhandled exception for {Method} {Path} ({RequestId})", ctx.Request.Method, ctx.Request.Path, ctx.TraceIdentifier);
            await ErrorWriter.Write(ctx, 500, "internal_error", "Something went wrong.");
        }
    }
}

/// <summary>
/// The API is only reachable through the UI proxy: requests must carry the shared key (constant-time compare), otherwise 404.
/// Because the proxy is authenticated, X-Forwarded-For is trusted for the client IP. Health checks are exempt.
/// </summary>
public sealed class ProxyGateMiddleware(RequestDelegate next, IOptions<ApiOptions> options, ITokenService tokens)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        var key = options.Value.ProxyKey;
        if (!string.IsNullOrEmpty(key) && !ctx.Request.Path.StartsWithSegments("/health"))
        {
            var sent = ctx.Request.Headers["X-Internal-Proxy-Key"].ToString();
            if (sent.Length == 0 || !tokens.ConstantTimeEquals(sent, key)) { ctx.Response.StatusCode = 404; return; }
            var xff = ctx.Request.Headers["X-Forwarded-For"].ToString().Split(',')[0].Trim();
            if (IPAddress.TryParse(xff, out var ip)) ctx.Connection.RemoteIpAddress = ip;
        }
        await next(ctx);
    }
}

public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext ctx)
    {
        ctx.Response.OnStarting(() =>
        {
            var h = ctx.Response.Headers;
            h["X-Content-Type-Options"] = "nosniff";
            h["Referrer-Policy"] = "no-referrer";
            h["X-Frame-Options"] = "DENY";
            if (!h.ContainsKey("Content-Security-Policy")) h["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'"; // an endpoint may set a stricter one (avatars add sandbox)
            h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
            h["Strict-Transport-Security"] = "max-age=63072000; includeSubDomains";
            if (ctx.Request.Path.StartsWithSegments("/api")) h["Cache-Control"] = "no-store";
            return Task.CompletedTask;
        });
        return next(ctx);
    }
}

/// <summary>
/// Locked / MFA-pending sessions are blocked here, server-side, for every endpoint that doesn't opt in with metadata.
/// Runs after authentication so the session state comes from the database, not from the client.
/// </summary>
public sealed class SessionStateMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        var p = ctx.Principal();
        if (p is not null)
        {
            var endpoint = ctx.GetEndpoint();
            if (p.Locked && endpoint?.Metadata.GetMetadata<AllowLocked>() is null) throw DomainException.SessionLocked();
            if (p.MfaPending && endpoint?.Metadata.GetMetadata<AllowMfaPending>() is null)
                throw new DomainException(ErrorCodes.MfaRequired, "Second-factor verification is required.", 403);
            if (p.MfaEnrollmentRequired && endpoint?.Metadata.GetMetadata<AllowMfaEnrollment>() is null) throw DomainException.MfaEnrollmentRequired();
        }
        await next(ctx);
    }
}

/// <summary>
/// CSRF defence for state-changing requests: strict Origin match (SameSite=Lax cookies are the first layer);
/// authenticated sessions must also present the per-session token in X-CSRF-Token.
/// </summary>
public sealed class CsrfMiddleware(RequestDelegate next, IOptions<ApiOptions> options, ITokenService tokens)
{
    private static readonly HashSet<string> Safe = ["GET", "HEAD", "OPTIONS", "TRACE"];

    public async Task InvokeAsync(HttpContext ctx)
    {
        if (Safe.Contains(ctx.Request.Method) || !ctx.Request.Path.StartsWithSegments("/api")) { await next(ctx); return; }
        // server-to-server endpoints carry no Origin and no session; they authenticate themselves (a secret URL or a key) and never act on a cookie
        if (ctx.GetEndpoint()?.Metadata.GetMetadata<MachineEndpoint>() is not null && (ctx.Principal() is null || ctx.ApiKey() is not null)) { await next(ctx); return; }

        var origin = ctx.Request.Headers.Origin.ToString();
        if (!string.Equals(origin, options.Value.Origin, StringComparison.OrdinalIgnoreCase))
        {
            await ErrorWriter.Write(ctx, 403, ErrorCodes.Denied, "Cross-origin request rejected."); return;
        }
        // Public endpoints (e.g. login) are protected by the strict Origin check above; they have no session to bind a token to,
        // and requiring one would lock out a user whose stale-but-valid cookie is still present.
        var isPublic = ctx.GetEndpoint()?.Metadata.GetMetadata<Microsoft.AspNetCore.Authorization.IAllowAnonymous>() is not null;
        var p = ctx.Principal();
        if (p is not null && !isPublic)
        {
            var sent = ctx.Request.Headers["X-CSRF-Token"].ToString();
            if (sent.Length == 0 || !tokens.ConstantTimeEquals(sent, tokens.CsrfToken(p.SessionId)))
            {
                await ErrorWriter.Write(ctx, 403, ErrorCodes.Denied, "Missing or invalid CSRF token."); return;
            }
        }
        await next(ctx);
    }
}
