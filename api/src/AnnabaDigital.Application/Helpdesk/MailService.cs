using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Application.Helpdesk;

public sealed record MailItemDto(Guid Id, string Direction, string Kind, string Status, Guid PersonId, string? Person, string? OtherParty, string? Address, string? Carrier, string? Waybill,
    string? Description, Guid? LocationId, string? Place, DateTimeOffset LoggedAt, DateTimeOffset? DoneAt, string? CollectedBy, string? Note, int WaitingDays, bool Anonymised, bool CanHandle, int Version);
public sealed record LogMailRequest(string? Direction, string? Kind, string? PersonEmail, Guid? PersonId, string? OtherParty, string? Address, string? Carrier, string? Waybill, string? Description, Guid? LocationId);
public sealed record CollectMailRequest(string? CollectedBy, string? Note);
public sealed record SendMailRequest(string? Carrier, string? Waybill, string? Note);
public sealed record MailSummary(int Waiting, int WaitingOver2Days, int ReceivedToday, int CollectedToday, int OutgoingPending, int SentThisWeek, int? OldestWaitingDays, IReadOnlyList<DayCount> Received30, IReadOnlyList<CountItem> ByCarrier);
public sealed record PagedMail(IReadOnlyList<MailItemDto> Data, int Page, int PageSize, int Total);

/// <summary>
/// VIS-004/005. The mailroom log. Reception (<c>mail.manage</c>) logs incoming letters and parcels for an employee (who is told at once and reminded after two days
/// if it is still waiting) and records who collected them; it logs outgoing items an employee sends and records the courier and waybill when they leave.
/// Everyone sees the mail logged for or by them. Personal details are blanked after the visitor retention period.
/// </summary>
public sealed class MailService(IAppDb db, IClock clock, AuditWriter audit, AuthorizationService authz, NotificationService notifications, IAppLinks links, ITenantContext tenant)
{
    private static string? Clean(string? s) { var t = (s ?? "").Trim(); return t.Length == 0 ? null : t; }
    private static DomainException Invalid(Dictionary<string, string> e) => DomainException.Validation("Please correct the highlighted fields.", e);
    private static string Name(MailStatus s) => s switch { MailStatus.Collected => "collected", MailStatus.Registered => "registered", MailStatus.Sent => "sent", MailStatus.Voided => "voided", _ => "waiting" };
    private static string Name(MailKind k) => k switch { MailKind.Letter => "letter", MailKind.Document => "document", MailKind.Other => "other", _ => "parcel" };
    private static MailKind? ParseKind(string? s) => (s ?? "").Trim().ToLowerInvariant() switch { "letter" => MailKind.Letter, "document" => MailKind.Document, "parcel" => MailKind.Parcel, "other" => MailKind.Other, _ => null };

    private async Task<bool> DeskAsync(Guid userId, CancellationToken ct) => (await authz.GetEffectivePermissionsAsync(userId, ct)).Contains(Permissions.MailManage);
    private sealed record Place(Guid Id, Guid? ParentId, string Name);
    private async Task<Dictionary<Guid, Place>> PlacesAsync(CancellationToken ct) => await db.Locations.AsNoTracking().Select(l => new Place(l.Id, l.ParentId, l.Name)).ToDictionaryAsync(l => l.Id, ct);
    private static string PathOf(Dictionary<Guid, Place> places, Guid id)
    {
        var parts = new List<string>();
        for (Guid? c = id; c is { } k && places.TryGetValue(k, out var p) && parts.Count < 6; c = p.ParentId) parts.Insert(0, p.Name);
        return string.Join(" › ", parts);
    }

