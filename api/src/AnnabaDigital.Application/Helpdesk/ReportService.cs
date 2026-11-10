using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Domain.Entities;

namespace AnnabaDigital.Application.Helpdesk;

public sealed record OpsKpis(int Open, int Overdue, int AtRisk, int PendingApproval, int Unassigned, int Created, int Resolved, double? AvgFirstResponseMinutes, double? AvgResolutionMinutes, double? SlaAttainmentPercent);
public sealed record DayPoint(string Day, int Created, int Resolved);
public sealed record CountItem(string Key, string Label, int Count);
public sealed record AttentionItem(Guid Id, string Number, string Subject, string Status, string Priority, string? Assignee, string? Team, string SlaState, DateTimeOffset? DueAt);
public sealed record OperationsReport(int Days, DateTimeOffset From, DateTimeOffset To, OpsKpis Kpis, IReadOnlyList<DayPoint> Daily, IReadOnlyList<CountItem> ByStatus, IReadOnlyList<CountItem> ByPriority,
    IReadOnlyList<CountItem> BacklogByTeam, IReadOnlyList<CountItem> Aging, IReadOnlyList<AttentionItem> Attention);

public sealed record SlaRow(string Key, string Label, int Met, int Breached, double? AttainmentPercent, double? AvgResolutionMinutes);
public sealed record BreachItem(Guid Id, string Number, string Subject, string Status, string Priority, string ServiceName, string? Team, string? Assignee, DateTimeOffset BreachedAt, string Reason);
public sealed record SlaReport(int Days, DateTimeOffset From, DateTimeOffset To, int Met, int Breached, double? AttainmentPercent, int OpenBreached, int OpenAtRisk,
    IReadOnlyList<SlaRow> ByPriority, IReadOnlyList<SlaRow> ByTeam, IReadOnlyList<SlaRow> ByService, IReadOnlyList<BreachItem> Breaches);

/// <summary>
/// RPT-001/002. Read-only aggregates over the tickets the person may see (a team member sees their teams' numbers, a supervisor everything), for the last N days (1–90).
/// "Open" = waiting for approval, submitted, assigned, in progress, on hold or reopened (a resolved ticket awaiting confirmation is no longer backlog). Times are wall-clock.
/// SLA attainment counts tickets <i>resolved</i> in the period that had a policy: met ÷ (met + breached). Nothing here changes data.
/// Every number is computed by the database (counts, groups and averages), so the cost does not grow with the number of rows read into memory and nothing is cut off at a row limit.
/// </summary>
public sealed class ReportService(IAppDb db, IClock clock, TicketService tickets)
{
    private static readonly TicketStatus[] Open = [TicketStatus.PendingApproval, TicketStatus.Submitted, TicketStatus.Assigned, TicketStatus.InProgress, TicketStatus.OnHold, TicketStatus.Reopened];
    private static readonly TicketPriority[] Priorities = [TicketPriority.Urgent, TicketPriority.High, TicketPriority.Medium, TicketPriority.Low];
    private static string Lower<T>(T e) where T : struct, Enum => e.ToString().ToLowerInvariant();
    private static double? Round(double? x) => x is null ? null : Math.Round(x.Value, 1);
    private static double? Pct(int met, int breached) => met + breached == 0 ? null : Math.Round(100.0 * met / (met + breached), 1);

