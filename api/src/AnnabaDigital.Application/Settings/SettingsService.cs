using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;

namespace AnnabaDigital.Application.Settings;

public sealed record SettingsView(
    string RegistrationMode, int PasswordMinLength, bool RequireBreachCheck, int SessionIdleMinutes, int SessionAbsoluteHours, int RememberIdleDays, int RememberAbsoluteDays,
    int LockoutAttempts, int LockoutMinutes, string MfaPolicy, int InvitationExpiryDays, int AuditRetentionDays, string EmailFrom, bool RequireReauthForCritical,
    string EmailProvider, bool EmailApiKeySet, string EmailApiKeySource);
public sealed record UpdateSettingsRequest(
    string? RegistrationMode, int? PasswordMinLength, bool? RequireBreachCheck, int? SessionIdleMinutes, int? SessionAbsoluteHours, int? RememberIdleDays, int? RememberAbsoluteDays,
    int? LockoutAttempts, int? LockoutMinutes, string? MfaPolicy, int? InvitationExpiryDays, int? AuditRetentionDays, string? EmailFrom, bool? RequireReauthForCritical);
/// <summary>What the (unauthenticated) sign-in and registration screens need — nothing sensitive.</summary>
public sealed record PublicSettings(bool RegistrationOpen, int PasswordMinLength, string MfaPolicy, bool EmailEnabled, bool SsoEnabled, string? SsoName);

/// <summary>SET-002. Only authorised administrators; every change is validated, audited (secrets never), and requires recent re-authentication.</summary>
public sealed record TenantPolicyItem(string Key, string Kind, string Strict, int Min, int Max, string[]? Options, object Platform, object? Own, object Effective);
public sealed record TenantPolicyView(string TenantName, TenantPolicyItem[] Items);
/// <summary>Setting key → new value, or null to go back to the platform's value.</summary>
public sealed record SaveTenantPolicy(Dictionary<string, JsonElement>? Values);

public sealed partial class SettingsService(IAppDb db, IClock clock, SettingsProvider provider, ISecretProtector protector, IEmailInfo emailInfo, AuditWriter audit, IOidcClient oidc, ITenantContext tenant)
{
    /// <summary>These settings (password policy, sessions, e-mail, single sign-on) apply to every organisation, so only the platform organisation may read or change them.</summary>
    private void RequirePlatform() { if (!tenant.IsPlatform) throw DomainException.DeniedBecause("Only the platform organisation can view or change system-wide settings."); }

    public const string EmailKeyName = "email.resendApiKey";
    private static byte[] Aad => System.Text.Encoding.UTF8.GetBytes("setting:" + EmailKeyName);

    [GeneratedRegex(@"^([^<>@\s]+@[^<>@\s]+\.[^<>@\s]+|[^<>]{1,100}<[^<>@\s]+@[^<>@\s]+\.[^<>@\s]+>)$")] private static partial Regex FromShape();

    private async Task<bool> HasDbKeyAsync(CancellationToken ct) => await db.Settings.AnyAsync(s => s.Key == EmailKeyName && s.IsSecret && s.ValueCiphertext != null, ct);

    public async Task<SettingsView> GetAsync(CancellationToken ct = default)
    {
        RequirePlatform();
        var s = await provider.GetGlobalAsync(ct); var dbKey = await HasDbKeyAsync(ct);
        return new SettingsView(s.RegistrationMode, s.PasswordMinLength, s.RequireBreachCheck, s.SessionIdleMinutes, s.SessionAbsoluteHours, s.RememberIdleDays, s.RememberAbsoluteDays,
            s.LockoutAttempts, s.LockoutMinutes, s.MfaPolicy, s.InvitationExpiryDays, s.AuditRetentionDays, s.EmailFrom, s.RequireReauthForCritical,
            emailInfo.Provider, dbKey || emailInfo.ConfigurationKeyPresent, dbKey ? "database" : emailInfo.ConfigurationKeyPresent ? "configuration" : "none");
    }

    public async Task<PublicSettings> PublicAsync(CancellationToken ct = default)
    {
        var s = await provider.GetGlobalAsync(ct);
        // A Resend key stored by an administrator (database) also counts as configured.
        var emailEnabled = emailInfo.Configured || (emailInfo.Provider.Equals("resend", StringComparison.OrdinalIgnoreCase) && await HasDbKeyAsync(ct));
        return new PublicSettings(s.RegistrationMode == "open", s.PasswordMinLength, s.MfaPolicy, emailEnabled, oidc.Enabled, oidc.Enabled ? oidc.DisplayName : null);
    }