    private MailItemDto ToDto(MailItem m, bool desk, IReadOnlyDictionary<Guid, string> names, Dictionary<Guid, Place> places)
    {
        var waiting = m.Direction == MailDirection.Incoming && m.Status == MailStatus.Received ? (int)Math.Floor((clock.UtcNow - m.LoggedAt).TotalDays) : 0;
        return new MailItemDto(m.Id, m.Direction == MailDirection.Incoming ? "incoming" : "outgoing", Name(m.Kind), Name(m.Status), m.PersonId, names.GetValueOrDefault(m.PersonId), m.OtherParty, m.Address,
            m.Carrier, m.Waybill, m.Description, m.LocationId, m.LocationId is { } l && places.ContainsKey(l) ? PathOf(places, l) : null, m.LoggedAt, m.DoneAt, m.CollectedBy, desk ? m.Note : null,
            waiting, m.AnonymisedAt is not null, desk && m.AnonymisedAt is null && m.Status is MailStatus.Received or MailStatus.Registered, m.Version);
    }
    private async Task<Dictionary<Guid, string>> NamesAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        return await db.Users.AsNoTracking().Where(u => list.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Name, ct);
    }
    private async Task<MailItemDto> DtoAsync(Guid id, bool desk, CancellationToken ct)
    {
        var m = await db.MailItems.AsNoTracking().FirstAsync(x => x.Id == id, ct);
        return ToDto(m, desk, await NamesAsync([m.PersonId], ct), await PlacesAsync(ct));
    }

    // ================================================================ reading
    /// <summary><c>scope</c>: <c>mine</c> (logged for or by me) or <c>desk</c> (everything, <c>mail.manage</c>). <c>status</c>: waiting, collected, registered, sent, voided, or <c>open</c> (waiting and registered).</summary>
    public async Task<PagedMail> ListAsync(string? scope, string? direction, string? status, string? q, int page, int pageSize, SessionPrincipal p, CancellationToken ct = default)
    {
        var desk = await DeskAsync(p.UserId, ct);
        var s = (scope ?? "mine").Trim().ToLowerInvariant();
        if (s == "desk" && !desk) throw DomainException.Denied(Permissions.MailManage);
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 200);
        var query = db.MailItems.AsNoTracking().AsQueryable();
        if (s != "desk") query = query.Where(m => m.PersonId == p.UserId && m.Status != MailStatus.Voided);
        switch ((direction ?? "").Trim().ToLowerInvariant())
        {
            case "incoming": query = query.Where(m => m.Direction == MailDirection.Incoming); break;
            case "outgoing": query = query.Where(m => m.Direction == MailDirection.Outgoing); break;
            case "": break;
            default: throw Invalid(new Dictionary<string, string> { ["direction"] = "Use incoming or outgoing." });
        }
        switch ((status ?? "").Trim().ToLowerInvariant())
        {
            case "": break;
            case "open": query = query.Where(m => m.Status == MailStatus.Received || m.Status == MailStatus.Registered); break;
            case var st:
                var want = Enum.GetValues<MailStatus>().Where(x => Name(x) == st).ToList();
                if (want.Count == 0) throw Invalid(new Dictionary<string, string> { ["status"] = "Use waiting, collected, registered, sent, voided or open." });
                query = query.Where(m => want.Contains(m.Status)); break;
        }
        if (Clean(q) is { } text)
        {
            var like = "%" + text.ToLowerInvariant().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            var people = await db.Users.AsNoTracking().Where(u => EF.Functions.Like(u.Name.ToLower(), like, "\\") || EF.Functions.Like(u.Email.ToLower(), like, "\\")).Select(u => u.Id).Take(200).ToListAsync(ct);
            query = query.Where(m => people.Contains(m.PersonId) || (m.Waybill != null && EF.Functions.Like(m.Waybill.ToLower(), like, "\\")) || (m.OtherParty != null && EF.Functions.Like(m.OtherParty.ToLower(), like, "\\")));
        }
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(m => m.LoggedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var names = await NamesAsync(rows.Select(m => m.PersonId), ct);
        var places = await PlacesAsync(ct);
        return new PagedMail([.. rows.Select(m => ToDto(m, desk, names, places))], page, pageSize, total);
    }

    // ================================================================ the mailroom
    public async Task<MailItemDto> LogAsync(LogMailRequest req, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var e = new Dictionary<string, string>();
        var dir = (req.Direction ?? "").Trim().ToLowerInvariant() switch { "incoming" => MailDirection.Incoming, "outgoing" => MailDirection.Outgoing, _ => (MailDirection?)null };
        if (dir is null) e["direction"] = "Use incoming or outgoing.";
        var kind = ParseKind(req.Kind) ?? MailKind.Parcel;
        if (req.Kind is not null && ParseKind(req.Kind) is null) e["kind"] = "Use letter, document, parcel or other.";
        Guid? person = req.PersonId;
        if (person is null && Clean(req.PersonEmail)?.ToLowerInvariant() is { } em)
            person = await db.Users.AsNoTracking().Where(u => u.Email.ToLower() == em && u.Status == UserStatus.Active).Select(u => (Guid?)u.Id).FirstOrDefaultAsync(ct);
        else if (person is { } pid && !await db.Users.AnyAsync(u => u.Id == pid && u.Status == UserStatus.Active, ct)) person = null;
        if (person is null) e["personEmail"] = dir == MailDirection.Outgoing ? "Give the e-mail of the person sending it." : "Give the e-mail of the person it is for.";
        var other = Clean(req.OtherParty); if (other is { Length: > 200 }) e["otherParty"] = "Use at most 200 characters.";
        var address = Clean(req.Address); if (address is { Length: > 500 }) e["address"] = "Use at most 500 characters.";
        if (dir == MailDirection.Outgoing && other is null) e["otherParty"] = "Give the addressee.";
        var carrier = Clean(req.Carrier); if (carrier is { Length: > 60 }) e["carrier"] = "Use at most 60 characters.";
        var waybill = Clean(req.Waybill)?.ToUpperInvariant(); if (waybill is { Length: > 60 }) e["waybill"] = "Use at most 60 characters.";
        var description = Clean(req.Description); if (description is { Length: > 300 }) e["description"] = "Use at most 300 characters.";
        if (req.LocationId is { } lid && !await db.Locations.AnyAsync(l => l.Id == lid && l.ArchivedAt == null, ct)) e["locationId"] = "Choose an active place.";
        if (e.Count > 0) throw Invalid(e);
        var now = clock.UtcNow;
        var m = new MailItem
        {
            Direction = dir!.Value, Kind = kind, Status = dir == MailDirection.Incoming ? MailStatus.Received : MailStatus.Registered, PersonId = person!.Value, OtherParty = other, Address = address,
            Carrier = carrier, Waybill = waybill, Description = description, LocationId = req.LocationId, LoggedAt = now, LoggedById = actor.UserId, UpdatedAt = now,
        };
        db.MailItems.Add(m);
        audit.Record(dir == MailDirection.Incoming ? "mail.receive" : "mail.register", "mail", m.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email,
            new Dictionary<string, string> { ["kind"] = Name(kind), ["carrier"] = carrier ?? "" });
        if (dir == MailDirection.Incoming)
        {
            var what = Name(kind) switch { "letter" => "A letter", "document" => "A document", "other" => "An item", _ => "A parcel" };
            var title = $"{what} for you is at reception";
            var body = string.Join(" · ", new[] { other is null ? null : $"from {other}", carrier, waybill }.Where(x => x is not null));
            await notifications.SendAsync(m.PersonId, "mail.arrived", title, body.Length == 0 ? null : body, "/mail", null,
                (to, name) => Email.EmailTemplates.Notification(to, name, title, body, links.Origin.TrimEnd('/') + "/mail"), ct: ct);
        }
        await db.SaveChangesAsync(ct);
        return await DtoAsync(m.Id, true, ct);
    }

    public async Task<MailItemDto> CollectAsync(Guid id, CollectMailRequest req, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var m = await db.MailItems.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Item");
        if (m.Direction != MailDirection.Incoming || m.Status != MailStatus.Received) throw DomainException.Conflict("Only incoming mail that is waiting can be collected.");
        var by = Clean(req.CollectedBy) ?? await db.Users.AsNoTracking().Where(u => u.Id == m.PersonId).Select(u => u.Name).FirstAsync(ct);
        if (by.Length > 150) throw Invalid(new Dictionary<string, string> { ["collectedBy"] = "Use at most 150 characters." });
        var note = Clean(req.Note); if (note is { Length: > 300 }) throw Invalid(new Dictionary<string, string> { ["note"] = "Use at most 300 characters." });
        var now = clock.UtcNow;
        m.Status = MailStatus.Collected; m.DoneAt = now; m.DoneById = actor.UserId; m.CollectedBy = by; m.Note = note ?? m.Note; m.UpdatedAt = now; m.Version++;
        audit.Record("mail.collect", "mail", m.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["collectedBy"] = by });
        await db.SaveChangesAsync(ct);
        return await DtoAsync(m.Id, true, ct);
    }

    public async Task<MailItemDto> SendAsync(Guid id, SendMailRequest req, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var m = await db.MailItems.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Item");
        if (m.Direction != MailDirection.Outgoing || m.Status != MailStatus.Registered) throw DomainException.Conflict("Only outgoing mail that has not left can be sent.");
        var e = new Dictionary<string, string>();
        var carrier = Clean(req.Carrier) ?? m.Carrier; if (carrier is null) e["carrier"] = "Give the courier."; else if (carrier.Length > 60) e["carrier"] = "Use at most 60 characters.";
        var waybill = Clean(req.Waybill)?.ToUpperInvariant() ?? m.Waybill; if (waybill is null) e["waybill"] = "Give the waybill number."; else if (waybill.Length > 60) e["waybill"] = "Use at most 60 characters.";
        if (e.Count > 0) throw Invalid(e);
        var now = clock.UtcNow;
        m.Status = MailStatus.Sent; m.Carrier = carrier; m.Waybill = waybill; m.DoneAt = now; m.DoneById = actor.UserId; m.Note = Clean(req.Note) ?? m.Note; m.UpdatedAt = now; m.Version++;
        audit.Record("mail.send", "mail", m.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["carrier"] = carrier!, ["waybill"] = waybill! });
        var title = $"Your mail to {m.OtherParty} has left with {carrier}";
        await notifications.SendAsync(m.PersonId, "mail.sent", title, $"Waybill {waybill}", "/mail", null, ct: ct);
        await db.SaveChangesAsync(ct);
        return await DtoAsync(m.Id, true, ct);
    }

    public async Task<MailItemDto> VoidAsync(Guid id, CollectMailRequest req, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var m = await db.MailItems.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Item");
        if (m.Status is not (MailStatus.Received or MailStatus.Registered)) throw DomainException.Conflict("Only mail that is still at reception can be voided.");
        var note = Clean(req.Note); if (note is null) throw Invalid(new Dictionary<string, string> { ["note"] = "Say why it is voided." });
        m.Status = MailStatus.Voided; m.Note = note; m.UpdatedAt = clock.UtcNow; m.Version++;
        audit.Record("mail.void", "mail", m.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["reason"] = note });
        await db.SaveChangesAsync(ct);
        return await DtoAsync(m.Id, true, ct);
    }

    // ================================================================ summary
    public async Task<MailSummary> SummaryAsync(CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var zoneId = await db.Calendars.AsNoTracking().Where(c => c.IsDefault && c.ArchivedAt == null).Select(c => c.TimeZone).FirstOrDefaultAsync(ct) ?? CalendarService.DefaultTimeZone;
        TimeZoneInfo zone; try { zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId); } catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException) { zone = TimeZoneInfo.Utc; }
        var today = TimeZoneInfo.ConvertTime(now, zone).Date;
        var dayStart = new DateTimeOffset(today, zone.GetUtcOffset(today)).ToUniversalTime();
        var from30 = dayStart.AddDays(-29); var week = dayStart.AddDays(-6);
        var rows = await db.MailItems.AsNoTracking().Where(m => m.LoggedAt >= from30 || m.Status == MailStatus.Received || m.Status == MailStatus.Registered || m.DoneAt >= dayStart)
            .Select(m => new { m.Direction, m.Status, m.LoggedAt, m.DoneAt, m.Carrier }).Take(100_000).ToListAsync(ct);
        var waiting = rows.Where(m => m.Direction == MailDirection.Incoming && m.Status == MailStatus.Received).ToList();
        var incoming30 = rows.Where(m => m.Direction == MailDirection.Incoming && m.LoggedAt >= from30 && m.Status != MailStatus.Voided).ToList();
        return new MailSummary(waiting.Count, waiting.Count(m => m.LoggedAt <= now.AddDays(-MailRules.ReminderDays)),
            rows.Count(m => m.Direction == MailDirection.Incoming && m.LoggedAt >= dayStart && m.Status != MailStatus.Voided),
            rows.Count(m => m.Status == MailStatus.Collected && m.DoneAt >= dayStart),
            rows.Count(m => m.Direction == MailDirection.Outgoing && m.Status == MailStatus.Registered),
            rows.Count(m => m.Status == MailStatus.Sent && m.DoneAt >= week),
            waiting.Count == 0 ? null : (int)Math.Floor((now - waiting.Min(m => m.LoggedAt)).TotalDays),
            [.. Enumerable.Range(0, 30).Select(i => dayStart.AddDays(-29 + i)).Select(d => new DayCount(TimeZoneInfo.ConvertTime(d, zone).ToString("yyyy-MM-dd"), incoming30.Count(m => m.LoggedAt >= d && m.LoggedAt < d.AddDays(1))))],
            [.. incoming30.GroupBy(m => m.Carrier ?? "").Select(g => new CountItem(g.Key == "" ? "none" : g.Key.ToLowerInvariant(), g.Key == "" ? "Not recorded" : g.Key, g.Count())).OrderByDescending(x => x.Count).Take(8)]);
    }

    // ================================================================ daily run: reminders and retention
    public sealed record RunResult(int Reminded, int Anonymised);

    /// <summary>For every organisation: reminds recipients once of incoming mail waiting two days or more, and blanks the names, addresses, waybills and
    /// descriptions of finished items older than the visitor retention period.</summary>
    public async Task<RunResult> RunAsync(CancellationToken ct = default)
    {
        var now = clock.UtcNow; var due = now.AddDays(-MailRules.ReminderDays);
        var orgs = await db.MailItems.IgnoreQueryFilters().AsNoTracking().Where(m => (m.Status == MailStatus.Received && m.RemindedAt == null && m.LoggedAt <= due) || (m.AnonymisedAt == null && m.Status != MailStatus.Received && m.Status != MailStatus.Registered))
            .Select(m => m.TenantId).Distinct().ToListAsync(ct);
        int reminded = 0, anon = 0;
        foreach (var org in orgs)
        {
            using (tenant.Switch(org))
            {
                db.ResetTracking();
                foreach (var m in await db.MailItems.AsNoTracking().Where(m => m.Status == MailStatus.Received && m.RemindedAt == null && m.LoggedAt <= due).Take(500).ToListAsync(ct))
                {
                    if (await db.MailItems.Where(x => x.Id == m.Id && x.RemindedAt == null).ExecuteUpdateAsync(s => s.SetProperty(x => x.RemindedAt, now), ct) == 0) continue;
                    await notifications.SendAsync(m.PersonId, "mail.arrived", "Your mail is still waiting at reception", $"Since {(int)(now - m.LoggedAt).TotalDays} days" + (m.OtherParty is null ? "" : $" · from {m.OtherParty}"), "/mail", null, alwaysMail: false, ct: ct);
                    await db.SaveChangesAsync(ct); reminded++;
                }
                var days = await db.VisitorSettings.AsNoTracking().Select(s => (int?)s.RetentionDays).FirstOrDefaultAsync(ct) ?? VisitRules.DefaultRetentionDays;
                var cutoff = now.AddDays(-days);
                anon += await db.MailItems.Where(m => m.AnonymisedAt == null && m.Status != MailStatus.Received && m.Status != MailStatus.Registered && m.LoggedAt < cutoff && (m.DoneAt == null || m.DoneAt < cutoff))
                    .ExecuteUpdateAsync(s => s.SetProperty(m => m.OtherParty, (string?)null).SetProperty(m => m.Address, (string?)null).SetProperty(m => m.Waybill, (string?)null)
                        .SetProperty(m => m.Description, (string?)null).SetProperty(m => m.CollectedBy, (string?)null).SetProperty(m => m.Note, (string?)null)
                        .SetProperty(m => m.AnonymisedAt, now).SetProperty(m => m.UpdatedAt, now), ct);
            }
        }
        if (reminded + anon > 0) audit.Record("mail.housekeeping", "mail", null, Outcome.Success, new ClientInfo(null, "background", null), null, "system (mail)",
            new Dictionary<string, string> { ["reminded"] = reminded.ToString(), ["anonymised"] = anon.ToString() });
        return new RunResult(reminded, anon);
    }
}