    // ================================================================ operations
    public async Task<OperationsReport> OperationsAsync(int days, SessionPrincipal who, CancellationToken ct = default)
    {
        days = Math.Clamp(days, 1, 90);
        var scope = await tickets.QueueScopeAsync(who, ct);
        var to = clock.UtcNow; var from = to.Date.AddDays(-(days - 1)); var fromAt = new DateTimeOffset(from, TimeSpan.Zero);
        var open = scope.Where(t => Open.Contains(t.Status));

        // one pass over the open tickets, grouped by everything the screen needs; the totals below are sums of these few groups
        var d1 = to.AddDays(-1); var d3 = to.AddDays(-3); var d7 = to.AddDays(-7); var d14 = to.AddDays(-14);
        var groups = await open.GroupBy(t => new { t.Status, t.Priority, t.TeamId, t.SlaState, Unassigned = t.AssigneeId == null }).Select(g => new
        {
            g.Key.Status, g.Key.Priority, g.Key.TeamId, g.Key.SlaState, g.Key.Unassigned, N = g.Count(),
            A1 = g.Count(t => t.CreatedAt > d1), A3 = g.Count(t => t.CreatedAt <= d1 && t.CreatedAt > d3), A7 = g.Count(t => t.CreatedAt <= d3 && t.CreatedAt > d7),
            A14 = g.Count(t => t.CreatedAt <= d7 && t.CreatedAt > d14), A15 = g.Count(t => t.CreatedAt <= d14),
        }).ToListAsync(ct);
        // the period: what was created and what was resolved in it, per day, with the averages
        var created = scope.Where(t => t.CreatedAt >= fromAt);
        var resolved = scope.Where(t => t.ResolvedAt != null && t.ResolvedAt >= fromAt);
        // day number counted from the first day of the period (its midnight is UTC, so these are UTC days)
        var createdPerDay = await created.GroupBy(t => Math.Floor((t.CreatedAt - fromAt).TotalDays)).Select(g => new { Day = g.Key, N = g.Count() }).ToDictionaryAsync(x => (int)x.Day, x => x.N, ct);
        var resolvedPerDay = await resolved.GroupBy(t => Math.Floor((t.ResolvedAt!.Value - fromAt).TotalDays)).Select(g => new { Day = g.Key, N = g.Count() }).ToDictionaryAsync(x => (int)x.Day, x => x.N, ct);
        var createdCount = createdPerDay.Values.Sum(); var resolvedCount = resolvedPerDay.Values.Sum();
        var firstResponse = await created.Where(t => t.FirstRespondedAt != null).Select(t => (double?)(t.FirstRespondedAt!.Value - t.CreatedAt).TotalMinutes).AverageAsync(ct);
        var resolution = await resolved.Select(t => (double?)(t.ResolvedAt!.Value - t.CreatedAt).TotalMinutes).AverageAsync(ct);
        var met = await resolved.CountAsync(t => t.SlaPolicyId != null && t.SlaState == SlaState.Met, ct); var breached = await resolved.CountAsync(t => t.SlaPolicyId != null && t.SlaState == SlaState.Breached, ct);
        var daily = Enumerable.Range(0, days).Select(i => new DayPoint(from.AddDays(i).ToString("yyyy-MM-dd"), createdPerDay.GetValueOrDefault(i), resolvedPerDay.GetValueOrDefault(i))).ToList();

        // the backlog by status, priority and team
        var teamNames = await db.Teams.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var byStatus = groups.GroupBy(x => x.Status).OrderBy(g => (int)g.Key).Select(g => new CountItem(Lower(g.Key), Lower(g.Key), g.Sum(x => x.N))).ToList();
        var byPriority = Priorities.Select(pr => new CountItem(Lower(pr), Lower(pr), groups.Where(x => x.Priority == pr).Sum(x => x.N))).ToList();
        var backlog = groups.GroupBy(x => x.TeamId).Select(g => new CountItem(g.Key?.ToString() ?? "none", g.Key is { } id && teamNames.TryGetValue(id, out var n) ? n : "", g.Sum(x => x.N))).OrderByDescending(x => x.Count).Take(12).ToList();
        var aging = new[] { new CountItem("lt1d", "lt1d", groups.Sum(x => x.A1)), new CountItem("d1_3", "d1_3", groups.Sum(x => x.A3)), new CountItem("d3_7", "d3_7", groups.Sum(x => x.A7)), new CountItem("d7_14", "d7_14", groups.Sum(x => x.A14)), new CountItem("gt14d", "gt14d", groups.Sum(x => x.A15)) };
        var counts = new { Open = groups.Sum(x => x.N), Overdue = groups.Where(x => x.SlaState == SlaState.Breached).Sum(x => x.N), AtRisk = groups.Where(x => x.SlaState == SlaState.AtRisk).Sum(x => x.N),
            Pending = groups.Where(x => x.Status == TicketStatus.PendingApproval).Sum(x => x.N), Unassigned = groups.Where(x => x.Unassigned && x.Status != TicketStatus.PendingApproval).Sum(x => x.N) };

        // what needs attention now: breached first, then at risk, then the soonest deadline
        var attention = await open.Where(t => t.SlaState == SlaState.Breached || t.SlaState == SlaState.AtRisk)
            .OrderBy(t => t.SlaState == SlaState.Breached ? 0 : 1).ThenBy(t => t.ResolutionDueAt == null).ThenBy(t => t.ResolutionDueAt).ThenBy(t => t.Id).Take(10)
            .Select(t => new { t.Id, t.Number, t.Subject, t.Status, t.Priority, t.TeamId, t.AssigneeId, t.SlaState, t.ResolutionDueAt }).ToListAsync(ct);
        var peopleIds = attention.Where(t => t.AssigneeId != null).Select(t => t.AssigneeId!.Value).Distinct().ToList();
        var people = await db.Users.AsNoTracking().Where(u => peopleIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Name, ct);

        return new OperationsReport(days, fromAt, to,
            new OpsKpis(counts.Open, counts.Overdue, counts.AtRisk, counts.Pending, counts.Unassigned, createdCount, resolvedCount, Round(firstResponse), Round(resolution), Pct(met, breached)),
            daily, byStatus, byPriority, backlog, aging,
            [.. attention.Select(t => new AttentionItem(t.Id, t.Number, t.Subject, Lower(t.Status), Lower(t.Priority), t.AssigneeId is { } a ? people.GetValueOrDefault(a) : null, t.TeamId is { } tm ? teamNames.GetValueOrDefault(tm) : null, Lower(t.SlaState), t.ResolutionDueAt))]);
    }

    // ================================================================ SLA
    private sealed record Tally(int Met, int Breached, double? Avg);

