using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Application.Users;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Application.Helpdesk;

public sealed record VisitItem(Guid Id, string VisitorName, string? Company, string? Email, string? Phone, string? VehiclePlate, Guid HostId, string? Host, Guid? LocationId, string? Place,
    string? Purpose, DateTimeOffset ExpectedFrom, DateTimeOffset ExpectedUntil, string Status, string? Flag, string? BadgeNumber, string? IdLast4, DateTimeOffset? AcknowledgedAt,
    DateTimeOffset? CheckedInAt, DateTimeOffset? CheckedOutAt, bool Anonymised, bool CanChange, bool CanReceive, int Version);
public sealed record SaveVisitRequest(string? VisitorName, string? Company, string? Email, string? Phone, string? VehiclePlate, Guid? HostId, Guid? LocationId, string? Purpose,
    DateTimeOffset? ExpectedFrom, DateTimeOffset? ExpectedUntil, int? Version = null, string? HostEmail = null);
public sealed record CheckInVisitRequest(string? BadgeNumber, string? IdLast4, bool? Acknowledged);
public sealed record VisitorSettingsDto(string? Acknowledgement, bool AskIdLast4, int RetentionDays);
public sealed record VisitorBlockItem(Guid Id, string Name, string? Company, string Reason, string? By, DateTimeOffset CreatedAt);
public sealed record SaveVisitorBlockRequest(string? Name, string? Company, string? Reason);
public sealed record VisitSummary(int ExpectedToday, int OnSiteNow, int CheckedOutToday, int NoShows30, int Flagged, int Visitors30, IReadOnlyList<DayCount> PerDay, IReadOnlyList<CountItem> BySite);
public sealed record PagedVisits(IReadOnlyList<VisitItem> Data, int Page, int PageSize, int Total);

/// <summary>
/// VIS-001/002/005 and RPT-005 (visitors). Anyone signed in pre-registers their own visitors (like making a request); reception (<c>visitors.checkin</c>) registers
/// walk-ins for any host and checks visitors in and out; <c>visitors.read_all</c> sees every visit and the visitor report; <c>visitors.manage</c> keeps the
/// block list and the settings. Hosts are told when their visitor arrives. Visits nobody showed up for become no-shows, and personal details are blanked after
/// the retention period.
/// </summary>
public sealed class VisitService(IAppDb db, IClock clock, AuditWriter audit, AuthorizationService authz, NotificationService notifications, IAppLinks links, ITenantContext tenant)
{
    private static string? Clean(string? s) { var t = (s ?? "").Trim(); return t.Length == 0 ? null : t; }
    private static DomainException Invalid(Dictionary<string, string> e) => DomainException.Validation("Please correct the highlighted fields.", e);
    public static string StatusName(VisitStatus s) => s switch
    {
        VisitStatus.CheckedIn => "checked_in", VisitStatus.CheckedOut => "checked_out", VisitStatus.Cancelled => "cancelled", VisitStatus.NoShow => "no_show", _ => "expected",
    };

    private sealed record Who(Guid UserId, string Email, bool Desk, bool ReadAll, bool Manage);
    private async Task<Who> WhoAsync(SessionPrincipal p, CancellationToken ct)
    {
        var perms = await authz.GetEffectivePermissionsAsync(p.UserId, ct);
        return new Who(p.UserId, p.Email, perms.Contains(Permissions.VisitorsCheckIn), perms.Contains(Permissions.VisitorsReadAll), perms.Contains(Permissions.VisitorsManage));
    }
    private sealed record Place(Guid Id, Guid? ParentId, string Name);
    private async Task<Dictionary<Guid, Place>> PlacesAsync(CancellationToken ct) => await db.Locations.AsNoTracking().Select(l => new Place(l.Id, l.ParentId, l.Name)).ToDictionaryAsync(l => l.Id, ct);
    private static string PathOf(Dictionary<Guid, Place> places, Guid id)
    {
        var parts = new List<string>();
        for (Guid? c = id; c is { } k && places.TryGetValue(k, out var p) && parts.Count < 6; c = p.ParentId) parts.Insert(0, p.Name);
        return string.Join(" › ", parts);
    }
    private static Guid? Top(Dictionary<Guid, Place> places, Guid? id) { Guid? top = null; for (var (c, n) = (id, 0); c is { } k && places.TryGetValue(k, out var p) && n < 10; c = p.ParentId, n++) top = k; return top; }