    public async Task<SettingsView> UpdateAsync(UpdateSettingsRequest req, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        RequirePlatform();
        var cur = await provider.GetGlobalAsync(ct);
        Guards.RequireRecentReauthForCritical(actor, cur, clock); // settings.manage is a critical permission
        var next = cur with
        {
            RegistrationMode = req.RegistrationMode ?? cur.RegistrationMode, PasswordMinLength = req.PasswordMinLength ?? cur.PasswordMinLength, RequireBreachCheck = req.RequireBreachCheck ?? cur.RequireBreachCheck,
            SessionIdleMinutes = req.SessionIdleMinutes ?? cur.SessionIdleMinutes, SessionAbsoluteHours = req.SessionAbsoluteHours ?? cur.SessionAbsoluteHours, RememberIdleDays = req.RememberIdleDays ?? cur.RememberIdleDays,
            RememberAbsoluteDays = req.RememberAbsoluteDays ?? cur.RememberAbsoluteDays, LockoutAttempts = req.LockoutAttempts ?? cur.LockoutAttempts, LockoutMinutes = req.LockoutMinutes ?? cur.LockoutMinutes,
            MfaPolicy = req.MfaPolicy ?? cur.MfaPolicy, InvitationExpiryDays = req.InvitationExpiryDays ?? cur.InvitationExpiryDays, AuditRetentionDays = req.AuditRetentionDays ?? cur.AuditRetentionDays,
            EmailFrom = req.EmailFrom?.Trim() ?? cur.EmailFrom, RequireReauthForCritical = req.RequireReauthForCritical ?? cur.RequireReauthForCritical,
        };

        var f = new Dictionary<string, string>();
        if (next.RegistrationMode is not ("open" or "invitation-only")) f["registrationMode"] = "Use open or invitation-only.";
        if (next.PasswordMinLength is < 8 or > 128) f["passwordMinLength"] = "Use 8–128.";
        if (next.SessionIdleMinutes is < 5 or > 1440) f["sessionIdleMinutes"] = "Use 5–1440 minutes.";
        if (next.SessionAbsoluteHours is < 1 or > 168) f["sessionAbsoluteHours"] = "Use 1–168 hours.";
        if (next.SessionAbsoluteHours * 60 < next.SessionIdleMinutes) f["sessionAbsoluteHours"] = "The absolute lifetime can't be shorter than the idle timeout.";
        if (next.RememberIdleDays is < 1 or > 90) f["rememberIdleDays"] = "Use 1–90 days.";
        if (next.RememberAbsoluteDays is < 1 or > 365 || next.RememberAbsoluteDays < next.RememberIdleDays) f["rememberAbsoluteDays"] = "Use 1–365 days, not less than the idle window.";
        if (next.LockoutAttempts is < 3 or > 20) f["lockoutAttempts"] = "Use 3–20 attempts.";
        if (next.LockoutMinutes is < 1 or > 1440) f["lockoutMinutes"] = "Use 1–1440 minutes.";
        if (next.MfaPolicy is not ("off" or "optional" or "required")) f["mfaPolicy"] = "Use off, optional or required.";
        if (next.InvitationExpiryDays is < 1 or > 30) f["invitationExpiryDays"] = "Use 1–30 days.";
        if (next.AuditRetentionDays is < 30 or > 3650) f["auditRetentionDays"] = "Use 30–3650 days.";
        if (next.EmailFrom.Length > 200 || !FromShape().IsMatch(next.EmailFrom)) f["emailFrom"] = "Use an address like no-reply@example.com or Name <no-reply@example.com>.";
        if (f.Count > 0) throw DomainException.Validation("Please correct the highlighted fields.", f);

        var changes = new List<(string Key, object Before, object After)>();
        void C(string key, object before, object after) { if (!Equals(before, after)) changes.Add((key, before, after)); }
        C("registrationMode", cur.RegistrationMode, next.RegistrationMode); C("passwordMinLength", cur.PasswordMinLength, next.PasswordMinLength); C("requireBreachCheck", cur.RequireBreachCheck, next.RequireBreachCheck);
        C("sessionIdleMinutes", cur.SessionIdleMinutes, next.SessionIdleMinutes); C("sessionAbsoluteHours", cur.SessionAbsoluteHours, next.SessionAbsoluteHours); C("rememberIdleDays", cur.RememberIdleDays, next.RememberIdleDays);
        C("rememberAbsoluteDays", cur.RememberAbsoluteDays, next.RememberAbsoluteDays); C("lockoutAttempts", cur.LockoutAttempts, next.LockoutAttempts); C("lockoutMinutes", cur.LockoutMinutes, next.LockoutMinutes);
        C("mfaPolicy", cur.MfaPolicy, next.MfaPolicy); C("invitationExpiryDays", cur.InvitationExpiryDays, next.InvitationExpiryDays); C("auditRetentionDays", cur.AuditRetentionDays, next.AuditRetentionDays);
        C("emailFrom", cur.EmailFrom, next.EmailFrom); C("requireReauthForCritical", cur.RequireReauthForCritical, next.RequireReauthForCritical);
        if (changes.Count == 0) throw DomainException.Validation("No changes to save.");

        await db.InTransactionAsync(async () =>
        {
            foreach (var (key, _, after) in changes)
            {
                var row = await db.Settings.FirstOrDefaultAsync(s => s.Key == key, ct);
                if (row is null) db.Settings.Add(new SystemSetting { Key = key, ValueJson = JsonSerializer.Serialize(after), UpdatedBy = actor.UserId, UpdatedAt = clock.UtcNow });
                else { row.ValueJson = JsonSerializer.Serialize(after); row.UpdatedBy = actor.UserId; row.UpdatedAt = clock.UtcNow; row.Version++; }
            }
            audit.Record("settings.update", "system", null, Outcome.Success, client, actor.UserId, actor.Email, diff: [.. changes.Select(c => new AuditDiff(c.Key, c.Before.ToString()!, c.After.ToString()!))]);
            if (changes.Any(c => c.Key is "mfaPolicy" or "registrationMode" or "lockoutAttempts" or "passwordMinLength" or "requireReauthForCritical"))
                audit.Security("settings.changed", Severity.Medium, $"Security policy changed: {string.Join(", ", changes.Select(c => c.Key))}", null);
            await db.SaveChangesAsync(ct);
            return 0;
        }, ct);
        return await GetAsync(ct);
    }

