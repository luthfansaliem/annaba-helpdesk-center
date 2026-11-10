namespace AnnabaDigital.Domain.Entities;

/// <summary>Where a work order stands. Stored as text. The moves between them live in <see cref="AnnabaDigital.Domain.WorkOrderRules"/>.</summary>
public enum WorkOrderStatus { Open = 0, InProgress = 1, OnHold = 2, Done = 3, Cancelled = 4 }

/// <summary>
/// TKT-006. A piece of work spawned from a ticket: its own person or team, instructions, checklist, time, parts and costs, and photo evidence. Numbered after its ticket
/// (<c>GA-2026-000123-W1</c>). A <see cref="Required"/> work order must be done (or cancelled) before its ticket can be resolved; when the ticket asks for it, finishing the last
/// required one resolves the ticket.
/// </summary>
public class WorkOrder : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TicketId { get; set; }
    public int Seq { get; set; }
    public string Number { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Instructions { get; set; }
    public Guid? AssigneeId { get; set; }
    public Guid? TeamId { get; set; }
    public Guid? AssetId { get; set; }
    /// <summary>R2-4. The vendor doing the work (instead of, or with, a person of the organisation), and the purchase order it runs under (a reference only).</summary>
    public Guid? VendorId { get; set; }
    public string? PurchaseOrder { get; set; }
    public bool Required { get; set; } = true;
    /// <summary>TKT-007. Completing needs at least one "after" photo.</summary>
    public bool RequireAfterPhoto { get; set; }
    public WorkOrderStatus Status { get; set; } = WorkOrderStatus.Open;
    public string? HoldReason { get; set; }
    public DateTimeOffset? DueAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? CompletionNote { get; set; }
    public string? CancelReason { get; set; }
    public Guid CreatedById { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int Version { get; set; } = 1;
}

/// <summary>TKT-007. One checklist line of a work order. Required lines must be ticked before the work order can be completed.</summary>
public class WorkOrderTask : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid WorkOrderId { get; set; }
    public int Position { get; set; }
    public string Text { get; set; } = "";
    public bool Required { get; set; } = true;
    public DateTimeOffset? DoneAt { get; set; }
    public Guid? DoneById { get; set; }
    public string? DoneByLabel { get; set; }
    public string? Note { get; set; }
}

/// <summary>TKT-014. Time someone spent on a work order (entered by hand).</summary>
public class WorkOrderTime : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid WorkOrderId { get; set; }
    public Guid UserId { get; set; }
    public string UserLabel { get; set; } = "";
    public int Minutes { get; set; }
    public DateOnly WorkDate { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>TKT-006. A part, a service or another cost on a work order. Amounts are in the organisation's currency.</summary>
public class WorkOrderCost : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid WorkOrderId { get; set; }
    /// <summary><c>part</c>, <c>labour</c>, <c>service</c> or <c>other</c>.</summary>
    public string Kind { get; set; } = "part";
    public string Description { get; set; } = "";
    public decimal Quantity { get; set; } = 1;
    public decimal UnitCost { get; set; }
    public Guid CreatedById { get; set; }
    public string CreatedByLabel { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>TKT-007. A reusable checklist (for example "AC service: clean filters, check refrigerant…"), copied into a work order when chosen; preventive maintenance will use them too.</summary>
public class ChecklistTemplate : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Name { get; set; } = "";
    /// <summary>JSON array of <c>{ "text": "…", "required": true }</c>.</summary>
    public string ItemsJson { get; set; } = "[]";
    public DateTimeOffset? ArchivedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int Version { get; set; } = 1;
}
