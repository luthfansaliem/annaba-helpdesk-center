namespace AnnabaDigital.Application.Helpdesk;

// Module analytics (UI review, docs/52): one small read-only summary per module, shown above its list. Each one is scoped exactly like the module's list
// (the same permission, and for work orders the same "work orders I may see"), and every number is counted by the database.

public sealed record WeekPoint(string Week, int Created, int Done);
public sealed record WorkOrderSummary(int Open, int Overdue, int DueThisWeek, int Done30, int OnTime30, int Late30, double? AvgCompletionHours, int InHouseOpen, int VendorOpen,
    IReadOnlyList<CountItem> ByStatus, IReadOnlyList<WeekPoint> Weekly, IReadOnlyList<CountItem> ByVendor);

public sealed record AssetTicketCount(Guid Id, string Tag, string Name, int Tickets);
public sealed record AssetSummary(int Active, int Retired, int WarrantyExpired, int Warranty30, int Warranty90, int Unplaced, decimal? Value, int PmDue30,
    IReadOnlyList<CountItem> ByStatus, IReadOnlyList<CountItem> ByType, IReadOnlyList<CountItem> BySite, IReadOnlyList<AssetTicketCount> MostTickets);

public sealed record VendorRank(Guid Id, string Name, int WorkOrders, int Done, int Open, int OnTime, int Late, decimal? Spend, double? Rating, int Ratings);
public sealed record VendorSummary(int Active, int Blocked, int ContractsActive, int ContractsExpiring60, int OpenWorkOrders, int OverdueWorkOrders, decimal? Spend12Months,
    IReadOnlyList<VendorRank> Top);

public sealed record PlanDue(Guid Id, string Name, DateOnly NextDueOn);
public sealed record MaintenanceSummary(int Active, int Paused, int Due7, int Due30, int OverdueVisits, ComplianceRow Last90, IReadOnlyList<ComplianceRow> Monthly, IReadOnlyList<PlanDue> Upcoming);

public sealed record KbTop(Guid Id, string Title, int Views, int Deflections, int? HelpfulPercent);
public sealed record KbSummary(int Published, int Drafts, int Archived, int ReviewDue, int Views, int Deflections, int Helpful, int NotHelpful, IReadOnlyList<KbTop> Top, IReadOnlyList<CountItem> ByCategory);
