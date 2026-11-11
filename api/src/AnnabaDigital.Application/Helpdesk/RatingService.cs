using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Application.Users;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Application.Helpdesk;

public sealed record RateRequest(int Score, string? Comment);
public sealed record TicketRatingInfo(int? Score, string? Comment, DateTimeOffset? At, bool CanRate, DateTimeOffset? RateUntil);
public sealed record CsatRow(string Key, string Label, int Count, double? Average, int? SatisfiedPercent, int Low);
public sealed record CsatComment(Guid TicketId, string Number, string Subject, int Score, string Comment, string? Agent, DateTimeOffset At);
/// <summary>One step of the satisfaction trend: a week (<c>yyyy-MM-dd</c>, its first day) for periods up to 90 days, otherwise a month (<c>yyyy-MM</c>).</summary>
public sealed record CsatPoint(string Period, int Count, int Satisfied, int Low, double? Average, int? SatisfiedPercent);
public sealed record CsatReport(int Days, DateTimeOffset From, DateTimeOffset To, string By, bool OwnOnly, int Count, double? Average, int? SatisfiedPercent, int Low, IReadOnlyList<int> Distribution,
    IReadOnlyList<CsatRow> Rows, IReadOnlyList<CsatComment> Comments, string TrendUnit, IReadOnlyList<CsatPoint> Trend);

/// <summary>
/// TKT-009 / RPT-006. Requesters rate resolved requests (1–5 and a comment) for 14 days after resolution; a score of 2 or less alerts the team's leads (or, without leads, the people
/// who see every ticket) once. The report shows everything to people with <c>tickets.read_all</c> and only their own ratings (as the person working the ticket) to other staff.
/// </summary>
public sealed class RatingService(IAppDb db, IClock clock, AuditWriter audit, AuthorizationService authz, NotificationService notifications, IAppLinks links)
{
    public const int MaxAlertRecipients = 20;
    private static string? Clean(string? s) { var t = (s ?? "").Trim(); return t.Length == 0 ? null : t; }

    /// <summary>The rating block of a ticket as one viewer sees it.</summary>
    public async Task<TicketRatingInfo> ForTicketAsync(Ticket t, Guid viewerId, CancellationToken ct = default)
    {
        var r = await db.TicketRatings.AsNoTracking().FirstOrDefaultAsync(x => x.TicketId == t.Id, ct);
        var planned = await db.MaintenanceVisits.AnyAsync(v => v.TicketId == t.Id, ct);
        var can = RatingRules.CanRate(t.Status, t.ResolvedAt, clock.UtcNow, t.RequesterId == viewerId, planned);
        return new TicketRatingInfo(r?.Score, r?.Comment, r?.UpdatedAt, can, can ? t.ResolvedAt + RatingRules.Window : null);
    }

    public async Task<TicketRatingInfo> RateAsync(Guid ticketId, RateRequest req, SessionPrincipal who, ClientInfo client, CancellationToken ct = default)
    {
        var t = await db.Tickets.FirstOrDefaultAsync(x => x.Id == ticketId && (x.RequesterId == who.UserId || x.CreatedById == who.UserId), ct) ?? throw DomainException.NotFound("Ticket");
        if (t.RequesterId != who.UserId) throw DomainException.DeniedBecause("Only the person the request is for can rate it.");
        var now = clock.UtcNow;
        var planned = await db.MaintenanceVisits.AnyAsync(v => v.TicketId == t.Id, ct);
        if (!RatingRules.CanRate(t.Status, t.ResolvedAt, now, true, planned))
            throw DomainException.Conflict(planned ? "Planned maintenance is not rated." : t.Status is TicketStatus.Resolved or TicketStatus.Closed ? "Rating is open for 14 days after the request was resolved." : "You can rate the request once it is resolved.");
        var e = new Dictionary<string, string>();
        if (req.Score is < RatingRules.MinScore or > RatingRules.MaxScore) e["score"] = "Choose 1 to 5.";
        var comment = Clean(req.Comment);
        if (comment is { Length: > RatingRules.CommentMax }) e["comment"] = $"Use at most {RatingRules.CommentMax} characters.";
        if (e.Count > 0) throw DomainException.Validation("Please correct the highlighted fields.", e);

        var r = await db.TicketRatings.FirstOrDefaultAsync(x => x.TicketId == t.Id, ct);
        var before = r?.Score;
        if (r is null) { r = new TicketRating { TicketId = t.Id, RaterId = who.UserId, CreatedAt = now }; db.TicketRatings.Add(r); }
        r.Score = req.Score; r.Comment = comment; r.UpdatedAt = now; r.AssigneeId = t.AssigneeId; r.TeamId = t.TeamId; r.LocationId = t.LocationId;
        db.TicketEvents.Add(new TicketEvent { TicketId = t.Id, Kind = TicketEventKind.Updated, ActorId = who.UserId, ActorLabel = who.Name, Internal = false, CreatedAt = now,
            Body = before is null ? $"Rated {req.Score} out of 5." : $"Changed the rating from {before} to {req.Score} out of 5." });
        audit.Record("ticket.rate", "ticket", t.Id.ToString(), Outcome.Success, client, who.UserId, who.Email, new Dictionary<string, string> { ["number"] = t.Number, ["score"] = req.Score.ToString() });
        if (RatingRules.IsLow(req.Score) && r.AlertedAt is null)
        {
            r.AlertedAt = now;
            await AlertAsync(t, req.Score, comment, who.UserId, ct);
        }
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { throw DomainException.Conflict("The rating was saved at the same moment from somewhere else. Reload and try again."); }
        return await ForTicketAsync(t, who.UserId, ct);
    }