    // ---------------------------------------------------------------- write-only secret
    /// <summary>Stores the email provider key encrypted (AES-GCM, key outside the DB). It can be replaced or removed but never read back.</summary>
    public async Task<SettingsView> SetEmailApiKeyAsync(string? value, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        RequirePlatform();
        Guards.RequireRecentReauthForCritical(actor, await provider.GetGlobalAsync(ct), clock);
        var v = (value ?? "").Trim();
        if (v.Length is < 8 or > 300 || v.Any(char.IsWhiteSpace)) throw DomainException.Validation("That doesn't look like an API key.", new Dictionary<string, string> { ["value"] = "Paste the key exactly as issued (8–300 characters, no spaces)." });
        var enc = protector.Protect(System.Text.Encoding.UTF8.GetBytes(v), Aad);
        var row = await db.Settings.FirstOrDefaultAsync(s => s.Key == EmailKeyName, ct);
        if (row is null) { row = new SystemSetting { Key = EmailKeyName, IsSecret = true }; db.Settings.Add(row); } else row.Version++;
        row.ValueJson = null; row.IsSecret = true; row.ValueCiphertext = [.. enc.Nonce, .. enc.Ciphertext]; row.KeyVersion = enc.KeyVersion; row.UpdatedBy = actor.UserId; row.UpdatedAt = clock.UtcNow;
        audit.Record("settings.secret.set", "system", EmailKeyName, Outcome.Success, client, actor.UserId, actor.Email); // the value is never recorded
        await db.SaveChangesAsync(ct);
        return await GetAsync(ct);
    }

    public async Task<SettingsView> ClearEmailApiKeyAsync(SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        RequirePlatform();
        Guards.RequireRecentReauthForCritical(actor, await provider.GetGlobalAsync(ct), clock);
        var n = await db.Settings.Where(s => s.Key == EmailKeyName).ExecuteDeleteAsync(ct);
        if (n == 0) throw DomainException.NotFound("Stored API key");
        audit.Record("settings.secret.clear", "system", EmailKeyName, Outcome.Success, client, actor.UserId, actor.Email);
        await db.SaveChangesAsync(ct);
        return await GetAsync(ct);
    }

