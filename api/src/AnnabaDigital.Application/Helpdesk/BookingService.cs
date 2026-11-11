using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Application.Helpdesk;

public sealed record ResourceItem(Guid Id, string Kind, string Name, string? Code, string? Description, Guid? LocationId, string? Place, int? Capacity, IReadOnlyList<string> Features,
    string? OpenFrom, string? OpenUntil, int OpenDays, int BufferMinutes, int? MaxMinutes, bool NeedsApproval, int? CheckInMinutes, IReadOnlyList<Guid> AllowedRoleIds, bool Archived, string TimeZone, bool CanBook, int Version);
public sealed record SaveResourceRequest(string? Kind, string? Name, string? Code, string? Description, Guid? LocationId, int? Capacity, string[]? Features, string? OpenFrom, string? OpenUntil,
    int? OpenDays, int? BufferMinutes, int? MaxMinutes, bool? NeedsApproval, Guid[]? AllowedRoleIds, int? Version = null, int? CheckInMinutes = null);
/// <summary>A booking as one viewer sees it: the title, the person and the note are only shown to the people involved and to <c>bookings.read_all</c>.</summary>
public sealed record BookingItem(Guid Id, Guid ResourceId, string ResourceName, string ResourceKind, string? Title, Guid? BookedForId, string? BookedFor, DateTimeOffset StartsAt, DateTimeOffset EndsAt,
    DateTimeOffset BlockedUntil, int? Attendees, string? Note, string Status, bool Mine, bool CanChange, string? CancelReason, int Version,
    Guid? SeriesId = null, string? Repeat = null, DateTimeOffset? CheckedInAt = null, bool CanCheckIn = false, bool CanDecide = false, string? DecisionNote = null, bool NeedsCheckIn = false);
public sealed record SaveBookingRequest(Guid? ResourceId, DateTimeOffset? StartsAt, DateTimeOffset? EndsAt, string? Title, int? Attendees, string? Note, Guid? BookedForId = null, int? Version = null);
/// <summary>BKG-003. A repeating booking: <c>every</c> is <c>day</c>, <c>weekday</c> (Monday to Friday) or <c>week</c>, until a date (inclusive) or for a number of times.</summary>
public sealed record SaveSeriesRequest(Guid? ResourceId, DateTimeOffset? StartsAt, DateTimeOffset? EndsAt, string? Title, int? Attendees, string? Note, string? Every, DateOnly? Until, int? Count, Guid? BookedForId = null);
public sealed record SkippedOccurrence(DateTimeOffset StartsAt, string Reason);
public sealed record SeriesResult(Guid SeriesId, IReadOnlyList<BookingItem> Made, IReadOnlyList<SkippedOccurrence> Skipped);
/// <summary><c>scope</c>: <c>this</c> (default) or <c>following</c> (this booking and the later ones of its series).</summary>
public sealed record CancelBookingRequest(string? Reason, string? Scope = null);
public sealed record DecideBookingRequest(string? Note);
public sealed record AvailabilityRow(ResourceItem Resource, IReadOnlyList<BookingItem> Bookings, bool FreeNow);
public sealed record AvailabilityView(DateTimeOffset From, DateTimeOffset To, IReadOnlyList<AvailabilityRow> Rows);
public sealed record DayCount(string Day, int Bookings);
public sealed record BookingSummary(int Resources, int BookingsToday, int InUseNow, int FreeNow, int MyUpcoming, int? UtilisationPercent, IReadOnlyList<CountItem> ByKind, IReadOnlyList<DayCount> Next7Days,
    IReadOnlyList<CountItem> Busiest, int PendingApprovals = 0, int NoShows7 = 0);
public sealed record PagedBookings(IReadOnlyList<BookingItem> Data, int Page, int PageSize, int Total);
public sealed record BookingFeedInfo(bool Active, DateTimeOffset? CreatedAt, DateTimeOffset? LastReadAt, string? Url);

/// <summary>
/// BKG-001/002/003/005. Bookable resources (rooms, vehicles, parking, equipment) and their bookings. Anyone signed in (no permission, like making a request) books for themselves and changes or
/// cancels their own bookings; <c>bookings.manage</c> books for others, changes anyone's and approves bookings of resources that need it; <c>bookings.read_all</c> sees who
/// booked what; <c>resources.manage</c> maintains the resources. Two live bookings of one resource never overlap: the application says so politely and the
/// database's exclusion constraint makes it so. A background run sends reminders, releases rooms nobody checked in to and closes approvals that came too late.
/// </summary>
public sealed class BookingService(IAppDb db, IClock clock, AuditWriter audit, AuthorizationService authz, NotificationService notifications, IAppLinks links, ITenantContext tenant)
{
    public const int MaxFeatures = 20, MaxAllowedRoles = 20, MaxBufferMinutes = 240, MaxWindowDays = 31, MaxRows = 200, MaxApproverAlerts = 20;

    private static string? Clean(string? s) { var t = (s ?? "").Trim(); return t.Length == 0 ? null : t; }
    private static DomainException Invalid(Dictionary<string, string> e) => DomainException.Validation("Please correct the highlighted fields.", e);
    public static string KindName(ResourceKind k) => k switch { ResourceKind.Vehicle => "vehicle", ResourceKind.Parking => "parking", ResourceKind.Equipment => "equipment", _ => "room" };
    public static ResourceKind? ParseKind(string? s) => (s ?? "").Trim().ToLowerInvariant() switch
    {
        "room" => ResourceKind.Room, "vehicle" => ResourceKind.Vehicle, "parking" => ResourceKind.Parking, "equipment" => ResourceKind.Equipment, _ => null,
    };
    public static string StatusName(BookingStatus s) => s switch
    {
        BookingStatus.Cancelled => "cancelled", BookingStatus.Pending => "pending", BookingStatus.Rejected => "rejected", BookingStatus.Released => "released", _ => "confirmed",
    };
    /// <summary>The statuses that hold the slot (the database constraint uses the same list).</summary>
    private static readonly BookingStatus[] Live = [BookingStatus.Confirmed, BookingStatus.Pending];

