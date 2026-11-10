namespace AnnabaDigital.Domain.Entities;

public enum LocationType { Region = 0, Site = 1, Building = 2, Floor = 3, Room = 4 }

/// <summary>
/// A place in the organisation: region → site → building → floor → room. A child is always deeper than its parent, which also rules out cycles.
/// Only a region or a site can stand at the top. Archived places stay on old tickets but cannot be chosen any more.
/// </summary>
public class Location : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid? ParentId { get; set; }
    public LocationType Type { get; set; } = LocationType.Site;
    /// <summary>Short unique code (upper case) used on labels and in reports, e.g. <c>JKT-HQ-L3</c>.</summary>
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Address { get; set; }
    /// <summary>IANA time zone id, e.g. Asia/Jakarta. Inherited by children when empty.</summary>
    public string? TimeZone { get; set; }
    /// <summary>Business calendar that applies here. Inherited by children when empty, falling back to the organisation's default calendar.</summary>
    public Guid? CalendarId { get; set; }
    public Guid? ManagerUserId { get; set; }
    public int? Capacity { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int Version { get; set; } = 1;

    public static int Rank(LocationType t) => (int)t;
    /// <summary>The structural rule: only a region or a site stands at the top; below a parent the type must be deeper.</summary>
    public static bool CanSit(LocationType child, LocationType? parent) => parent is null ? child is LocationType.Region or LocationType.Site : Rank(child) > Rank(parent.Value);
}

/// <summary>When the organisation (or one site) works. Service-level clocks count only the time inside these windows.</summary>
public class BusinessCalendar : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Name { get; set; } = "";
    public string TimeZone { get; set; } = "Asia/Jakarta";
    /// <summary>24/7: working windows and holidays are ignored.</summary>
    public bool IsAlwaysOpen { get; set; }
    /// <summary>Exactly one calendar per organisation is the default.</summary>
    public bool IsDefault { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int Version { get; set; } = 1;
    public List<CalendarPeriod> Periods { get; set; } = [];
    public List<CalendarHoliday> Holidays { get; set; } = [];
}

/// <summary>One working window on a weekday, in minutes after local midnight (0–1440). A day may have several windows (e.g. a lunch break).</summary>
public class CalendarPeriod : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid CalendarId { get; set; }
    public DayOfWeek Day { get; set; }
    public int StartMinute { get; set; }
    public int EndMinute { get; set; }
}

/// <summary>A local date on which the calendar is closed.</summary>
public class CalendarHoliday : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid CalendarId { get; set; }
    public DateOnly Date { get; set; }
    public string Name { get; set; } = "";
}

/// <summary>A group of agents who work requests together (assignment and queues build on this).</summary>
public class Team : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int Version { get; set; } = 1;
    public List<TeamMember> Members { get; set; } = [];
}

public class TeamMember : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid TeamId { get; set; }
    public Guid UserId { get; set; }
    public bool IsLead { get; set; }
    public DateTimeOffset AddedAt { get; set; }
    public User? User { get; set; }
}
