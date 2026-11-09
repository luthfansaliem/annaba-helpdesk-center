using System.Text.Json;
using AnnabaDigital.Domain.Entities;

namespace AnnabaDigital.Application.Common;

/// <summary>How one security setting may be tuned by an organisation: it can only be as strict as, or stricter than, the platform's value.</summary>
public sealed record PolicyRule(string Key, string Kind, int Min, int Max, string Strict, string[]? Options = null);

/// <summary>
/// The security settings an organisation may tighten for itself. The platform's value is both the default and the loosest allowed: an organisation can raise a minimum
/// length, shorten a timeout, require MFA — never relax one. Reading applies the same rule again (stricter of the two), so tightening the platform later is never undone
/// by an old override.
/// </summary>
public static class TenantPolicy
{
    public static readonly PolicyRule[] Rules =
    [
        new("passwordMinLength", "int", 8, 128, "higher"),
        new("requireBreachCheck", "bool", 0, 1, "true"),
        new("sessionIdleMinutes", "int", 5, 1440, "lower"),
        new("sessionAbsoluteHours", "int", 1, 168, "lower"),
        new("rememberIdleDays", "int", 1, 90, "lower"),
        new("rememberAbsoluteDays", "int", 1, 365, "lower"),
        new("lockoutAttempts", "int", 3, 20, "lower"),
        new("lockoutMinutes", "int", 1, 1440, "higher"),
        new("mfaPolicy", "enum", 0, 0, "higher", ["off", "optional", "required"]),
        new("invitationExpiryDays", "int", 1, 30, "lower"),
        new("requireReauthForCritical", "bool", 0, 1, "true"),
    ];

    public static PolicyRule? Rule(string key) => Rules.FirstOrDefault(r => r.Key == key);

    public static object Value(SecuritySettings s, string key) => key switch
    {
        "passwordMinLength" => s.PasswordMinLength, "requireBreachCheck" => s.RequireBreachCheck, "sessionIdleMinutes" => s.SessionIdleMinutes,
        "sessionAbsoluteHours" => s.SessionAbsoluteHours, "rememberIdleDays" => s.RememberIdleDays, "rememberAbsoluteDays" => s.RememberAbsoluteDays,
        "lockoutAttempts" => s.LockoutAttempts, "lockoutMinutes" => s.LockoutMinutes, "mfaPolicy" => s.MfaPolicy, "invitationExpiryDays" => s.InvitationExpiryDays,
        "requireReauthForCritical" => s.RequireReauthForCritical, _ => throw new ArgumentOutOfRangeException(nameof(key)),
    };

    private static SecuritySettings With(SecuritySettings s, string key, object v) => key switch
    {
        "passwordMinLength" => s with { PasswordMinLength = (int)v }, "requireBreachCheck" => s with { RequireBreachCheck = (bool)v },
        "sessionIdleMinutes" => s with { SessionIdleMinutes = (int)v }, "sessionAbsoluteHours" => s with { SessionAbsoluteHours = (int)v },
        "rememberIdleDays" => s with { RememberIdleDays = (int)v }, "rememberAbsoluteDays" => s with { RememberAbsoluteDays = (int)v },
        "lockoutAttempts" => s with { LockoutAttempts = (int)v }, "lockoutMinutes" => s with { LockoutMinutes = (int)v }, "mfaPolicy" => s with { MfaPolicy = (string)v },
        "invitationExpiryDays" => s with { InvitationExpiryDays = (int)v }, "requireReauthForCritical" => s with { RequireReauthForCritical = (bool)v },
        _ => s,
    };

    /// <summary>Parses a stored/requested JSON value into the rule's type, or null when it doesn't fit the type or range.</summary>
    public static object? Parse(PolicyRule r, JsonElement e)
    {
        try
        {
            switch (r.Kind)
            {
                case "int": { if (e.ValueKind != JsonValueKind.Number || !e.TryGetInt32(out var i) || i < r.Min || i > r.Max) return null; return i; }
                case "bool": return e.ValueKind is JsonValueKind.True or JsonValueKind.False ? e.GetBoolean() : null;
                case "enum": { var s = e.ValueKind == JsonValueKind.String ? e.GetString() : null; return s is not null && r.Options!.Contains(s) ? s : null; }
            }
        }
        catch (InvalidOperationException) { }
        return null;
    }

    /// <summary>True when <paramref name="candidate"/> is at least as strict as <paramref name="platform"/>.</summary>
    public static bool AtLeastAsStrict(PolicyRule r, object candidate, object platform) => r.Strict switch
    {
        "higher" when r.Kind == "enum" => Array.IndexOf(r.Options!, (string)candidate) >= Array.IndexOf(r.Options!, (string)platform),
        "higher" => (int)candidate >= (int)platform,
        "lower" => (int)candidate <= (int)platform,
        "true" => (bool)candidate || !(bool)platform, // may switch a protection on; may not switch the platform's off
        _ => true,
    };

    /// <summary>The platform's settings with the organisation's overrides laid over them, each clamped to be no looser than the platform's.</summary>
    public static SecuritySettings Apply(SecuritySettings platform, IEnumerable<(string Key, string ValueJson)> overrides)
    {
        var s = platform;
        foreach (var (key, json) in overrides)
        {
            var r = Rule(key); if (r is null) continue;
            JsonElement e; try { e = JsonDocument.Parse(json).RootElement; } catch (JsonException) { continue; }
            if (Parse(r, e) is not { } v) continue;
            s = With(s, key, AtLeastAsStrict(r, v, Value(platform, key)) ? v : Value(platform, key));
        }
        // keep the same cross-field sanity the platform enforces
        if (s.SessionAbsoluteHours * 60 < s.SessionIdleMinutes) s = s with { SessionAbsoluteHours = platform.SessionAbsoluteHours };
        if (s.RememberAbsoluteDays < s.RememberIdleDays) s = s with { RememberAbsoluteDays = platform.RememberAbsoluteDays, RememberIdleDays = platform.RememberIdleDays };
        return s;
    }
}