    // ================================================================ places and time zones
    private sealed record Place(Guid Id, Guid? ParentId, string Name, string? TimeZone);
    private async Task<Dictionary<Guid, Place>> PlacesAsync(CancellationToken ct)
        => await db.Locations.AsNoTracking().Select(l => new Place(l.Id, l.ParentId, l.Name, l.TimeZone)).ToDictionaryAsync(l => l.Id, ct);
    private static string PathOf(Dictionary<Guid, Place> places, Guid id)
    {
        var parts = new List<string>();
        for (Guid? c = id; c is { } k && places.TryGetValue(k, out var p) && parts.Count < 6; c = p.ParentId) parts.Insert(0, p.Name);
        return string.Join(" › ", parts);
    }
    private static HashSet<Guid> Below(Dictionary<Guid, Place> places, Guid root)
    {
        var set = new HashSet<Guid> { root }; var added = true;
        while (added) { added = false; foreach (var p in places.Values) if (p.ParentId is { } par && set.Contains(par) && set.Add(p.Id)) added = true; }
        return set;
    }
    private async Task<string> DefaultZoneAsync(CancellationToken ct)
        => await db.Calendars.AsNoTracking().Where(c => c.IsDefault && c.ArchivedAt == null).Select(c => c.TimeZone).FirstOrDefaultAsync(ct) ?? CalendarService.DefaultTimeZone;
    /// <summary>The place's own time zone, else the nearest parent's, else the organisation's default calendar's.</summary>
    private static string ZoneId(Dictionary<Guid, Place> places, Guid? locationId, string fallback)
    {
        for (var (c, n) = (locationId, 0); c is { } k && places.TryGetValue(k, out var p) && n < 10; c = p.ParentId, n++)
            if (!string.IsNullOrWhiteSpace(p.TimeZone)) return p.TimeZone!;
        return fallback;
    }
    private static TimeZoneInfo Zone(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException) { return TimeZoneInfo.Utc; }
    }
    private async Task<TimeZoneInfo> ZoneOfAsync(BookableResource r, CancellationToken ct) => Zone(ZoneId(await PlacesAsync(ct), r.LocationId, await DefaultZoneAsync(ct)));
    private async Task<string> LocalAsync(BookableResource r, DateTimeOffset at, CancellationToken ct) => TimeZoneInfo.ConvertTime(at, await ZoneOfAsync(r, ct)).ToString("ddd d MMM yyyy, HH:mm", System.Globalization.CultureInfo.InvariantCulture);

    private sealed record Viewer(Guid UserId, string Name, bool ReadAll, bool Manage, bool ManageResources, HashSet<Guid> Roles);
    private async Task<Viewer> ViewerAsync(SessionPrincipal p, CancellationToken ct)
    {
        var perms = await authz.GetEffectivePermissionsAsync(p.UserId, ct);
        var roles = (await db.UserRoles.AsNoTracking().Where(r => r.UserId == p.UserId).Select(r => r.RoleId).ToListAsync(ct)).ToHashSet();
        return new Viewer(p.UserId, p.Name, perms.Contains(Permissions.BookingsReadAll), perms.Contains(Permissions.BookingsManage), perms.Contains(Permissions.ResourcesManage), roles);
    }
    private static bool MayBook(BookableResource r, Viewer v) => r.ArchivedAt == null && (v.Manage || r.AllowedRoleIds.Count == 0 || r.AllowedRoleIds.Any(v.Roles.Contains));

    private static ResourceItem ToItem(BookableResource r, Dictionary<Guid, Place> places, string fallbackZone, Viewer v)
        => new(r.Id, KindName(r.Kind), r.Name, r.Code, r.Description, r.LocationId, r.LocationId is { } l && places.ContainsKey(l) ? PathOf(places, l) : null, r.Capacity, r.Features,
            r.OpenFrom?.ToString("HH:mm"), r.OpenUntil?.ToString("HH:mm"), r.OpenDays, r.BufferMinutes, r.MaxMinutes, r.NeedsApproval, r.CheckInMinutes, r.AllowedRoleIds, r.ArchivedAt != null,
            ZoneId(places, r.LocationId, fallbackZone), MayBook(r, v), r.Version);

    /// <summary>Check-in is open from 15 minutes before the start until the release time (or the end when there is none).</summary>
    private bool CheckInOpen(Booking b, BookableResource r)
    {
        var now = clock.UtcNow;
        if (b.Status != BookingStatus.Confirmed || b.CheckedInAt is not null || r.CheckInMinutes is null) return false;
        var closes = b.StartsAt.AddMinutes(r.CheckInMinutes.Value); if (closes > b.EndsAt) closes = b.EndsAt;
        return now >= b.StartsAt.AddMinutes(-BookingRules.CheckInEarlyMinutes) && now < closes;
    }

    /// <param name="drives">The viewer drives this vehicle booking (R3-3): they see it like their own.</param>
    private BookingItem ToItem(Booking b, BookableResource r, Viewer v, IReadOnlyDictionary<Guid, string> names, bool drives = false)
    {
        var mine = b.BookedForId == v.UserId || b.CreatedById == v.UserId || drives;
        var see = mine || v.ReadAll || v.Manage;
        var live = b.HoldsSlot && b.EndsAt > clock.UtcNow;
        return new BookingItem(b.Id, r.Id, r.Name, KindName(r.Kind), see ? b.Title : null, see ? b.BookedForId : null, see ? names.GetValueOrDefault(b.BookedForId) : null,
            b.StartsAt, b.EndsAt, b.BlockedUntil, see ? b.Attendees : null, see ? b.Note : null, StatusName(b.Status), mine, live && (mine || v.Manage), see ? b.CancelReason : null, b.Version,
            b.SeriesId, b.Repeat, b.CheckedInAt, (mine || v.Manage) && CheckInOpen(b, r), v.Manage && b.Status == BookingStatus.Pending && b.EndsAt > clock.UtcNow, see ? b.DecisionNote : null,
            r.CheckInMinutes is not null && b.Status == BookingStatus.Confirmed && b.CheckedInAt is null);
    }
    private async Task<Dictionary<Guid, string>> NamesAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        return await db.Users.AsNoTracking().Where(u => list.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Name, ct);
    }

    // ================================================================ resources
    public async Task<IReadOnlyList<ResourceItem>> ListResourcesAsync(string? kind, Guid? locationId, string? q, int? capacity, string? feature, bool includeArchived, SessionPrincipal p, CancellationToken ct = default)
    {
        var v = await ViewerAsync(p, ct);
        var query = db.BookableResources.AsNoTracking().AsQueryable();
        if (!includeArchived || !v.ManageResources) query = query.Where(r => r.ArchivedAt == null);
        if (ParseKind(kind) is { } k) query = query.Where(r => r.Kind == k);
        if (Clean(q) is { } text)
        {
            var like = "%" + text.ToLowerInvariant().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            query = query.Where(r => EF.Functions.Like(r.Name.ToLower(), like, "\\") || (r.Code != null && EF.Functions.Like(r.Code.ToLower(), like, "\\")));
        }
        if (capacity is > 0) query = query.Where(r => r.Capacity >= capacity);
        if (Clean(feature)?.ToLowerInvariant() is { } f) query = query.Where(r => r.Features.Contains(f));
        var places = await PlacesAsync(ct);
        if (locationId is { } loc)
        {
            var below = Below(places, loc);
            query = query.Where(r => r.LocationId != null && below.Contains(r.LocationId.Value));
        }
        var rows = await query.OrderBy(r => r.Kind).ThenBy(r => r.Name).Take(MaxRows).ToListAsync(ct);
        var zone = await DefaultZoneAsync(ct);
        return [.. rows.Select(r => ToItem(r, places, zone, v))];
    }

    public async Task<ResourceItem> GetResourceAsync(Guid id, SessionPrincipal p, CancellationToken ct = default)
    {
        var v = await ViewerAsync(p, ct);
        var r = await db.BookableResources.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && (x.ArchivedAt == null || v.ManageResources), ct) ?? throw DomainException.NotFound("Resource");
        return ToItem(r, await PlacesAsync(ct), await DefaultZoneAsync(ct), v);
    }

    public async Task<ResourceItem> SaveResourceAsync(Guid? id, SaveResourceRequest req, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var e = new Dictionary<string, string>();
        var kind = ParseKind(req.Kind);
        if (kind is null) e["kind"] = "Choose room, vehicle, parking or equipment.";
        var name = Clean(req.Name);
        if (name is null) e["name"] = "Give it a name."; else if (name.Length > 120) e["name"] = "Use at most 120 characters.";
        var code = Clean(req.Code)?.ToUpperInvariant();
        if (code is { Length: > 30 }) e["code"] = "Use at most 30 characters.";
        var description = Clean(req.Description);
        if (description is { Length: > 1000 }) e["description"] = "Use at most 1000 characters.";
        if (req.Capacity is < 1 or > 1000) e["capacity"] = "Use 1 to 1000, or leave it empty.";
        var features = (req.Features ?? []).Select(f => (f ?? "").Trim().ToLowerInvariant().Replace(' ', '_')).Where(f => f.Length > 0).Distinct().ToList();
        if (features.Count > MaxFeatures) e["features"] = $"Use at most {MaxFeatures} features.";
        else if (features.Any(f => f.Length > 40 || !f.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '_'))) e["features"] = "Use letters, digits and underscores (at most 40 each).";
        TimeOnly? from = null, until = null;
        if (Clean(req.OpenFrom) is { } of) { if (TimeOnly.TryParseExact(of, "HH:mm", out var t)) from = t; else e["openFrom"] = "Use HH:mm, e.g. 08:00."; }
        if (Clean(req.OpenUntil) is { } ou) { if (TimeOnly.TryParseExact(ou, "HH:mm", out var t)) until = t; else e["openUntil"] = "Use HH:mm, e.g. 18:00."; }
        if ((from is null) != (until is null) && !e.ContainsKey("openFrom") && !e.ContainsKey("openUntil")) e[from is null ? "openFrom" : "openUntil"] = "Give both times, or neither for around the clock.";
        else if (from is { } a && until is { } b && b <= a) e["openUntil"] = "The closing time must be after the opening time.";
        var days = req.OpenDays ?? 127;
        if (days is < 1 or > 127) e["openDays"] = "Choose at least one day.";
        var buffer = req.BufferMinutes ?? 0;
        if (buffer < 0 || buffer > MaxBufferMinutes || buffer % BookingRules.StepMinutes != 0) e["bufferMinutes"] = $"Use 0 to {MaxBufferMinutes} minutes, in steps of {BookingRules.StepMinutes}.";
        if (req.MaxMinutes is { } mx && (mx < 15 || mx > BookingRules.MaxDays * 24 * 60 || mx % BookingRules.StepMinutes != 0)) e["maxMinutes"] = $"Use 15 minutes to {BookingRules.MaxDays} days, in steps of {BookingRules.StepMinutes} minutes.";
        if (req.CheckInMinutes is { } ci && !BookingRules.CheckInChoices.Contains(ci)) e["checkInMinutes"] = $"Use one of {string.Join(", ", BookingRules.CheckInChoices)} minutes, or no check-in.";
        if (req.LocationId is { } lid && !await db.Locations.AnyAsync(l => l.Id == lid && l.ArchivedAt == null, ct)) e["locationId"] = "Choose an active place.";
        var roles = (req.AllowedRoleIds ?? []).Distinct().ToList();
        if (roles.Count > MaxAllowedRoles) e["allowedRoleIds"] = $"Choose at most {MaxAllowedRoles} roles.";
        else if (roles.Count > 0 && await db.Roles.CountAsync(r => roles.Contains(r.Id), ct) != roles.Count) e["allowedRoleIds"] = "One of the roles does not exist.";
        if (code is not null && await db.BookableResources.AnyAsync(r => r.Code == code && r.Id != (id ?? Guid.Empty), ct)) e["code"] = "Another resource already uses this code.";
        if (e.Count > 0) throw Invalid(e);

        var now = clock.UtcNow;
        BookableResource r;
        if (id is null) { r = new BookableResource { CreatedAt = now }; db.BookableResources.Add(r); }
        else
        {
            r = await db.BookableResources.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Resource");
            if (req.Version is { } ver && ver != r.Version) throw DomainException.Conflict("This resource was changed by someone else. Reload and try again.");
            r.Version++;
        }
        r.Kind = kind!.Value; r.Name = name!; r.Code = code; r.Description = description; r.LocationId = req.LocationId; r.Capacity = req.Capacity; r.Features = features;
        r.OpenFrom = from; r.OpenUntil = until; r.OpenDays = days; r.BufferMinutes = buffer; r.MaxMinutes = req.MaxMinutes; r.NeedsApproval = req.NeedsApproval ?? false; r.CheckInMinutes = req.CheckInMinutes;
        r.AllowedRoleIds = roles; r.UpdatedAt = now;
        audit.Record(id is null ? "resource.create" : "resource.update", "resource", r.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email,
            new Dictionary<string, string> { ["name"] = r.Name, ["kind"] = KindName(r.Kind), ["approval"] = r.NeedsApproval ? "true" : "false" });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw DomainException.Conflict("This resource was changed by someone else. Reload and try again."); }
        catch (DbUpdateException) { throw DomainException.Conflict("Another resource already uses this code."); }
        return await GetResourceAsync(r.Id, actor, ct);
    }

    public async Task<ResourceItem> ArchiveResourceAsync(Guid id, bool archive, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var r = await db.BookableResources.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Resource");
        var now = clock.UtcNow;
        if (archive && r.ArchivedAt is null)
        {
            if (await db.Bookings.AnyAsync(b => b.ResourceId == id && Live.Contains(b.Status) && b.EndsAt > now, ct))
                throw DomainException.Conflict("This resource still has upcoming bookings. Cancel or move them first.");
            r.ArchivedAt = now;
        }
        else if (!archive) r.ArchivedAt = null;
        r.UpdatedAt = now; r.Version++;
        audit.Record(archive ? "resource.archive" : "resource.restore", "resource", r.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["name"] = r.Name });
        await db.SaveChangesAsync(ct);
        return await GetResourceAsync(r.Id, actor, ct);
    }

    // ================================================================ availability (day / week views)
    /// <summary>The resources that match, each with its live bookings between <paramref name="from"/> and <paramref name="to"/> (at most 31 days).</summary>
    public async Task<AvailabilityView> AvailabilityAsync(DateTimeOffset? from, DateTimeOffset? to, string? kind, Guid? locationId, int? capacity, string? feature, Guid? resourceId, SessionPrincipal p, CancellationToken ct = default)
    {
        var start = (from ?? clock.UtcNow.Date).ToUniversalTime(); var end = (to ?? start.AddDays(1)).ToUniversalTime();
        if (end <= start) throw DomainException.Validation("The end must be after the start.", new Dictionary<string, string> { ["to"] = "The end must be after the start." });
        if ((end - start).TotalDays > MaxWindowDays) throw DomainException.Validation($"Choose at most {MaxWindowDays} days.", new Dictionary<string, string> { ["to"] = $"Choose at most {MaxWindowDays} days." });
        var v = await ViewerAsync(p, ct);
        var resources = resourceId is { } rid ? [await GetResourceAsync(rid, p, ct)] : await ListResourcesAsync(kind, locationId, null, capacity, feature, false, p, ct);
        var ids = resources.Select(r => r.Id).ToList();
        var bookings = await db.Bookings.AsNoTracking().Where(b => ids.Contains(b.ResourceId) && Live.Contains(b.Status) && b.StartsAt < end && b.BlockedUntil > start)
            .OrderBy(b => b.StartsAt).Take(5000).ToListAsync(ct);
        var names = await NamesAsync(bookings.Select(b => b.BookedForId), ct);
        var entities = await db.BookableResources.AsNoTracking().Where(r => ids.Contains(r.Id)).ToDictionaryAsync(r => r.Id, ct);
        var now = clock.UtcNow;
        return new AvailabilityView(start, end, [.. resources.Select(r => new AvailabilityRow(r,
            [.. bookings.Where(b => b.ResourceId == r.Id).Select(b => ToItem(b, entities[r.Id], v, names))],
            !bookings.Any(b => b.ResourceId == r.Id && b.StartsAt <= now && b.BlockedUntil > now)))]);
    }

    // ================================================================ reading bookings
    /// <summary><c>scope</c>: <c>mine</c> (booked for or by me), <c>all</c> (needs <c>bookings.read_all</c> or <c>bookings.manage</c>) or <c>pending</c> (waiting for approval; needs <c>bookings.manage</c>).</summary>
    public async Task<PagedBookings> ListAsync(string? scope, DateTimeOffset? from, DateTimeOffset? to, Guid? resourceId, bool includeCancelled, int page, int pageSize, SessionPrincipal p, CancellationToken ct = default)
    {
        var v = await ViewerAsync(p, ct);
        var s = (scope ?? "mine").Trim().ToLowerInvariant();
        if (s == "all" && !v.ReadAll && !v.Manage) throw DomainException.Denied(Permissions.BookingsReadAll);
        if (s == "pending" && !v.Manage) throw DomainException.Denied(Permissions.BookingsManage);
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100);
        var q = db.Bookings.AsNoTracking().AsQueryable();
        if (s == "pending") q = q.Where(b => b.Status == BookingStatus.Pending);
        else
        {
            if (s != "all") q = q.Where(b => b.BookedForId == v.UserId || b.CreatedById == v.UserId || db.VehicleTrips.Any(t => t.BookingId == b.Id && t.DriverId == v.UserId));
            if (!includeCancelled) q = q.Where(b => Live.Contains(b.Status));
        }
        if (from?.ToUniversalTime() is { } f) q = q.Where(b => b.EndsAt > f);
        if (to?.ToUniversalTime() is { } t) q = q.Where(b => b.StartsAt < t);
        if (resourceId is { } r) q = q.Where(b => b.ResourceId == r);
        var total = await q.CountAsync(ct);
        var rows = await q.OrderBy(b => b.StartsAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var rids = rows.Select(b => b.ResourceId).Distinct().ToList();
        var resources = await db.BookableResources.AsNoTracking().Where(x => rids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var names = await NamesAsync(rows.Select(b => b.BookedForId), ct);
        var ids = rows.Select(b => b.Id).ToList();
        var driving = (await db.VehicleTrips.AsNoTracking().Where(t => ids.Contains(t.BookingId) && t.DriverId == v.UserId).Select(t => t.BookingId).ToListAsync(ct)).ToHashSet();
        return new PagedBookings([.. rows.Select(b => ToItem(b, resources[b.ResourceId], v, names, driving.Contains(b.Id)))], page, pageSize, total);
    }

    public async Task<BookingItem> GetAsync(Guid id, SessionPrincipal p, CancellationToken ct = default)
    {
        var v = await ViewerAsync(p, ct);
        var b = await db.Bookings.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Booking");
        var drives = await db.VehicleTrips.AnyAsync(t => t.BookingId == id && t.DriverId == v.UserId, ct);
        if (!(b.BookedForId == v.UserId || b.CreatedById == v.UserId || drives || v.ReadAll || v.Manage)) throw DomainException.NotFound("Booking");
        var r = await db.BookableResources.AsNoTracking().FirstAsync(x => x.Id == b.ResourceId, ct);
        return ToItem(b, r, v, await NamesAsync([b.BookedForId], ct), drives);
    }

    // ================================================================ booking rules
    private sealed record Checked(BookableResource Resource, string Title, string? Note, int? Attendees, Guid ForId, DateTimeOffset Start, DateTimeOffset End, TimeZoneInfo Zone);

    /// <summary>Validates the fields shared by a single booking and a series; <paramref name="existing"/> is the booking being changed.</summary>
    private async Task<(Dictionary<string, string> Errors, BookableResource? Resource, string? Title, string? Note, Guid ForId)> FieldsAsync(Guid? resourceId, string? rawTitle, string? rawNote, Guid? bookedForId, Booking? existing, Viewer v, CancellationToken ct)
    {
        var e = new Dictionary<string, string>();
        BookableResource? r = null;
        if (resourceId is null) e["resourceId"] = "Choose what to book.";
        else
        {
            r = await db.BookableResources.AsNoTracking().FirstOrDefaultAsync(x => x.Id == resourceId, ct);
            if (r is null || r.ArchivedAt is not null) e["resourceId"] = "Choose a resource that can be booked.";
            else if (!MayBook(r, v)) e["resourceId"] = "This resource can only be booked by certain roles.";
        }
        var title = Clean(rawTitle);
        if (title is null) e["title"] = "Say what it is for."; else if (title.Length > BookingRules.TitleMax) e["title"] = $"Use at most {BookingRules.TitleMax} characters.";
        var note = Clean(rawNote);
        if (note is { Length: > BookingRules.NoteMax }) e["note"] = $"Use at most {BookingRules.NoteMax} characters.";
        var forId = bookedForId ?? existing?.BookedForId ?? v.UserId;
        if (forId != v.UserId && forId != existing?.BookedForId)
        {
            if (!v.Manage) e["bookedForId"] = "You can only book for yourself.";
            else if (!await db.Users.AnyAsync(u => u.Id == forId && u.Status == UserStatus.Active, ct)) e["bookedForId"] = "Choose an active person.";
        }
        return (e, r, title, note, forId);
    }

    /// <summary>Why one occurrence cannot be booked (field → message), or an empty dictionary.</summary>
    private Dictionary<string, string> TimeErrors(BookableResource r, DateTimeOffset s, DateTimeOffset en, int? attendees, TimeZoneInfo zone, DateTimeOffset? keptStart)
    {
        var e = new Dictionary<string, string>();
        var now = clock.UtcNow;
        if (en <= s) e["endsAt"] = "The end must be after the start.";
        else if (s.Second != 0 || s.Millisecond != 0 || en.Second != 0 || en.Millisecond != 0 || s.Minute % BookingRules.StepMinutes != 0 || en.Minute % BookingRules.StepMinutes != 0)
            e["startsAt"] = $"Use whole {BookingRules.StepMinutes}-minute steps.";
        else if (s < now - BookingRules.Grace && s != keptStart) e["startsAt"] = "This time has already passed.";
        else if (s > now.AddDays(BookingRules.HorizonDays)) e["startsAt"] = $"You can book up to {BookingRules.HorizonDays} days ahead.";
        else if ((en - s).TotalMinutes > (r.MaxMinutes ?? BookingRules.MaxDays * 24 * 60)) e["endsAt"] = r.MaxMinutes is { } mm ? $"A booking of this resource can last at most {Hm(mm)}." : $"A booking can last at most {BookingRules.MaxDays} days.";
        else if (attendees is { } att && (att < 1 || (r.Capacity is { } cap && att > cap))) e["attendees"] = r.Capacity is { } c2 ? $"It holds at most {c2}." : "Use at least 1.";
        else if (OutsideHours(r, s, en, zone) is { } why) e["startsAt"] = why;
        return e;
    }

    private async Task<Booking?> ClashAsync(Guid resourceId, DateTimeOffset start, DateTimeOffset blocked, Guid? except, CancellationToken ct)
        => await db.Bookings.AsNoTracking().Where(x => x.ResourceId == resourceId && Live.Contains(x.Status) && x.Id != (except ?? Guid.Empty) && x.StartsAt < blocked && x.BlockedUntil > start)
            .OrderBy(x => x.StartsAt).FirstOrDefaultAsync(ct);

    // ================================================================ booking and changing
    public async Task<BookingItem> SaveAsync(Guid? id, SaveBookingRequest req, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var v = await ViewerAsync(actor, ct);
        var now = clock.UtcNow;
        Booking? b = null;
        if (id is { } bid)
        {
            b = await db.Bookings.FirstOrDefaultAsync(x => x.Id == bid, ct) ?? throw DomainException.NotFound("Booking");
            var mine = b.BookedForId == v.UserId || b.CreatedById == v.UserId;
            if (!mine && !v.Manage) throw (v.ReadAll ? DomainException.Denied(Permissions.BookingsManage) : DomainException.NotFound("Booking"));
            if (!b.HoldsSlot) throw DomainException.Conflict($"This booking was {StatusName(b.Status)}.");
            if (b.EndsAt <= now) throw DomainException.Conflict("This booking is already over.");
            if (req.Version is { } ver && ver != b.Version) throw DomainException.Conflict("This booking was changed by someone else. Reload and try again.");
        }

        var (e, r, title, note, forId) = await FieldsAsync(req.ResourceId ?? b?.ResourceId, req.Title, req.Note, req.BookedForId, b, v, ct);
        if (req.StartsAt is null) e["startsAt"] = "Choose when it starts.";
        if (req.EndsAt is null) e["endsAt"] = "Choose when it ends.";
        TimeZoneInfo zone = TimeZoneInfo.Utc;
        if (e.Count == 0)
        {
            zone = await ZoneOfAsync(r!, ct);
            foreach (var kv in TimeErrors(r!, req.StartsAt!.Value.ToUniversalTime(), req.EndsAt!.Value.ToUniversalTime(), req.Attendees, zone, b?.StartsAt)) e[kv.Key] = kv.Value;
        }
        if (e.Count > 0) throw Invalid(e);

        var start = req.StartsAt!.Value.ToUniversalTime(); var end = req.EndsAt!.Value.ToUniversalTime(); var blocked = end.AddMinutes(r!.BufferMinutes);
        if (await ClashAsync(r.Id, start, blocked, id, ct) is { } clash) throw Taken(r, clash.StartsAt, clash.BlockedUntil, zone);

        var isNew = b is null;
        if (b is null) { b = new Booking { CreatedById = v.UserId, CreatedAt = now }; db.Bookings.Add(b); }
        else b.Version++;
        var moved = !isNew && (b.StartsAt != start || b.EndsAt != end || b.ResourceId != r.Id);
        var previousFor = b.BookedForId;
        b.ResourceId = r.Id; b.Title = title!; b.Note = note; b.Attendees = req.Attendees; b.BookedForId = forId; b.StartsAt = start; b.EndsAt = end; b.BlockedUntil = blocked; b.UpdatedAt = now;
        if (moved) { b.RemindedAt = null; b.CheckedInAt = null; }
        // a resource that needs approval: a new booking, or one moved by someone who cannot approve, waits again
        var waits = r.NeedsApproval && !v.Manage && (isNew || moved);
        if (waits) { b.Status = BookingStatus.Pending; b.DecidedById = null; b.DecidedAt = null; b.DecisionNote = null; }
        else if (isNew) b.Status = BookingStatus.Confirmed;
        var meta = new Dictionary<string, string> { ["resource"] = r.Name, ["starts"] = start.ToString("O"), ["ends"] = end.ToString("O"), ["status"] = StatusName(b.Status) };
        if (forId != v.UserId) meta["for"] = forId.ToString();
        if (moved) meta["moved"] = "true";
        audit.Record(isNew ? "booking.create" : "booking.update", "booking", b.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, meta);
        // notifications are saved with the booking (and dropped with it when the save fails)
        var when = await LocalAsync(r, start, ct);
        if (forId != v.UserId && (isNew || forId != previousFor)) await TellAsync(forId, "booking.changed", $"{r.Name} was booked for you", $"{title} · {when}", ct);
        if (waits) await TellApproversAsync(r, title!, when, v.UserId, ct);
        await SaveBookingAsync(r, b.Id, start, blocked, zone, ct);
        return await GetAsync(b.Id, actor, ct);
    }

    /// <summary>Saves, turning a lost race (the exclusion constraint, or the deadlock two racing inserts can cause) into a 409 that names the clashing time.</summary>
    private async Task SaveBookingAsync(BookableResource r, Guid bookingId, DateTimeOffset start, DateTimeOffset blocked, TimeZoneInfo zone, CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw DomainException.Conflict("This booking was changed by someone else. Reload and try again."); }
        catch (Exception x) when (x is DbUpdateException || x is InvalidOperationException { InnerException: DbUpdateException })
        {
            // EF reports a deadlock (40P01) as a transient InvalidOperationException around the DbUpdateException. Either way the other booking won.
            db.ResetTracking();
            var other = await ClashAsync(r.Id, start, blocked, bookingId, ct);
            throw other is null ? DomainException.Conflict($"{r.Name} was just booked by someone else for this time. Choose another time.") : Taken(r, other.StartsAt, other.BlockedUntil, zone);
        }
    }

    /// <summary>BKG-003. Books the same time on every day, weekday or week until a date or for a number of times (at most 52). Occurrences that clash or break a rule
    /// are skipped and listed; the others are booked together (one save) and share a series id. Local times stay the same across daylight-saving changes.</summary>
    public async Task<SeriesResult> SaveSeriesAsync(SaveSeriesRequest req, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var v = await ViewerAsync(actor, ct);
        var now = clock.UtcNow;
        var (e, r, title, note, forId) = await FieldsAsync(req.ResourceId, req.Title, req.Note, req.BookedForId, null, v, ct);
        var every = (req.Every ?? "").Trim().ToLowerInvariant();
        if (every is not ("day" or "weekday" or "week")) e["every"] = "Choose every day, every weekday or every week.";
        if (req.StartsAt is null) e["startsAt"] = "Choose when it starts.";
        if (req.EndsAt is null) e["endsAt"] = "Choose when it ends.";
        if (req.Until is null && req.Count is null) e["until"] = "Choose an end date or a number of times.";
        if (req.Count is < 2 or > BookingRules.MaxOccurrences) e["count"] = $"Use 2 to {BookingRules.MaxOccurrences} times.";
        if (e.Count > 0) throw Invalid(e);

        var zone = await ZoneOfAsync(r!, ct);
        var first = TimeZoneInfo.ConvertTime(req.StartsAt!.Value, zone); var length = req.EndsAt!.Value - req.StartsAt.Value;
        if (req.Until is { } u && u < DateOnly.FromDateTime(first.DateTime)) throw Invalid(new Dictionary<string, string> { ["until"] = "The end date is before the first booking." });
        var firstErrors = TimeErrors(r!, req.StartsAt.Value.ToUniversalTime(), req.EndsAt.Value.ToUniversalTime(), req.Attendees, zone, null);
        if (firstErrors.Count > 0) throw Invalid(firstErrors);

        // the local start of each occurrence, then its UTC instant in the resource's zone
        var starts = new List<DateTimeOffset>();
        for (var d = first.DateTime; starts.Count < (req.Count ?? BookingRules.MaxOccurrences) && (req.Until is null || DateOnly.FromDateTime(d) <= req.Until); d = every == "week" ? d.AddDays(7) : d.AddDays(1))
        {
            if (every == "weekday" && d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
            var local = DateTime.SpecifyKind(d, DateTimeKind.Unspecified);
            if (zone.IsInvalidTime(local)) continue;   // a start that does not exist on a daylight-saving night
            starts.Add(new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime());
        }
        if (req.Until is not null && starts.Count >= BookingRules.MaxOccurrences && DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(starts[^1], zone).DateTime) < req.Until)
            throw Invalid(new Dictionary<string, string> { ["until"] = $"That is more than {BookingRules.MaxOccurrences} bookings. Choose an earlier end date." });

        var seriesId = Guid.CreateVersion7();
        var waits = r!.NeedsApproval && !v.Manage;
        var made = new List<Booking>(); var skipped = new List<SkippedOccurrence>();
        foreach (var s in starts)
        {
            var en = s + length; var blocked = en.AddMinutes(r.BufferMinutes);
            var why = TimeErrors(r, s, en, req.Attendees, zone, null);
            if (why.Count > 0) { skipped.Add(new SkippedOccurrence(s, why.Values.First())); continue; }
            if (await ClashAsync(r.Id, s, blocked, null, ct) is { } clash) { skipped.Add(new SkippedOccurrence(s, $"Already taken from {TimeZoneInfo.ConvertTime(clash.StartsAt, zone):HH:mm} to {TimeZoneInfo.ConvertTime(clash.BlockedUntil, zone):HH:mm}.")); continue; }
            if (made.Any(m => m.StartsAt < blocked && m.BlockedUntil > s)) { skipped.Add(new SkippedOccurrence(s, "Overlaps the previous booking of this series.")); continue; }
            made.Add(new Booking
            {
                ResourceId = r.Id, Title = title!, Note = note, Attendees = req.Attendees, BookedForId = forId, CreatedById = v.UserId, StartsAt = s, EndsAt = en, BlockedUntil = blocked,
                Status = waits ? BookingStatus.Pending : BookingStatus.Confirmed, SeriesId = seriesId, Repeat = every, CreatedAt = now, UpdatedAt = now,
            });
        }
        if (made.Count == 0) throw DomainException.Conflict("None of these times can be booked: " + string.Join(" ", skipped.Take(3).Select(x => FormattableString.Invariant($"{TimeZoneInfo.ConvertTime(x.StartsAt, zone):ddd d MMM}: {x.Reason}"))));
        db.Bookings.AddRange(made);
        audit.Record("booking.series", "booking", seriesId.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string>
        {
            ["resource"] = r.Name, ["every"] = every, ["made"] = made.Count.ToString(), ["skipped"] = skipped.Count.ToString(), ["first"] = made[0].StartsAt.ToString("O"),
        });
        var when = $"{await LocalAsync(r, made[0].StartsAt, ct)}, {made.Count}×";
        if (forId != v.UserId) await TellAsync(forId, "booking.changed", $"{r.Name} was booked for you", $"{title} · {when}", ct);
        if (waits) await TellApproversAsync(r, title!, when, v.UserId, ct);
        await SaveBookingAsync(r, made[0].Id, made[0].StartsAt, made[^1].BlockedUntil, zone, ct);
        var names = await NamesAsync([forId], ct);
        return new SeriesResult(seriesId, [.. made.Select(b => ToItem(b, r, v, names))], skipped);
    }

    // ================================================================ approval, check-in, cancelling
    public async Task<BookingItem> DecideAsync(Guid id, bool approve, DecideBookingRequest req, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var v = await ViewerAsync(actor, ct);
        if (!v.Manage) throw DomainException.Denied(Permissions.BookingsManage);
        var b = await db.Bookings.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Booking");
        if (b.Status != BookingStatus.Pending) throw DomainException.Conflict($"This booking is {StatusName(b.Status)}, not waiting for approval.");
        var now = clock.UtcNow;
        if (b.EndsAt <= now) throw DomainException.Conflict("This booking is already over.");
        var note = Clean(req.Note);
        if (note is { Length: > 500 }) throw Invalid(new Dictionary<string, string> { ["note"] = "Use at most 500 characters." });
        if (!approve && note is null) throw Invalid(new Dictionary<string, string> { ["note"] = "Tell the person why it is not approved." });
        b.Status = approve ? BookingStatus.Confirmed : BookingStatus.Rejected; b.DecidedById = v.UserId; b.DecidedAt = now; b.DecisionNote = note; b.UpdatedAt = now; b.Version++;
        var r = await db.BookableResources.AsNoTracking().FirstAsync(x => x.Id == b.ResourceId, ct);
        audit.Record(approve ? "booking.approve" : "booking.reject", "booking", b.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["resource"] = r.Name, ["starts"] = b.StartsAt.ToString("O") });
        await TellAsync(b.BookedForId, "booking.decided", approve ? $"Your booking of {r.Name} is approved" : $"Your booking of {r.Name} is not approved",
            $"{b.Title} · {await LocalAsync(r, b.StartsAt, ct)}" + (note is null ? "" : $" — {note}"), ct);
        if (b.CreatedById != b.BookedForId) await TellAsync(b.CreatedById, "booking.decided", approve ? $"The booking of {r.Name} is approved" : $"The booking of {r.Name} is not approved", $"{b.Title} · {await LocalAsync(r, b.StartsAt, ct)}", ct);
        await db.SaveChangesAsync(ct);
        return await GetAsync(b.Id, actor, ct);
    }

    public async Task<BookingItem> CheckInAsync(Guid id, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var v = await ViewerAsync(actor, ct);
        var b = await db.Bookings.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Booking");
        var mine = b.BookedForId == v.UserId || b.CreatedById == v.UserId;
        if (!mine && !v.Manage) throw (v.ReadAll ? DomainException.Denied(Permissions.BookingsManage) : DomainException.NotFound("Booking"));
        var r = await db.BookableResources.AsNoTracking().FirstAsync(x => x.Id == b.ResourceId, ct);
        if (r.CheckInMinutes is null) throw DomainException.Conflict("This resource needs no check-in.");
        if (b.CheckedInAt is not null) throw DomainException.Conflict("Already checked in.");
        if (!CheckInOpen(b, r))
            throw DomainException.Conflict(b.Status != BookingStatus.Confirmed ? $"This booking is {StatusName(b.Status)}."
                : clock.UtcNow < b.StartsAt.AddMinutes(-BookingRules.CheckInEarlyMinutes) ? $"Check-in opens {BookingRules.CheckInEarlyMinutes} minutes before the start." : "Check-in has closed.");
        b.CheckedInAt = clock.UtcNow; b.UpdatedAt = b.CheckedInAt.Value; b.Version++;
        audit.Record("booking.check_in", "booking", b.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["resource"] = r.Name });
        await db.SaveChangesAsync(ct);
        return await GetAsync(b.Id, actor, ct);
    }

    /// <summary>Cancels the booking, or with <c>scope=following</c> it and the later live bookings of its series; returns the booking asked about.</summary>
    public async Task<BookingItem> CancelAsync(Guid id, CancelBookingRequest req, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var v = await ViewerAsync(actor, ct);
        var b = await db.Bookings.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Booking");
        var mine = b.BookedForId == v.UserId || b.CreatedById == v.UserId;
        if (!mine && !v.Manage) throw (v.ReadAll ? DomainException.Denied(Permissions.BookingsManage) : DomainException.NotFound("Booking"));
        if (!b.HoldsSlot) throw DomainException.Conflict($"This booking is already {StatusName(b.Status)}.");
        var now = clock.UtcNow;
        if (b.EndsAt <= now) throw DomainException.Conflict("This booking is already over.");
        var reason = Clean(req.Reason);
        if (reason is { Length: > 500 }) throw Invalid(new Dictionary<string, string> { ["reason"] = "Use at most 500 characters." });
        if (!mine && reason is null) throw Invalid(new Dictionary<string, string> { ["reason"] = "Tell the person why their booking is cancelled." });
        var scope = (req.Scope ?? "this").Trim().ToLowerInvariant();
        if (scope is not ("this" or "following")) throw Invalid(new Dictionary<string, string> { ["scope"] = "Use this or following." });
        var targets = scope == "following" && b.SeriesId is { } sid
            ? await db.Bookings.Where(x => x.SeriesId == sid && x.StartsAt >= b.StartsAt && x.EndsAt > now && (x.Status == BookingStatus.Confirmed || x.Status == BookingStatus.Pending)).OrderBy(x => x.StartsAt).ToListAsync(ct)
            : [b];
        foreach (var t in targets) { t.Status = BookingStatus.Cancelled; t.CancelledAt = now; t.CancelledById = v.UserId; t.CancelReason = reason; t.UpdatedAt = now; t.Version++; }
        var r = await db.BookableResources.AsNoTracking().FirstAsync(x => x.Id == b.ResourceId, ct);
        audit.Record("booking.cancel", "booking", b.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email,
            new Dictionary<string, string> { ["resource"] = r.Name, ["starts"] = b.StartsAt.ToString("O"), ["count"] = targets.Count.ToString(), ["scope"] = scope });
        if (b.BookedForId != v.UserId)
            await TellAsync(b.BookedForId, "booking.changed", targets.Count > 1 ? $"{targets.Count} bookings of {r.Name} were cancelled" : $"Your booking of {r.Name} was cancelled",
                $"{b.Title} · {await LocalAsync(r, b.StartsAt, ct)}" + (reason is null ? "" : $" — {reason}"), ct);
        await db.SaveChangesAsync(ct);
        var names = await NamesAsync([b.BookedForId], ct);
        return ToItem(b, r, v, names);
    }

    // ================================================================ background run: reminders, no-shows, late approvals
    public sealed record RunResult(int Reminded, int Released, int Expired);

    /// <summary>For every organisation: reminds people 30 minutes before their booking starts, releases bookings nobody checked in to in time, and refuses
    /// bookings still waiting for approval when they start. Each item is claimed in one UPDATE, so several instances never act twice.</summary>
    public async Task<RunResult> RunAsync(CancellationToken ct = default)
    {
        var now = clock.UtcNow; var soon = now.AddMinutes(BookingRules.ReminderMinutes);
        var orgs = await db.Bookings.IgnoreQueryFilters().AsNoTracking()
            .Where(b => (b.Status == BookingStatus.Confirmed && ((b.RemindedAt == null && b.StartsAt > now && b.StartsAt <= soon) || (b.CheckedInAt == null && b.StartsAt <= now && b.EndsAt > now)))
                        || (b.Status == BookingStatus.Pending && b.StartsAt <= now))
            .Select(b => b.TenantId).Distinct().ToListAsync(ct);
        int reminded = 0, released = 0, expired = 0;
        foreach (var org in orgs)
        {
            using (tenant.Switch(org))
            {
                db.ResetTracking();
                // reminders
                var due = await db.Bookings.AsNoTracking().Where(b => b.Status == BookingStatus.Confirmed && b.RemindedAt == null && b.StartsAt > now && b.StartsAt <= soon).Take(500).ToListAsync(ct);
                foreach (var b in due)
                {
                    if (await db.Bookings.Where(x => x.Id == b.Id && x.RemindedAt == null).ExecuteUpdateAsync(s => s.SetProperty(x => x.RemindedAt, now), ct) == 0) continue;
                    var r = await db.BookableResources.AsNoTracking().FirstAsync(x => x.Id == b.ResourceId, ct);
                    var checkIn = r.CheckInMinutes is { } m ? $" Check in within {m} minutes of the start or it is released." : "";
                    await TellAsync(b.BookedForId, "booking.reminder", $"{r.Name} at {TimeZoneInfo.ConvertTime(b.StartsAt, await ZoneOfAsync(r, ct)):HH:mm}", $"{b.Title}.{checkIn}", ct);
                    await db.SaveChangesAsync(ct); reminded++;
                }
                // no-shows: released when check-in closed without anyone checking in
                var resources = await db.BookableResources.AsNoTracking().Where(r => r.CheckInMinutes != null).ToDictionaryAsync(r => r.Id, ct);
                if (resources.Count > 0)
                {
                    var ids = resources.Keys.ToList();
                    var open = await db.Bookings.AsNoTracking().Where(b => ids.Contains(b.ResourceId) && b.Status == BookingStatus.Confirmed && b.CheckedInAt == null && b.StartsAt <= now && b.EndsAt > now).Take(500).ToListAsync(ct);
                    foreach (var b in open.Where(b => b.StartsAt.AddMinutes(resources[b.ResourceId].CheckInMinutes!.Value) <= now))
                    {
                        if (await db.Bookings.Where(x => x.Id == b.Id && x.Status == BookingStatus.Confirmed && x.CheckedInAt == null)
                                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, BookingStatus.Released).SetProperty(x => x.UpdatedAt, now).SetProperty(x => x.Version, x => x.Version + 1), ct) == 0) continue;
                        var r = resources[b.ResourceId];
                        audit.Record("booking.release", "booking", b.Id.ToString(), Outcome.Success, Client, null, "system (bookings)", new Dictionary<string, string> { ["resource"] = r.Name, ["starts"] = b.StartsAt.ToString("O") });
                        await TellAsync(b.BookedForId, "booking.changed", $"{r.Name} was released", $"{b.Title}: nobody checked in within {r.CheckInMinutes} minutes, so the room is free for others.", ct);
                        await db.SaveChangesAsync(ct); released++;
                    }
                }
                // approvals that came too late
                var late = await db.Bookings.AsNoTracking().Where(b => b.Status == BookingStatus.Pending && b.StartsAt <= now).Take(500).ToListAsync(ct);
                foreach (var b in late)
                {
                    if (await db.Bookings.Where(x => x.Id == b.Id && x.Status == BookingStatus.Pending)
                            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, BookingStatus.Rejected).SetProperty(x => x.DecidedAt, now).SetProperty(x => x.DecisionNote, "Not approved before it started.")
                                .SetProperty(x => x.UpdatedAt, now).SetProperty(x => x.Version, x => x.Version + 1), ct) == 0) continue;
                    var r = await db.BookableResources.AsNoTracking().FirstAsync(x => x.Id == b.ResourceId, ct);
                    audit.Record("booking.expire", "booking", b.Id.ToString(), Outcome.Success, Client, null, "system (bookings)", new Dictionary<string, string> { ["resource"] = r.Name });
                    await TellAsync(b.BookedForId, "booking.decided", $"Your booking of {r.Name} was not approved in time", $"{b.Title}: nobody approved it before it started.", ct);
                    await db.SaveChangesAsync(ct); expired++;
                }
            }
        }
        return new RunResult(reminded, released, expired);
    }
    private static readonly ClientInfo Client = new(null, "background", null);

    // ================================================================ calendar feed (ICS)
    public static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
    private string FeedUrl(string token) => $"{links.Origin.TrimEnd('/')}/api/calendar/{token}.ics";

    public async Task<BookingFeedInfo> FeedInfoAsync(SessionPrincipal p, CancellationToken ct = default)
    {
        var f = await db.BookingFeeds.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == p.UserId, ct);
        return new BookingFeedInfo(f is not null, f?.CreatedAt, f?.LastReadAt, null);
    }

    /// <summary>Makes a new secret feed link (the old one stops working). The link is returned once; only its hash is kept.</summary>
    public async Task<BookingFeedInfo> NewFeedAsync(SessionPrincipal p, ClientInfo client, CancellationToken ct = default)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var now = clock.UtcNow;
        var f = await db.BookingFeeds.FirstOrDefaultAsync(x => x.UserId == p.UserId, ct);
        if (f is null) { f = new BookingFeed { UserId = p.UserId }; db.BookingFeeds.Add(f); }
        f.TokenHash = HashToken(token); f.CreatedAt = now; f.LastReadAt = null;
        audit.Record("booking.feed_create", "user", p.UserId.ToString(), Outcome.Success, client, p.UserId, p.Email);
        await db.SaveChangesAsync(ct);
        return new BookingFeedInfo(true, now, null, FeedUrl(token));
    }

    public async Task<BookingFeedInfo> RevokeFeedAsync(SessionPrincipal p, ClientInfo client, CancellationToken ct = default)
    {
        var f = await db.BookingFeeds.FirstOrDefaultAsync(x => x.UserId == p.UserId, ct);
        if (f is not null) { db.BookingFeeds.Remove(f); audit.Record("booking.feed_revoke", "user", p.UserId.ToString(), Outcome.Success, client, p.UserId, p.Email); await db.SaveChangesAsync(ct); }
        return new BookingFeedInfo(false, null, null, null);
    }

    /// <summary>The person's bookings from 30 days ago to 180 days ahead as an iCalendar file. The secret token is the credential; an unknown token is a 404 and a
    /// suspended or closed account gets nothing.</summary>
    public async Task<string> FeedAsync(string token, CancellationToken ct = default)
    {
        token = (token ?? "").Trim();
        if (token.EndsWith(".ics", StringComparison.OrdinalIgnoreCase)) token = token[..^4];
        if (token.Length is < 20 or > 100) throw DomainException.NotFound("Calendar");
        var hash = HashToken(token);
        var f = await db.BookingFeeds.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(x => x.TokenHash == hash, ct) ?? throw DomainException.NotFound("Calendar");
        using (tenant.Switch(f.TenantId))
        {
            if (!await db.Users.AnyAsync(u => u.Id == f.UserId && u.Status == UserStatus.Active, ct)) throw DomainException.NotFound("Calendar");
            var now = clock.UtcNow;
            await db.BookingFeeds.Where(x => x.UserId == f.UserId).ExecuteUpdateAsync(s => s.SetProperty(x => x.LastReadAt, now), ct);
            var rows = await db.Bookings.AsNoTracking().Where(b => b.BookedForId == f.UserId && Live.Contains(b.Status) && b.EndsAt > now.AddDays(-30) && b.StartsAt < now.AddDays(180))
                .OrderBy(b => b.StartsAt).Take(1000).ToListAsync(ct);
            var rids = rows.Select(b => b.ResourceId).Distinct().ToList();
            var resources = await db.BookableResources.AsNoTracking().Where(r => rids.Contains(r.Id)).ToDictionaryAsync(r => r.Id, ct);
            var places = await PlacesAsync(ct);
            var sb = new StringBuilder();
            void Line(string s) => sb.Append(Fold(s)).Append("\r\n");
            Line("BEGIN:VCALENDAR"); Line("VERSION:2.0"); Line("PRODID:-//Annaba Helpdesk//Bookings//EN"); Line("CALSCALE:GREGORIAN"); Line("METHOD:PUBLISH");
            Line("X-WR-CALNAME:" + Ics("My bookings"));
            foreach (var b in rows)
            {
                var r = resources[b.ResourceId];
                Line("BEGIN:VEVENT");
                Line($"UID:{b.Id}@annaba-helpdesk");
                Line($"DTSTAMP:{b.UpdatedAt.UtcDateTime:yyyyMMdd'T'HHmmss'Z'}");
                Line($"DTSTART:{b.StartsAt.UtcDateTime:yyyyMMdd'T'HHmmss'Z'}");
                Line($"DTEND:{b.EndsAt.UtcDateTime:yyyyMMdd'T'HHmmss'Z'}");
                Line("SUMMARY:" + Ics($"{b.Title} · {r.Name}"));
                if (r.LocationId is { } l && places.ContainsKey(l)) Line("LOCATION:" + Ics(PathOf(places, l)));
                Line("STATUS:" + (b.Status == BookingStatus.Pending ? "TENTATIVE" : "CONFIRMED"));
                if (b.Note is not null) Line("DESCRIPTION:" + Ics(b.Note));
                Line("URL:" + links.Origin.TrimEnd('/') + "/bookings");
                Line("END:VEVENT");
            }
            Line("END:VCALENDAR");
            return sb.ToString();
        }
    }
    /// <summary>RFC 5545 text escaping.</summary>
    private static string Ics(string s) => s.Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,").Replace("\r\n", "\\n").Replace("\n", "\\n").Replace("\r", "");
    /// <summary>RFC 5545 line folding at 75 octets.</summary>
    private static string Fold(string s)
    {
        var bytes = Encoding.UTF8.GetBytes(s);
        if (bytes.Length <= 75) return s;
        var sb = new StringBuilder(); var used = 0;
        foreach (var rune in s.EnumerateRunes())
        {
            var n = rune.Utf8SequenceLength;
            if (used + n > 75) { sb.Append("\r\n "); used = 1; }   // a continuation line starts with one space
            sb.Append(rune.ToString()); used += n;
        }
        return sb.ToString();
    }

    // ================================================================ summary (KPI strip)
    /// <summary>Resources, bookings today (in the organisation's time zone), what is in use and free right now, the viewer's upcoming bookings, room and vehicle
    /// utilisation over the last 7 days (booked hours inside opening hours ÷ opening hours), bookings per day for the next week, the busiest resources, bookings
    /// waiting for approval (for people who approve) and no-shows released in the last 7 days.</summary>
    public async Task<BookingSummary> SummaryAsync(SessionPrincipal p, CancellationToken ct = default)
    {
        var v = await ViewerAsync(p, ct);
        var now = clock.UtcNow;
        var places = await PlacesAsync(ct);
        var fallback = await DefaultZoneAsync(ct);
        var zone = Zone(fallback);
        var localToday = TimeZoneInfo.ConvertTime(now, zone).Date;
        var dayStart = new DateTimeOffset(localToday, zone.GetUtcOffset(localToday)).ToUniversalTime();
        var resources = await db.BookableResources.AsNoTracking().Where(r => r.ArchivedAt == null).ToListAsync(ct);
        var weekAgo = now.AddDays(-7); var weekAhead = dayStart.AddDays(7);
        var bookings = await db.Bookings.AsNoTracking().Where(b => b.Status == BookingStatus.Confirmed && b.StartsAt < weekAhead && b.EndsAt > weekAgo)
            .Select(b => new { b.ResourceId, b.StartsAt, b.EndsAt, b.BlockedUntil, b.BookedForId, b.CreatedById }).Take(50_000).ToListAsync(ct);
        var live = resources.Select(r => r.Id).ToHashSet();
        bookings = [.. bookings.Where(b => live.Contains(b.ResourceId))];
        var inUse = bookings.Where(b => b.StartsAt <= now && b.EndsAt > now).Select(b => b.ResourceId).Distinct().Count();
        var today = bookings.Count(b => b.StartsAt >= dayStart && b.StartsAt < dayStart.AddDays(1));
        var mine = await db.Bookings.CountAsync(b => Live.Contains(b.Status) && b.EndsAt > now && (b.BookedForId == v.UserId || b.CreatedById == v.UserId), ct);
        var pending = v.Manage ? await db.Bookings.CountAsync(b => b.Status == BookingStatus.Pending && b.EndsAt > now, ct) : 0;
        var noShows = await db.Bookings.CountAsync(b => b.Status == BookingStatus.Released && b.StartsAt >= weekAgo, ct);

        double open = 0, booked = 0;
        foreach (var r in resources.Where(r => r.Kind is ResourceKind.Room or ResourceKind.Vehicle))
        {
            var rz = Zone(ZoneId(places, r.LocationId, fallback));
            foreach (var (ws, we) in OpenWindows(r, weekAgo, now, rz))
            {
                open += (we - ws).TotalHours;
                foreach (var b in bookings.Where(b => b.ResourceId == r.Id && b.StartsAt < we && b.EndsAt > ws))
                    booked += ((b.EndsAt < we ? b.EndsAt : we) - (b.StartsAt > ws ? b.StartsAt : ws)).TotalHours;
            }
        }
        var next7 = Enumerable.Range(0, 7).Select(i => dayStart.AddDays(i)).Select(d => new DayCount(TimeZoneInfo.ConvertTime(d, zone).ToString("yyyy-MM-dd"), bookings.Count(b => b.StartsAt >= d && b.StartsAt < d.AddDays(1)))).ToList();
        var busiest = bookings.Where(b => b.StartsAt >= weekAgo && b.StartsAt < now).GroupBy(b => b.ResourceId).Select(g => new { g.Key, N = g.Count() }).OrderByDescending(x => x.N).Take(5)
            .Select(x => new CountItem(x.Key.ToString(), resources.First(r => r.Id == x.Key).Name, x.N)).ToList();
        return new BookingSummary(resources.Count, today, inUse, resources.Count - inUse, mine, open <= 0 ? null : (int)Math.Round(100 * booked / open),
            [.. Enum.GetValues<ResourceKind>().Select(k => new CountItem(KindName(k), KindName(k), resources.Count(r => r.Kind == k)))], next7, busiest, pending, noShows);
    }

    // ================================================================ helpers
    private static string Hm(int minutes) => minutes % 1440 == 0 ? $"{minutes / 1440} day(s)" : minutes % 60 == 0 ? $"{minutes / 60} h" : $"{minutes / 60} h {minutes % 60} min";

    /// <summary>Why [start, end) is outside the resource's opening days or hours (in its own time zone), or null when it fits.</summary>
    public static string? OutsideHours(BookableResource r, DateTimeOffset start, DateTimeOffset end, TimeZoneInfo zone)
    {
        var ls = TimeZoneInfo.ConvertTime(start, zone); var le = TimeZoneInfo.ConvertTime(end, zone);
        if (r.OpenFrom is { } from && r.OpenUntil is { } until)
        {
            if (ls.Date != le.Date) return $"It can be booked from {from:HH\\:mm} to {until:HH\\:mm}, within one day.";
            if (!BookingRules.OpenOn(r.OpenDays, ls.DayOfWeek)) return "It cannot be booked on this day.";
            if (TimeOnly.FromTimeSpan(ls.TimeOfDay) < from || TimeOnly.FromTimeSpan(le.TimeOfDay) > until) return $"It can be booked from {from:HH\\:mm} to {until:HH\\:mm}.";
            return null;
        }
        for (var d = ls.Date; d <= le.AddTicks(-1).Date; d = d.AddDays(1))
            if (!BookingRules.OpenOn(r.OpenDays, d.DayOfWeek)) return "It cannot be booked on one of these days.";
        return null;
    }

    /// <summary>The resource's opening windows (UTC) between two instants.</summary>
    public static IEnumerable<(DateTimeOffset Start, DateTimeOffset End)> OpenWindows(BookableResource r, DateTimeOffset from, DateTimeOffset to, TimeZoneInfo zone)
    {
        var first = TimeZoneInfo.ConvertTime(from, zone).Date; var last = TimeZoneInfo.ConvertTime(to, zone).Date;
        for (var d = first; d <= last; d = d.AddDays(1))
        {
            if (!BookingRules.OpenOn(r.OpenDays, d.DayOfWeek)) continue;
            var ls = r.OpenFrom is { } f ? d + f.ToTimeSpan() : d; var le = r.OpenUntil is { } u ? d + u.ToTimeSpan() : d.AddDays(1);
            var ws = new DateTimeOffset(ls, zone.GetUtcOffset(ls)); var we = new DateTimeOffset(le, zone.GetUtcOffset(le));
            if (ws < from) ws = from; if (we > to) we = to;
            if (we > ws) yield return (ws, we);
        }
    }

    private static DomainException Taken(BookableResource r, DateTimeOffset from, DateTimeOffset until, TimeZoneInfo zone)
        => DomainException.Conflict(FormattableString.Invariant($"{r.Name} is already taken from {TimeZoneInfo.ConvertTime(from, zone):ddd d MMM HH:mm} to {TimeZoneInfo.ConvertTime(until, zone):HH:mm}. Choose another time or resource."));

    private async Task TellAsync(Guid userId, string kind, string title, string body, CancellationToken ct)
        => await notifications.SendAsync(userId, kind, title, body, "/bookings", null, (to, name) => Email.EmailTemplates.Notification(to, name, title, body, links.Origin.TrimEnd('/') + "/bookings"), ct: ct);

    /// <summary>Everyone active who may approve (holds <c>bookings.manage</c>), at most 20, except the person who booked.</summary>
    private async Task TellApproversAsync(BookableResource r, string title, string when, Guid except, CancellationToken ct)
    {
        var people = await db.Users.AsNoTracking().Where(u => u.Status == UserStatus.Active && u.Id != except
                && u.UserRoles.Any(ur => ur.Role!.ArchivedAt == null && ur.Role.Permissions.Any(p => p.PermissionKey == Permissions.BookingsManage)))
            .OrderBy(u => u.Id).Select(u => u.Id).Take(MaxApproverAlerts).ToListAsync(ct);
        foreach (var uid in people) await TellAsync(uid, "booking.approval", $"{r.Name} needs your approval", $"{title} · {when}", ct);
    }
}