    private VisitItem ToItem(Visit v, Who w, IReadOnlyDictionary<Guid, string> names, Dictionary<Guid, Place> places)
    {
        var own = v.HostId == w.UserId || v.CreatedById == w.UserId;
        var open = v.Status == VisitStatus.Expected && v.AnonymisedAt is null;
        return new VisitItem(v.Id, v.VisitorName, v.Company, v.Email, v.Phone, v.VehiclePlate, v.HostId, names.GetValueOrDefault(v.HostId), v.LocationId,
            v.LocationId is { } l && places.ContainsKey(l) ? PathOf(places, l) : null, v.Purpose, v.ExpectedFrom, v.ExpectedUntil, StatusName(v.Status),
            w.Desk || w.ReadAll || w.Manage ? v.Flag : null, v.BadgeNumber, w.Desk || w.ReadAll ? v.IdLast4 : null, v.AcknowledgedAt, v.CheckedInAt, v.CheckedOutAt, v.AnonymisedAt is not null,
            open && (own || w.Desk), w.Desk && v.AnonymisedAt is null && v.Status is VisitStatus.Expected or VisitStatus.CheckedIn, v.Version);
    }
    private async Task<Dictionary<Guid, string>> NamesAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        return await db.Users.AsNoTracking().Where(u => list.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Name, ct);
    }
    private async Task<VisitItem> ItemAsync(Guid id, Who w, CancellationToken ct)
    {
        var v = await db.Visits.AsNoTracking().FirstAsync(x => x.Id == id, ct);
        return ToItem(v, w, await NamesAsync([v.HostId], ct), await PlacesAsync(ct));
    }

    /// <summary>The active block-list entry this visitor matches: same name (spaces and case ignored) and, when the entry names a company, the same company.</summary>
    private async Task<VisitorBlock?> BlockedAsync(string name, string? company, CancellationToken ct)
    {
        var key = VisitRules.Key(name); var comp = company is null ? null : VisitRules.Key(company);
        var active = await db.VisitorBlocks.AsNoTracking().Where(b => b.RemovedAt == null).ToListAsync(ct);
        return active.FirstOrDefault(b => VisitRules.Key(b.Name) == key && (b.Company is null || (comp is not null && VisitRules.Key(b.Company) == comp)));
    }

    // ================================================================ reading
    /// <summary><c>scope</c>: <c>mine</c> (visitors I host or registered), <c>day</c> (everyone expected or on site on <paramref name="date"/>, for reception) or
    /// <c>all</c> (<c>visitors.read_all</c>).</summary>
    public async Task<PagedVisits> ListAsync(string? scope, DateOnly? date, Guid? locationId, string? status, string? q, int page, int pageSize, SessionPrincipal p, CancellationToken ct = default)
    {
        var w = await WhoAsync(p, ct);
        var s = (scope ?? "mine").Trim().ToLowerInvariant();
        if (s == "day" && !w.Desk && !w.ReadAll) throw DomainException.Denied(Permissions.VisitorsCheckIn);
        if (s == "all" && !w.ReadAll) throw DomainException.Denied(Permissions.VisitorsReadAll);
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 200);
        var query = db.Visits.AsNoTracking().AsQueryable();
        if (s == "mine") query = query.Where(v => v.HostId == w.UserId || v.CreatedById == w.UserId);
        var places = await PlacesAsync(ct);
        if (s == "day")
        {
            var zone = await ZoneAsync(ct);
            var d = date ?? DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.UtcNow, zone).DateTime);
            var start = new DateTimeOffset(d.ToDateTime(TimeOnly.MinValue), zone.GetUtcOffset(d.ToDateTime(TimeOnly.MinValue))).ToUniversalTime(); var end = start.AddDays(1);
            query = query.Where(v => (v.ExpectedFrom < end && v.ExpectedUntil > start) || v.Status == VisitStatus.CheckedIn);
        }
        if (locationId is { } loc) { var below = places.Values.Where(x => Top(places, x.Id) == Top(places, loc) || x.Id == loc).Select(x => x.Id).ToHashSet(); query = query.Where(v => v.LocationId != null && below.Contains(v.LocationId.Value)); }
        if (Clean(status) is { } st)
        {
            var want = Enum.GetValues<VisitStatus>().Where(x => StatusName(x) == st).ToList();
            if (want.Count == 0) throw Invalid(new Dictionary<string, string> { ["status"] = "Use expected, checked_in, checked_out, cancelled or no_show." });
            query = query.Where(v => want.Contains(v.Status));
        }
        if (Clean(q) is { } text)
        {
            var like = "%" + text.ToLowerInvariant().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            query = query.Where(v => EF.Functions.Like(v.VisitorName.ToLower(), like, "\\") || (v.Company != null && EF.Functions.Like(v.Company.ToLower(), like, "\\")) || (v.VehiclePlate != null && EF.Functions.Like(v.VehiclePlate.ToLower(), like, "\\")));
        }
        var total = await query.CountAsync(ct);
        var rows = await (s == "mine" ? query.OrderByDescending(v => v.ExpectedFrom) : query.OrderBy(v => v.ExpectedFrom)).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var names = await NamesAsync(rows.Select(v => v.HostId), ct);
        return new PagedVisits([.. rows.Select(v => ToItem(v, w, names, places))], page, pageSize, total);
    }

    public async Task<VisitItem> GetAsync(Guid id, SessionPrincipal p, CancellationToken ct = default)
    {
        var w = await WhoAsync(p, ct);
        var v = await db.Visits.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Visit");
        if (!(v.HostId == w.UserId || v.CreatedById == w.UserId || w.Desk || w.ReadAll)) throw DomainException.NotFound("Visit");
        return ToItem(v, w, await NamesAsync([v.HostId], ct), await PlacesAsync(ct));
    }

    private async Task<TimeZoneInfo> ZoneAsync(CancellationToken ct)
    {
        var id = await db.Calendars.AsNoTracking().Where(c => c.IsDefault && c.ArchivedAt == null).Select(c => c.TimeZone).FirstOrDefaultAsync(ct) ?? CalendarService.DefaultTimeZone;
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException) { return TimeZoneInfo.Utc; }
    }

    // ================================================================ registering
    public async Task<VisitItem> SaveAsync(Guid? id, SaveVisitRequest req, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var w = await WhoAsync(actor, ct);
        var now = clock.UtcNow;
        Visit? v = null;
        if (id is { } vid)
        {
            v = await db.Visits.FirstOrDefaultAsync(x => x.Id == vid, ct) ?? throw DomainException.NotFound("Visit");
            var own = v.HostId == w.UserId || v.CreatedById == w.UserId;
            if (!own && !w.Desk) throw (w.ReadAll ? DomainException.Denied(Permissions.VisitorsCheckIn) : DomainException.NotFound("Visit"));
            if (v.Status != VisitStatus.Expected || v.AnonymisedAt is not null) throw DomainException.Conflict("Only an expected visit can be changed.");
            if (req.Version is { } ver && ver != v.Version) throw DomainException.Conflict("This visit was changed by someone else. Reload and try again.");
        }
        var e = new Dictionary<string, string>();
        var name = Clean(req.VisitorName);
        if (name is null) e["visitorName"] = "Give the visitor's name."; else if (name.Length > 150) e["visitorName"] = "Use at most 150 characters.";
        var company = Clean(req.Company); if (company is { Length: > 150 }) e["company"] = "Use at most 150 characters.";
        var email = Clean(req.Email)?.ToLowerInvariant();
        if (email is { } em && (em.Length > 200 || !em.Contains('@') || em.StartsWith('@') || em.EndsWith('@'))) e["email"] = "Use a valid e-mail address.";
        var phone = Clean(req.Phone); if (phone is { Length: > 40 }) e["phone"] = "Use at most 40 characters.";
        var plate = Clean(req.VehiclePlate)?.ToUpperInvariant(); if (plate is { Length: > 20 }) e["vehiclePlate"] = "Use at most 20 characters.";
        var purpose = Clean(req.Purpose); if (purpose is { Length: > 300 }) e["purpose"] = "Use at most 300 characters.";
        Guid? byEmail = null;
        if (Clean(req.HostEmail)?.ToLowerInvariant() is { } he)
        {
            byEmail = await db.Users.AsNoTracking().Where(u => u.Email.ToLower() == he && u.Status == UserStatus.Active).Select(u => (Guid?)u.Id).FirstOrDefaultAsync(ct);
            if (byEmail is null) e["hostEmail"] = "Nobody active has this e-mail address.";
        }
        var host = req.HostId ?? byEmail ?? v?.HostId ?? w.UserId;
        if (host != w.UserId && host != v?.HostId)
        {
            if (!w.Desk) e["hostId"] = "You can only register your own visitors.";
            else if (!await db.Users.AnyAsync(u => u.Id == host && u.Status == UserStatus.Active, ct)) e["hostId"] = "Choose an active person.";
        }
        if (req.LocationId is { } lid && !await db.Locations.AnyAsync(l => l.Id == lid && l.ArchivedAt == null, ct)) e["locationId"] = "Choose an active place.";
        if (req.ExpectedFrom is null) e["expectedFrom"] = "Say when they arrive.";
        if (req.ExpectedUntil is null) e["expectedUntil"] = "Say until when.";
        if (req.ExpectedFrom is { } f && req.ExpectedUntil is { } u)
        {
            if (u <= f) e["expectedUntil"] = "The end must be after the arrival.";
            else if ((u - f).TotalHours > VisitRules.MaxVisitHours) e["expectedUntil"] = "A visit lasts at most 7 days.";
            else if (u < now && v is null) e["expectedFrom"] = "This time has already passed.";
            else if (f > now.AddDays(365)) e["expectedFrom"] = "Register visitors at most a year ahead.";
        }
        if (e.Count > 0) throw Invalid(e);

        var block = await BlockedAsync(name!, company, ct);
        var isNew = v is null;
        if (v is null) { v = new Visit { CreatedById = w.UserId, CreatedAt = now }; db.Visits.Add(v); }
        else v.Version++;
        v.VisitorName = name!; v.Company = company; v.Email = email; v.Phone = phone; v.VehiclePlate = plate; v.HostId = host; v.LocationId = req.LocationId; v.Purpose = purpose;
        v.ExpectedFrom = req.ExpectedFrom!.Value.ToUniversalTime(); v.ExpectedUntil = req.ExpectedUntil!.Value.ToUniversalTime(); v.UpdatedAt = now;
        v.Flag = block is null ? null : $"On the block list: {block.Reason}";
        audit.Record(isNew ? "visit.register" : "visit.update", "visit", v.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email,
            new Dictionary<string, string> { ["host"] = host.ToString(), ["from"] = v.ExpectedFrom.ToString("O"), ["flagged"] = block is null ? "no" : "yes" });
        if (block is not null) await TellAsync(Permissions.VisitorsManage, "visitor.flagged", $"A blocked visitor was registered: {name}", $"{block.Reason} · host {actor.Name}", actor.UserId, ct);
        await db.SaveChangesAsync(ct);
        return await ItemAsync(v.Id, w, ct);
    }

    public async Task<VisitItem> CancelAsync(Guid id, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var w = await WhoAsync(actor, ct);
        var v = await db.Visits.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Visit");
        if (!(v.HostId == w.UserId || v.CreatedById == w.UserId || w.Desk)) throw (w.ReadAll ? DomainException.Denied(Permissions.VisitorsCheckIn) : DomainException.NotFound("Visit"));
        if (v.Status != VisitStatus.Expected) throw DomainException.Conflict("Only an expected visit can be cancelled.");
        v.Status = VisitStatus.Cancelled; v.UpdatedAt = clock.UtcNow; v.Version++;
        audit.Record("visit.cancel", "visit", v.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email);
        await db.SaveChangesAsync(ct);
        return await ItemAsync(v.Id, w, ct);
    }

    // ================================================================ reception
    public async Task<VisitItem> CheckInAsync(Guid id, CheckInVisitRequest req, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var w = await WhoAsync(actor, ct);
        if (!w.Desk) throw DomainException.Denied(Permissions.VisitorsCheckIn);
        var v = await db.Visits.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Visit");
        if (v.Status != VisitStatus.Expected) throw DomainException.Conflict($"This visit is {StatusName(v.Status).Replace('_', ' ')}.");
        var block = await BlockedAsync(v.VisitorName, v.Company, ct);
        if (block is not null) throw DomainException.Conflict($"{v.VisitorName} is on the block list: {block.Reason}. Ask General Affairs before letting them in.");
        var settings = await SettingsAsync(ct);
        var e = new Dictionary<string, string>();
        var badge = Clean(req.BadgeNumber);
        if (badge is null) e["badgeNumber"] = "Give the badge number."; else if (badge.Length > 20) e["badgeNumber"] = "Use at most 20 characters.";
        var last4 = Clean(req.IdLast4)?.ToUpperInvariant();
        if (settings.AskIdLast4 && (last4 is null || last4.Length != 4)) e["idLast4"] = "Give the last 4 characters of the ID.";
        else if (!settings.AskIdLast4) last4 = null;
        if (settings.Acknowledgement is not null && req.Acknowledged != true) e["acknowledged"] = "The visitor must accept the acknowledgement.";
        if (badge is not null && await db.Visits.AnyAsync(x => x.Status == VisitStatus.CheckedIn && x.BadgeNumber == badge && x.Id != id, ct)) e["badgeNumber"] = "This badge is with another visitor on site.";
        if (e.Count > 0) throw Invalid(e);
        var now = clock.UtcNow;
        v.Status = VisitStatus.CheckedIn; v.BadgeNumber = badge; v.IdLast4 = last4; v.AcknowledgedAt = settings.Acknowledgement is null ? null : now;
        v.CheckedInAt = now; v.CheckedInById = actor.UserId; v.UpdatedAt = now; v.Version++;
        audit.Record("visit.check_in", "visit", v.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["badge"] = badge! });
        var title = $"{v.VisitorName}{(v.Company is null ? "" : $" ({v.Company})")} has arrived";
        await notifications.SendAsync(v.HostId, "visitor.arrived", title, v.Purpose ?? "At reception.", "/visitors", null,
            (to, name) => Email.EmailTemplates.Notification(to, name, title, v.Purpose ?? "At reception.", links.Origin.TrimEnd('/') + "/visitors"), ct: ct);
        await db.SaveChangesAsync(ct);
        return await ItemAsync(v.Id, w, ct);
    }

    public async Task<VisitItem> CheckOutAsync(Guid id, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var w = await WhoAsync(actor, ct);
        if (!w.Desk) throw DomainException.Denied(Permissions.VisitorsCheckIn);
        var v = await db.Visits.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Visit");
        if (v.Status != VisitStatus.CheckedIn) throw DomainException.Conflict("Only a visitor on site can be checked out.");
        var now = clock.UtcNow;
        v.Status = VisitStatus.CheckedOut; v.CheckedOutAt = now; v.CheckedOutById = actor.UserId; v.UpdatedAt = now; v.Version++;
        audit.Record("visit.check_out", "visit", v.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["badge"] = v.BadgeNumber ?? "" });
        await db.SaveChangesAsync(ct);
        return await ItemAsync(v.Id, w, ct);
    }

    // ================================================================ summary (KPI strip and visitor report)
    public async Task<VisitSummary> SummaryAsync(SessionPrincipal p, CancellationToken ct = default)
    {
        var w = await WhoAsync(p, ct);
        if (!w.Desk && !w.ReadAll) throw DomainException.Denied(Permissions.VisitorsReadAll);
        var zone = await ZoneAsync(ct);
        var now = clock.UtcNow;
        var today = TimeZoneInfo.ConvertTime(now, zone).Date;
        var dayStart = new DateTimeOffset(today, zone.GetUtcOffset(today)).ToUniversalTime(); var dayEnd = dayStart.AddDays(1);
        var from30 = dayStart.AddDays(-29);
        var rows = await db.Visits.AsNoTracking().Where(v => v.ExpectedFrom < dayEnd && v.ExpectedUntil > from30 || v.Status == VisitStatus.CheckedIn)
            .Select(v => new { v.Status, v.ExpectedFrom, v.ExpectedUntil, v.CheckedInAt, v.CheckedOutAt, v.LocationId, v.Flag }).Take(100_000).ToListAsync(ct);
        var places = await PlacesAsync(ct);
        var visited = rows.Where(v => v.CheckedInAt != null && v.CheckedInAt >= from30).ToList();
        var perDay = Enumerable.Range(0, 30).Select(i => dayStart.AddDays(-29 + i)).Select(d => new DayCount(TimeZoneInfo.ConvertTime(d, zone).ToString("yyyy-MM-dd"), visited.Count(v => v.CheckedInAt >= d && v.CheckedInAt < d.AddDays(1)))).ToList();
        var bySite = visited.GroupBy(v => Top(places, v.LocationId)).Select(g => new CountItem(g.Key?.ToString() ?? "none", g.Key is { } k ? places[k].Name : "No place", g.Count())).OrderByDescending(x => x.Count).ToList();
        return new VisitSummary(
            rows.Count(v => v.Status == VisitStatus.Expected && v.ExpectedFrom < dayEnd && v.ExpectedUntil > dayStart),
            rows.Count(v => v.Status == VisitStatus.CheckedIn),
            rows.Count(v => v.Status == VisitStatus.CheckedOut && v.CheckedOutAt >= dayStart),
            rows.Count(v => v.Status == VisitStatus.NoShow && v.ExpectedFrom >= from30),
            rows.Count(v => v.Flag != null && v.Status == VisitStatus.Expected && v.ExpectedUntil > now),
            visited.Count, perDay, bySite);
    }

    // ================================================================ settings and block list
    public async Task<VisitorSettingsDto> SettingsAsync(CancellationToken ct = default)
    {
        var s = await db.VisitorSettings.AsNoTracking().FirstOrDefaultAsync(ct);
        return new VisitorSettingsDto(s?.Acknowledgement, s?.AskIdLast4 ?? false, s?.RetentionDays ?? VisitRules.DefaultRetentionDays);
    }

    public async Task<VisitorSettingsDto> SaveSettingsAsync(VisitorSettingsDto req, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var ack = Clean(req.Acknowledgement);
        var e = new Dictionary<string, string>();
        if (ack is { Length: > 4000 }) e["acknowledgement"] = "Use at most 4000 characters.";
        if (req.RetentionDays is < VisitRules.MinRetentionDays or > VisitRules.MaxRetentionDays) e["retentionDays"] = $"Use {VisitRules.MinRetentionDays} to {VisitRules.MaxRetentionDays} days.";
        if (e.Count > 0) throw Invalid(e);
        var s = await db.VisitorSettings.FirstOrDefaultAsync(ct);
        if (s is null) { s = new VisitorSettings(); db.VisitorSettings.Add(s); }
        s.Acknowledgement = ack; s.AskIdLast4 = req.AskIdLast4; s.RetentionDays = req.RetentionDays; s.UpdatedAt = clock.UtcNow;
        audit.Record("visitor.settings", "settings", "visitors", Outcome.Success, client, actor.UserId, actor.Email,
            new Dictionary<string, string> { ["retentionDays"] = req.RetentionDays.ToString(), ["askId"] = req.AskIdLast4 ? "yes" : "no", ["acknowledgement"] = ack is null ? "none" : "set" });
        await db.SaveChangesAsync(ct);
        return await SettingsAsync(ct);
    }

    public async Task<IReadOnlyList<VisitorBlockItem>> BlocksAsync(CancellationToken ct = default)
    {
        var rows = await db.VisitorBlocks.AsNoTracking().Where(b => b.RemovedAt == null).OrderBy(b => b.Name).ToListAsync(ct);
        var names = await NamesAsync(rows.Select(b => b.CreatedById), ct);
        return [.. rows.Select(b => new VisitorBlockItem(b.Id, b.Name, b.Company, b.Reason, names.GetValueOrDefault(b.CreatedById), b.CreatedAt))];
    }

    public async Task<IReadOnlyList<VisitorBlockItem>> AddBlockAsync(SaveVisitorBlockRequest req, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var e = new Dictionary<string, string>();
        var name = Clean(req.Name); if (name is null) e["name"] = "Give the name."; else if (name.Length > 150) e["name"] = "Use at most 150 characters.";
        var company = Clean(req.Company); if (company is { Length: > 150 }) e["company"] = "Use at most 150 characters.";
        var reason = Clean(req.Reason); if (reason is null) e["reason"] = "Say why."; else if (reason.Length > 300) e["reason"] = "Use at most 300 characters.";
        if (e.Count > 0) throw Invalid(e);
        var now = clock.UtcNow;
        var b = new VisitorBlock { Name = name!, Company = company, Reason = reason!, CreatedById = actor.UserId, CreatedAt = now };
        db.VisitorBlocks.Add(b);
        audit.Record("visitor.block", "visitor_block", b.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["reason"] = reason! });
        // flag the visits already expected for this person
        var key = VisitRules.Key(name!); var comp = company is null ? null : VisitRules.Key(company);
        foreach (var v in await db.Visits.Where(v => v.Status == VisitStatus.Expected && v.AnonymisedAt == null).ToListAsync(ct))
            if (VisitRules.Key(v.VisitorName) == key && (comp is null || (v.Company is not null && VisitRules.Key(v.Company) == comp))) { v.Flag = $"On the block list: {reason}"; v.UpdatedAt = now; }
        await db.SaveChangesAsync(ct);
        return await BlocksAsync(ct);
    }

    public async Task<IReadOnlyList<VisitorBlockItem>> RemoveBlockAsync(Guid id, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var b = await db.VisitorBlocks.FirstOrDefaultAsync(x => x.Id == id && x.RemovedAt == null, ct) ?? throw DomainException.NotFound("Entry");
        b.RemovedAt = clock.UtcNow;
        audit.Record("visitor.unblock", "visitor_block", b.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email);
        await db.SaveChangesAsync(ct);
        foreach (var v in await db.Visits.Where(v => v.Status == VisitStatus.Expected && v.Flag != null).ToListAsync(ct))
            if (await BlockedAsync(v.VisitorName, v.Company, ct) is null) { v.Flag = null; v.UpdatedAt = clock.UtcNow; }
        await db.SaveChangesAsync(ct);
        return await BlocksAsync(ct);
    }

    // ================================================================ daily run: no-shows and retention
    public sealed record RunResult(int NoShows, int Anonymised);

    /// <summary>For every organisation: expected visits whose time has passed become no-shows; visits that ended more than the retention period ago lose the
    /// visitor's name, company, contact, plate, badge and ID digits (the counts stay). Claimed in single UPDATEs, so safe to run twice.</summary>
    public async Task<RunResult> RunAsync(CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var orgs = await db.Visits.IgnoreQueryFilters().AsNoTracking().Where(v => (v.Status == VisitStatus.Expected && v.ExpectedUntil < now) || (v.AnonymisedAt == null && v.Status != VisitStatus.Expected && v.Status != VisitStatus.CheckedIn))
            .Select(v => v.TenantId).Distinct().ToListAsync(ct);
        int noShows = 0, anon = 0;
        foreach (var org in orgs)
        {
            using (tenant.Switch(org))
            {
                db.ResetTracking();
                noShows += await db.Visits.Where(v => v.Status == VisitStatus.Expected && v.ExpectedUntil < now)
                    .ExecuteUpdateAsync(s => s.SetProperty(v => v.Status, VisitStatus.NoShow).SetProperty(v => v.UpdatedAt, now).SetProperty(v => v.Version, v => v.Version + 1), ct);
                var days = (await SettingsAsync(ct)).RetentionDays;
                var cutoff = now.AddDays(-days);
                anon += await db.Visits.Where(v => v.AnonymisedAt == null && v.Status != VisitStatus.Expected && v.Status != VisitStatus.CheckedIn && v.ExpectedUntil < cutoff && (v.CheckedOutAt == null || v.CheckedOutAt < cutoff))
                    .ExecuteUpdateAsync(s => s.SetProperty(v => v.VisitorName, VisitRules.Anonymous).SetProperty(v => v.Company, (string?)null).SetProperty(v => v.Email, (string?)null)
                        .SetProperty(v => v.Phone, (string?)null).SetProperty(v => v.VehiclePlate, (string?)null).SetProperty(v => v.BadgeNumber, (string?)null).SetProperty(v => v.IdLast4, (string?)null)
                        .SetProperty(v => v.Purpose, (string?)null).SetProperty(v => v.Flag, (string?)null).SetProperty(v => v.AnonymisedAt, now).SetProperty(v => v.UpdatedAt, now), ct);
            }
        }
        if (noShows + anon > 0) audit.Record("visit.housekeeping", "visit", null, Outcome.Success, new ClientInfo(null, "background", null), null, "system (visitors)",
            new Dictionary<string, string> { ["noShows"] = noShows.ToString(), ["anonymised"] = anon.ToString() });
        return new RunResult(noShows, anon);
    }

    private async Task TellAsync(string permission, string kind, string title, string body, Guid except, CancellationToken ct)
    {
        var people = await db.Users.AsNoTracking().Where(u => u.Status == UserStatus.Active && u.Id != except
                && u.UserRoles.Any(ur => ur.Role!.ArchivedAt == null && ur.Role.Permissions.Any(p => p.PermissionKey == permission)))
            .OrderBy(u => u.Id).Select(u => u.Id).Take(20).ToListAsync(ct);
        foreach (var uid in people) await notifications.SendAsync(uid, kind, title, body, "/reception", null, ct: ct);
    }
}
