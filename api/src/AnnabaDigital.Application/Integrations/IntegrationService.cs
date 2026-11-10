using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Application.Users;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;

namespace AnnabaDigital.Application.Integrations;

public sealed record ApiKeyItem(Guid Id, string Name, string Prefix, IReadOnlyList<string> Scopes, string? Owner, DateTimeOffset CreatedAt, DateTimeOffset? ExpiresAt, DateTimeOffset? LastUsedAt, bool Active);
public sealed record CreateApiKeyRequest(string? Name, string[]? Scopes, DateOnly? ExpiresOn);
public sealed record NewApiKey(ApiKeyItem Key, string Token);
public sealed record ApiKeyIdentity(Guid KeyId, Guid TenantId, Guid UserId, string Email, string Name, IReadOnlySet<string> Scopes);
public sealed record WebhookItem(Guid Id, string Url, string? Description, IReadOnlyList<string> Events, bool Enabled, DateTimeOffset CreatedAt, int Pending, int Failed, DateTimeOffset? LastDeliveredAt, int Version);
public sealed record SaveWebhookRequest(string? Url, string? Description, string[]? Events, bool Enabled = true, int? Version = null);
public sealed record NewWebhook(WebhookItem Webhook, string Secret);
public sealed record DeliveryItem(Guid Id, Guid EventId, string Event, int Attempts, DateTimeOffset CreatedAt, DateTimeOffset? DeliveredAt, DateTimeOffset? FailedAt, DateTimeOffset NextAttemptAt, int? LastStatus, string? LastError);

