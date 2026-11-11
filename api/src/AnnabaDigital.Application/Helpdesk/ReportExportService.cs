using System.Globalization;
using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Application.Helpdesk;

public sealed record ReportKind(string Key, string Label, string Permission, string Description, int DefaultDays);
public sealed record ReportFile(byte[] Content, string ContentType, string FileName, int Rows, bool Truncated);
public sealed record ReportScheduleDto(Guid Id, string Name, string Report, string ReportLabel, string Format, string Frequency, int Weekday, int MonthDay, int Hour, int Days,
    IReadOnlyList<ReportRecipient> Recipients, bool Active, DateTimeOffset NextRunAt, DateTimeOffset? LastRunAt, string? LastResult, string? Owner, bool Mine, int Version);
public sealed record ReportRecipient(Guid Id, string Name, string Email, bool CanSee);
public sealed record SaveReportScheduleRequest(string? Name, string? Report, string? Format, string? Frequency, int? Weekday, int? MonthDay, int? Hour, int? Days, string[]? Recipients, bool? Active, int? Version);
public sealed record ReportRunResult(int Schedules, int Sent, int Skipped);

/// <summary>
/// RPT-007. Every report as a table, downloaded now or e-mailed on a schedule as CSV or XLSX. A report is always built with the permissions of the person who
/// receives it: the download with the caller's, each scheduled e-mail with its recipient's (people who lost access are skipped and the owner sees it in the
/// last result). Exports are limited to <see cref="ReportRules.MaxRows"/> rows and audited.
/// </summary>
public sealed class ReportExportService(IAppDb db, IClock clock, AuditWriter audit, AuthorizationService authz, ITenantContext tenant, IEmailQueue mail, IAppLinks links,
    TicketService tickets, ReportService reports, RatingService ratings, CostService costs, MaintenanceService maintenance, VehicleService vehicles, InventoryService inventory)
{
    public static readonly IReadOnlyList<ReportKind> Kinds =
    [
        new("tickets", "Tickets", Permissions.TicketsReadTeam, "Every ticket raised in the period that you can see: status, priority, team, assignee, SLA.", 30),
        new("sla", "SLA attainment", Permissions.TicketsReadTeam, "Met and breached targets by priority, team and service, with the average time to resolve.", 30),
        new("csat", "Satisfaction", Permissions.TicketsReadTeam, "Ratings per week or month: count, satisfied, low and the average.", 90),
        new("costs", "Costs", Permissions.CostRead, "Work-order and vehicle costs by category: parts, labour, services, invoices, time.", 30),
        new("compliance", "Maintenance compliance", Permissions.PmRead, "Planned visits per plan: on time, late, open, overdue, missed.", 90),
        new("utilisation", "Room and vehicle utilisation", Permissions.BookingsReadAll, "Booked against open hours per resource, no-shows, trips and kilometres.", 30),
        new("inventory", "Stock on hand", Permissions.InventoryRead, "Every item at every place: quantity, reorder level, low flag and value.", 1),
    ];
    private static readonly ClientInfo Background = new(null, "background", null);
    private static DomainException Invalid(Dictionary<string, string> e) => DomainException.Validation("Please correct the highlighted fields.", e);
    private static string? Clean(string? s) { var t = (s ?? "").Trim(); return t.Length == 0 ? null : t; }
    private static string Words(Enum e)
    {
        var s = e.ToString(); var sb = new System.Text.StringBuilder();
        for (var i = 0; i < s.Length; i++) { if (i > 0 && char.IsUpper(s[i])) sb.Append(' ').Append(char.ToLowerInvariant(s[i])); else sb.Append(s[i]); }
        return sb.ToString();
    }

    // ================================================================ building
    public async Task<IReadOnlyList<ReportKind>> AvailableAsync(Guid userId, CancellationToken ct = default)
    {
        var perms = await authz.GetEffectivePermissionsAsync(userId, ct);
        return [.. Kinds.Where(k => perms.Contains(k.Permission) || (k.Permission == Permissions.TicketsReadTeam && perms.Contains(Permissions.TicketsReadAll)))];
    }
    private async Task<bool> MaySeeAsync(Guid userId, string report, CancellationToken ct) => (await AvailableAsync(userId, ct)).Any(k => k.Key == report);

    private async Task<TimeZoneInfo> ZoneAsync(CancellationToken ct)
    {
        var id = await db.Calendars.AsNoTracking().Where(c => c.IsDefault && c.ArchivedAt == null).Select(c => c.TimeZone).FirstOrDefaultAsync(ct) ?? CalendarService.DefaultTimeZone;
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException) { return TimeZoneInfo.Utc; }
    }

    /// <summary>The report as the given person would see it in the app, over the last <paramref name="days"/> days.</summary>
    public async Task<ReportTable> BuildAsync(string report, int days, SessionPrincipal who, CancellationToken ct = default)
    {
        var kind = Kinds.FirstOrDefault(k => k.Key == report) ?? throw Invalid(new Dictionary<string, string> { ["report"] = "Choose a report." });
        if (!await MaySeeAsync(who.UserId, report, ct)) throw DomainException.Denied(kind.Permission);
        days = Math.Clamp(days, 1, ReportRules.MaxDays);
        var zone = await ZoneAsync(ct);
        string Local(DateTimeOffset? at) => at is { } a ? TimeZoneInfo.ConvertTime(a, zone).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : "";
        var now = clock.UtcNow;
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        var from = today.AddDays(-(days - 1));
        var title = $"{kind.Label} {from:yyyy-MM-dd} to {today:yyyy-MM-dd}";
        switch (report)
        {
            case "tickets":
            {
                var since = now.AddDays(-days);
                var q = (await tickets.QueueScopeAsync(who, ct)).Where(t => t.CreatedAt >= since);
                var total = await q.CountAsync(ct);
                var rows = await q.OrderByDescending(t => t.CreatedAt).Take(ReportRules.MaxRows).Select(t => new { t.Number, t.Subject, t.ServiceName, t.Status, t.Priority, t.TeamId, t.AssigneeId, t.LocationId, t.CreatedAt, t.ResolvedAt, t.SlaState }).ToListAsync(ct);
                var teams = await db.Teams.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Name, ct);
                var people = rows.Where(r => r.AssigneeId != null).Select(r => r.AssigneeId!.Value).Distinct().ToList();
                var names = await db.Users.AsNoTracking().Where(u => people.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Name, ct);
                var places = await db.Locations.AsNoTracking().ToDictionaryAsync(l => l.Id, l => l.Name, ct);
                return new ReportTable(title, ["Number", "Subject", "Service", "Status", "Priority", "Team", "Assignee", "Place", "Created", "Resolved", "SLA"],
                    [.. rows.Select(r => new object?[] { r.Number, r.Subject, r.ServiceName, Words(r.Status), Words(r.Priority), r.TeamId is { } tm ? teams.GetValueOrDefault(tm) : null,
                        r.AssigneeId is { } a ? names.GetValueOrDefault(a) : null, r.LocationId is { } l ? places.GetValueOrDefault(l) : null, Local(r.CreatedAt), Local(r.ResolvedAt), Words(r.SlaState) })],
                    total > ReportRules.MaxRows);
            }
            case "sla":
            {
                var s = await reports.SlaAsync(Math.Min(days, 90), who, ct);
                IEnumerable<object?[]> Part(string group, IReadOnlyList<SlaRow> rows) => rows.Select(r => new object?[] { group, r.Label, r.Met, r.Breached, r.AttainmentPercent is { } p ? Math.Round(p, 1) : null,
                    r.AvgResolutionMinutes is { } m ? Math.Round(m / 60, 1) : null });
                return new ReportTable(title, ["Group", "Name", "Met", "Breached", "Attainment %", "Average hours to resolve"],
                    [new object?[] { "All", "All tickets", s.Met, s.Breached, s.AttainmentPercent is { } ap ? Math.Round(ap, 1) : null, null }, .. Part("Priority", s.ByPriority), .. Part("Team", s.ByTeam), .. Part("Service", s.ByService)]);
            }
            case "csat":
            {
                var c = await ratings.ReportAsync(days, "team", who, ct);
                return new ReportTable(title, ["Period", "Ratings", "Satisfied", "Low", "Average", "Satisfied %"],
                    [.. c.Trend.Select(p => new object?[] { p.Period, p.Count, p.Satisfied, p.Low, p.Average is { } a ? Math.Round(a, 2) : null, p.SatisfiedPercent })]);
            }
            case "costs":
            {
                var c = await costs.ReportAsync(from, today, "category", ct);
                object?[] Row(CostRow r) => [r.Label, r.Part, r.Labour, r.Service, r.Invoice, r.Other, r.Time, r.Vehicle, r.Total, r.WorkOrders];
                return new ReportTable($"{title} ({c.Currency})", ["Category", "Parts", "Labour", "Services", "Invoices", "Other", "Logged time", "Vehicle trips", "Total", "Work orders"],
                    [.. c.Rows.Select(Row), Row(c.Total)]);
            }
            case "compliance":
            {
                var c = await maintenance.ComplianceAsync(from, today, "plan", ct);
                object?[] Row(ComplianceRow r) => [r.Label, r.Planned, r.OnTime, r.Late, r.Open, r.Overdue, r.Missed, r.Cancelled, r.Percent];
                return new ReportTable(title, ["Plan", "Planned", "On time", "Late", "Open", "Overdue", "Missed", "Cancelled", "Compliance %"], [.. c.Rows.Select(Row), Row(c.Total)]);
            }
            case "utilisation":
            {
                var u = await vehicles.UtilisationAsync(days, null, ct);
                object?[] Row(UtilisationRow r) => [r.Name, r.Kind, r.Place, Math.Round(r.OpenHours, 1), Math.Round(r.BookedHours, 1), r.Percent, r.Bookings, r.NoShows, r.Cancelled, r.Trips, r.Km, r.TripCost];
                return new ReportTable(title, ["Resource", "Kind", "Place", "Open hours", "Booked hours", "Utilisation %", "Bookings", "No-shows", "Cancelled", "Trips", "Km", "Trip cost"],
                    [.. u.Rows.Select(Row), Row(u.Total)]);
            }
            default:
            {
                var items = await inventory.ListAsync(null, null, null, false, false, ct);
                var rows = items.SelectMany(i => i.Stock.Count == 0 ? [new object?[] { i.Sku, i.Name, i.Category, null, 0, i.Unit, i.ReorderLevel, "no", i.UnitCost, 0m }]
                    : i.Stock.Select(s => new object?[] { i.Sku, i.Name, i.Category, s.Place, s.Quantity, i.Unit, i.ReorderLevel, s.Low ? "yes" : "no", i.UnitCost, i.UnitCost is { } c ? c * s.Quantity : null })).ToList();
                return new ReportTable($"Stock on hand {today:yyyy-MM-dd}", ["SKU", "Item", "Category", "Place", "Quantity", "Unit", "Reorder at", "Low", "Unit cost", "Value"],
                    [.. rows.Take(ReportRules.MaxRows)], rows.Count > ReportRules.MaxRows);
            }
        }
    }

    /// <summary>Download now (the export button): the caller's own view of the report.</summary>
    public async Task<ReportFile> ExportAsync(string? report, int? days, string? format, SessionPrincipal who, ClientInfo client, CancellationToken ct = default)
    {
        var key = Clean(report)?.ToLowerInvariant() ?? "";
        var fmt = Clean(format)?.ToLowerInvariant() ?? "csv";
        if (!ReportRules.Formats.Contains(fmt)) throw Invalid(new Dictionary<string, string> { ["format"] = "Use csv or xlsx." });
        var kind = Kinds.FirstOrDefault(k => k.Key == key) ?? throw Invalid(new Dictionary<string, string> { ["report"] = "Choose a report." });
        var table = await BuildAsync(key, days ?? kind.DefaultDays, who, ct);
        var (content, type, ext) = Tabular.Write(table, fmt);
        audit.Record("report.export", "report", key, Outcome.Success, client, who.UserId, who.Email,
            new Dictionary<string, string> { ["format"] = fmt, ["rows"] = table.Rows.Count.ToString(CultureInfo.InvariantCulture), ["days"] = (days ?? kind.DefaultDays).ToString(CultureInfo.InvariantCulture) });
        await db.SaveChangesAsync(ct);
        return new ReportFile(content, type, $"{key}-{clock.UtcNow:yyyyMMdd}.{ext}", table.Rows.Count, table.Truncated);
    }

    // ================================================================ schedules
    /// <summary>The next run strictly after <paramref name="after"/>, at the schedule's local hour in <paramref name="zone"/>.</summary>
    public static DateTimeOffset NextRun(ReportSchedule s, DateTimeOffset after, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(after, zone).DateTime;
        for (var d = local.Date; d <= local.Date.AddDays(62); d = d.AddDays(1))
        {
            var fits = s.Frequency switch
            {
                "daily" => true,
                "weekly" => ((int)d.DayOfWeek == 0 ? 7 : (int)d.DayOfWeek) == s.Weekday,
                _ => d.Day == s.MonthDay,
            };
            if (!fits) continue;
            var at = d.AddHours(s.Hour);
            if (zone.IsInvalidTime(at)) at = at.AddHours(1);
            var off = new DateTimeOffset(at, zone.GetUtcOffset(at)).ToUniversalTime();
            if (off > after) return off;
        }
        return after.AddDays(1);
    }

    private async Task<ReportScheduleDto> DtoAsync(ReportSchedule s, Guid viewer, CancellationToken ct)
    {
        var ids = s.RecipientIds.Append(s.OwnerId).Distinct().ToList();
        var users = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => new { u.Name, u.Email, u.Status }, ct);
        var recips = new List<ReportRecipient>();
        foreach (var id in s.RecipientIds)
            if (users.TryGetValue(id, out var u)) recips.Add(new ReportRecipient(id, u.Name, u.Email, u.Status == UserStatus.Active && await MaySeeAsync(id, s.Report, ct)));
        return new ReportScheduleDto(s.Id, s.Name, s.Report, Kinds.FirstOrDefault(k => k.Key == s.Report)?.Label ?? s.Report, s.Format, s.Frequency, s.Weekday, s.MonthDay, s.Hour, s.Days,
            recips, s.Active, s.NextRunAt, s.LastRunAt, s.LastResult, users.GetValueOrDefault(s.OwnerId)?.Name, s.OwnerId == viewer, s.Version);
    }

    /// <summary>The caller's schedules; people with <c>audit.read</c> see every schedule (who sends what to whom).</summary>
    public async Task<IReadOnlyList<ReportScheduleDto>> ListAsync(SessionPrincipal p, CancellationToken ct = default)
    {
        var all = (await authz.GetEffectivePermissionsAsync(p.UserId, ct)).Contains(Permissions.AuditRead);
        var rows = await db.ReportSchedules.AsNoTracking().Where(s => all || s.OwnerId == p.UserId).OrderBy(s => s.Name).Take(200).ToListAsync(ct);
        var list = new List<ReportScheduleDto>();
        foreach (var s in rows) list.Add(await DtoAsync(s, p.UserId, ct));
        return list;
    }

    public async Task<ReportScheduleDto> SaveAsync(Guid? id, SaveReportScheduleRequest req, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var e = new Dictionary<string, string>();
        var name = Clean(req.Name); if (name is null) e["name"] = "Give the schedule a name."; else if (name.Length > 120) e["name"] = "Use at most 120 characters.";
        var report = Clean(req.Report)?.ToLowerInvariant() ?? "";
        if (!ReportRules.Reports.Contains(report)) e["report"] = "Choose a report.";
        else if (!await MaySeeAsync(actor.UserId, report, ct)) e["report"] = "You cannot see this report yourself.";
        var format = Clean(req.Format)?.ToLowerInvariant() ?? "csv"; if (!ReportRules.Formats.Contains(format)) e["format"] = "Use csv or xlsx.";
        var freq = Clean(req.Frequency)?.ToLowerInvariant() ?? "weekly"; if (!ReportRules.Frequencies.Contains(freq)) e["frequency"] = "Use daily, weekly or monthly.";
        var weekday = req.Weekday ?? 1; if (weekday is < 1 or > 7) e["weekday"] = "Choose a day of the week.";
        var monthDay = req.MonthDay ?? 1; if (monthDay is < 1 or > 28) e["monthDay"] = "Use a day from 1 to 28.";
        var hour = req.Hour ?? 7; if (hour is < 0 or > 23) e["hour"] = "Use an hour from 0 to 23.";
        var days = req.Days ?? (freq == "daily" ? 1 : freq == "weekly" ? 7 : 30); if (days is < 1 or > ReportRules.MaxDays) e["days"] = $"Use 1 to {ReportRules.MaxDays} days.";
        var emails = (req.Recipients ?? []).Select(x => x.Trim().ToLowerInvariant()).Where(x => x.Length > 0).Distinct().ToList();
        var recipients = new List<Guid>();
        if (emails.Count == 0) e["recipients"] = "Add at least one person.";
        else if (emails.Count > ReportRules.MaxRecipients) e["recipients"] = $"Send to at most {ReportRules.MaxRecipients} people.";
        else
        {
            var found = await db.Users.AsNoTracking().Where(u => emails.Contains(u.Email) && u.Status == UserStatus.Active).Select(u => new { u.Id, u.Email }).ToListAsync(ct);
            var unknown = emails.Except(found.Select(f => f.Email)).ToList();
            if (unknown.Count > 0) e["recipients"] = $"Not an active person here: {string.Join(", ", unknown.Take(3))}.";
            else if (e.ContainsKey("report") is false)
            {
                var blocked = new List<string>();
                foreach (var f in found) { if (await MaySeeAsync(f.Id, report, ct)) recipients.Add(f.Id); else blocked.Add(f.Email); }
                if (blocked.Count > 0) e["recipients"] = $"Cannot see this report: {string.Join(", ", blocked.Take(3))}.";
            }
        }
        if (e.Count > 0) throw Invalid(e);
        var now = clock.UtcNow;
        ReportSchedule s;
        if (id is null)
        {
            if (await db.ReportSchedules.CountAsync(x => x.OwnerId == actor.UserId, ct) >= ReportRules.MaxSchedulesPerOwner) throw DomainException.Conflict($"You can keep at most {ReportRules.MaxSchedulesPerOwner} schedules.");
            s = new ReportSchedule { OwnerId = actor.UserId, CreatedAt = now }; db.ReportSchedules.Add(s);
        }
        else
        {
            s = await db.ReportSchedules.FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == actor.UserId, ct) ?? throw DomainException.NotFound("Schedule");
            if (req.Version is { } v && v != s.Version) throw DomainException.Conflict("This schedule was changed by someone else. Reload and try again.");
            s.Version++;
        }
        s.Name = name!; s.Report = report; s.Format = format; s.Frequency = freq; s.Weekday = weekday; s.MonthDay = monthDay; s.Hour = hour; s.Days = days;
        s.RecipientIds = recipients; s.Active = req.Active ?? true; s.UpdatedAt = now;
        s.NextRunAt = NextRun(s, now, await ZoneAsync(ct));
        audit.Record(id is null ? "report.schedule_create" : "report.schedule_update", "report_schedule", s.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email,
            new Dictionary<string, string> { ["report"] = report, ["frequency"] = freq, ["recipients"] = recipients.Count.ToString(CultureInfo.InvariantCulture) });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw DomainException.Conflict("This schedule was changed by someone else. Reload and try again."); }
        return await DtoAsync(s, actor.UserId, ct);
    }

    public async Task DeleteAsync(Guid id, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var s = await db.ReportSchedules.FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == actor.UserId, ct) ?? throw DomainException.NotFound("Schedule");
        db.ReportSchedules.Remove(s);
        audit.Record("report.schedule_delete", "report_schedule", s.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["report"] = s.Report });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Sends a schedule now (the owner's "Send now"); the regular time is unchanged.</summary>
    public async Task<ReportScheduleDto> SendNowAsync(Guid id, SessionPrincipal actor, CancellationToken ct = default)
    {
        var s = await db.ReportSchedules.FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == actor.UserId, ct) ?? throw DomainException.NotFound("Schedule");
        await SendAsync(s, ct);
        await db.SaveChangesAsync(ct);
        return await DtoAsync(s, actor.UserId, ct);
    }

    /// <summary>Background run (every few minutes): sends every active schedule that is due, in every organisation.</summary>
    public async Task<ReportRunResult> RunDueAsync(CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var orgs = await db.ReportSchedules.IgnoreQueryFilters().AsNoTracking().Where(s => s.Active && s.NextRunAt <= now).Select(s => s.TenantId).Distinct().ToListAsync(ct);
        int schedules = 0, sent = 0, skipped = 0;
        foreach (var org in orgs)
        {
            using (tenant.Switch(org))
            {
                db.ResetTracking();
                var zone = await ZoneAsync(ct);
                var due = await db.ReportSchedules.AsNoTracking().Where(s => s.Active && s.NextRunAt <= now).OrderBy(s => s.NextRunAt).Take(100).ToListAsync(ct);
                foreach (var d in due)
                {
                    // claim: move the next run first, so two instances never send the same report twice
                    var next = NextRun(d, now, zone);
                    if (await db.ReportSchedules.Where(x => x.Id == d.Id && x.NextRunAt == d.NextRunAt).ExecuteUpdateAsync(u => u.SetProperty(x => x.NextRunAt, next), ct) == 0) continue;
                    var s = await db.ReportSchedules.FirstAsync(x => x.Id == d.Id, ct);
                    var (n, k) = await SendAsync(s, ct);
                    await db.SaveChangesAsync(ct);
                    schedules++; sent += n; skipped += k;
                }
            }
        }
        return new ReportRunResult(schedules, sent, skipped);
    }

    /// <summary>Builds the report for each recipient with their own access and queues the e-mail with the file attached. An owner who can no longer see
    /// the report (or left) pauses the schedule.</summary>
    private async Task<(int Sent, int Skipped)> SendAsync(ReportSchedule s, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var kind = Kinds.First(k => k.Key == s.Report);
        var owner = await db.Users.AsNoTracking().Where(u => u.Id == s.OwnerId).Select(u => new { u.Status }).FirstOrDefaultAsync(ct);
        s.LastRunAt = now; s.UpdatedAt = now;
        if (owner?.Status != UserStatus.Active || !await MaySeeAsync(s.OwnerId, s.Report, ct))
        {
            s.Active = false; s.LastResult = "Paused: the owner can no longer see this report.";
            audit.Record("report.schedule_paused", "report_schedule", s.Id.ToString(), Outcome.Success, Background, null, "system (reports)", new Dictionary<string, string> { ["report"] = s.Report });
            return (0, 0);
        }
        var people = await db.Users.AsNoTracking().Where(u => s.RecipientIds.Contains(u.Id)).Select(u => new { u.Id, u.Email, u.Name, u.Status }).ToListAsync(ct);
        int sent = 0, skipped = s.RecipientIds.Count - people.Count, failed = 0;
        foreach (var u in people)
        {
            if (u.Status != UserStatus.Active || !await MaySeeAsync(u.Id, s.Report, ct)) { skipped++; continue; }
            try
            {
                var table = await BuildAsync(s.Report, s.Days, new SessionPrincipal(Guid.Empty, u.Id, u.Email, u.Name, false, false, null, TenantId: tenant.TenantId), ct);
                var (content, type, ext) = Tabular.Write(table, s.Format);
                var file = $"{s.Report}-{now:yyyyMMdd}.{ext}";
                var more = table.Truncated ? $"\n\nOnly the first {ReportRules.MaxRows} rows are included. Open the report in the app for the rest." : "";
                var text = $"Hello {u.Name},\n\n\"{s.Name}\" is attached: {table.Title}, {table.Rows.Count} rows.{more}\n\nYou receive this because you are on the list of this scheduled report. Its owner can remove you.\n{links.Origin}/reports/schedules";
                var html = $"<p>Hello {System.Net.WebUtility.HtmlEncode(u.Name)},</p><p>&ldquo;{System.Net.WebUtility.HtmlEncode(s.Name)}&rdquo; is attached: {System.Net.WebUtility.HtmlEncode(table.Title)}, {table.Rows.Count} rows.{(table.Truncated ? $" Only the first {ReportRules.MaxRows} rows are included." : "")}</p><p style=\"color:#666\">You receive this because you are on the list of this scheduled report. Its owner can remove you.</p>";
                mail.Enqueue(new EmailMessage(u.Email, $"{s.Name} — {table.Title}", text, html) { Attachment = new EmailAttachment(file, type, content) });
                sent++;
            }
            catch (DomainException) { failed++; }
        }
        s.LastResult = $"Sent to {sent} {(sent == 1 ? "person" : "people")}" + (skipped > 0 ? $"; {skipped} skipped (no access)" : "") + (failed > 0 ? $"; {failed} failed" : "") + ".";
        audit.Record("report.schedule_sent", "report_schedule", s.Id.ToString(), Outcome.Success, Background, s.OwnerId, "system (reports)",
            new Dictionary<string, string> { ["report"] = s.Report, ["format"] = s.Format, ["sent"] = sent.ToString(CultureInfo.InvariantCulture), ["skipped"] = skipped.ToString(CultureInfo.InvariantCulture) });
        return (sent, skipped + failed);
    }
}