    public async Task<SlaReport> SlaAsync(int days, SessionPrincipal who, CancellationToken ct = default)
    {
        days = Math.Clamp(days, 1, 90);
        var scope = await tickets.QueueScopeAsync(who, ct);
        var to = clock.UtcNow; var fromAt = new DateTimeOffset(to.Date.AddDays(-(days - 1)), TimeSpan.Zero);
        var open = scope.Where(t => Open.Contains(t.Status));
        var openBreached = await open.CountAsync(t => t.SlaState == SlaState.Breached, ct); var openAtRisk = await open.CountAsync(t => t.SlaState == SlaState.AtRisk, ct);

        // tickets resolved in the period that had a policy, grouped by the database
        var done = scope.Where(t => t.SlaPolicyId != null && t.ResolvedAt != null && t.ResolvedAt >= fromAt);
        var teamNames = await db.Teams.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var all = await done.GroupBy(t => 1).Select(g => new { Met = g.Count(t => t.SlaState == SlaState.Met), Breached = g.Count(t => t.SlaState == SlaState.Breached) }).FirstOrDefaultAsync(ct);
        var perPriority = (await done.GroupBy(t => t.Priority).Select(g => new { g.Key, Met = g.Count(t => t.SlaState == SlaState.Met), Breached = g.Count(t => t.SlaState == SlaState.Breached), Avg = g.Average(t => (double?)(t.ResolvedAt!.Value - t.CreatedAt).TotalMinutes) }).ToListAsync(ct))
            .ToDictionary(x => x.Key, x => new Tally(x.Met, x.Breached, x.Avg));
        var perTeam = await done.GroupBy(t => t.TeamId).Select(g => new { g.Key, Met = g.Count(t => t.SlaState == SlaState.Met), Breached = g.Count(t => t.SlaState == SlaState.Breached), Avg = g.Average(t => (double?)(t.ResolvedAt!.Value - t.CreatedAt).TotalMinutes) }).ToListAsync(ct);
        var perService = await done.GroupBy(t => t.ServiceName).Select(g => new { g.Key, Met = g.Count(t => t.SlaState == SlaState.Met), Breached = g.Count(t => t.SlaState == SlaState.Breached), Avg = g.Average(t => (double?)(t.ResolvedAt!.Value - t.CreatedAt).TotalMinutes) }).ToListAsync(ct);

        static SlaRow Row(string key, string label, int m, int b, double? avg) => new(key, label, m, b, Pct(m, b), Round(avg));
        var byPriority = Priorities.Select(p => perPriority.TryGetValue(p, out var x) ? Row(Lower(p), Lower(p), x.Met, x.Breached, x.Avg) : Row(Lower(p), Lower(p), 0, 0, null)).ToList();
        var byTeam = perTeam.Select(x => Row(x.Key?.ToString() ?? "none", x.Key is { } id && teamNames.TryGetValue(id, out var n) ? n : "", x.Met, x.Breached, x.Avg)).OrderBy(r => r.AttainmentPercent ?? 101).ToList();
        var byService = perService.Select(x => Row(x.Key, x.Key, x.Met, x.Breached, x.Avg)).OrderBy(r => r.AttainmentPercent ?? 101).ThenByDescending(r => r.Met + r.Breached).Take(15).ToList();

        var breaches = await scope.Where(t => t.SlaBreachedAt != null && t.SlaBreachedAt >= fromAt).OrderByDescending(t => t.SlaBreachedAt).Take(50)
            .Select(t => new { t.Id, t.Number, t.Subject, t.Status, t.Priority, t.ServiceName, t.TeamId, t.AssigneeId, t.SlaBreachedAt, t.FirstRespondedAt, t.FirstResponseDueAt }).ToListAsync(ct);
        var peopleIds = breaches.Where(t => t.AssigneeId != null).Select(t => t.AssigneeId!.Value).Distinct().ToList();
        var people = await db.Users.AsNoTracking().Where(u => peopleIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Name, ct);
        // the reason is the target that was missed first: a first response that never came (or came late), otherwise the resolution
        string Reason(DateTimeOffset breachedAt, DateTimeOffset? firstDue, DateTimeOffset? responded) => firstDue is { } d && breachedAt == d && (responded is null || responded > d) ? "first_response" : "resolution";

        int met = all?.Met ?? 0, breached = all?.Breached ?? 0;
        return new SlaReport(days, fromAt, to, met, breached, Pct(met, breached), openBreached, openAtRisk, byPriority, byTeam, byService,
            [.. breaches.Select(t => new BreachItem(t.Id, t.Number, t.Subject, Lower(t.Status), Lower(t.Priority), t.ServiceName, t.TeamId is { } tm ? teamNames.GetValueOrDefault(tm) : null,
                t.AssigneeId is { } a ? people.GetValueOrDefault(a) : null, t.SlaBreachedAt!.Value, Reason(t.SlaBreachedAt!.Value, t.FirstResponseDueAt, t.FirstRespondedAt)))]);
    }
}
