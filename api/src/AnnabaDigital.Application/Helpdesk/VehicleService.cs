using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Application.Helpdesk;

public sealed record ChecklistAnswer(string Item, bool Ok);
public sealed record TripItem(Guid BookingId, Guid ResourceId, string ResourceName, string? Title, DateTimeOffset StartsAt, DateTimeOffset EndsAt, string BookingStatus,
    bool NeedsDriver, Guid? DriverId, string? Driver, string? DriverLicence, string? Destination, string? Purpose,
    int? OdometerOut, DateTimeOffset? PickedUpAt, int? OdometerIn, DateTimeOffset? ReturnedAt, int? Distance, decimal? FuelCost, decimal? TollCost, decimal? ParkingCost, decimal Cost,
    string? DamageNotes, IReadOnlyList<ChecklistAnswer> Checklist, bool CanEdit, bool CanAssignDriver, bool CanPickUp, bool CanReturn, int? LastOdometer, int Version);
public sealed record SaveTripRequest(string? Destination, string? Purpose, bool? NeedsDriver, Guid? DriverId, int? Version = null);
public sealed record PickUpRequest(int? OdometerOut);
public sealed record ReturnRequest(int? OdometerIn, decimal? FuelCost, decimal? TollCost, decimal? ParkingCost, string? DamageNotes, Dictionary<string, bool>? Checklist);
public sealed record ResourceDocumentItem(Guid Id, Guid ResourceId, string Kind, string? Number, DateOnly ExpiresOn, string? Note, string State);
public sealed record SaveResourceDocumentRequest(string? Kind, string? Number, DateOnly? ExpiresOn, string? Note);
public sealed record LicenceItem(Guid UserId, string Name, string Email, string Number, string? Class, DateOnly ExpiresOn, string State);
public sealed record SaveLicenceRequest(string? Number, string? Class, DateOnly? ExpiresOn);
public sealed record AddLicenceRequest(string? Email, string? Number, string? Class, DateOnly? ExpiresOn);
public sealed record DriverOption(Guid UserId, string Name, string? Class, DateOnly ExpiresOn, string State);
public sealed record UtilisationRow(Guid ResourceId, string Name, string Kind, string? Place, double OpenHours, double BookedHours, int? Percent, int Bookings, int NoShows, int Cancelled,
    int Trips, int Km, decimal TripCost);
public sealed record UtilisationReport(int Days, DateTimeOffset From, DateTimeOffset To, string? Kind, IReadOnlyList<UtilisationRow> Rows, UtilisationRow Total);

/// <summary>
/// BKG-004 and RPT-005 (rooms and vehicles). The trip of a vehicle booking (driver, destination, odometer, fuel, toll and parking costs, damage, return checklist),
/// vehicle documents and driver licences with expiry reminders, and the utilisation report. The people a booking is for (or by) and its driver handle the trip;
/// <c>bookings.manage</c> assigns drivers and handles any trip; <c>resources.manage</c> keeps documents and licences; <c>bookings.read_all</c> reads the report.
/// </summary>
public sealed class VehicleService(IAppDb db, IClock clock, AuditWriter audit, AuthorizationService authz, NotificationService notifications, IAppLinks links, ITenantContext tenant)
{
    private static string? Clean(string? s) { var t = (s ?? "").Trim(); return t.Length == 0 ? null : t; }
    private static DomainException Invalid(Dictionary<string, string> e) => DomainException.Validation("Please correct the highlighted fields.", e);
    public static string KindName(ResourceDocumentKind k) => k switch
    {
        ResourceDocumentKind.Registration => "registration", ResourceDocumentKind.Insurance => "insurance", ResourceDocumentKind.Inspection => "inspection", ResourceDocumentKind.Service => "service", _ => "other",
    };
    public static ResourceDocumentKind? ParseKind(string? s) => (s ?? "").Trim().ToLowerInvariant() switch
    {
        "registration" => ResourceDocumentKind.Registration, "insurance" => ResourceDocumentKind.Insurance, "inspection" => ResourceDocumentKind.Inspection,
        "service" => ResourceDocumentKind.Service, "other" => ResourceDocumentKind.Other, _ => null,
    };
    private DateOnly Today => DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
    private string State(DateOnly expires) => expires < Today ? "expired" : expires <= Today.AddDays(VehicleRules.NoticeDays) ? "soon" : "ok";

