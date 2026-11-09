namespace AnnabaDigital.Api.Http;

/// <summary>Endpoint metadata: reachable while the session is locked (unlock, logout, me, csrf).</summary>
public sealed record AllowLocked;
/// <summary>Endpoint metadata: reachable while the second factor is still pending (mfa verify, logout).</summary>
public sealed record AllowMfaPending;
/// <summary>Endpoint metadata: reachable while org policy requires MFA and the user has not enrolled yet.</summary>
public sealed record AllowMfaEnrollment;

public static class Redirects
{
    /// <summary>Only same-origin relative paths are allowed (open-redirect protection).</summary>
    public static string Safe(string? value, string fallback = "/dashboard")
    {
        if (string.IsNullOrEmpty(value) || value.Length > 2000) return fallback;
        if (!value.StartsWith('/') || value.StartsWith("//") || value.StartsWith("/\\")) return fallback;
        if (value.Any(c => c is '\r' or '\n' or '\\' || char.IsControl(c))) return fallback;
        if (value.StartsWith("/login", StringComparison.OrdinalIgnoreCase)) return fallback;
        return value;
    }
}
