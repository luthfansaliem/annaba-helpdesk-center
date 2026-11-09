namespace AnnabaDigital.Domain.Entities;

public class Session : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    /// <summary>SHA-256 (hex) of the 256-bit random token held only in the cookie.</summary>
    public string TokenHash { get; set; } = "";
    public Guid UserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastActiveAt { get; set; }
    public DateTimeOffset IdleExpiresAt { get; set; }
    public DateTimeOffset AbsoluteExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? RevokedReason { get; set; }
    /// <summary>Server-side lock: while set, every request except unlock/logout is rejected (423).</summary>
    public DateTimeOffset? LockedAt { get; set; }
    /// <summary>True until the second factor has been verified (session unusable meanwhile).</summary>
    public bool MfaRequired { get; set; }
    public DateTimeOffset? MfaVerifiedAt { get; set; }
    public DateTimeOffset? ReauthAt { get; set; }
    public bool Remember { get; set; }
    public string? Ip { get; set; }
    public string? UserAgent { get; set; }
    public string DeviceLabel { get; set; } = "Unknown device";
    public string BrowserLabel { get; set; } = "Unknown browser";
    /// <summary>Platform support mode: the organisation this session is currently administering (null = its own). Ends by itself after a couple of hours.</summary>
    public Guid? ActingTenantId { get; set; }
    public DateTimeOffset? ActingSince { get; set; }
    public User? User { get; set; }
}

public class AuditEvent : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public long Id { get; set; }
    public DateTimeOffset Ts { get; set; }
    public Guid? ActorId { get; set; }
    public string ActorLabel { get; set; } = "anonymous";
    public string Action { get; set; } = "";
    public string TargetType { get; set; } = "";
    public string? TargetId { get; set; }
    public Outcome Outcome { get; set; }
    public string? Ip { get; set; }
    public string? UserAgent { get; set; }
    public string? RequestId { get; set; }
    /// <summary>Redacted metadata (jsonb).</summary>
    public string? MetaJson { get; set; }
    /// <summary>Safe before/after diff (jsonb).</summary>
    public string? DiffJson { get; set; }
}

public class SecurityEvent : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public DateTimeOffset Ts { get; set; }
    public string Type { get; set; } = "";
    public Severity Severity { get; set; }
    public Guid? UserId { get; set; }
    public string Detail { get; set; } = "";
    public string? MetaJson { get; set; }
    public Guid? AcknowledgedBy { get; set; }
    public DateTimeOffset? AcknowledgedAt { get; set; }
}

public class SystemSetting
{
    public string Key { get; set; } = "";
    public string? ValueJson { get; set; }
    public bool IsSecret { get; set; }
    public byte[]? ValueCiphertext { get; set; }
    public int KeyVersion { get; set; } = 1;
    public Guid? UpdatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int Version { get; set; } = 1;
}

public class RateLimitBucket
{
    public string Key { get; set; } = "";
    public int Count { get; set; }
    public DateTimeOffset WindowStart { get; set; }
    public DateTimeOffset? BlockedUntil { get; set; }
}

/// <summary>
/// Durable outgoing email. The body holds one-time links, so it is wiped as soon as the message is sent or abandoned;
/// only the delivery metadata stays until retention removes the row.
/// </summary>
public class OutboxEmail : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string ToAddress { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Text { get; set; } = "";
    public string Html { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public DateTimeOffset? FailedAt { get; set; }
    /// <summary>Exception type name only; provider responses are never stored.</summary>
    public string? LastError { get; set; }
}