    // ================================================================ trips
    private sealed record Who(Guid UserId, bool Manage);
    private async Task<Who> WhoAsync(SessionPrincipal p, CancellationToken ct) => new(p.UserId, (await authz.GetEffectivePermissionsAsync(p.UserId, ct)).Contains(Permissions.BookingsManage));

    private async Task<(Booking Booking, BookableResource Resource)> VehicleBookingAsync(Guid bookingId, Who w, bool tracked, CancellationToken ct)
    {
        var q = tracked ? db.Bookings : db.Bookings.AsNoTracking();
        var b = await q.FirstOrDefaultAsync(x => x.Id == bookingId, ct) ?? throw DomainException.NotFound("Booking");
        var trip = await db.VehicleTrips.AsNoTracking().FirstOrDefaultAsync(t => t.BookingId == bookingId, ct);
        var involved = b.BookedForId == w.UserId || b.CreatedById == w.UserId || trip?.DriverId == w.UserId;
        if (!involved && !w.Manage) throw DomainException.NotFound("Booking");
        var r = await db.BookableResources.AsNoTracking().FirstAsync(x => x.Id == b.ResourceId, ct);
        if (r.Kind != ResourceKind.Vehicle) throw DomainException.Conflict("Only vehicle bookings have a trip.");
        return (b, r);
    }

    /// <summary>The trip row of the booking, created on first use. Two requests creating it at once (a double click, or saving and picking up together) both end
    /// up with the one row: the loser of the insert re-reads the winner's.</summary>
    private async Task<VehicleTrip> TripOfAsync(Booking b, CancellationToken ct)
    {
        var t = await db.VehicleTrips.FirstOrDefaultAsync(x => x.BookingId == b.Id, ct);
        if (t is not null) return t;
        db.VehicleTrips.Add(new VehicleTrip { BookingId = b.Id, ResourceId = b.ResourceId, CreatedAt = clock.UtcNow, UpdatedAt = clock.UtcNow });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { db.ResetTracking(); }
        return await db.VehicleTrips.FirstAsync(x => x.BookingId == b.Id, ct);
    }

