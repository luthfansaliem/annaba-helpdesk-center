namespace AnnabaDigital.Domain.Entities;

/// <summary>How urgent a request is by default. The ticket slice reuses this for the priority matrix.</summary>
public enum TicketPriority { Low = 0, Medium = 1, High = 2, Urgent = 3 }

/// <summary>Who may see a service in the requester portal. "Staff" = people who can manage the catalog (agents, supervisors, admins).</summary>
public enum ServiceAudience { Everyone = 0, Staff = 1 }

/// <summary>
/// A group of services, e.g. <i>Facilities</i>. Two levels at most: a root category may hold sub-categories, a sub-category may not.
/// Archived categories disappear from the portal; they can only be archived once nothing live is left inside.
/// </summary>
public class ServiceCategory : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid? ParentId { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    /// <summary>A key from the UI icon set (never markup).</summary>
    public string? Icon { get; set; }
    public int SortOrder { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int Version { get; set; } = 1;
}

/// <summary>
/// Something a person can ask for, e.g. <i>AC repair</i>. A service starts unpublished so the form can be built first. Its form is versioned:
/// every save creates a new immutable <see cref="ServiceFormVersion"/>, so tickets always keep the version they were created with.
/// </summary>
public class ServiceItem : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid CategoryId { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? Icon { get; set; }
    /// <summary>The team that receives these requests by default (assignment rules refine this later).</summary>
    public Guid? OwningTeamId { get; set; }
    public TicketPriority DefaultPriority { get; set; } = TicketPriority.Medium;
    public ServiceAudience Audience { get; set; } = ServiceAudience.Everyone;
    public bool IsPublished { get; set; }
    /// <summary>Number of the form version in force; 0 = the service has no form yet (no questions).</summary>
    public int CurrentFormVersion { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int Version { get; set; } = 1;
}

/// <summary>One immutable snapshot of a service's form. <see cref="FieldsJson"/> is the field list as validated by <c>FormEngine</c>.</summary>
public class ServiceFormVersion : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid ServiceId { get; set; }
    public int Number { get; set; }
    public string FieldsJson { get; set; } = "[]";
    public int FieldCount { get; set; }
    public string? Note { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
