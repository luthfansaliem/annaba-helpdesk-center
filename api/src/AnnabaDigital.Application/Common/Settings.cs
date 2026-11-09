using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;

namespace AnnabaDigital.Application.Common;

/// <summary>Effective security settings (DB values override these defaults — docs/04 D-14).</summary>
public sealed record SecuritySettings
{
    public string RegistrationMode { get; init; } = "invitation-only";
    public int PasswordMinLength { get; init; } = 12;
    public bool RequireBreachCheck { get; init; } = true;
    public int SessionIdleMinutes { get; init; } = 30;
    public int SessionAbsoluteHours { get; init; } = 12;
    public int RememberIdleDays { get; init; } = 7;
    public int RememberAbsoluteDays { get; init; } = 30;
    public int LockoutAttempts { get; init; } = 5;
    public int LockoutMinutes { get; init; } = 15;
    public string MfaPolicy { get; init; } = "optional";
    public int InvitationExpiryDays { get; init; } = 7;
    public int AuditRetentionDays { get; init; } = 365;
    public string EmailFrom { get; init; } = "no-reply@annabadigital.io";
    /// <summary>Critical operations (role assignment…) require a password re-entry within the last 5 minutes (docs/09 §3.8).</summary>
    public bool RequireReauthForCritical { get; init; } = true;

    public static readonly IReadOnlyDictionary<string, object> Defaults = new Dictionary<string, object>
    {
        ["registrationMode"] = "invitation-only", ["passwordMinLength"] = 12, ["requireBreachCheck"] = true, ["sessionIdleMinutes"] = 30,
        ["sessionAbsoluteHours"] = 12, ["rememberIdleDays"] = 7, ["rememberAbsoluteDays"] = 30, ["lockoutAttempts"] = 5, ["lockoutMinutes"] = 15,
        ["mfaPolicy"] = "optional", ["invitationExpiryDays"] = 7, ["auditRetentionDays"] = 365, ["emailFrom"] = "no-reply@annabadigital.io", ["requireReauthForCritical"] = true,
    };
}

/// <summary>
/// Settings as they apply to one organisation: the platform's values, with that organisation's stricter overrides laid over them (<see cref="TenantPolicy"/>).
/// <see cref="GetAsync"/> uses the organisation of the current request; anonymous flows that know the person pass their organisation with <see cref="GetForAsync"/>.
/// The platform organisation itself always uses the platform values.
/// </summary>
public sealed class SettingsProvider(IAppDb db, ITenantContext tenant, AppCache cache, TenantDirectory tenants)
{
    public Task<SecuritySettings> GetAsync(CancellationToken ct = default) => tenant.Scoped ? GetForAsync(tenant.TenantId, ct) : GetGlobalAsync(ct);

    public Task<SecuritySettings> GetForAsync(Guid tenantId, CancellationToken ct = default) => cache.GetOrAddAsync("settings:" + tenantId, async () =>
    {
        var global = await GetGlobalAsync(ct);
        if ((await tenants.GetAsync(tenantId, ct))?.IsPlatform == true) return global;
        var rows = await db.TenantSettings.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == tenantId).Select(x => new { x.Key, x.ValueJson }).ToListAsync(ct);
        return rows.Count == 0 ? global : TenantPolicy.Apply(global, rows.Select(r => (r.Key, r.ValueJson)));
    });

    /// <summary>Call after changing system or organisation settings so this instance sees the change at once (others within the cache time limit).</summary>
    public void Invalidate() => cache.Clear();

    /// <summary>The platform's own values (what the system-settings page edits).</summary>
    public Task<SecuritySettings> GetGlobalAsync(CancellationToken ct = default) => cache.GetOrAddAsync("settings:global", () => LoadGlobalAsync(ct));

    private async Task<SecuritySettings> LoadGlobalAsync(CancellationToken ct)
    {
        var rows = await db.Settings.AsNoTracking().Where(s => !s.IsSecret).ToListAsync(ct);
        T Get<T>(string key, T fallback)
        {
            var row = rows.FirstOrDefault(r => r.Key == key);
            if (row?.ValueJson is null) return fallback;
            try { return JsonSerializer.Deserialize<T>(row.ValueJson) ?? fallback; } catch (JsonException) { return fallback; }
        }
        var d = new SecuritySettings();
        return new SecuritySettings
        {
            RegistrationMode = Get("registrationMode", d.RegistrationMode), PasswordMinLength = Get("passwordMinLength", d.PasswordMinLength),
            RequireBreachCheck = Get("requireBreachCheck", d.RequireBreachCheck), SessionIdleMinutes = Get("sessionIdleMinutes", d.SessionIdleMinutes),
            SessionAbsoluteHours = Get("sessionAbsoluteHours", d.SessionAbsoluteHours), RememberIdleDays = Get("rememberIdleDays", d.RememberIdleDays),
            RememberAbsoluteDays = Get("rememberAbsoluteDays", d.RememberAbsoluteDays), LockoutAttempts = Get("lockoutAttempts", d.LockoutAttempts),
            LockoutMinutes = Get("lockoutMinutes", d.LockoutMinutes), MfaPolicy = Get("mfaPolicy", d.MfaPolicy),
            InvitationExpiryDays = Get("invitationExpiryDays", d.InvitationExpiryDays), AuditRetentionDays = Get("auditRetentionDays", d.AuditRetentionDays),
            EmailFrom = Get("emailFrom", d.EmailFrom), RequireReauthForCritical = Get("requireReauthForCritical", d.RequireReauthForCritical),
        };
    }
}
