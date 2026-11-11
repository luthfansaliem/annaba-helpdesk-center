namespace AnnabaDigital.Application.Helpdesk;

// Module analytics (UI review, docs/52): one small read-only summary per module, shown above its list. Each one is scoped exactly like the module's list
// (the same permission, and for work orders the same "work orders I may see"), and every number is counted by the database.
// Summaries with a time window take `?days=` (docs/54): 7 to 365, clamped, with a default per module; snapshots (assets, knowledge base) have none.

public static class InsightWindow
{
    public const int Min = 7, Max = 365;
    public static int Of(int? days, int fallback) => days is { } d ? Math.Clamp(d, Min, Max) : fallback;
}

public sealed record WeekPoint(string Week, int Created, int Done);
/// <summary>Finished work orders of one service category in the period and their average time from creation to completion.</summary>
public sealed record CategoryTime(string Key, string Label, int Done, double? AvgHours);
public sealed record WorkOrderSummary(int Days, int Open, int Overdue, int DueThisWeek, int Done, int OnTime, int Late, double? AvgCompletionHours, int InHouseOpen, int VendorOpen,
    IReadOnlyList<CountItem> ByStatus, IReadOnlyList<WeekPoint> Weekly, IReadOnlyList<CountItem> ByVendor, IReadOnlyList<CategoryTime> ByCategory);

public sealed record AssetTicketCount(Guid Id, string Tag, string Name, int Tickets);
public sealed record AssetSummary(int Active, int Retired, int WarrantyExpired, int Warranty30, int Warranty90, int Unplaced, decimal? Value, int PmDue30,
    IReadOnlyList<CountItem> ByStatus, IReadOnlyList<CountItem> ByType, IReadOnlyList<CountItem> BySite, IReadOnlyList<AssetTicketCount> MostTickets);

public sealed record VendorRank(Guid Id, string Name, int WorkOrders, int Done, int Open, int OnTime, int Late, decimal? Spend, double? Rating, int Ratings);
public sealed record VendorSummary(int Days, int Active, int Blocked, int ContractsActive, int ContractsExpiring60, int OpenWorkOrders, int OverdueWorkOrders, decimal? Spend,
    IReadOnlyList<VendorRank> Top);

public sealed record PlanDue(Guid Id, string Name, DateOnly NextDueOn);
public sealed record MaintenanceSummary(int Days, int Active, int Paused, int Due7, int Due30, int OverdueVisits, ComplianceRow Period, IReadOnlyList<ComplianceRow> Monthly, IReadOnlyList<PlanDue> Upcoming);

public sealed record KbTop(Guid Id, string Title, int Views, int Deflections, int? HelpfulPercent);
public sealed record KbSummary(int Published, int Drafts, int Archived, int ReviewDue, int Views, int Deflections, int Helpful, int NotHelpful, IReadOnlyList<KbTop> Top, IReadOnlyList<CountItem> ByCategory);