    private async Task CommitTripAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw DomainException.Conflict("The trip was changed at the same moment. Reload and try again."); }
    }

    private async Task<int?> LastOdometerAsync(Guid resourceId, Guid exceptBooking, CancellationToken ct)
        => await db.VehicleTrips.AsNoTracking().Where(t => t.ResourceId == resourceId && t.BookingId != exceptBooking && t.OdometerIn != null).MaxAsync(t => t.OdometerIn, ct);

    public async Task<TripItem> GetTripAsync(Guid bookingId, SessionPrincipal p, CancellationToken ct = default)
    {
        var w = await WhoAsync(p, ct);
        var (b, r) = await VehicleBookingAsync(bookingId, w, false, ct);
        var t = await db.VehicleTrips.AsNoTracking().FirstOrDefaultAsync(x => x.BookingId == bookingId, ct) ?? new VehicleTrip { BookingId = b.Id, ResourceId = r.Id };
        var driverId = t.DriverId ?? b.BookedForId;
        var names = await db.Users.AsNoTracking().Where(u => u.Id == driverId).Select(u => u.Name).FirstOrDefaultAsync(ct);
        var licence = await db.DriverLicences.AsNoTracking().FirstOrDefaultAsync(l => l.UserId == driverId, ct);
        var checklist = JsonSerializer.Deserialize<List<ChecklistAnswer>>(t.ChecklistJson, Json) ?? [];
        var now = clock.UtcNow;
        var live = b.Status == BookingStatus.Confirmed;
        var involved = b.BookedForId == w.UserId || b.CreatedById == w.UserId || t.DriverId == w.UserId;
        var may = involved || w.Manage;
        var cost = (t.FuelCost ?? 0) + (t.TollCost ?? 0) + (t.ParkingCost ?? 0);
        return new TripItem(b.Id, r.Id, r.Name, b.Title, b.StartsAt, b.EndsAt, BookingService.StatusName(b.Status),
            t.DriverId is null && t.NeedsDriver, t.DriverId, t.DriverId is null && t.NeedsDriver ? null : names, licence is null ? null : $"{licence.Number} · {State(licence.ExpiresOn)}",
            t.Destination, t.Purpose, t.OdometerOut, t.PickedUpAt, t.OdometerIn, t.ReturnedAt, t.OdometerIn - t.OdometerOut, t.FuelCost, t.TollCost, t.ParkingCost, cost, t.DamageNotes, checklist,
            may && t.ReturnedAt is null && (b.HoldsSlot), w.Manage && t.ReturnedAt is null && b.HoldsSlot,
            may && live && t.PickedUpAt is null && now >= b.StartsAt.AddMinutes(-30) && now < b.EndsAt,
            may && live && t.PickedUpAt is not null && t.ReturnedAt is null,
            await LastOdometerAsync(r.Id, b.Id, ct), t.Version);
    }
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<TripItem> SaveTripAsync(Guid bookingId, SaveTripRequest req, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var w = await WhoAsync(actor, ct);
        var (b, r) = await VehicleBookingAsync(bookingId, w, false, ct);
        if (!b.HoldsSlot) throw DomainException.Conflict($"This booking is {BookingService.StatusName(b.Status)}.");
        var t = await TripOfAsync(b, ct);
        if (t.ReturnedAt is not null) throw DomainException.Conflict("The vehicle is already back; the trip can no longer change.");
        if (req.Version is { } v && v != t.Version && t.CreatedAt != t.UpdatedAt) throw DomainException.Conflict("This trip was changed by someone else. Reload and try again.");
        var e = new Dictionary<string, string>();
        var dest = Clean(req.Destination); var purpose = Clean(req.Purpose);
        if (dest is { Length: > 200 }) e["destination"] = "Use at most 200 characters.";
        if (purpose is { Length: > 300 }) e["purpose"] = "Use at most 300 characters.";
        if (req.DriverId is { } d && d != t.DriverId)
        {
            if (!w.Manage && d != w.UserId) e["driverId"] = "Only General Affairs assigns another driver.";
            else if (!await db.Users.AnyAsync(u => u.Id == d && u.Status == UserStatus.Active, ct)) e["driverId"] = "Choose an active person.";
            else if (await db.DriverLicences.AsNoTracking().FirstOrDefaultAsync(l => l.UserId == d, ct) is { } lic && lic.ExpiresOn < DateOnly.FromDateTime(b.StartsAt.UtcDateTime))
                e["driverId"] = FormattableString.Invariant($"Their driving licence expired on {lic.ExpiresOn:d MMM yyyy}.");
        }
        if (e.Count > 0) throw Invalid(e);
        var wantsDriver = req.NeedsDriver == true && (req.DriverId ?? t.DriverId) is null && !t.NeedsDriver;   // tell General Affairs once
        t.Destination = dest; t.Purpose = purpose;
        if (req.DriverId is { } nd) t.DriverId = nd;
        t.NeedsDriver = t.DriverId is null && (req.NeedsDriver ?? t.NeedsDriver);
        t.UpdatedAt = clock.UtcNow; t.Version++;
        audit.Record("trip.update", "booking", b.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["vehicle"] = r.Name, ["driver"] = t.DriverId?.ToString() ?? (t.NeedsDriver ? "requested" : "self") });
        if (req.DriverId is { } assigned && assigned != actor.UserId)
            await notifications.SendAsync(assigned, "booking.changed", $"You drive {r.Name}", $"{b.Title} · {dest ?? "—"}", "/bookings", null, ct: ct);
        if (wantsDriver) await TellManagersAsync($"{r.Name} needs a driver", $"{b.Title} · {dest ?? "—"}", actor.UserId, ct);
        await CommitTripAsync(ct);
        return await GetTripAsync(b.Id, actor, ct);
    }

    public async Task<TripItem> PickUpAsync(Guid bookingId, PickUpRequest req, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var w = await WhoAsync(actor, ct);
        var (b, r) = await VehicleBookingAsync(bookingId, w, false, ct);
        var now = clock.UtcNow;
        if (b.Status != BookingStatus.Confirmed) throw DomainException.Conflict($"This booking is {BookingService.StatusName(b.Status)}.");
        if (now < b.StartsAt.AddMinutes(-30)) throw DomainException.Conflict("The vehicle can be picked up from 30 minutes before the booking.");
        if (now >= b.EndsAt) throw DomainException.Conflict("This booking is already over.");
        var t = await TripOfAsync(b, ct);
        if (t.PickedUpAt is not null) throw DomainException.Conflict("Already picked up.");
        var last = await LastOdometerAsync(r.Id, b.Id, ct);
        if (req.OdometerOut is not { } km || km < 0 || km > VehicleRules.MaxOdometer) throw Invalid(new Dictionary<string, string> { ["odometerOut"] = "Read the odometer (km)." });
        if (last is { } l && km < l) throw Invalid(new Dictionary<string, string> { ["odometerOut"] = $"It was {l.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)} km when it last came back. Check the reading." });
        t.OdometerOut = km; t.PickedUpAt = now; t.PickedUpById = actor.UserId; t.UpdatedAt = now; t.Version++;
        if (b.CheckedInAt is null) { var tb = await db.Bookings.FirstAsync(x => x.Id == b.Id, ct); tb.CheckedInAt = now; tb.UpdatedAt = now; tb.Version++; }   // picking up counts as checking in
        audit.Record("trip.pick_up", "booking", b.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["vehicle"] = r.Name, ["odometer"] = km.ToString() });
        await CommitTripAsync(ct);
        return await GetTripAsync(b.Id, actor, ct);
    }

    /// <summary>Returns the vehicle: the odometer, the costs paid, the checklist and any damage. Returning early ends the booking now (on a 5-minute step), so the car is free again.</summary>
    public async Task<TripItem> ReturnAsync(Guid bookingId, ReturnRequest req, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var w = await WhoAsync(actor, ct);
        var (b, r) = await VehicleBookingAsync(bookingId, w, false, ct);
        var t = await TripOfAsync(b, ct);
        if (t.PickedUpAt is null) throw DomainException.Conflict("Record the pick-up first.");
        if (t.ReturnedAt is not null) throw DomainException.Conflict("Already returned.");
        var e = new Dictionary<string, string>();
        if (req.OdometerIn is not { } km || km > VehicleRules.MaxOdometer) e["odometerIn"] = "Read the odometer (km).";
        else if (km < t.OdometerOut) e["odometerIn"] = $"It was {t.OdometerOut?.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)} km at pick-up.";
        foreach (var (key, val) in new[] { ("fuelCost", req.FuelCost), ("tollCost", req.TollCost), ("parkingCost", req.ParkingCost) })
            if (val is < 0 || val > VehicleRules.MaxCost) e[key] = "Use an amount of 0 or more.";
        var answers = VehicleRules.ReturnChecklist.Select(i => new ChecklistAnswer(i, req.Checklist?.GetValueOrDefault(i) ?? false)).ToList();
        if (req.Checklist is null || VehicleRules.ReturnChecklist.Any(i => !req.Checklist.ContainsKey(i))) e["checklist"] = "Answer every item of the checklist.";
        var damage = Clean(req.DamageNotes);
        if (damage is { Length: > 2000 }) e["damageNotes"] = "Use at most 2000 characters.";
        if (answers.Any(a => a.Item == "no_new_damage" && !a.Ok) && damage is null) e["damageNotes"] = "Describe the damage.";
        if (e.Count > 0) throw Invalid(e);
        var now = clock.UtcNow;
        t.OdometerIn = req.OdometerIn; t.FuelCost = req.FuelCost; t.TollCost = req.TollCost; t.ParkingCost = req.ParkingCost; t.DamageNotes = damage;
        t.ChecklistJson = JsonSerializer.Serialize(answers, Json); t.ReturnedAt = now; t.ReturnedById = actor.UserId; t.UpdatedAt = now; t.Version++;
        if (now < b.EndsAt)
        {
            var tb = await db.Bookings.FirstAsync(x => x.Id == b.Id, ct);
            var end = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, TimeSpan.Zero);
            end = end.AddMinutes((BookingRules.StepMinutes - end.Minute % BookingRules.StepMinutes) % BookingRules.StepMinutes);
            if (end <= tb.StartsAt) end = tb.StartsAt.AddMinutes(BookingRules.StepMinutes);
            if (end < tb.EndsAt) { tb.EndsAt = end; tb.BlockedUntil = end.AddMinutes(r.BufferMinutes); tb.UpdatedAt = now; tb.Version++; }
        }
        audit.Record("trip.return", "booking", b.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string>
        {
            ["vehicle"] = r.Name, ["km"] = (req.OdometerIn - t.OdometerOut).ToString()!, ["cost"] = ((req.FuelCost ?? 0) + (req.TollCost ?? 0) + (req.ParkingCost ?? 0)).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["damage"] = damage is null ? "no" : "yes",
        });
        if (damage is not null) await TellManagersAsync($"Damage reported on {r.Name}", damage, actor.UserId, ct);
        await CommitTripAsync(ct);
        return await GetTripAsync(b.Id, actor, ct);
    }

    private async Task TellManagersAsync(string title, string body, Guid except, CancellationToken ct)
    {
        var people = await db.Users.AsNoTracking().Where(u => u.Status == UserStatus.Active && u.Id != except
                && u.UserRoles.Any(ur => ur.Role!.ArchivedAt == null && ur.Role.Permissions.Any(p => p.PermissionKey == Permissions.BookingsManage)))
            .OrderBy(u => u.Id).Select(u => u.Id).Take(20).ToListAsync(ct);
        foreach (var uid in people) await notifications.SendAsync(uid, "vehicle.alert", title, body, "/bookings", null, (to, name) => Email.EmailTemplates.Notification(to, name, title, body, links.Origin.TrimEnd('/') + "/bookings"), ct: ct);
    }

    // ================================================================ documents (STNK, insurance, KIR, service)
    public async Task<IReadOnlyList<ResourceDocumentItem>> DocumentsAsync(Guid resourceId, CancellationToken ct = default)
    {
        if (!await db.BookableResources.AnyAsync(r => r.Id == resourceId, ct)) throw DomainException.NotFound("Resource");
        var rows = await db.ResourceDocuments.AsNoTracking().Where(d => d.ResourceId == resourceId).OrderBy(d => d.ExpiresOn).ToListAsync(ct);
        return [.. rows.Select(d => new ResourceDocumentItem(d.Id, d.ResourceId, KindName(d.Kind), d.Number, d.ExpiresOn, d.Note, State(d.ExpiresOn)))];
    }

    public async Task<IReadOnlyList<ResourceDocumentItem>> SaveDocumentAsync(Guid resourceId, Guid? id, SaveResourceDocumentRequest req, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var r = await db.BookableResources.AsNoTracking().FirstOrDefaultAsync(x => x.Id == resourceId, ct) ?? throw DomainException.NotFound("Resource");
        var e = new Dictionary<string, string>();
        var kind = ParseKind(req.Kind);
        if (kind is null) e["kind"] = "Choose registration, insurance, inspection, service or other.";
        var number = Clean(req.Number);
        if (number is { Length: > 60 }) e["number"] = "Use at most 60 characters.";
        if (req.ExpiresOn is null) e["expiresOn"] = "Give the date it expires.";
        var note = Clean(req.Note);
        if (note is { Length: > 500 }) e["note"] = "Use at most 500 characters.";
        if (e.Count > 0) throw Invalid(e);
        var now = clock.UtcNow;
        ResourceDocument d;
        if (id is null)
        {
            if (await db.ResourceDocuments.CountAsync(x => x.ResourceId == resourceId, ct) >= 30) throw DomainException.Conflict("A resource keeps at most 30 documents.");
            d = new ResourceDocument { ResourceId = resourceId, CreatedAt = now }; db.ResourceDocuments.Add(d);
        }
        else d = await db.ResourceDocuments.FirstOrDefaultAsync(x => x.Id == id && x.ResourceId == resourceId, ct) ?? throw DomainException.NotFound("Document");
        if (d.ExpiresOn != req.ExpiresOn) d.Reminded = null;
        d.Kind = kind!.Value; d.Number = number; d.ExpiresOn = req.ExpiresOn!.Value; d.Note = note; d.UpdatedAt = now;
        audit.Record(id is null ? "resource.document_add" : "resource.document_update", "resource", r.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email,
            new Dictionary<string, string> { ["kind"] = KindName(d.Kind), ["expires"] = d.ExpiresOn.ToString("O") });
        await db.SaveChangesAsync(ct);
        return await DocumentsAsync(resourceId, ct);
    }

    public async Task<IReadOnlyList<ResourceDocumentItem>> DeleteDocumentAsync(Guid resourceId, Guid id, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var d = await db.ResourceDocuments.FirstOrDefaultAsync(x => x.Id == id && x.ResourceId == resourceId, ct) ?? throw DomainException.NotFound("Document");
        db.ResourceDocuments.Remove(d);
        audit.Record("resource.document_remove", "resource", resourceId.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["kind"] = KindName(d.Kind) });
        await db.SaveChangesAsync(ct);
        return await DocumentsAsync(resourceId, ct);
    }

    // ================================================================ driver licences (SIM)
    public async Task<IReadOnlyList<LicenceItem>> LicencesAsync(CancellationToken ct = default)
    {
        var rows = await (from l in db.DriverLicences.AsNoTracking() join u in db.Users.AsNoTracking() on l.UserId equals u.Id orderby u.Name select new { l, u.Name, u.Email }).ToListAsync(ct);
        return [.. rows.Select(x => new LicenceItem(x.l.UserId, x.Name, x.Email, x.l.Number, x.l.Class, x.l.ExpiresOn, State(x.l.ExpiresOn)))];
    }

    /// <summary>People who can be given a car to drive: everyone with a licence on record, those with a valid one first.</summary>
    public async Task<IReadOnlyList<DriverOption>> DriversAsync(CancellationToken ct = default)
        => [.. (await LicencesAsync(ct)).OrderBy(l => l.State == "expired").ThenBy(l => l.Name).Select(l => new DriverOption(l.UserId, l.Name, l.Class, l.ExpiresOn, l.State))];

    public async Task<IReadOnlyList<LicenceItem>> SaveLicenceAsync(Guid userId, SaveLicenceRequest req, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        if (!await db.Users.AnyAsync(u => u.Id == userId, ct)) throw DomainException.NotFound("Person");
        var e = new Dictionary<string, string>();
        var number = Clean(req.Number);
        if (number is null) e["number"] = "Give the licence number."; else if (number.Length > 40) e["number"] = "Use at most 40 characters.";
        var cls = Clean(req.Class)?.ToUpperInvariant();
        if (cls is { Length: > 10 }) e["class"] = "Use at most 10 characters.";
        if (req.ExpiresOn is null) e["expiresOn"] = "Give the date it expires.";
        if (e.Count > 0) throw Invalid(e);
        var now = clock.UtcNow;
        var l = await db.DriverLicences.FirstOrDefaultAsync(x => x.UserId == userId, ct);
        if (l is null) { l = new DriverLicence { UserId = userId, CreatedAt = now }; db.DriverLicences.Add(l); }
        if (l.ExpiresOn != req.ExpiresOn) l.Reminded = null;
        l.Number = number!; l.Class = cls; l.ExpiresOn = req.ExpiresOn!.Value; l.UpdatedAt = now;
        audit.Record("driver.licence_save", "user", userId.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["expires"] = l.ExpiresOn.ToString("O") });
        await db.SaveChangesAsync(ct);
        return await LicencesAsync(ct);
    }

    /// <summary>Records a licence for the active person with this e-mail (the people who keep resources need not be able to browse users).</summary>
    public async Task<IReadOnlyList<LicenceItem>> AddLicenceAsync(AddLicenceRequest req, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var email = Clean(req.Email)?.ToLowerInvariant();
        var userId = email is null ? (Guid?)null : await db.Users.AsNoTracking().Where(u => u.Email.ToLower() == email && u.Status == UserStatus.Active).Select(u => (Guid?)u.Id).FirstOrDefaultAsync(ct);
        if (userId is null) throw Invalid(new Dictionary<string, string> { ["email"] = "Nobody active has this e-mail address." });
        return await SaveLicenceAsync(userId.Value, new SaveLicenceRequest(req.Number, req.Class, req.ExpiresOn), actor, client, ct);
    }

    public async Task<IReadOnlyList<LicenceItem>> DeleteLicenceAsync(Guid userId, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var l = await db.DriverLicences.FirstOrDefaultAsync(x => x.UserId == userId, ct) ?? throw DomainException.NotFound("Licence");
        db.DriverLicences.Remove(l);
        audit.Record("driver.licence_remove", "user", userId.ToString(), Outcome.Success, client, actor.UserId, actor.Email);
        await db.SaveChangesAsync(ct);
        return await LicencesAsync(ct);
    }

    // ================================================================ expiry reminders (daily)
    /// <summary>For every organisation: tells the people who keep resources about documents expiring within 30 days and on the day they expire, and drivers (and
    /// those people) about licences. Each reminder is claimed in one UPDATE so it goes once per stage.</summary>
    public async Task<int> RunExpiryRemindersAsync(CancellationToken ct = default)
    {
        var today = Today; var soon = today.AddDays(VehicleRules.NoticeDays);
        var orgs = (await db.ResourceDocuments.IgnoreQueryFilters().AsNoTracking().Where(d => d.ExpiresOn <= soon && (d.Reminded == null || (d.Reminded == "soon" && d.ExpiresOn < today))).Select(d => d.TenantId).Distinct().ToListAsync(ct))
            .Union(await db.DriverLicences.IgnoreQueryFilters().AsNoTracking().Where(d => d.ExpiresOn <= soon && (d.Reminded == null || (d.Reminded == "soon" && d.ExpiresOn < today))).Select(d => d.TenantId).Distinct().ToListAsync(ct)).ToList();
        var sent = 0;
        foreach (var org in orgs)
        {
            using (tenant.Switch(org))
            {
                db.ResetTracking();
                var keepers = await db.Users.AsNoTracking().Where(u => u.Status == UserStatus.Active && u.UserRoles.Any(ur => ur.Role!.ArchivedAt == null && ur.Role.Permissions.Any(p => p.PermissionKey == Permissions.ResourcesManage)))
                    .OrderBy(u => u.Id).Select(u => u.Id).Take(20).ToListAsync(ct);
                var docs = await db.ResourceDocuments.AsNoTracking().Where(d => d.ExpiresOn <= soon && (d.Reminded == null || (d.Reminded == "soon" && d.ExpiresOn < today))).Take(500).ToListAsync(ct);
                foreach (var d in docs)
                {
                    var stage = d.ExpiresOn < today ? "expired" : "soon";
                    if (await db.ResourceDocuments.Where(x => x.Id == d.Id && x.Reminded == d.Reminded).ExecuteUpdateAsync(s => s.SetProperty(x => x.Reminded, stage), ct) == 0) continue;
                    var r = await db.BookableResources.AsNoTracking().FirstAsync(x => x.Id == d.ResourceId, ct);
                    var title = stage == "expired" ? $"{r.Name}: {Label(d.Kind)} has expired" : FormattableString.Invariant($"{r.Name}: {Label(d.Kind)} expires on {d.ExpiresOn:d MMM yyyy}");
                    foreach (var uid in keepers) await notifications.SendAsync(uid, "resource.document", title, d.Number, "/setup/resources", null, ct: ct);
                    await db.SaveChangesAsync(ct); sent++;
                }
                var lics = await db.DriverLicences.AsNoTracking().Where(d => d.ExpiresOn <= soon && (d.Reminded == null || (d.Reminded == "soon" && d.ExpiresOn < today))).Take(500).ToListAsync(ct);
                foreach (var l in lics)
                {
                    var stage = l.ExpiresOn < today ? "expired" : "soon";
                    if (await db.DriverLicences.Where(x => x.UserId == l.UserId && x.Reminded == l.Reminded).ExecuteUpdateAsync(s => s.SetProperty(x => x.Reminded, stage), ct) == 0) continue;
                    var name = await db.Users.AsNoTracking().Where(u => u.Id == l.UserId).Select(u => u.Name).FirstOrDefaultAsync(ct) ?? "—";
                    var title = stage == "expired" ? $"Driving licence of {name} has expired" : FormattableString.Invariant($"Driving licence of {name} expires on {l.ExpiresOn:d MMM yyyy}");
                    foreach (var uid in keepers.Append(l.UserId).Distinct()) await notifications.SendAsync(uid, "resource.document", title, l.Number, uid == l.UserId ? "/bookings" : "/setup/resources", null, ct: ct);
                    await db.SaveChangesAsync(ct); sent++;
                }
            }
        }
        return sent;
    }
    private static string Label(ResourceDocumentKind k) => k switch
    {
        ResourceDocumentKind.Registration => "the registration (STNK)", ResourceDocumentKind.Insurance => "the insurance", ResourceDocumentKind.Inspection => "the inspection (KIR)",
        ResourceDocumentKind.Service => "the service", _ => "a document",
    };

    // ================================================================ utilisation (RPT-005)
    /// <summary>Per resource over the last <paramref name="days"/> (7–365): opening hours, hours booked inside them (confirmed and checked-in bookings), the share,
    /// bookings, no-shows (released), cancellations, and for vehicles the trips returned, kilometres driven and their cost. Needs <c>bookings.read_all</c>.</summary>
    public async Task<UtilisationReport> UtilisationAsync(int? days, string? kind, CancellationToken ct = default)
    {
        var period = InsightWindow.Of(days, 30);
        var to = clock.UtcNow; var from = to.AddDays(-period);
        var k = BookingService.ParseKind(kind);
        var resources = await db.BookableResources.AsNoTracking().Where(r => r.ArchivedAt == null || r.ArchivedAt > from).Where(r => k == null || r.Kind == k).OrderBy(r => r.Kind).ThenBy(r => r.Name).ToListAsync(ct);
        var ids = resources.Select(r => r.Id).ToList();
        var bookings = await db.Bookings.AsNoTracking().Where(b => ids.Contains(b.ResourceId) && b.StartsAt < to && b.EndsAt > from)
            .Select(b => new { b.ResourceId, b.StartsAt, b.EndsAt, b.Status }).Take(100_000).ToListAsync(ct);
        var trips = await db.VehicleTrips.AsNoTracking().Where(t => ids.Contains(t.ResourceId) && t.ReturnedAt >= from && t.ReturnedAt < to)
            .Select(t => new { t.ResourceId, t.OdometerIn, t.OdometerOut, t.FuelCost, t.TollCost, t.ParkingCost }).ToListAsync(ct);
        var places = await db.Locations.AsNoTracking().Select(l => new { l.Id, l.ParentId, l.Name, l.TimeZone }).ToDictionaryAsync(l => l.Id, ct);
        var fallback = await db.Calendars.AsNoTracking().Where(c => c.IsDefault && c.ArchivedAt == null).Select(c => c.TimeZone).FirstOrDefaultAsync(ct) ?? CalendarService.DefaultTimeZone;
        string ZoneId(Guid? id) { for (var (c, n) = (id, 0); c is { } x && places.TryGetValue(x, out var p) && n < 10; c = p.ParentId, n++) if (!string.IsNullOrWhiteSpace(p.TimeZone)) return p.TimeZone!; return fallback; }
        static TimeZoneInfo Zone(string id) { try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException) { return TimeZoneInfo.Utc; } }

        var rows = new List<UtilisationRow>();
        foreach (var r in resources)
        {
            double open = 0, booked = 0;
            var mine = bookings.Where(b => b.ResourceId == r.Id).ToList();
            foreach (var (ws, we) in BookingService.OpenWindows(r, from, to, Zone(ZoneId(r.LocationId))))
            {
                open += (we - ws).TotalHours;
                foreach (var b in mine.Where(b => b.Status == BookingStatus.Confirmed && b.StartsAt < we && b.EndsAt > ws))
                    booked += ((b.EndsAt < we ? b.EndsAt : we) - (b.StartsAt > ws ? b.StartsAt : ws)).TotalHours;
            }
            var rt = trips.Where(t => t.ResourceId == r.Id).ToList();
            rows.Add(new UtilisationRow(r.Id, r.Name, BookingService.KindName(r.Kind), r.LocationId is { } l && places.TryGetValue(l, out var pl) ? pl.Name : null, Math.Round(open, 1), Math.Round(booked, 1),
                open <= 0 ? null : (int)Math.Round(100 * booked / open), mine.Count(b => b.Status is BookingStatus.Confirmed or BookingStatus.Released), mine.Count(b => b.Status == BookingStatus.Released),
                mine.Count(b => b.Status == BookingStatus.Cancelled), rt.Count, rt.Sum(t => (t.OdometerIn ?? 0) - (t.OdometerOut ?? 0)), rt.Sum(t => (t.FuelCost ?? 0) + (t.TollCost ?? 0) + (t.ParkingCost ?? 0))));
        }
        var tOpen = rows.Sum(x => x.OpenHours); var tBooked = rows.Sum(x => x.BookedHours);
        var total = new UtilisationRow(Guid.Empty, "Total", kind ?? "all", null, Math.Round(tOpen, 1), Math.Round(tBooked, 1), tOpen <= 0 ? null : (int)Math.Round(100 * tBooked / tOpen),
            rows.Sum(x => x.Bookings), rows.Sum(x => x.NoShows), rows.Sum(x => x.Cancelled), rows.Sum(x => x.Trips), rows.Sum(x => x.Km), rows.Sum(x => x.TripCost));
        return new UtilisationReport(period, from, to, k is null ? null : BookingService.KindName(k.Value), rows, total);
    }
}
