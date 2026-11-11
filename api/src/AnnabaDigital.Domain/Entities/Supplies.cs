namespace AnnabaDigital.Domain.Entities;

/// <summary>Pending (waits for the requester's manager) → Open (to fulfil) → PartlyFulfilled → Fulfilled; or Rejected, or Cancelled by the requester before anything was issued.
/// A partly fulfilled request stays open with its back-order (the quantity not yet issued) until fulfilled or closed.</summary>
public enum SupplyStatus { Pending = 0, Open = 1, PartlyFulfilled = 2, Fulfilled = 3, Rejected = 4, Cancelled = 5, Closed = 6 }

/// <summary>
/// INV-002. An employee's request for stock items (a cart), to be issued from one stock place. Above the organisation's approval amount it first waits for the
/// requester's manager. Fulfilment issues stock through the ledger (one Issue movement per line, sharing the request id); what cannot be issued yet is the back-order.
/// </summary>
public class SupplyRequest : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    /// <summary>Human number per organisation and year, e.g. <c>SR-2026-0042</c>.</summary>
    public string Number { get; set; } = "";
    public Guid RequesterId { get; set; }
    /// <summary>The stock place the items come from (and where they are collected).</summary>
    public Guid LocationId { get; set; }
    public string? Note { get; set; }
    public SupplyStatus Status { get; set; }
    /// <summary>Sum of quantity × unit cost when requested (for the approval rule and reports).</summary>
    public decimal EstimatedCost { get; set; }
    public Guid? ApproverId { get; set; }
    public Guid? DecidedById { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public string? DecisionNote { get; set; }
    public DateTimeOffset? FulfilledAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int Version { get; set; } = 1;
    public List<SupplyRequestLine> Lines { get; set; } = [];
}

public class SupplyRequestLine : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid RequestId { get; set; }
    public Guid ItemId { get; set; }
    public int Quantity { get; set; }
    public int Issued { get; set; }
    public decimal? UnitCost { get; set; }
}

/// <summary>Per organisation: requests whose estimated cost is at or above this amount wait for the requester's manager (empty: never). The default stock place for requests.</summary>
public class SupplySettings : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public decimal? ApprovalAbove { get; set; }
    public Guid? DefaultLocationId { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public static class SupplyRules
{
    public const int MaxLines = 30, MaxQuantity = 10_000;
}
