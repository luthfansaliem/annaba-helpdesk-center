using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;

namespace AnnabaDigital.Application.Helpdesk;

public sealed record LocationItem(Guid Id, Guid? ParentId, string Type, string Code, string Name, string? Address, string? TimeZone, Guid? CalendarId, Guid? ManagerUserId, string? ManagerName,
    int? Capacity, bool Archived, int Children, int Version);

public sealed record SaveLocationRequest(string? Type, Guid? ParentId, string? Code, string? Name, string? Address, string? TimeZone, Guid? CalendarId, Guid? ManagerUserId, int? Capacity, int? Version = null);

/// <summary>
/// LOC-001/002. Places are a tree (region → site → building → floor → room). Rules enforced here, each with a test: structure (a child is deeper than its parent, only region/site at the top),
/// unique codes among live places, valid time zone / calendar / manager inside the same organisation, no archiving a place that still has live children, optimistic concurrency, full audit.
/// </summary>
public sealed partial class LocationService(IAppDb db, IClock clock, AuditWriter audit)
{
    [GeneratedRegex("^[A-Z0-9][A-Z0-9._-]{0,29}$")] private static partial Regex CodeRule();

    private static LocationType ParseType(string? s, Dictionary<string, string> errors)
    {
        if (Enum.TryParse<LocationType>(s?.Trim(), true, out var t) && Enum.IsDefined(t)) return t;
        errors["type"] = "Choose region, site, building, floor or room.";
        return LocationType.Site;
    }

