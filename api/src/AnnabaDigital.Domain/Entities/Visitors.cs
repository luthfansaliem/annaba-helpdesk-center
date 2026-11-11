namespace AnnabaDigital.Domain.Entities;

public enum VisitStatus { Expected = 0, CheckedIn = 1, CheckedOut = 2, Cancelled = 3, NoShow = 4 }

/// <summary>
/// VIS-001/002/005. One visit: who comes (name, company, contact, vehicle plate), whom they visit (the host), where and when, and what happened at reception (badge,
/// the last digits of an ID when the organisation asks for them, the acknowledgement accepted, check-in and check-out). Personal details are blanked
/// (<see cref="AnonymisedAt"/>) after the organisation's retention period; the counts stay for the visitor report.
/// </summary>
public class Visit : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string VisitorName { get; set; } = "";
    public string? Company { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? VehiclePlate { get; set; }
    public Guid HostId { get; set; }
    public Guid? LocationId { get; set; }
    public string? Purpose { get; set; }
    public DateTimeOffset ExpectedFrom { get; set; }
    public DateTimeOffset ExpectedUntil { get; set; }
    public VisitStatus Status { get; set; } = VisitStatus.Expected;
    /// <summary>Set when the visitor matched the block list when registered (reception sees why; check-in is refused while the entry is active).</summary>
    public string? Flag { get; set; }
    public string? BadgeNumber { get; set; }
    /// <summary>The last four characters of the ID shown at reception, when the organisation asks for it (never the whole number).</summary>
    public string? IdLast4 { get; set; }
    public DateTimeOffset? AcknowledgedAt { get; set; }
    public DateTimeOffset? CheckedInAt { get; set; }
    public Guid? CheckedInById { get; set; }
    public DateTimeOffset? CheckedOutAt { get; set; }
    public Guid? CheckedOutById { get; set; }
    public Guid CreatedById { get; set; }
    public DateTimeOffset? AnonymisedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int Version { get; set; } = 1;
}

/// <summary>VIS-002. Someone reception must not let in. A visit whose name (and company, when the entry has one) matches an active entry is flagged and cannot be checked in.</summary>
public class VisitorBlock : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Name { get; set; } = "";
    public string? Company { get; set; }
    public string Reason { get; set; } = "";
    public Guid CreatedById { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RemovedAt { get; set; }
}

/// <summary>VIS-002/005. Per organisation: the acknowledgement visitors accept at check-in (house rules, NDA), whether reception records the last digits of an ID,
/// and how many days after the visit personal details are kept.</summary>
public class VisitorSettings : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public string? Acknowledgement { get; set; }
    public bool AskIdLast4 { get; set; }
    public int RetentionDays { get; set; } = VisitRules.DefaultRetentionDays;
    public DateTimeOffset UpdatedAt { get; set; }
}

public static class VisitRules
{
    public const int DefaultRetentionDays = 180, MinRetentionDays = 7, MaxRetentionDays = 3650;
    public const int MaxVisitHours = 24 * 7;
    public const string Anonymous = "Visitor";
    public static string Key(string s) => string.Join(' ', (s ?? "").Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
}
