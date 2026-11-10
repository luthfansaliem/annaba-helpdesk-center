using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;

namespace AnnabaDigital.Application.Helpdesk;

public sealed record PeriodDto(int Day, int StartMinute, int EndMinute);
public sealed record HolidayDto(string Date, string Name);
public sealed record CalendarItem(Guid Id, string Name, string TimeZone, bool IsAlwaysOpen, bool IsDefault, bool Archived, int Locations, int Version, IReadOnlyList<PeriodDto> Periods, IReadOnlyList<HolidayDto> Holidays);
public sealed record SaveCalendarRequest(string? Name, string? TimeZone, bool? IsAlwaysOpen, PeriodDto[]? Periods, HolidayDto[]? Holidays, int? Version = null);

/// <summary>
/// SLA-003. A calendar says when work happens (weekly windows, holidays, a time zone, or 24/7). Windows and holidays are replaced as a whole on every save, so a calendar is
/// never half-edited. Exactly one live calendar is the organisation's default; the default can't be archived and a calendar in use by locations can't be archived.
/// </summary>
public sealed class CalendarService(IAppDb db, IClock clock, AuditWriter audit)
{
    public const string DefaultName = "Office hours";
    public const string DefaultTimeZone = "Asia/Jakarta";

    public async Task<IReadOnlyList<CalendarItem>> ListAsync(bool includeArchived, CancellationToken ct = default)
    {
        var rows = await db.Calendars.AsNoTracking().Where(c => includeArchived || c.ArchivedAt == null).Include(c => c.Periods).Include(c => c.Holidays).AsSplitQuery().ToListAsync(ct);
        var used = await db.Locations.AsNoTracking().Where(l => l.CalendarId != null && l.ArchivedAt == null).GroupBy(l => l.CalendarId!.Value).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
        return [.. rows.OrderByDescending(c => c.IsDefault).ThenBy(c => c.Name).Select(c => Map(c, used.GetValueOrDefault(c.Id)))];
    }

    public async Task<CalendarItem> GetAsync(Guid id, CancellationToken ct = default)
        => (await ListAsync(true, ct)).FirstOrDefault(c => c.Id == id) ?? throw DomainException.NotFound("Calendar");

    private static CalendarItem Map(BusinessCalendar c, int locations) => new(c.Id, c.Name, c.TimeZone, c.IsAlwaysOpen, c.IsDefault, c.ArchivedAt is not null, locations, c.Version,
        [.. c.Periods.OrderBy(p => ((int)p.Day + 6) % 7).ThenBy(p => p.StartMinute).Select(p => new PeriodDto((int)p.Day, p.StartMinute, p.EndMinute))],
        [.. c.Holidays.OrderBy(h => h.Date).Select(h => new HolidayDto(h.Date.ToString("yyyy-MM-dd"), h.Name))]);

    private static (List<CalendarPeriod> Periods, List<CalendarHoliday> Holidays) Validate(SaveCalendarRequest req, Dictionary<string, string> errors)
    {
        var name = (req.Name ?? "").Trim();
        if (name.Length < 2 || name.Length > 100) errors["name"] = "Use 2–100 characters.";
        if (!LocationService.IsValidTimeZone(req.TimeZone?.Trim())) errors["timeZone"] = "Use an IANA time zone such as Asia/Jakarta.";

        var periods = new List<CalendarPeriod>(); var holidays = new List<CalendarHoliday>();
        var always = req.IsAlwaysOpen == true;
        var input = req.Periods ?? [];
        if (!always)
        {
            if (input.Length == 0) errors["periods"] = "Add at least one working window, or switch the calendar to 24/7.";
            else if (input.Length > 70) errors["periods"] = "Use at most 70 windows.";
            foreach (var p in input)
            {
                if (p.Day is < 0 or > 6 || p.StartMinute < 0 || p.EndMinute > 1440 || p.EndMinute <= p.StartMinute) { errors["periods"] = "Each window needs a weekday and an end time after its start (00:00–24:00)."; break; }
                periods.Add(new CalendarPeriod { Day = (DayOfWeek)p.Day, StartMinute = p.StartMinute, EndMinute = p.EndMinute });
            }
            if (!errors.ContainsKey("periods"))
                foreach (var g in periods.GroupBy(p => p.Day))
                {
                    var sorted = g.OrderBy(p => p.StartMinute).ToList();
                    for (var i = 1; i < sorted.Count; i++) if (sorted[i].StartMinute < sorted[i - 1].EndMinute) { errors["periods"] = $"Two windows overlap on {g.Key}."; break; }
                }
        }
        var seen = new HashSet<DateOnly>();
        foreach (var h in req.Holidays ?? [])
        {
            var hn = (h.Name ?? "").Trim();
            if (!DateOnly.TryParseExact(h.Date, "yyyy-MM-dd", out var d) || hn.Length is < 1 or > 100) { errors["holidays"] = "Each holiday needs a date (YYYY-MM-DD) and a name of up to 100 characters."; break; }
            if (!seen.Add(d)) { errors["holidays"] = $"{h.Date} is listed twice."; break; }
            holidays.Add(new CalendarHoliday { Date = d, Name = hn });
        }
        if ((req.Holidays?.Length ?? 0) > 400) errors["holidays"] = "Use at most 400 holidays.";
        return (periods, holidays);
    }

