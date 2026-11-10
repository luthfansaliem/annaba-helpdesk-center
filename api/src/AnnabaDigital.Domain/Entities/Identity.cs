namespace AnnabaDigital.Domain.Entities;

public class User : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    /// <summary>Always stored lower-cased (DB check + unique index).</summary>
    public string Email { get; set; } = "";
    public string Name { get; set; } = "";
    public UserStatus Status { get; set; } = UserStatus.Pending;
    public DateTimeOffset? EmailVerifiedAt { get; set; }
    public int FailedLoginCount { get; set; }
    /// <summary>Temporary lock after failed attempts (status stays Active).</summary>
    public DateTimeOffset? LockedUntil { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeactivatedAt { get; set; }
    public int Version { get; set; } = 1;

    public UserCredential? Credential { get; set; }
    public UserProfile? Profile { get; set; }
    public List<UserRole> UserRoles { get; set; } = [];
    public List<MfaFactor> MfaFactors { get; set; } = [];
}

public class UserCredential
{
    public Guid UserId { get; set; }
    public string PasswordHash { get; set; } = "";
    public DateTimeOffset PasswordChangedAt { get; set; }
    public bool MustChange { get; set; }
}

public class UserProfile
{
    public Guid UserId { get; set; }
    public string? Phone { get; set; }
    public string? Department { get; set; }
    public string? JobTitle { get; set; }
    public string Language { get; set; } = "en";
    public string Timezone { get; set; } = "Asia/Jakarta";
    public bool NotifyEmail { get; set; } = true;
    public bool NotifySecurity { get; set; } = true;
    public bool NotifyProduct { get; set; }
    public int AutoLockMinutes { get; set; } = 15;
    public Guid? AvatarId { get; set; }
    /// <summary>APR-002. Who this person reports to; an approval step can ask for "the requester's manager".</summary>
    public Guid? ManagerId { get; set; }
}

public class Avatar
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    public string ContentType { get; set; } = "image/webp";
    public byte[] Bytes { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Extension point for OIDC/SAML (not used yet).</summary>
public class ExternalIdentity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    public string Provider { get; set; } = "";
    public string Subject { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

public class EmailVerificationToken
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    public string? NewEmail { get; set; }
    public string TokenHash { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class PasswordResetToken
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
    public string? RequestIp { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class MfaFactor
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    public MfaType Type { get; set; } = MfaType.Totp;
    public byte[] SecretCiphertext { get; set; } = [];
    public byte[] SecretNonce { get; set; } = [];
    public int KeyVersion { get; set; } = 1;
    public DateTimeOffset? ConfirmedAt { get; set; }
    public long LastUsedStep { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class MfaRecoveryCode
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    public string CodeHash { get; set; } = "";
    public DateTimeOffset? UsedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
