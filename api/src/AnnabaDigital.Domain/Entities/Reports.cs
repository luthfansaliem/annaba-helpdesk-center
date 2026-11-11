namespace AnnabaDigital.Domain.Entities;

/// <summary>
/// RPT-007. A report e-mailed on a schedule as CSV or XLSX. The owner chooses a report they may see and people who may see it too; at each run the report is
/// built separately for every recipient with that person's own access (so nobody receives rows they could not open in the app) and anyone who lost access is skipped.
/// Times are in the organisation's default calendar time zone.
/// </summary>
public class ReportSchedule : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid OwnerId { get; set; }
    public string Name { get; set; } = "";
    /// <summary>A key of <see cref="ReportRules.Reports"/>.</summary>
    public string Report { get; set; } = "";
    /// <summary><c>csv</c> or <c>xlsx</c>.</summary>
    public string Format { get; set; } = "csv";
    /// <summary><c>daily</c>, <c>weekly</c> or <c>monthly</c>.</summary>
    public string Frequency { get; set; } = "weekly";
    /// <summary>Weekly: ISO day of the week (1 Monday … 7 Sunday).</summary>
    public int Weekday { get; set; } = 1;
    /// <summary>Monthly: day of the month (1–28).</summary>
    public int MonthDay { get; set; } = 1;
    /// <summary>Local hour (0–23) in the organisation's time zone.</summary>
    public int Hour { get; set; } = 7;
    /// <summary>The period each report covers, in days back from the run.</summary>
    public int Days { get; set; } = 7;
    public List<Guid> RecipientIds { get; set; } = [];
    public bool Active { get; set; } = true;
    public DateTimeOffset NextRunAt { get; set; }
    public DateTimeOffset? LastRunAt { get; set; }
    /// <summary>What happened at the last run, for the list ("Sent to 3 people; 1 skipped (no access)").</summary>
    public string? LastResult { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int Version { get; set; } = 1;
}

public static class ReportRules
{
    public const int MaxRecipients = 20, MaxRows = 5000, MaxSchedulesPerOwner = 30, MaxDays = 365;
    public static readonly string[] Reports = ["tickets", "sla", "csat", "costs", "compliance", "utilisation", "inventory"];
    public static readonly string[] Formats = ["csv", "xlsx"];
    public static readonly string[] Frequencies = ["daily", "weekly", "monthly"];
}