    public async Task<CalendarItem> CreateAsync(SaveCalendarRequest req, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var errors = new Dictionary<string, string>();
        var (periods, holidays) = Validate(req, errors);
        var name = (req.Name ?? "").Trim();
        if (errors.Count == 0 && await db.Calendars.AnyAsync(c => c.Name == name && c.ArchivedAt == null, ct)) errors["name"] = "A calendar with this name already exists.";
        if (errors.Count > 0) throw DomainException.Validation("Please correct the highlighted fields.", errors);

        var now = clock.UtcNow;
        var c = new BusinessCalendar { Name = name, TimeZone = req.TimeZone!.Trim(), IsAlwaysOpen = req.IsAlwaysOpen == true, CreatedAt = now, UpdatedAt = now };
        c.IsDefault = !await db.Calendars.AnyAsync(x => x.IsDefault && x.ArchivedAt == null, ct); // the first calendar of an organisation is its default
        foreach (var p in periods) c.Periods.Add(p);
        foreach (var h in holidays) c.Holidays.Add(h);
        db.Calendars.Add(c);
        audit.Record("calendar.create", "calendar", c.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["name"] = name, ["timeZone"] = c.TimeZone, ["windows"] = periods.Count.ToString(), ["holidays"] = holidays.Count.ToString() });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { throw DomainException.Conflict("A calendar with this name already exists."); }
        return await GetAsync(c.Id, ct);
    }

    public async Task<CalendarItem> UpdateAsync(Guid id, SaveCalendarRequest req, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var c = await db.Calendars.Include(x => x.Periods).Include(x => x.Holidays).AsSplitQuery().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Calendar");
        if (c.ArchivedAt is not null) throw DomainException.Conflict("This calendar is archived. Restore it before editing.");
        if (req.Version is { } v && v != c.Version) throw DomainException.Conflict("This calendar was changed by someone else. Reload and try again.");
        var errors = new Dictionary<string, string>();
        var (periods, holidays) = Validate(req, errors);
        var name = (req.Name ?? "").Trim();
        if (errors.Count == 0 && await db.Calendars.AnyAsync(x => x.Name == name && x.ArchivedAt == null && x.Id != id, ct)) errors["name"] = "A calendar with this name already exists.";
        if (errors.Count > 0) throw DomainException.Validation("Please correct the highlighted fields.", errors);

        var before = $"{c.Name} · {c.TimeZone} · {(c.IsAlwaysOpen ? "24/7" : c.Periods.Count + " windows")} · {c.Holidays.Count} holidays";
        c.Name = name; c.TimeZone = req.TimeZone!.Trim(); c.IsAlwaysOpen = req.IsAlwaysOpen == true;
        // Windows are replaced outright. Holidays are matched by date (a delete + insert of the same date in one save would trip the unique index).
        db.CalendarPeriods.RemoveRange(c.Periods.ToList());
        // New rows already carry client-generated keys, so they must be added through the DbSet: attaching them through the
        // tracked parent's collection would make EF treat them as existing rows (UPDATE of 0 rows -> concurrency error).
        foreach (var p in periods) { p.CalendarId = id; db.CalendarPeriods.Add(p); }
        var keep = holidays.ToDictionary(h => h.Date);
        foreach (var old in c.Holidays.ToList())
        {
            if (keep.Remove(old.Date, out var same)) old.Name = same.Name;
            else db.CalendarHolidays.Remove(old);
        }
        foreach (var h in keep.Values) { h.CalendarId = id; db.CalendarHolidays.Add(h); }
        c.Version++; c.UpdatedAt = clock.UtcNow;
        var after = $"{c.Name} · {c.TimeZone} · {(c.IsAlwaysOpen ? "24/7" : periods.Count + " windows")} · {holidays.Count} holidays";
        audit.Record("calendar.update", "calendar", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["name"] = c.Name }, [new AuditDiff("calendar", before, after)]);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw DomainException.Conflict("This calendar was changed by someone else. Reload and try again."); }
        catch (DbUpdateException) { throw DomainException.Conflict("A calendar with this name already exists."); }
        return await GetAsync(id, ct);
    }

    public async Task<CalendarItem> SetDefaultAsync(Guid id, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        await db.InTransactionAsync(async () =>
        {
            var target = await db.Calendars.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Calendar");
            if (target.ArchivedAt is not null) throw DomainException.Conflict("Restore this calendar before making it the default.");
            if (target.IsDefault) return 0;
            var current = await db.Calendars.Where(x => x.IsDefault && x.ArchivedAt == null).ToListAsync(ct);
            foreach (var x in current) { x.IsDefault = false; x.Version++; }
            await db.SaveChangesAsync(ct); // free the one-default index before taking it
            target.IsDefault = true; target.Version++;
            audit.Record("calendar.set_default", "calendar", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["name"] = target.Name });
            return await db.SaveChangesAsync(ct);
        }, ct);
        return await GetAsync(id, ct);
    }

    public async Task<CalendarItem> ArchiveAsync(Guid id, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var c = await db.Calendars.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Calendar");
        if (c.ArchivedAt is not null) throw DomainException.Conflict("This calendar is already archived.");
        if (c.IsDefault) throw DomainException.Invariant("The default calendar can't be archived. Make another calendar the default first.");
        if (await db.Locations.AnyAsync(l => l.CalendarId == id && l.ArchivedAt == null, ct)) throw DomainException.Invariant("Locations still use this calendar. Change them first.");
        c.ArchivedAt = clock.UtcNow; c.UpdatedAt = c.ArchivedAt.Value; c.Version++;
        audit.Record("calendar.archive", "calendar", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["name"] = c.Name });
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<CalendarItem> RestoreAsync(Guid id, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var c = await db.Calendars.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Calendar");
        if (c.ArchivedAt is null) throw DomainException.Conflict("This calendar is not archived.");
        if (await db.Calendars.AnyAsync(x => x.Name == c.Name && x.ArchivedAt == null, ct)) throw DomainException.Conflict("A calendar with this name exists. Rename that one first.");
        c.ArchivedAt = null; c.UpdatedAt = clock.UtcNow; c.Version++;
        audit.Record("calendar.restore", "calendar", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["name"] = c.Name });
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>Builds the pure working-time engine for a stored calendar (used by service-level clocks).</summary>
    public static WorkingCalendar ToEngine(BusinessCalendar c)
        => new(TimeZoneInfo.FindSystemTimeZoneById(c.TimeZone), c.Periods.Select(p => new WorkingWindow(p.Day, p.StartMinute, p.EndMinute)), c.Holidays.Select(h => h.Date), c.IsAlwaysOpen);
}

