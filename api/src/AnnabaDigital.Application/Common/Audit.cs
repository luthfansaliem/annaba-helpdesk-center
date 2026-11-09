using System.Text.Json;
using System.Text.RegularExpressions;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Domain.Entities;

namespace AnnabaDigital.Application.Common;

/// <summary>Central redaction: nothing that looks like a credential may reach audit metadata, logs or errors.</summary>
public static partial class Redactor
{
    public const string Mask = "[REDACTED]";
    [GeneratedRegex("pass(word)?|token|secret|otp|recovery|apikey|api_key|authorization|cookie|csrf", RegexOptions.IgnoreCase)]
    private static partial Regex SecretKey();

    public static Dictionary<string, string>? Redact(IDictionary<string, string>? meta)
        => meta?.ToDictionary(kv => kv.Key, kv => SecretKey().IsMatch(kv.Key) ? Mask : kv.Value);

    public static bool LooksSecret(string key) => SecretKey().IsMatch(key);
}

public sealed record AuditDiff(string Field, string Before, string After);

/// <summary>Adds audit/security rows to the current unit of work. Callers save inside the same transaction as the change.</summary>
public sealed class AuditWriter(IAppDb db, IClock clock, ITenantContext tenant)
{
    public void Record(string action, string targetType, string? targetId, Outcome outcome, ClientInfo client, Guid? actorId = null, string? actorLabel = null,
        IDictionary<string, string>? meta = null, IEnumerable<AuditDiff>? diff = null)
    {
        db.AuditEvents.Add(new AuditEvent
        {
            Ts = clock.UtcNow, ActorId = actorId, ActorLabel = (actorLabel ?? (actorId is null ? "anonymous" : actorId.ToString()!)) + (tenant.Support && actorId is not null ? " (platform support)" : ""), Action = action,
            TargetType = targetType, TargetId = targetId, Outcome = outcome, Ip = client.Ip, UserAgent = Truncate(client.UserAgent, 300), RequestId = client.RequestId,
            MetaJson = meta is null ? null : JsonSerializer.Serialize(Redactor.Redact(meta)),
            DiffJson = diff is null ? null : JsonSerializer.Serialize(diff.Select(d => new { d.Field, d.Before, d.After })),
        });
    }

    public void Security(string type, Severity severity, string detail, Guid? userId = null)
        => db.SecurityEvents.Add(new SecurityEvent { Ts = clock.UtcNow, Type = type, Severity = severity, Detail = detail, UserId = userId });

    private static string? Truncate(string? s, int n) => s is null ? null : s.Length <= n ? s : s[..n];
}

public static class DeviceInfo
{
    /// <summary>Very small UA parser — only for friendly labels in the sessions list; never used for security decisions.</summary>
    public static (string Device, string Browser) Describe(string? ua)
    {
        if (string.IsNullOrWhiteSpace(ua)) return ("Unknown device", "Unknown browser");
        var device = ua.Contains("iPhone") ? "iPhone" : ua.Contains("iPad") ? "iPad" : ua.Contains("Android") ? "Android device"
            : ua.Contains("Macintosh") ? "Mac" : ua.Contains("Windows") ? "Windows PC" : ua.Contains("Linux") ? "Linux PC" : "Unknown device";
        var browser = ua.Contains("Edg/") ? "Edge" : ua.Contains("Firefox/") ? "Firefox" : ua.Contains("Chrome/") ? "Chrome" : ua.Contains("Safari/") ? "Safari" : "Unknown browser";
        return (device, browser);
    }
}
