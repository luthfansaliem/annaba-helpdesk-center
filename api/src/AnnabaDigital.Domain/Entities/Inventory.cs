namespace AnnabaDigital.Domain.Entities;

/// <summary>INV-001. A stock item: ATK (office supplies), pantry, cleaning supplies, uniforms… with its unit and the level at which it should be reordered.
/// Stock is kept per site (<see cref="StockBalance"/>); the quantity only ever changes through movements (<see cref="StockMovement"/>).</summary>
public class InventoryItem : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    /// <summary>Unique per organisation, upper case, e.g. <c>ATK-PEN-BLK</c>.</summary>
    public string Sku { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>One of <see cref="InventoryRules.Categories"/>.</summary>
    public string Category { get; set; } = "atk";
    /// <summary>pcs, box, ream, pack, bottle, litre, kg, set…</summary>
    public string Unit { get; set; } = "pcs";
    /// <summary>At or below this per site the item is low and the people who keep stock are told.</summary>
    public int ReorderLevel { get; set; }
    /// <summary>The usual quantity to buy when it is low (suggested purchase list).</summary>
    public int? ReorderQuantity { get; set; }
    /// <summary>The cost per unit of the last receipt (the stock value and supply costs use it).</summary>
    public decimal? UnitCost { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int Version { get; set; } = 1;
}

/// <summary>INV-003. The cached balance of one item at one site; the database refuses a negative quantity. Always equal to the sum of the item's movements there.</summary>
public class StockBalance : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid ItemId { get; set; }
    public Guid LocationId { get; set; }
    public int Quantity { get; set; }
    /// <summary>Set when the low-stock alert went out; cleared when the balance rises above the reorder level again.</summary>
    public DateTimeOffset? LowNotifiedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public enum StockMovementKind { Receipt = 0, Issue = 1, Adjustment = 2, TransferOut = 3, TransferIn = 4, Count = 5 }

/// <summary>INV-003. One change of stock: a receipt, an issue, an adjustment with a reason, either half of a transfer between sites, or a stock-take correction.
/// Never edited or deleted; <see cref="BalanceAfter"/> is the site balance right after it.</summary>
public class StockMovement : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid ItemId { get; set; }
    public Guid LocationId { get; set; }
    public StockMovementKind Kind { get; set; }
    /// <summary>Signed: positive adds stock, negative takes it.</summary>
    public int Quantity { get; set; }
    public int BalanceAfter { get; set; }
    public decimal? UnitCost { get; set; }
    /// <summary>Why or for what: supplier and invoice for a receipt, who for an issue, the reason for an adjustment.</summary>
    public string? Reference { get; set; }
    /// <summary>Both halves of a transfer share this id; a supply request's issues carry the request id (R3-7).</summary>
    public Guid? GroupId { get; set; }
    public Guid ById { get; set; }
    public DateTimeOffset At { get; set; }
}

/// <summary>INV-004. A cycle count of one site: the expected quantities are frozen when it starts; closing it posts a Count movement for every difference.</summary>
public class StockTake : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid LocationId { get; set; }
    public string? Note { get; set; }
    public Guid StartedById { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public Guid? ClosedById { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public List<StockTakeLine> Lines { get; set; } = [];
}

public class StockTakeLine : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid StockTakeId { get; set; }
    public Guid ItemId { get; set; }
    public int Expected { get; set; }
    public int? Counted { get; set; }
}

public static class InventoryRules
{
    public static readonly string[] Categories = ["atk", "pantry", "cleaning", "uniform", "toiletries", "it", "other"];
    public const int MaxQuantity = 1_000_000;
}