/// <summary>What every new organisation starts with, so its helpdesk works the moment it exists.</summary>
public static class HelpdeskDefaults
{
    /// <summary>Idempotent. Creates the "Office hours" default calendar (Mon–Fri 08:00–17:00, Asia/Jakarta) when the organisation has no calendar at all, and the starter service catalog when it has no categories.</summary>
    public static async Task SeedAsync(IAppDb db, Guid tenantId, DateTimeOffset now, CancellationToken ct = default)
    {
        await SeedCalendarAsync(db, tenantId, now, ct);
        await CatalogTemplates.SeedAsync(db, tenantId, now, ct);
        await SlaDefaults.SeedAsync(db, tenantId, now, ct);
    }

    private static async Task SeedCalendarAsync(IAppDb db, Guid tenantId, DateTimeOffset now, CancellationToken ct)
    {
        if (await db.Calendars.IgnoreQueryFilters().AnyAsync(c => c.TenantId == tenantId, ct)) return;
        var c = new BusinessCalendar { TenantId = tenantId, Name = CalendarService.DefaultName, TimeZone = CalendarService.DefaultTimeZone, IsDefault = true, CreatedAt = now, UpdatedAt = now };
        foreach (var d in new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday })
            c.Periods.Add(new CalendarPeriod { TenantId = tenantId, Day = d, StartMinute = 8 * 60, EndMinute = 17 * 60 });
        db.Calendars.Add(c);
        await db.SaveChangesAsync(ct);
    }
}