/// <summary>
/// INT-001/002. API keys (made by people with <c>integrations.manage</c> after re-entering their password; the token is shown once and only its hash is kept) and webhook
/// endpoints (HTTPS to public addresses only; the signing secret shown once, kept encrypted). <see cref="PublishAsync"/> writes one delivery per subscribed endpoint into the
/// same unit of work as the change that caused it (a transactional outbox); the dispatcher signs, sends and retries them.
/// </summary>
public sealed class IntegrationService(IAppDb db, IClock clock, AuditWriter audit, ITenantContext tenant, ISecretProtector protector, SettingsProvider settings, IConfiguration config)
{
    private static DomainException Invalid(Dictionary<string, string> e) => DomainException.Validation("Please correct the highlighted fields.", e);
    private static string? Clean(string? s) { var t = (s ?? "").Trim(); return t.Length == 0 ? null : t; }
    private static string Token(int bytes) => Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytes)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private bool AllowPrivate => string.Equals(config["Webhooks:AllowPrivateTargets"], "true", StringComparison.OrdinalIgnoreCase);
    private static byte[] Aad(Guid endpointId) => Encoding.UTF8.GetBytes("webhook:" + endpointId);

    // ================================================================ API keys
    public async Task<IReadOnlyList<ApiKeyItem>> KeysAsync(CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        return (await db.ApiKeys.AsNoTracking().OrderByDescending(k => k.CreatedAt).Select(k => new { k, Owner = db.Users.Where(u => u.Id == k.UserId).Select(u => u.Name).FirstOrDefault() }).ToListAsync(ct))
            .Select(x => new ApiKeyItem(x.k.Id, x.k.Name, x.k.Prefix, x.k.Scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries), x.Owner, x.k.CreatedAt, x.k.ExpiresAt, x.k.LastUsedAt,
                x.k.RevokedAt is null && (x.k.ExpiresAt is null || x.k.ExpiresAt > now))).ToList();
    }

    public async Task<NewApiKey> CreateKeyAsync(CreateApiKeyRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        Guards.RequireRecentReauthForCritical(actor, await settings.GetAsync(ct), clock);
        var e = new Dictionary<string, string>();
        var name = (r.Name ?? "").Trim();
        if (name.Length is < 3 or > 80) e["name"] = "Use 3–80 characters.";
        var scopes = (r.Scopes ?? []).Select(s => s.Trim().ToLowerInvariant()).Distinct().ToList();
        if (scopes.Count == 0 || scopes.Any(s => !IntegrationRules.Scopes.Contains(s))) e["scopes"] = $"Choose from {string.Join(", ", IntegrationRules.Scopes)}.";
        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        if (r.ExpiresOn is { } exp && (exp <= today || exp > today.AddYears(3))) e["expiresOn"] = "Use a date after today and within three years.";
        if (await db.ApiKeys.CountAsync(k => k.RevokedAt == null, ct) >= IntegrationRules.MaxKeys) e["name"] = $"At most {IntegrationRules.MaxKeys} active keys.";
        if (e.Count > 0) throw Invalid(e);
        var token = IntegrationRules.TokenPrefix + Token(32);
        var k = new ApiKey
        {
            Name = name, TokenHash = IntegrationRules.Hash(token), Prefix = token.Substring(IntegrationRules.TokenPrefix.Length, 8), Scopes = string.Join(' ', scopes.Order()), UserId = actor.UserId,
            CreatedAt = clock.UtcNow, ExpiresAt = r.ExpiresOn is { } d ? new DateTimeOffset(d.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero) : null,
        };
        db.ApiKeys.Add(k);
        audit.Record("integration.key.create", "api_key", k.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["name"] = name, ["scopes"] = k.Scopes, ["prefix"] = k.Prefix });
        await db.SaveChangesAsync(ct);
        return new NewApiKey((await KeysAsync(ct)).First(x => x.Id == k.Id), token);
    }

    public async Task RevokeKeyAsync(Guid id, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var k = await db.ApiKeys.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("API key");
        k.RevokedAt ??= clock.UtcNow;
        audit.Record("integration.key.revoke", "api_key", k.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["name"] = k.Name, ["prefix"] = k.Prefix });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>The key behind a bearer token: active, not expired, its person active. Records when it was last used (at most once a minute). Null otherwise.</summary>
    public async Task<ApiKeyIdentity?> ValidateAsync(string token, CancellationToken ct = default)
    {
        if (!token.StartsWith(IntegrationRules.TokenPrefix, StringComparison.Ordinal) || token.Length is < 20 or > 100) return null;
        var hash = IntegrationRules.Hash(token); var now = clock.UtcNow;
        var row = await db.ApiKeys.IgnoreQueryFilters().AsNoTracking().Where(k => k.TokenHash == hash && k.RevokedAt == null && (k.ExpiresAt == null || k.ExpiresAt > now))
            .Select(k => new { k.Id, k.TenantId, k.UserId, k.Scopes, k.LastUsedAt, User = db.Users.IgnoreQueryFilters().Where(u => u.Id == k.UserId && u.Status == UserStatus.Active).Select(u => new { u.Email, u.Name, u.TenantId }).FirstOrDefault() })
            .FirstOrDefaultAsync(ct);
        if (row?.User is null || row.User.TenantId != row.TenantId) return null;
        if (row.LastUsedAt is null || now - row.LastUsedAt > TimeSpan.FromMinutes(1))
            await db.ApiKeys.IgnoreQueryFilters().Where(k => k.Id == row.Id).ExecuteUpdateAsync(u => u.SetProperty(k => k.LastUsedAt, now), ct);
        return new ApiKeyIdentity(row.Id, row.TenantId, row.UserId, row.User.Email, row.User.Name, row.Scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet());
    }

    /// <summary>The ticket made earlier with this key and Idempotency-Key, if any.</summary>
    public Task<Guid?> IdempotentTicketAsync(Guid keyId, string key, CancellationToken ct = default)
        => db.ApiIdempotency.AsNoTracking().Where(x => x.ApiKeyId == keyId && x.Key == key).Select(x => (Guid?)x.TicketId).FirstOrDefaultAsync(ct);

    public async Task RememberIdempotentAsync(Guid keyId, string key, Guid ticketId, CancellationToken ct = default)
    {
        db.ApiIdempotency.Add(new ApiIdempotency { ApiKeyId = keyId, Key = key, TicketId = ticketId, CreatedAt = clock.UtcNow });
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateException) { db.ResetTracking(); }   // a concurrent repeat stored it first
    }

    // ================================================================ webhooks
    public async Task<IReadOnlyList<WebhookItem>> WebhooksAsync(CancellationToken ct = default)
        => await db.WebhookEndpoints.AsNoTracking().OrderBy(w => w.CreatedAt).Select(w => new WebhookItem(w.Id, w.Url, w.Description, w.Events.Split(' ', StringSplitOptions.RemoveEmptyEntries), w.Enabled, w.CreatedAt,
            db.WebhookDeliveries.Count(d => d.EndpointId == w.Id && d.DeliveredAt == null && d.FailedAt == null), db.WebhookDeliveries.Count(d => d.EndpointId == w.Id && d.FailedAt != null),
            db.WebhookDeliveries.Where(d => d.EndpointId == w.Id && d.DeliveredAt != null).Max(d => d.DeliveredAt), w.Version)).ToListAsync(ct);

    private (string Url, string? Description, List<string> Events) Check(SaveWebhookRequest r)
    {
        var e = new Dictionary<string, string>();
        var url = (r.Url ?? "").Trim();
        if (IntegrationRules.CheckUrl(url, AllowPrivate) is { } bad) e["url"] = bad;
        var events = (r.Events ?? []).Select(x => x.Trim().ToLowerInvariant()).Distinct().ToList();
        if (events.Count == 0 || events.Any(x => !IntegrationRules.Events.Contains(x))) e["events"] = $"Choose from {string.Join(", ", IntegrationRules.Events)}.";
        var desc = Clean(r.Description);
        if (desc is { Length: > 200 }) e["description"] = "Use at most 200 characters.";
        if (e.Count > 0) throw Invalid(e);
        return (url, desc, events);
    }

    public async Task<NewWebhook> CreateWebhookAsync(SaveWebhookRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        Guards.RequireRecentReauthForCritical(actor, await settings.GetAsync(ct), clock);
        var (url, desc, events) = Check(r);
        if (await db.WebhookEndpoints.CountAsync(ct) >= IntegrationRules.MaxEndpoints) throw Invalid(new() { ["url"] = $"At most {IntegrationRules.MaxEndpoints} webhooks." });
        var w = new WebhookEndpoint { Url = url, Description = desc, Events = string.Join(' ', events.Order()), Enabled = r.Enabled, CreatedById = actor.UserId, CreatedAt = clock.UtcNow, UpdatedAt = clock.UtcNow };
        var secret = "whsec_" + Token(32);
        var enc = protector.Protect(Encoding.UTF8.GetBytes(secret), Aad(w.Id));
        w.SecretCiphertext = [.. enc.Nonce, .. enc.Ciphertext]; w.SecretKeyVersion = enc.KeyVersion;
        db.WebhookEndpoints.Add(w);
        audit.Record("integration.webhook.create", "webhook", w.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["url"] = url, ["events"] = w.Events });
        await db.SaveChangesAsync(ct);
        return new NewWebhook((await WebhooksAsync(ct)).First(x => x.Id == w.Id), secret);
    }

    public async Task<WebhookItem> UpdateWebhookAsync(Guid id, SaveWebhookRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var w = await db.WebhookEndpoints.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Webhook");
        if (r.Version is { } v && v != w.Version) throw DomainException.Conflict("This webhook was changed by someone else. Reload and try again.");
        var (url, desc, events) = Check(r);
        if (url != w.Url) Guards.RequireRecentReauthForCritical(actor, await settings.GetAsync(ct), clock);     // a new destination for the organisation's data
        w.Url = url; w.Description = desc; w.Events = string.Join(' ', events.Order()); w.Enabled = r.Enabled; w.UpdatedAt = clock.UtcNow; w.Version++;
        audit.Record("integration.webhook.update", "webhook", w.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["url"] = url, ["events"] = w.Events, ["enabled"] = r.Enabled.ToString() });
        await db.SaveChangesAsync(ct);
        return (await WebhooksAsync(ct)).First(x => x.Id == id);
    }

    public async Task DeleteWebhookAsync(Guid id, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var w = await db.WebhookEndpoints.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Webhook");
        db.WebhookEndpoints.Remove(w);
        audit.Record("integration.webhook.delete", "webhook", w.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["url"] = w.Url });
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<DeliveryItem>> DeliveriesAsync(Guid id, CancellationToken ct = default)
    {
        if (!await db.WebhookEndpoints.AnyAsync(x => x.Id == id, ct)) throw DomainException.NotFound("Webhook");
        return await db.WebhookDeliveries.AsNoTracking().Where(d => d.EndpointId == id).OrderByDescending(d => d.CreatedAt).Take(50)
            .Select(d => new DeliveryItem(d.Id, d.EventId, d.Event, d.Attempts, d.CreatedAt, d.DeliveredAt, d.FailedAt, d.NextAttemptAt, d.LastStatus, d.LastError)).ToListAsync(ct);
    }

    /// <summary>The signing secret of an endpoint (for the dispatcher, which runs in the endpoint's organisation).</summary>
    public string SecretOf(WebhookEndpoint w)
    {
        var nonce = w.SecretCiphertext[..12]; var cipher = w.SecretCiphertext[12..];
        return Encoding.UTF8.GetString(protector.Unprotect(new ProtectedSecret(cipher, nonce, w.SecretKeyVersion), Aad(w.Id)));
    }

    // ================================================================ events (transactional outbox)
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Adds the event for every enabled endpoint subscribed to it; saved by the caller together with the change.</summary>
    public async Task PublishAsync(string evt, Ticket t, object? extra = null, CancellationToken ct = default)
    {
        var endpoints = await db.WebhookEndpoints.AsNoTracking().Where(w => w.Enabled).Select(w => new { w.Id, w.Events }).ToListAsync(ct);
        var targets = endpoints.Where(w => w.Events.Split(' ').Contains(evt)).ToList();
        if (targets.Count == 0) return;
        var eventId = Guid.CreateVersion7(); var now = clock.UtcNow;
        var payload = JsonSerializer.Serialize(new
        {
            id = eventId, type = evt, createdAt = now,
            data = new
            {
                ticket = new { t.Id, t.Number, t.Subject, Status = t.Status.ToString().ToLowerInvariant(), Priority = t.Priority.ToString().ToLowerInvariant(), t.ServiceId, t.ServiceName, t.RequesterId, t.AssigneeId, t.TeamId, t.LocationId, t.AssetId, t.CreatedAt, t.UpdatedAt },
                extra,
            },
        }, Json);
        foreach (var w in targets)
            db.WebhookDeliveries.Add(new WebhookDelivery { TenantId = tenant.TenantId, EndpointId = w.Id, EventId = eventId, Event = evt, PayloadJson = payload, NextAttemptAt = now, CreatedAt = now });
    }
}