    private async Task AlertAsync(Ticket t, int score, string? comment, Guid raterId, CancellationToken ct)
    {
        List<Guid> people = t.TeamId is { } team
            ? await db.TeamMembers.AsNoTracking().Where(m => m.TeamId == team && m.IsLead && m.User!.Status == UserStatus.Active).Select(m => m.UserId).ToListAsync(ct)
            : [];
        if (people.Count == 0)
            people = await db.Users.AsNoTracking().Where(u => u.Status == UserStatus.Active
                    && u.UserRoles.Any(ur => ur.Role!.ArchivedAt == null && ur.Role.Permissions.Any(p => p.PermissionKey == Permissions.TicketsReadAll)))
                .OrderBy(u => u.Id).Select(u => u.Id).Take(MaxAlertRecipients).ToListAsync(ct);
        var title = $"{t.Number} was rated {score} out of 5";
        var body = comment ?? t.Subject;
        var path = $"/tickets/{t.Id}";
        foreach (var uid in people.Where(p => p != raterId).Distinct().Take(MaxAlertRecipients))
            await notifications.SendAsync(uid, "ticket.low_rating", title, body, path, t.Id, (to, name) => Email.EmailTemplates.Notification(to, name, title, body, links.Origin.TrimEnd('/') + path), ct: ct);
    }