    public static bool IsValidTimeZone(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        try { TimeZoneInfo.FindSystemTimeZoneById(id); return true; } catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException) { return false; }
    }

    public async Task<IReadOnlyList<LocationItem>> ListAsync(bool includeArchived, CancellationToken ct = default)
    {
        var rows = await db.Locations.AsNoTracking().Where(l => includeArchived || l.ArchivedAt == null)
            .Select(l => new
            {
                l.Id, l.ParentId, l.Type, l.Code, l.Name, l.Address, l.TimeZone, l.CalendarId, l.ManagerUserId, l.Capacity, l.Version, Archived = l.ArchivedAt != null,
                Manager = db.Users.Where(u => u.Id == l.ManagerUserId).Select(u => u.Name).FirstOrDefault(),
                Children = db.Locations.Count(c => c.ParentId == l.Id && c.ArchivedAt == null),
            }).ToListAsync(ct);
        return [.. rows.OrderBy(r => r.Type).ThenBy(r => r.Name).Select(r => new LocationItem(r.Id, r.ParentId, r.Type.ToString().ToLowerInvariant(), r.Code, r.Name, r.Address, r.TimeZone, r.CalendarId, r.ManagerUserId, r.Manager, r.Capacity, r.Archived, r.Children, r.Version))];
    }

    public async Task<LocationItem> GetAsync(Guid id, CancellationToken ct = default)
        => (await ListAsync(true, ct)).FirstOrDefault(l => l.Id == id) ?? throw DomainException.NotFound("Location");

    private async Task ValidateAsync(SaveLocationRequest req, Guid? selfId, Dictionary<string, string> errors, CancellationToken ct)
    {
        var name = (req.Name ?? "").Trim();
        if (name.Length < 2 || name.Length > 120) errors["name"] = "Use 2–120 characters.";
        var code = (req.Code ?? "").Trim().ToUpperInvariant();
        if (!CodeRule().IsMatch(code)) errors["code"] = "Use up to 30 letters, digits, dot, dash or underscore (e.g. JKT-HQ-L3).";
        if (req.Address is { Length: > 300 }) errors["address"] = "Use at most 300 characters.";
        if (!string.IsNullOrWhiteSpace(req.TimeZone) && !IsValidTimeZone(req.TimeZone.Trim())) errors["timeZone"] = "Use an IANA time zone such as Asia/Jakarta.";
        if (req.Capacity is < 0 or > 100_000) errors["capacity"] = "Use a number between 0 and 100,000.";
        if (errors.Count > 0) return;
        if (await db.Locations.AnyAsync(l => l.Code == code && l.ArchivedAt == null && l.Id != selfId, ct)) errors["code"] = "This code is already used by another location.";
        if (req.CalendarId is { } cal && !await db.Calendars.AnyAsync(c => c.Id == cal && c.ArchivedAt == null, ct)) errors["calendarId"] = "Choose an existing business calendar.";
        if (req.ManagerUserId is { } m && !await db.Users.AnyAsync(u => u.Id == m, ct)) errors["managerUserId"] = "Choose a person in your organisation.";
    }

    private async Task<Location?> ParentAsync(Guid? parentId, LocationType type, Dictionary<string, string> errors, CancellationToken ct)
    {
        if (parentId is null)
        {
            if (!Location.CanSit(type, null)) errors["parentId"] = $"A {type.ToString().ToLowerInvariant()} must be inside another location.";
            return null;
        }
        var parent = await db.Locations.FirstOrDefaultAsync(l => l.Id == parentId, ct);
        if (parent is null || parent.ArchivedAt is not null) { errors["parentId"] = "Choose an existing location."; return null; }
        if (!Location.CanSit(type, parent.Type)) errors["parentId"] = $"A {type.ToString().ToLowerInvariant()} can't be inside a {parent.Type.ToString().ToLowerInvariant()}.";
        return parent;
    }

    public async Task<LocationItem> CreateAsync(SaveLocationRequest req, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var errors = new Dictionary<string, string>();
        var type = ParseType(req.Type, errors);
        await ValidateAsync(req, null, errors, ct);
        await ParentAsync(req.ParentId, type, errors, ct);
        if (errors.Count > 0) throw DomainException.Validation("Please correct the highlighted fields.", errors);

        var now = clock.UtcNow;
        var l = new Location
        {
            Type = type, ParentId = req.ParentId, Code = req.Code!.Trim().ToUpperInvariant(), Name = req.Name!.Trim(), Address = Blank(req.Address), TimeZone = Blank(req.TimeZone),
            CalendarId = req.CalendarId, ManagerUserId = req.ManagerUserId, Capacity = req.Capacity, CreatedAt = now, UpdatedAt = now,
        };
        db.Locations.Add(l);
        audit.Record("location.create", "location", l.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["code"] = l.Code, ["name"] = l.Name, ["type"] = type.ToString() });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { throw DomainException.Conflict("This code is already used by another location."); }
        return await GetAsync(l.Id, ct);
    }

    public async Task<LocationItem> UpdateAsync(Guid id, SaveLocationRequest req, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var l = await db.Locations.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Location");
        if (l.ArchivedAt is not null) throw DomainException.Conflict("This location is archived. Restore it before editing.");
        if (req.Version is { } v && v != l.Version) throw DomainException.Conflict("This location was changed by someone else. Reload and try again.");

        var errors = new Dictionary<string, string>();
        var type = ParseType(req.Type ?? l.Type.ToString(), errors);
        await ValidateAsync(req, id, errors, ct);
        await ParentAsync(req.ParentId, type, errors, ct);
        if (errors.Count == 0 && req.ParentId == id) errors["parentId"] = "A location can't be inside itself.";
        // Changing the type or the parent must keep the live children valid (and, because depth only grows, can never create a cycle).
        if (errors.Count == 0 && (type != l.Type || req.ParentId != l.ParentId))
        {
            var kids = await db.Locations.Where(c => c.ParentId == id && c.ArchivedAt == null).Select(c => c.Type).ToListAsync(ct);
            if (kids.Any(k => !Location.CanSit(k, type))) errors["type"] = "Its live children would no longer fit under this type. Move or archive them first.";
        }
        if (errors.Count > 0) throw DomainException.Validation("Please correct the highlighted fields.", errors);

        var diff = new List<AuditDiff>();
        void Set<T>(string field, T before, T after, Action apply) { if (!EqualityComparer<T>.Default.Equals(before, after)) { diff.Add(new(field, before?.ToString() ?? "—", after?.ToString() ?? "—")); apply(); } }
        Set("type", l.Type, type, () => l.Type = type);
        Set("parentId", l.ParentId, req.ParentId, () => l.ParentId = req.ParentId);
        Set("code", l.Code, req.Code!.Trim().ToUpperInvariant(), () => l.Code = req.Code!.Trim().ToUpperInvariant());
        Set("name", l.Name, req.Name!.Trim(), () => l.Name = req.Name!.Trim());
        Set("address", l.Address, Blank(req.Address), () => l.Address = Blank(req.Address));
        Set("timeZone", l.TimeZone, Blank(req.TimeZone), () => l.TimeZone = Blank(req.TimeZone));
        Set("calendarId", l.CalendarId, req.CalendarId, () => l.CalendarId = req.CalendarId);
        Set("managerUserId", l.ManagerUserId, req.ManagerUserId, () => l.ManagerUserId = req.ManagerUserId);
        Set("capacity", l.Capacity, req.Capacity, () => l.Capacity = req.Capacity);
        if (diff.Count > 0)
        {
            l.Version++; l.UpdatedAt = clock.UtcNow;
            audit.Record("location.update", "location", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["code"] = l.Code }, diff);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { throw DomainException.Conflict("This location was changed by someone else. Reload and try again."); }
            catch (DbUpdateException) { throw DomainException.Conflict("This code is already used by another location."); }
        }
        return await GetAsync(id, ct);
    }

    public async Task<LocationItem> ArchiveAsync(Guid id, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var l = await db.Locations.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Location");
        if (l.ArchivedAt is not null) throw DomainException.Conflict("This location is already archived.");
        if (await db.Locations.AnyAsync(c => c.ParentId == id && c.ArchivedAt == null, ct)) throw DomainException.Invariant("Archive or move the locations inside it first.");
        l.ArchivedAt = clock.UtcNow; l.UpdatedAt = l.ArchivedAt.Value; l.Version++;
        audit.Record("location.archive", "location", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["code"] = l.Code });
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<LocationItem> RestoreAsync(Guid id, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var l = await db.Locations.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Location");
        if (l.ArchivedAt is null) throw DomainException.Conflict("This location is not archived.");
        if (l.ParentId is { } p && await db.Locations.AnyAsync(x => x.Id == p && x.ArchivedAt != null, ct)) throw DomainException.Invariant("Restore the location it is inside first.");
        if (await db.Locations.AnyAsync(x => x.Code == l.Code && x.ArchivedAt == null, ct)) throw DomainException.Conflict("Its code is now used by another location. Rename that one first.");
        l.ArchivedAt = null; l.UpdatedAt = clock.UtcNow; l.Version++;
        audit.Record("location.restore", "location", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["code"] = l.Code });
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
