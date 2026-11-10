namespace AnnabaDigital.Domain.Entities;

/// <summary>
/// INT-001. A key for the public API (<c>/api/v1</c>). Only the SHA-256 of the token is stored. The key acts as the person who made it, limited to its scopes: it can never
/// do more than that person may, and it stops working when that person is suspended or leaves.
/// </summary>
public class ApiKey : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Name { get; set; } = "";
    public string TokenHash { get; set; } = "";
    /// <summary>The first characters of the token (after the "adk_" prefix), to recognise it.</summary>
    public string Prefix { get; set; } = "";
    /// <summary>Space-separated scopes, e.g. <c>tickets.read tickets.write</c>.</summary>
    public string Scopes { get; set; } = "";
    public Guid UserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

/// <summary>INT-001. A create call made with an Idempotency-Key: repeating it returns the same ticket instead of a second one.</summary>
public class ApiIdempotency : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid ApiKeyId { get; set; }
    public string Key { get; set; } = "";
    public Guid TicketId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>INT-002. Where to send events. The signing secret is kept encrypted (it must be readable to sign); it is shown once when made.</summary>
public class WebhookEndpoint : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Url { get; set; } = "";
    public string? Description { get; set; }
    /// <summary>Space-separated event names.</summary>
    public string Events { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public byte[] SecretCiphertext { get; set; } = [];
    public int SecretKeyVersion { get; set; }
    public Guid CreatedById { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int Version { get; set; } = 1;
}

/// <summary>INT-002. One event for one endpoint, in an outbox written with the change that caused it; sent and retried by the dispatcher.</summary>
public class WebhookDelivery : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid EndpointId { get; set; }
    /// <summary>Same for every endpoint that gets this event; receivers use it to ignore replays.</summary>
    public Guid EventId { get; set; }
    public string Event { get; set; } = "";
    public string PayloadJson { get; set; } = "{}";
    public int Attempts { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
    public DateTimeOffset? FailedAt { get; set; }
    public int? LastStatus { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
