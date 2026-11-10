namespace AnnabaDigital.Domain.Entities;

/// <summary>
/// R2-11 (PRD Q6). A sign-in of a vendor's person: it sees and works only the work orders given to that vendor, and nothing else of the organisation (no requests,
/// tickets, people, places or knowledge base). One vendor per person.
/// </summary>
public class VendorUser : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid UserId { get; set; }
    public Guid VendorId { get; set; }
    public Guid CreatedById { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>VND-001. A company the organisation buys services from (maintenance, cleaning, security…). A blocked vendor keeps its history but gets no new work.</summary>
public class Vendor : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Name { get; set; } = "";
    /// <summary>What they do, in a sentence (e.g. "AC and refrigeration").</summary>
    public string? Services { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Website { get; set; }
    public string? Address { get; set; }
    /// <summary>Tax number (NPWP in Indonesia), free text.</summary>
    public string? TaxId { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset? BlockedAt { get; set; }
    public string? BlockReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int Version { get; set; } = 1;
}

public class VendorContact : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid VendorId { get; set; }
    public int Position { get; set; }
    public string Name { get; set; } = "";
    public string? Role { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
}

/// <summary>VND-001. A place (and everything below it) the vendor covers.</summary>
public class VendorSite : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid VendorId { get; set; }
    public Guid LocationId { get; set; }
}

/// <summary>VND-001/005. A contract with a vendor: reference, dates and value. Reminders go out before it ends (<see cref="ExpiryNotice"/> works like an asset's warranty notice).</summary>
public class VendorContract : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid VendorId { get; set; }
    public string Title { get; set; } = "";
    public string? Reference { get; set; }
    public DateOnly StartsOn { get; set; }
    public DateOnly? EndsOn { get; set; }
    public decimal? Value { get; set; }
    public string? Notes { get; set; }
    /// <summary>0 none, 1 "ends within 30 days" sent, 2 "has ended" sent; reset when the end date changes.</summary>
    public int ExpiryNotice { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>VND-001. A document kept for a vendor (a signed contract, insurance, a certificate). Bytes behind the storage port, checked and scanned like ticket files; soft-deleted.</summary>
public class VendorDocument : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid VendorId { get; set; }
    public Guid? ContractId { get; set; }
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long Size { get; set; }
    public string Sha256 { get; set; } = "";
    public string StorageKey { get; set; } = "";
    public Guid UploadedById { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}