    // ================================================================ report
    public async Task<CsatReport> ReportAsync(int days, string? by, SessionPrincipal who, CancellationToken ct = default)
    {
        var perms = await authz.GetEffectivePermissionsAsync(who.UserId, ct);
        var all = perms.Contains(Permissions.TicketsReadAll);
        if (!all && !perms.Contains(Permissions.TicketsReadTeam)) throw DomainException.Denied(Permissions.TicketsReadTeam);
        days = Math.Clamp(days, 1, 365);
        var group = (by ?? "team").Trim().ToLowerInvariant();
        if (group is not ("agent" or "team" or "site" or "vendor")) throw DomainException.Validation("Unknown grouping.", new Dictionary<string, string> { ["by"] = "Use agent, team, site or vendor." });
        var to = clock.UtcNow; var from = to.AddDays(-days);
        var q = db.TicketRatings.AsNoTracking().Where(r => r.UpdatedAt >= from && r.UpdatedAt <= to);
        if (!all) { var me = who.UserId; q = q.Where(r => r.AssigneeId == me); }
        var rows = await q.Select(r => new { r.TicketId, r.Score, r.Comment, r.AssigneeId, r.TeamId, r.LocationId, r.UpdatedAt }).Take(100_000).ToListAsync(ct);

        var userIds = rows.Where(r => r.AssigneeId != null).Select(r => r.AssigneeId!.Value).Distinct().ToList();
        var users = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Name, ct);
        var teams = await db.Teams.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.Name, ct);
        var places = await db.Locations.AsNoTracking().Select(l => new { l.Id, l.ParentId, l.Name }).ToDictionaryAsync(l => l.Id, ct);
        Guid? Top(Guid? id) { Guid? top = null; for (var (c, n) = (id, 0); c is { } k && places.TryGetValue(k, out var p) && n < 10; c = p.ParentId, n++) top = k; return top; }
        var ticketIds = rows.Select(r => r.TicketId).ToList();
        var vendorsOf = group == "vendor"
            ? (await db.WorkOrders.AsNoTracking().Where(w => w.VendorId != null && ticketIds.Contains(w.TicketId)).Select(w => new { w.TicketId, VendorId = w.VendorId!.Value }).Distinct().ToListAsync(ct))
                .GroupBy(x => x.TicketId).ToDictionary(g => g.Key, g => g.Select(x => x.VendorId).ToList())
            : [];
        var vendorNames = group == "vendor" ? await db.Vendors.AsNoTracking().ToDictionaryAsync(v => v.Id, v => v.Name, ct) : [];

        IEnumerable<(string Key, string Label)> Keys(Guid ticketId, Guid? assignee, Guid? team, Guid? location) => group switch
        {
            "agent" => [assignee is { } a ? (a.ToString(), users.GetValueOrDefault(a) ?? "—") : ("none", "Nobody")],
            "site" => [Top(location) is { } s ? (s.ToString(), places[s].Name) : ("none", "No place")],
            "vendor" => vendorsOf.TryGetValue(ticketId, out var vs) ? vs.Select(v => (v.ToString(), vendorNames.GetValueOrDefault(v) ?? "—")) : [("none", "In-house")],
            _ => [team is { } tm ? (tm.ToString(), teams.GetValueOrDefault(tm) ?? "—") : ("none", "No team")],
        };
        static CsatRow Row(string key, string label, IReadOnlyCollection<int> s)
            => new(key, label, s.Count, s.Count == 0 ? null : Math.Round(s.Average(), 2), s.Count == 0 ? null : (int)Math.Round(s.Count(RatingRules.IsSatisfied) * 100.0 / s.Count), s.Count(RatingRules.IsLow));
        var grouped = rows.SelectMany(r => Keys(r.TicketId, r.AssigneeId, r.TeamId, r.LocationId).Select(k => (k, r.Score)))
            .GroupBy(x => x.k).Select(g => Row(g.Key.Key, g.Key.Label, [.. g.Select(x => x.Score)]))
            .OrderBy(r => r.Average ?? 6).ThenBy(r => r.Label, StringComparer.OrdinalIgnoreCase).ToList();
        var scores = rows.Select(r => r.Score).ToList();
        var total = Row("total", "Total", scores);

        var withText = rows.Where(r => r.Comment != null).OrderByDescending(r => r.UpdatedAt).Take(20).ToList();
        var tids = withText.Select(r => r.TicketId).ToList();
        var tk = await db.Tickets.AsNoTracking().Where(t => tids.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => new { t.Number, t.Subject }, ct);
        var comments = withText.Where(r => tk.ContainsKey(r.TicketId)).Select(r => new CsatComment(r.TicketId, tk[r.TicketId].Number, tk[r.TicketId].Subject, r.Score, r.Comment!,
            r.AssigneeId is { } a ? users.GetValueOrDefault(a) : null, r.UpdatedAt)).ToList();
        var (unit, trend) = Trend(from, to, rows.Select(r => (r.UpdatedAt, r.Score)).ToList());
        return new CsatReport(days, from, to, group, !all, total.Count, total.Average, total.SatisfiedPercent, total.Low,
            [.. Enumerable.Range(1, 5).Select(s => scores.Count(x => x == s))], grouped, comments, unit, trend);
    }

    /// <summary>Weekly points (from the period's first day) for up to 90 days, monthly points beyond; empty steps are kept so the line shows the gap.</summary>
    public static (string Unit, IReadOnlyList<CsatPoint> Points) Trend(DateTimeOffset from, DateTimeOffset to, IReadOnlyList<(DateTimeOffset At, int Score)> rows)
    {
        static CsatPoint Point(string label, IReadOnlyCollection<int> s) => new(label, s.Count, s.Count(RatingRules.IsSatisfied), s.Count(RatingRules.IsLow), s.Count == 0 ? null : Math.Round(s.Average(), 2),
            s.Count == 0 ? null : (int)Math.Round(s.Count(RatingRules.IsSatisfied) * 100.0 / s.Count));
        var start = from.UtcDateTime.Date;
        if ((to - from).TotalDays <= 90)
        {
            var weeks = (int)Math.Ceiling((to.UtcDateTime.Date - start).TotalDays / 7) + 1;
            var byWeek = rows.GroupBy(r => (int)((r.At.UtcDateTime.Date - start).TotalDays / 7)).ToDictionary(g => g.Key, g => g.Select(x => x.Score).ToList());
            return ("week", [.. Enumerable.Range(0, weeks).Select(i => Point(start.AddDays(7 * i).ToString("yyyy-MM-dd"), byWeek.GetValueOrDefault(i) ?? []))]);
        }
        var first = new DateTime(start.Year, start.Month, 1);
        var last = new DateTime(to.UtcDateTime.Year, to.UtcDateTime.Month, 1);
        var byMonth = rows.GroupBy(r => r.At.UtcDateTime.ToString("yyyy-MM")).ToDictionary(g => g.Key, g => g.Select(x => x.Score).ToList());
        var points = new List<CsatPoint>();
        for (var m = first; m <= last; m = m.AddMonths(1)) points.Add(Point(m.ToString("yyyy-MM"), byMonth.GetValueOrDefault(m.ToString("yyyy-MM")) ?? []));
        return ("month", points);
    }
}
