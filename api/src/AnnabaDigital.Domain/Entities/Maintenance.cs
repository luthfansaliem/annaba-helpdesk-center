namespace AnnabaDigital.Domain.Entities;

/// <summary>How often a maintenance plan repeats: every <c>n</c> days, weeks, months or years. Stored as text.</summary>
public enum PmUnit { Day = 0, Week = 1, Month = 2, Year = 3 }

/// <summary>
/// PM-001. A preventive-maintenance plan: what is serviced (one asset, every asset of a type — optionally only in a place — or a place itself), how often, how many days ahead the work
/// is prepared, and who does it with which checklist. The scheduler turns each due date into a <see cref="MaintenanceVisit"/>: a ticket on <see cref="ServiceId"/> with one work order.
/// Due dates are counted from <see cref="StartsOn"/> (occurrence <see cref="NextIndex"/>), never from the previous date, so monthly plans do not drift at month ends.
/// </summary>
public class MaintenancePlan : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Name { get; set; } = "";
    /// <summary>Instructions copied into every work order.</summary>
    public string? Instructions { get; set; }
    /// <summary>The catalog service the visits are filed under (it may be unpublished: staff-only).</summary>
    public Guid ServiceId { get; set; }
    public Guid? AssetId { get; set; }
    public Guid? AssetTypeId { get; set; }
    /// <summary>With <see cref="AssetTypeId"/>: only assets in this place or below it. Alone: the place itself is serviced.</summary>
    public Guid? LocationId { get; set; }
    public PmUnit Unit { get; set; } = PmUnit.Month;
    public int Every { get; set; } = 1;
    public DateOnly StartsOn { get; set; }
    public DateOnly? EndsOn { get; set; }
    /// <summary>The work is created this many days before the due date.</summary>
    public int LeadDays { get; set; } = 7;
    /// <summary>The occurrence the scheduler makes next (0 = <see cref="StartsOn"/>); <see cref="NextDueOn"/> is its date, null once the plan has ended.</summary>
    public int NextIndex { get; set; }
    public DateOnly? NextDueOn { get; set; }
    public Guid? ChecklistTemplateId { get; set; }
    public Guid? TeamId { get; set; }
    public Guid? AssigneeId { get; set; }
    public Guid? VendorId { get; set; }
    public bool RequireAfterPhoto { get; set; }
    public DateTimeOffset? PausedAt { get; set; }
    /// <summary>The person the visits' tickets are filed for (who made the plan, or the last to change it).</summary>
    public Guid OwnerId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int Version { get; set; } = 1;
}

/// <summary>
/// PM-002/003. One planned visit: a plan's due date for one target. Unique per plan, target and date, so a retried or concurrent run never makes it twice. A visit whose date had
/// already been overtaken by the next one when the scheduler caught up (it was down, or the plan was behind) is recorded as <see cref="Missed"/> without a ticket, so the
/// compliance report shows it.
/// </summary>
public class MaintenanceVisit : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid PlanId { get; set; }
    /// <summary>The asset id, the place id (a plan for a place), for uniqueness.</summary>
    public Guid TargetKey { get; set; }
    public Guid? AssetId { get; set; }
    public Guid? LocationId { get; set; }
    public DateOnly DueOn { get; set; }
    public Guid? TicketId { get; set; }
    public Guid? WorkOrderId { get; set; }
    public bool Missed { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
