namespace AnnabaDigital.Domain.Entities;

public enum TenantStatus { Active, Suspended }

/// <summary>An organisation that owns its own users, roles, invitations, sessions, audit trail and reviews. The default tenant is the platform tenant.</summary>
public class Tenant
{
    /// <summary>The tenant every pre-existing row belongs to; also the platform tenant, whose super admins create other tenants.</summary>
    public static readonly Guid DefaultId = new("00000000-0000-7000-8000-000000000001");

    public Guid Id { get; set; } = Guid.CreateVersion7();
    /// <summary>Short, URL-safe, unique identifier (a-z, 0-9, hyphen).</summary>
    public string Slug { get; set; } = "";
    public string Name { get; set; } = "";
    public TenantStatus Status { get; set; } = TenantStatus.Active;
    public bool IsPlatform { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Rows that belong to exactly one tenant. The database context filters every query by the signed-in person's tenant and stamps new rows with it.</summary>
public interface ITenantScoped { Guid TenantId { get; set; } }

/// <summary>An organisation's own security-policy value, stricter than (or equal to) the platform's. Only overrides are stored; no row means "use the platform value".</summary>
public class TenantSetting : ITenantScoped
{
    public Guid TenantId { get; set; }
    public string Key { get; set; } = "";
    public string ValueJson { get; set; } = "null";
    public Guid? UpdatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
