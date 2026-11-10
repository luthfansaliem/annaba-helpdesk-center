namespace AnnabaDigital.Domain.Entities;

/// <summary>Where an asset stands. Stored as text. <see cref="Retired"/> is reached only by retiring (with a reason) and left only by restoring.</summary>
public enum AssetStatus { InUse = 0, InRepair = 1, Stored = 2, Retired = 3 }

/// <summary>
/// AST-001. A kind of asset (air conditioner, vehicle, laptop, generator…) with the extra questions its assets answer. The questions use the form engine's definition (a subset of
/// its types), so changing them never rewrites the answers assets already have; they are checked again the next time an asset is edited.
/// </summary>
public class AssetType : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    /// <summary>JSON array of <c>FormField</c>.</summary>
    public string FieldsJson { get; set; } = "[]";
    public DateTimeOffset? ArchivedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int Version { get; set; } = 1;
}

/// <summary>AST-001. One physical thing the organisation looks after. The tag is unique per organisation (upper case); without one the register hands out <c>AST-000001</c>, <c>AST-000002</c>…</summary>
public class Asset : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Tag { get; set; } = "";
    public string Name { get; set; } = "";
    public Guid AssetTypeId { get; set; }
    public string? Serial { get; set; }
    public AssetStatus Status { get; set; } = AssetStatus.InUse;
    public Guid? LocationId { get; set; }
    /// <summary>The person who has it (a laptop, a car), if any.</summary>
    public Guid? AssignedToId { get; set; }
    public string? Department { get; set; }
    public DateOnly? PurchaseDate { get; set; }
    public decimal? PurchaseCost { get; set; }
    public DateOnly? WarrantyEnd { get; set; }
    /// <summary>Free text until the vendor register (R2-4) exists.</summary>
    public string? VendorName { get; set; }
    public string? Notes { get; set; }
    /// <summary>Normalised answers to the type's questions, keyed by question key.</summary>
    public string FieldsJson { get; set; } = "{}";
    public DateTimeOffset? RetiredAt { get; set; }
    public string? RetireReason { get; set; }
    /// <summary>AST-005. Which warranty reminder went out for the current warranty end: 0 none, 1 "ends within 30 days", 2 "has ended". Reset when the warranty end changes.</summary>
    public int WarrantyNotice { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int Version { get; set; } = 1;
}

public enum AssetEventKind { Created = 0, Updated = 1, Moved = 2, Assigned = 3, Status = 4, Retired = 5, Restored = 6 }

/// <summary>AST-004. One line of an asset's life (created, moved, handed to someone, status changed, retired…). Append-only; also mirrored in the audit log.</summary>
public class AssetEvent : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid AssetId { get; set; }
    public AssetEventKind Kind { get; set; }
    public Guid? ActorId { get; set; }
    public string ActorLabel { get; set; } = "";
    public string? Body { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Last automatic asset tag number handed out per organisation. Incremented only through one atomic upsert.</summary>
public class AssetCounter : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public int Last { get; set; }
}