    /// <summary>Decrypts the stored key for the email sender. (Internal use only — no endpoint returns it.)</summary>
    public async Task<string?> ReadEmailApiKeyAsync(CancellationToken ct = default)
    {
        var row = await db.Settings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == EmailKeyName && s.IsSecret, ct);
        if (row?.ValueCiphertext is not { Length: > 12 } blob) return null;
        return System.Text.Encoding.UTF8.GetString(protector.Unprotect(new ProtectedSecret(blob[12..], blob[..12], row.KeyVersion), Aad));
    }

    // ============================================================ organisation policy
    private async Task RequireOrganisationAsync(CancellationToken ct)
    {
        if (!tenant.Scoped || await db.Tenants.AsNoTracking().AnyAsync(t => t.Id == tenant.TenantId && t.IsPlatform, ct))
            throw DomainException.Conflict("The platform organisation uses the system-wide settings (System Settings).");
    }

    /// <summary>This organisation's security policy: the platform value it must at least meet, its own stricter choice (if any), and what applies.</summary>
    public async Task<TenantPolicyView> GetTenantPolicyAsync(CancellationToken ct = default)
    {
        await RequireOrganisationAsync(ct);
        var global = await provider.GetGlobalAsync(ct); var effective = await provider.GetAsync(ct);
        var own = (await db.TenantSettings.AsNoTracking().ToListAsync(ct)).ToDictionary(x => x.Key, x => x.ValueJson);
        var name = await db.Tenants.AsNoTracking().Where(t => t.Id == tenant.TenantId).Select(t => t.Name).FirstAsync(ct);
        object? Own(PolicyRule r) => own.TryGetValue(r.Key, out var json) && TenantPolicy.Parse(r, JsonDocument.Parse(json).RootElement) is { } v ? v : null;
        return new TenantPolicyView(name, [.. TenantPolicy.Rules.Select(r => new TenantPolicyItem(r.Key, r.Kind, r.Strict, r.Min, r.Max, r.Options, TenantPolicy.Value(global, r.Key), Own(r), TenantPolicy.Value(effective, r.Key)))]);
    }

    public async Task<TenantPolicyView> UpdateTenantPolicyAsync(SaveTenantPolicy req, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        await RequireOrganisationAsync(ct);
        Guards.RequireRecentReauthForCritical(actor, await provider.GetAsync(ct), clock); // settings.manage is a critical permission
        if (req.Values is null || req.Values.Count == 0) throw DomainException.Validation("Nothing to change.", new Dictionary<string, string> { ["values"] = "Send at least one setting." });
        var global = await provider.GetGlobalAsync(ct);
        var rows = await db.TenantSettings.ToListAsync(ct);
        var f = new Dictionary<string, string>(); var changes = new Dictionary<string, object?>();
        foreach (var (key, el) in req.Values)
        {
            var r = TenantPolicy.Rule(key);
            if (r is null) { f[key] = "Unknown setting."; continue; }
            if (el.ValueKind == JsonValueKind.Null) { changes[key] = null; continue; }
            if (TenantPolicy.Parse(r, el) is not { } v) { f[key] = r.Kind == "int" ? $"Use {r.Min}–{r.Max}." : r.Kind == "enum" ? $"Use {string.Join(", ", r.Options!)}." : "Use true or false."; continue; }
            if (!TenantPolicy.AtLeastAsStrict(r, v, TenantPolicy.Value(global, key))) { f[key] = $"Can't be weaker than the platform setting ({TenantPolicy.Value(global, key)})."; continue; }
            changes[key] = v;
        }
        if (f.Count == 0)
        {
            var merged = rows.Where(x => !changes.ContainsKey(x.Key)).Select(x => (x.Key, x.ValueJson)).Concat(changes.Where(c => c.Value is not null).Select(c => (c.Key, JsonSerializer.Serialize(c.Value)))).ToList();
            var next = TenantPolicy.Apply(global, merged);
            if (next.SessionAbsoluteHours * 60 < next.SessionIdleMinutes) f["sessionAbsoluteHours"] = "The absolute lifetime can't be shorter than the idle timeout.";
            if (next.RememberAbsoluteDays < next.RememberIdleDays) f["rememberAbsoluteDays"] = "Not less than the idle window.";
        }
        if (f.Count > 0) throw DomainException.Validation("Please correct the highlighted fields.", f);

        var now = clock.UtcNow; var diff = new List<AuditDiff>();
        string Show(string? json) => json is null ? "platform default" : json.Trim('"');
        foreach (var (key, value) in changes)
        {
            var row = rows.FirstOrDefault(x => x.Key == key); var before = row?.ValueJson;
            if (value is null) { if (row is not null) { db.TenantSettings.Remove(row); diff.Add(new AuditDiff(key, Show(before), "platform default")); } continue; }
            var json = JsonSerializer.Serialize(value);
            if (row is null) db.TenantSettings.Add(new TenantSetting { TenantId = tenant.TenantId, Key = key, ValueJson = json, UpdatedBy = actor.UserId, UpdatedAt = now });
            else if (row.ValueJson != json) { row.ValueJson = json; row.UpdatedBy = actor.UserId; row.UpdatedAt = now; }
            else continue;
            diff.Add(new AuditDiff(key, Show(before), Show(json)));
        }
        if (diff.Count > 0)
        {
            audit.Record("settings.tenant.update", "tenant_settings", tenant.TenantId.ToString(), Outcome.Success, client, actor.UserId, actor.Email, diff: diff);
            audit.Security("policy.change", Severity.Medium, $"Security policy changed: {string.Join(", ", diff.Select(d => d.Field))}", actor.UserId);
            await db.SaveChangesAsync(ct);
        }
        return await GetTenantPolicyAsync(ct);
    }
}
