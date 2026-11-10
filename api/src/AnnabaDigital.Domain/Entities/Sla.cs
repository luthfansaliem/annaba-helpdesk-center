namespace AnnabaDigital.Domain.Entities;

/// <summary>Where a ticket stands against its targets. Stored on the ticket (refreshed by actions and by a periodic check) so lists can filter and sort on it.</summary>
public enum SlaState { None = 0, OnTrack = 1, AtRisk = 2, Breached = 3, Paused = 4, Met = 5 }

/// <summary>
/// SLA-002. How fast a kind of ticket must be answered and resolved, in <i>working</i> minutes of a business calendar. Policies are tried in <see cref="Position"/> order and the
/// first live one whose conditions all match wins; empty conditions match everything. Escalation thresholds are percentages of the resolution target.
/// </summary>
public class SlaPolicy : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Name { get; set; } = "";
    public int Position { get; set; }
    public Guid? ServiceId { get; set; }
    public Guid? CategoryId { get; set; }
    /// <summary>Matches this place and everything inside it.</summary>
    public Guid? LocationId { get; set; }
    public TicketPriority? Priority { get; set; }
    /// <summary>Overrides the calendar otherwise taken from the ticket's place (and, failing that, the organisation default).</summary>
    public Guid? CalendarId { get; set; }
    public int FirstResponseMinutes { get; set; }
    public int ResolutionMinutes { get; set; }
    public int Escalate1Percent { get; set; } = 50;
    public int Escalate2Percent { get; set; } = 80;
    public DateTimeOffset? ArchivedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int Version { get; set; } = 1;
}

public enum AssignStrategy { None = 0, RoundRobin = 1, LeastLoaded = 2 }

/// <summary>
/// TKT-003. Routes a new ticket to a team and, optionally, to one of its members. Rules are tried in <see cref="Position"/> order; the first live rule whose conditions all
/// match wins. With no matching rule the service's owning team is used and nobody is assigned.
/// </summary>
public class AssignmentRule : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Name { get; set; } = "";
    public int Position { get; set; }
    public Guid? ServiceId { get; set; }
    public Guid? CategoryId { get; set; }
    public Guid? LocationId { get; set; }
    public TicketPriority? Priority { get; set; }
    public Guid TeamId { get; set; }
    public AssignStrategy Strategy { get; set; } = AssignStrategy.None;
    public DateTimeOffset? ArchivedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int Version { get; set; } = 1;
}
