using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Application.Users;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Application.Helpdesk;

public sealed record PlanListItem(Guid Id, string Name, string Target, string Unit, int Every, DateOnly? NextDueOn, int LeadDays, string? TeamName, string? AssigneeName, string? VendorName,
    bool Paused, int OpenVisits, int OverdueVisits);
public sealed record PlanVisit(Guid Id, DateOnly DueOn, Guid? AssetId, string? AssetTag, string? AssetName, string? LocationName, Guid? TicketId, string? TicketNumber, Guid? WorkOrderId, string? WorkOrderNumber,
    string State, DateTimeOffset? CompletedAt);
public sealed record ComplianceRow(string Key, string Label, int Planned, int OnTime, int Late, int Open, int Overdue, int Missed, int Cancelled, int? Percent);
public sealed record PlanDetail(Guid Id, string Name, string? Instructions, Guid ServiceId, string ServiceName, Guid? AssetId, string? AssetLabel, Guid? AssetTypeId, string? AssetTypeName, Guid? LocationId, string? LocationPath,
    string Target, string Unit, int Every, DateOnly StartsOn, DateOnly? EndsOn, int LeadDays, DateOnly? NextDueOn, IReadOnlyList<DateOnly> Upcoming, int Targets,
    Guid? ChecklistTemplateId, string? ChecklistName, Guid? TeamId, string? TeamName, Guid? AssigneeId, string? AssigneeName, Guid? VendorId, string? VendorName, bool RequireAfterPhoto,
    bool Paused, string? OwnerName, IReadOnlyList<PlanVisit> Visits, ComplianceRow LastYear, bool CanManage, int Version);
public sealed record SavePlanRequest(string? Name, string? Instructions, Guid? ServiceId, Guid? AssetId, Guid? AssetTypeId, Guid? LocationId, string? Unit, int Every, DateOnly? StartsOn, DateOnly? EndsOn,
    int? LeadDays, Guid? ChecklistTemplateId, Guid? TeamId, Guid? AssigneeId, Guid? VendorId, bool RequireAfterPhoto = false, int? Version = null);
public sealed record ComplianceReport(DateOnly From, DateOnly To, string By, IReadOnlyList<ComplianceRow> Rows, ComplianceRow Total);

/// <summary>
/// PM-001/002/003. Preventive maintenance. A plan says what is serviced, how often and by whom; the scheduler (the daily reminders worker, and right after a plan is saved) turns
/// each due date that has come within the plan's lead time into one visit per target: a ticket on the plan's service with one work order carrying the plan's checklist, due at the
/// end of the due date in the organisation's time zone. Idempotent and safe with several instances: an occurrence is claimed by moving the plan's counter with a conditional update,
/// and visits are unique per plan, target and date. When the scheduler was down, the dates already overtaken by the next one are recorded as missed (no ticket), so nobody gets a pile
/// of stale work and the compliance report still shows the gap. Reading needs <c>pm.read</c>, changing <c>pm.manage</c>.
/// </summary>
public sealed class MaintenanceService(IAppDb db, IClock clock, AuditWriter audit, AuthorizationService authz, ITenantContext tenant, TicketService tickets, WorkOrderService workOrders)
{
    private static DomainException Invalid(Dictionary<string, string> e) => DomainException.Validation("Please correct the highlighted fields.", e);
    private static string? Clean(string? s) { var t = (s ?? "").Trim(); return t.Length == 0 ? null : t; }
    private static readonly ClientInfo System = new(null, null, null);

    // ================================================================ time
    /// <summary>The organisation's date and zone: the default business calendar's time zone (Asia/Jakarta when there is none).</summary>
    private async Task<(DateOnly Today, TimeZoneInfo Zone)> TodayAsync(CancellationToken ct)
    {
        var id = await db.Calendars.AsNoTracking().Where(c => c.IsDefault && c.ArchivedAt == null).Select(c => c.TimeZone).FirstOrDefaultAsync(ct) ?? CalendarService.DefaultTimeZone;
        TimeZoneInfo zone;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById(id); } catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException) { zone = TimeZoneInfo.Utc; }
        return (DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.UtcNow, zone).DateTime), zone);
    }

    private static DateTimeOffset EndOfDay(DateOnly d, TimeZoneInfo zone)
    {
        var local = d.ToDateTime(new TimeOnly(23, 59, 59));
        return new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime();
    }

    // ================================================================ places and targets
    private sealed record Place(Guid Id, Guid? ParentId, string Name);
    private async Task<Dictionary<Guid, Place>> PlacesAsync(CancellationToken ct) => await db.Locations.AsNoTracking().Select(l => new Place(l.Id, l.ParentId, l.Name)).ToDictionaryAsync(l => l.Id, ct);
    private static string PathOf(Dictionary<Guid, Place> places, Guid id)
    {
        var parts = new List<string>();
        for (Guid? c = id; c is { } k && places.TryGetValue(k, out var p) && parts.Count < 6; c = p.ParentId) parts.Insert(0, p.Name);
        return string.Join(" › ", parts);
    }
    private static Guid? TopOf(Dictionary<Guid, Place> places, Guid? id)
    {
        Guid? top = null;
        for (var (c, n) = (id, 0); c is { } k && places.TryGetValue(k, out var p) && n < 10; c = p.ParentId, n++) top = k;
        return top;
    }
    private static List<Guid> Below(Dictionary<Guid, Place> places, Guid id)
    {
        var inside = new HashSet<Guid> { id }; var grew = true;
        while (grew) { grew = false; foreach (var l in places.Values) if (l.ParentId is { } par && inside.Contains(par) && inside.Add(l.Id)) grew = true; }
        return [.. inside];
    }

    private sealed record Target(Guid Key, Guid? AssetId, Guid? LocationId, string Label);
    private async Task<List<Target>> TargetsAsync(MaintenancePlan p, CancellationToken ct)
    {
        if (p.AssetId is { } aid)
            return await db.Assets.AsNoTracking().Where(a => a.Id == aid && a.RetiredAt == null).Select(a => new Target(a.Id, a.Id, a.LocationId, a.Tag + " " + a.Name)).ToListAsync(ct);
        if (p.AssetTypeId is { } tid)
        {
            var q = db.Assets.AsNoTracking().Where(a => a.AssetTypeId == tid && a.RetiredAt == null);
            if (p.LocationId is { } lid) { var inside = Below(await PlacesAsync(ct), lid); q = q.Where(a => a.LocationId != null && inside.Contains(a.LocationId.Value)); }
            return await q.OrderBy(a => a.Tag).Take(MaintenanceRules.MaxTargets).Select(a => new Target(a.Id, a.Id, a.LocationId, a.Tag + " " + a.Name)).ToListAsync(ct);
        }
        if (p.LocationId is { } pl)
            return await db.Locations.AsNoTracking().Where(l => l.Id == pl && l.ArchivedAt == null).Select(l => new Target(l.Id, null, l.Id, l.Name)).ToListAsync(ct);
        return [];
    }

    // ================================================================ reading
    private async Task<bool> CanManageAsync(SessionPrincipal p, CancellationToken ct) => (await authz.GetEffectivePermissionsAsync(p.UserId, ct)).Contains(Permissions.PmManage);

    /// <summary><paramref name="status"/>: <c>active</c>, <c>paused</c> or <c>all</c> (default). <paramref name="assetId"/>: plans for that asset, directly or through its type.</summary>
    public async Task<PagedResult<PlanListItem>> ListAsync(string? q, string? status, Guid? assetId, int page, int pageSize, CancellationToken ct = default)
    {
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100);
        var query = db.MaintenancePlans.AsNoTracking();
        if (Clean(q) is { } text)
        {
            var pat = "%" + text.ToLowerInvariant().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            query = query.Where(p => EF.Functions.Like(p.Name.ToLower(), pat, "\\"));
        }
        query = (status ?? "all").Trim().ToLowerInvariant() switch
        {
            "active" => query.Where(p => p.PausedAt == null),
            "paused" => query.Where(p => p.PausedAt != null),
            "all" => query,
            _ => throw Invalid(new() { ["status"] = "Use active, paused or all." }),
        };
        if (assetId is { } aid)
        {
            var type = await db.Assets.AsNoTracking().Where(a => a.Id == aid).Select(a => (Guid?)a.AssetTypeId).FirstOrDefaultAsync(ct);
            query = query.Where(p => p.AssetId == aid || (type != null && p.AssetTypeId == type));
        }
        var total = await query.CountAsync(ct);
        var (today, _) = await TodayAsync(ct);
        var rows = await query.OrderBy(p => p.Name).Skip((page - 1) * pageSize).Take(pageSize).Select(p => new
        {
            p.Id, p.Name, p.Unit, p.Every, p.NextDueOn, p.LeadDays, Paused = p.PausedAt != null, p.AssetId, p.AssetTypeId, p.LocationId,
            Asset = db.Assets.Where(a => a.Id == p.AssetId).Select(a => a.Tag + " " + a.Name).FirstOrDefault(),
            Type = db.AssetTypes.Where(a => a.Id == p.AssetTypeId).Select(a => a.Name).FirstOrDefault(),
            Place = db.Locations.Where(l => l.Id == p.LocationId).Select(l => l.Name).FirstOrDefault(),
            Team = db.Teams.Where(t => t.Id == p.TeamId).Select(t => t.Name).FirstOrDefault(),
            Person = db.Users.Where(u => u.Id == p.AssigneeId).Select(u => u.Name).FirstOrDefault(),
            Vendor = db.Vendors.Where(v => v.Id == p.VendorId).Select(v => v.Name).FirstOrDefault(),
            Open = db.MaintenanceVisits.Count(v => v.PlanId == p.Id && !v.Missed && db.WorkOrders.Any(w => w.Id == v.WorkOrderId && w.Status != WorkOrderStatus.Done && w.Status != WorkOrderStatus.Cancelled)),
            Overdue = db.MaintenanceVisits.Count(v => v.PlanId == p.Id && !v.Missed && v.DueOn < today && db.WorkOrders.Any(w => w.Id == v.WorkOrderId && w.Status != WorkOrderStatus.Done && w.Status != WorkOrderStatus.Cancelled)),
        }).ToListAsync(ct);
        return new PagedResult<PlanListItem>([.. rows.Select(r => new PlanListItem(r.Id, r.Name, TargetLabel(r.AssetId, r.Asset, r.AssetTypeId, r.Type, r.LocationId, r.Place), MaintenanceRules.UnitName(r.Unit), r.Every,
            r.NextDueOn, r.LeadDays, r.Team, r.Person, r.Vendor, r.Paused, r.Open, r.Overdue))], page, pageSize, total);
    }

    private static string TargetLabel(Guid? assetId, string? asset, Guid? typeId, string? type, Guid? placeId, string? place)
        => assetId is not null ? asset ?? "—" : typeId is not null ? (place is null ? $"Every {type}" : $"Every {type} in {place}") : place ?? "—";

    public async Task<PlanDetail> GetAsync(Guid id, SessionPrincipal who, CancellationToken ct = default)
    {
        var p = await db.MaintenancePlans.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Maintenance plan");
        var (today, _) = await TodayAsync(ct);
        var places = await PlacesAsync(ct);
        var service = await db.Services.AsNoTracking().Where(s => s.Id == p.ServiceId).Select(s => s.Name).FirstOrDefaultAsync(ct) ?? "—";
        var asset = p.AssetId is { } aid ? await db.Assets.AsNoTracking().Where(a => a.Id == aid).Select(a => a.Tag + " " + a.Name).FirstOrDefaultAsync(ct) : null;
        var type = p.AssetTypeId is { } tid ? await db.AssetTypes.AsNoTracking().Where(a => a.Id == tid).Select(a => a.Name).FirstOrDefaultAsync(ct) : null;
        var path = p.LocationId is { } lid && places.ContainsKey(lid) ? PathOf(places, lid) : null;
        var checklist = p.ChecklistTemplateId is { } cid ? await db.ChecklistTemplates.AsNoTracking().Where(c => c.Id == cid).Select(c => c.Name).FirstOrDefaultAsync(ct) : null;
        var team = p.TeamId is { } tm ? await db.Teams.AsNoTracking().Where(t => t.Id == tm).Select(t => t.Name).FirstOrDefaultAsync(ct) : null;
        var ids = new[] { p.AssigneeId ?? Guid.Empty, p.OwnerId };
        var people = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Name, ct);
        var vendor = p.VendorId is { } vid ? await db.Vendors.AsNoTracking().Where(v => v.Id == vid).Select(v => v.Name).FirstOrDefaultAsync(ct) : null;
        var targets = (await TargetsAsync(p, ct)).Count;

        // ordered and limited before projecting (EF cannot order by a projected record)
        var visits = (await VisitRows(db.MaintenanceVisits.AsNoTracking().Where(v => v.PlanId == id).OrderByDescending(v => v.DueOn).ThenBy(v => v.TargetKey).Take(50)).ToListAsync(ct))
            .OrderByDescending(v => v.DueOn).ThenBy(v => v.AssetTag, StringComparer.OrdinalIgnoreCase).ToList();
        var year = await VisitRows(db.MaintenanceVisits.AsNoTracking().Where(v => v.PlanId == id && v.DueOn > today.AddYears(-1) && v.DueOn <= today)).ToListAsync(ct);
        return new PlanDetail(p.Id, p.Name, p.Instructions, p.ServiceId, service, p.AssetId, asset, p.AssetTypeId, type, p.LocationId, path, TargetLabel(p.AssetId, asset, p.AssetTypeId, type, p.LocationId, path),
            MaintenanceRules.UnitName(p.Unit), p.Every, p.StartsOn, p.EndsOn, p.LeadDays, p.NextDueOn,
            p.NextDueOn is null ? [] : MaintenanceRules.Upcoming(p.StartsOn, p.Unit, p.Every, p.NextIndex, p.EndsOn, 5), targets,
            p.ChecklistTemplateId, checklist, p.TeamId, team, p.AssigneeId, p.AssigneeId is { } a ? people.GetValueOrDefault(a) : null, p.VendorId, vendor, p.RequireAfterPhoto,
            p.PausedAt is not null, people.GetValueOrDefault(p.OwnerId), [.. visits.Select(v => ToVisit(v, places, today))], Summarise("plan", p.Name, year.Select(v => State(v, today))),
            await CanManageAsync(who, ct), p.Version);
    }

    private sealed record VisitRow(Guid Id, Guid PlanId, DateOnly DueOn, bool Missed, Guid? AssetId, string? AssetTag, string? AssetName, Guid? LocationId, Guid? TicketId, string? TicketNumber, Guid? TicketTeamId,
        Guid? WorkOrderId, string? WorkOrderNumber, WorkOrderStatus? Status, DateTimeOffset? CompletedAt, DateTimeOffset? DueAt, Guid? WoTeamId, Guid? VendorId);
    private IQueryable<VisitRow> VisitRows(IQueryable<MaintenanceVisit> q) => q.Select(v => new VisitRow(v.Id, v.PlanId, v.DueOn, v.Missed, v.AssetId,
        db.Assets.Where(a => a.Id == v.AssetId).Select(a => a.Tag).FirstOrDefault(), db.Assets.Where(a => a.Id == v.AssetId).Select(a => a.Name).FirstOrDefault(), v.LocationId,
        v.TicketId, db.Tickets.Where(t => t.Id == v.TicketId).Select(t => t.Number).FirstOrDefault(), db.Tickets.Where(t => t.Id == v.TicketId).Select(t => t.TeamId).FirstOrDefault(),
        v.WorkOrderId, db.WorkOrders.Where(w => w.Id == v.WorkOrderId).Select(w => w.Number).FirstOrDefault(),
        db.WorkOrders.Where(w => w.Id == v.WorkOrderId).Select(w => (WorkOrderStatus?)w.Status).FirstOrDefault(),
        db.WorkOrders.Where(w => w.Id == v.WorkOrderId).Select(w => w.CompletedAt).FirstOrDefault(),
        db.WorkOrders.Where(w => w.Id == v.WorkOrderId).Select(w => w.DueAt).FirstOrDefault(),
        db.WorkOrders.Where(w => w.Id == v.WorkOrderId).Select(w => w.TeamId).FirstOrDefault(),
        db.WorkOrders.Where(w => w.Id == v.WorkOrderId).Select(w => w.VendorId).FirstOrDefault()));

    private static string State(VisitRow v, DateOnly today) => MaintenanceRules.VisitState(v.Missed, v.Status, v.CompletedAt, v.DueAt, v.DueOn, today);
    private static PlanVisit ToVisit(VisitRow v, Dictionary<Guid, Place> places, DateOnly today)
        => new(v.Id, v.DueOn, v.AssetId, v.AssetTag, v.AssetName, v.LocationId is { } l && places.TryGetValue(l, out var p) ? p.Name : null, v.TicketId, v.TicketNumber, v.WorkOrderId, v.WorkOrderNumber,
            State(v, today), v.Status == WorkOrderStatus.Done ? v.CompletedAt : null);

    /// <summary>
    /// Counts for the compliance report. <c>Percent</c> = done on time ÷ visits that are due or finished (planned minus the ones still open before their date); null when nothing is due yet.
    /// Cancelled and missed visits count against it: they were planned and not done.
    /// </summary>
    private static ComplianceRow Summarise(string key, string label, IEnumerable<string> states)
    {
        var s = states.ToList();
        int C(string x) => s.Count(v => v == x);
        var open = C("open"); var counted = s.Count - open - C("removed");
        return new ComplianceRow(key, label, s.Count, C("on_time"), C("late"), open, C("overdue"), C("missed"), C("cancelled"), counted == 0 ? null : (int)Math.Round(C("on_time") * 100.0 / counted));
    }

    // ================================================================ saving
    public async Task<PlanDetail> SaveAsync(Guid? id, SavePlanRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var e = new Dictionary<string, string>();
        var name = (r.Name ?? "").Trim(); var instr = Clean(r.Instructions);
        if (name.Length is < MaintenanceRules.NameMin or > MaintenanceRules.NameMax) e["name"] = $"Use {MaintenanceRules.NameMin}–{MaintenanceRules.NameMax} characters.";
        else if (await db.MaintenancePlans.AnyAsync(p => p.Id != id && p.Name.ToLower() == name.ToLower(), ct)) e["name"] = "Another plan already has this name.";
        if (instr is { Length: > MaintenanceRules.InstructionsMax }) e["instructions"] = $"Use at most {MaintenanceRules.InstructionsMax} characters.";

        MaintenancePlan? p = null;
        if (id is { } pid)
        {
            p = await db.MaintenancePlans.FirstOrDefaultAsync(x => x.Id == pid, ct) ?? throw DomainException.NotFound("Maintenance plan");
            if (r.Version is { } ver && ver != p.Version) throw DomainException.Conflict("This plan was changed by someone else. Reload and try again.");
        }

        if (r.ServiceId is not { } sid || !await db.Services.AnyAsync(s => s.Id == sid && s.ArchivedAt == null, ct)) e["serviceId"] = "Choose an active service.";
        if (r.AssetId is { } aid)
        {
            if (r.AssetTypeId is not null || r.LocationId is not null) e["target"] = "A plan for one asset can't also name a type or a place.";
            else if (aid != p?.AssetId && !await db.Assets.AnyAsync(a => a.Id == aid && a.RetiredAt == null, ct)) e["assetId"] = "Choose an asset that is not retired.";
        }
        else if (r.AssetTypeId is { } tid)
        {
            if (!await db.AssetTypes.AnyAsync(a => a.Id == tid && a.ArchivedAt == null, ct)) e["assetTypeId"] = "Choose an active asset type.";
        }
        else if (r.LocationId is null) e["target"] = "Choose an asset, an asset type or a place.";
        if (r.LocationId is { } lid && r.AssetId is null && lid != p?.LocationId && !await db.Locations.AnyAsync(l => l.Id == lid && l.ArchivedAt == null, ct)) e["locationId"] = "Choose an active place.";

        var unit = PmUnit.Month;
        if (!MaintenanceRules.TryUnit(r.Unit, out unit)) e["unit"] = "Use day, week, month or year.";
        else if (r.Every < 1 || r.Every > MaintenanceRules.MaxEvery(unit)) e["every"] = $"Use 1–{MaintenanceRules.MaxEvery(unit)}.";
        if (r.StartsOn is not { } start) e["startsOn"] = "Give the first date.";
        else if (start.Year is < 2000 or > 2100) e["startsOn"] = "Use a date between 2000 and 2100.";
        if (r.EndsOn is { } end && r.StartsOn is { } st && end < st) e["endsOn"] = "The plan can't end before it starts.";
        var lead = r.LeadDays ?? 7;
        if (lead is < 0 or > MaintenanceRules.MaxLeadDays) e["leadDays"] = $"Use 0–{MaintenanceRules.MaxLeadDays} days.";
        if (r.ChecklistTemplateId is { } cid && cid != p?.ChecklistTemplateId && !await db.ChecklistTemplates.AnyAsync(c => c.Id == cid && c.ArchivedAt == null, ct)) e["checklistTemplateId"] = "Choose an active checklist.";
        if (r.TeamId is { } team && !await db.Teams.AnyAsync(t => t.Id == team && t.ArchivedAt == null, ct)) e["teamId"] = "Choose an active team.";
        if (r.AssigneeId is { } who && !await db.Users.AnyAsync(u => u.Id == who && u.Status == UserStatus.Active, ct)) e["assigneeId"] = "Choose an active person.";
        if (r.VendorId is { } vid && vid != p?.VendorId && !await db.Vendors.AnyAsync(v => v.Id == vid && v.BlockedAt == null, ct)) e["vendorId"] = "Choose a vendor that is not blocked.";
        if (e.Count > 0) throw Invalid(e);

        var (today, zone) = await TodayAsync(ct);
        var now = clock.UtcNow;
        var created = p is null;
        p ??= new MaintenancePlan { CreatedAt = now, OwnerId = actor.UserId };
        var rescheduled = created || p.StartsOn != r.StartsOn!.Value || p.Unit != unit || p.Every != r.Every;
        p.Name = name; p.Instructions = instr; p.ServiceId = r.ServiceId!.Value; p.AssetId = r.AssetId; p.AssetTypeId = r.AssetId is null ? r.AssetTypeId : null; p.LocationId = r.AssetId is null ? r.LocationId : null;
        p.Unit = unit; p.Every = r.Every; p.StartsOn = r.StartsOn!.Value; p.EndsOn = r.EndsOn; p.LeadDays = lead;
        p.ChecklistTemplateId = r.ChecklistTemplateId; p.TeamId = r.TeamId; p.AssigneeId = r.AssigneeId; p.VendorId = r.VendorId; p.RequireAfterPhoto = r.RequireAfterPhoto;
        p.OwnerId = actor.UserId; p.UpdatedAt = now;
        // a new or changed rhythm starts from today: past dates of a schedule that did not exist are not "missed"
        if (rescheduled) p.NextIndex = MaintenanceRules.FirstIndexOnOrAfter(p.StartsOn, p.Unit, p.Every, today);
        p.NextDueOn = NextDue(p);
        if (created) db.MaintenancePlans.Add(p); else p.Version++;
        audit.Record(created ? "pm.plan.create" : "pm.plan.update", "maintenance_plan", p.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email,
            new Dictionary<string, string> { ["name"] = name, ["every"] = $"{p.Every} {MaintenanceRules.UnitName(p.Unit)}", ["next"] = p.NextDueOn?.ToString("yyyy-MM-dd") ?? "" });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw DomainException.Conflict("This plan was changed by someone else. Reload and try again."); }
        catch (DbUpdateException) { throw Invalid(new() { ["name"] = "Another plan already has this name." }); }

        await GenerateAsync(p.Id, today, zone, ct);       // work already within the lead time is made now, not at the next scheduler run
        return await GetAsync(p.Id, actor, ct);
    }

    private static DateOnly? NextDue(MaintenancePlan p)
    {
        var d = MaintenanceRules.DueOn(p.StartsOn, p.Unit, p.Every, p.NextIndex);
        return p.EndsOn is { } end && d > end ? null : d;
    }

    public async Task<PlanDetail> SetPausedAsync(Guid id, bool paused, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var p = await db.MaintenancePlans.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Maintenance plan");
        var (today, zone) = await TodayAsync(ct);
        if (paused && p.PausedAt is null) p.PausedAt = clock.UtcNow;
        else if (!paused && p.PausedAt is not null)
        {
            // pausing is deliberate: the dates that passed meanwhile are skipped, not reported as missed
            p.PausedAt = null;
            p.NextIndex = Math.Max(p.NextIndex, MaintenanceRules.FirstIndexOnOrAfter(p.StartsOn, p.Unit, p.Every, today));
            p.NextDueOn = NextDue(p);
        }
        else return await GetAsync(id, actor, ct);
        p.UpdatedAt = clock.UtcNow; p.Version++;
        audit.Record(paused ? "pm.plan.pause" : "pm.plan.resume", "maintenance_plan", p.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["name"] = p.Name });
        await db.SaveChangesAsync(ct);
        if (!paused) await GenerateAsync(p.Id, today, zone, ct);
        return await GetAsync(id, actor, ct);
    }

    /// <summary>Only a plan that never made a visit can be removed; one with history is paused instead, so the compliance report keeps it.</summary>
    public async Task DeleteAsync(Guid id, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var p = await db.MaintenancePlans.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Maintenance plan");
        if (await db.MaintenanceVisits.AnyAsync(v => v.PlanId == id, ct)) throw DomainException.Conflict("This plan already has visits. Pause it instead, so its history stays in the compliance report.");
        db.MaintenancePlans.Remove(p);
        audit.Record("pm.plan.delete", "maintenance_plan", p.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["name"] = p.Name });
        await db.SaveChangesAsync(ct);
    }

    // ================================================================ the scheduler
    /// <summary>Every organisation's plans whose next date has come within their lead time. Returns the number of visits made (tickets created).</summary>
    public async Task<int> RunDueAsync(CancellationToken ct = default)
    {
        var horizon = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime).AddDays(MaintenanceRules.MaxLeadDays + 2);
        var due = await db.MaintenancePlans.IgnoreQueryFilters().AsNoTracking().Where(p => p.PausedAt == null && p.NextDueOn != null && p.NextDueOn <= horizon)
            .Select(p => new { p.Id, p.TenantId, Due = p.NextDueOn!.Value, p.LeadDays }).ToListAsync(ct);
        var made = 0;
        foreach (var org in due.GroupBy(d => d.TenantId))
        {
            using (tenant.Switch(org.Key))
            {
                db.ResetTracking();
                var (today, zone) = await TodayAsync(ct);
                foreach (var p in org.Where(p => p.Due.AddDays(-p.LeadDays) <= today)) made += await GenerateAsync(p.Id, today, zone, ct);
            }
        }
        return made;
    }

    /// <summary>
    /// Makes the plan's occurrences that are within the lead time, one at a time: each is claimed by moving the plan's counter with a conditional update in the same transaction as its
    /// visits, so a concurrent run, a retry or a crash half-way never makes one twice. A failure is audited and leaves the occurrence for the next run.
    /// </summary>
    private async Task<int> GenerateAsync(Guid planId, DateOnly today, TimeZoneInfo zone, CancellationToken ct)
    {
        db.ResetTracking();
        var made = 0; var missed = 0; var dropped = 0;
        for (var step = 0; step < MaintenanceRules.MaxStepsPerRun; step++)
        {
            var p = await db.MaintenancePlans.AsNoTracking().FirstOrDefaultAsync(x => x.Id == planId, ct);
            if (p is null || p.PausedAt is not null || p.NextDueOn is not { } due || due.AddDays(-p.LeadDays) > today) break;
            var index = p.NextIndex; var nextIndex = index + 1;
            DateOnly? next = MaintenanceRules.DueOn(p.StartsOn, p.Unit, p.Every, nextIndex);
            if (p.EndsOn is { } end && next > end) next = null;
            var overtaken = next is { } n && n <= today;     // the next date has come too: this one can no longer be done on time
            int got;
            try
            {
                got = await db.InTransactionAsync(async () =>
                {
                    var claimed = await db.MaintenancePlans.Where(x => x.Id == planId && x.NextIndex == index && x.PausedAt == null)
                        .ExecuteUpdateAsync(u => u.SetProperty(x => x.NextIndex, nextIndex).SetProperty(x => x.NextDueOn, next), ct);
                    if (claimed == 0) return -1;
                    var have = await db.MaintenanceVisits.AsNoTracking().Where(v => v.PlanId == planId && v.DueOn == due).Select(v => v.TargetKey).ToListAsync(ct);
                    var targets = (await TargetsAsync(p, ct)).Where(t => !have.Contains(t.Key)).ToList();
                    var now = clock.UtcNow; var count = 0;
                    if (overtaken)
                    {
                        foreach (var t in targets)
                        {
                            if (missed >= MaintenanceRules.MaxMissedPerRun) { dropped++; continue; }
                            db.MaintenanceVisits.Add(new MaintenanceVisit { PlanId = planId, TargetKey = t.Key, AssetId = t.AssetId, LocationId = t.LocationId, DueOn = due, Missed = true, CreatedAt = now });
                            missed++;
                        }
                    }
                    else if (targets.Count > 0)
                    {
                        var service = await db.Services.AsNoTracking().FirstAsync(s => s.Id == p.ServiceId, ct);
                        var lines = await workOrders.TemplateLinesAsync(p.ChecklistTemplateId, ct);
                        var vendor = p.VendorId is { } vid && await db.Vendors.AnyAsync(v => v.Id == vid && v.BlockedAt == null, ct) ? p.VendorId : null;
                        var person = p.AssigneeId is { } aid && await db.Users.AnyAsync(u => u.Id == aid && u.Status == UserStatus.Active, ct) ? p.AssigneeId : null;
                        var team = p.TeamId is { } tid && await db.Teams.AnyAsync(t => t.Id == tid && t.ArchivedAt == null, ct) ? p.TeamId : null;
                        var dueAt = EndOfDay(due, zone);
                        foreach (var t in targets)
                        {
                            var subject = $"{p.Name} · {t.Label}"; if (subject.Length > 200) subject = subject[..200];
                            var text = $"Planned maintenance due {due:yyyy-MM-dd}.{(p.VendorId is not null && vendor is null ? " The plan's vendor is blocked, so no vendor was set." : "")}";
                            var ticket = await tickets.AddPlannedAsync(service, subject, text, p.OwnerId, t.LocationId, t.AssetId, team, person, now, ct);
                            var wo = await workOrders.AddPlannedAsync(ticket, p.Name, p.Instructions, lines, person, team, vendor, p.RequireAfterPhoto, dueAt, p.OwnerId, now, ct);
                            db.MaintenanceVisits.Add(new MaintenanceVisit { PlanId = planId, TargetKey = t.Key, AssetId = t.AssetId, LocationId = t.LocationId, DueOn = due, TicketId = ticket.Id, WorkOrderId = wo.Id, CreatedAt = now });
                            count++;
                        }
                    }
                    await db.SaveChangesAsync(ct);
                    return count;
                }, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                db.ResetTracking();
                audit.Record("pm.generate", "maintenance_plan", planId.ToString(), Outcome.Failure, System, null, "system (maintenance)", new Dictionary<string, string> { ["due"] = due.ToString("yyyy-MM-dd"), ["error"] = ex.GetType().Name });
                await db.SaveChangesAsync(ct);
                break;
            }
            if (got < 0) break;
            db.ResetTracking();
            made += got;
        }
        if (missed > 0 || dropped > 0)
        {
            audit.Record("pm.catch_up", "maintenance_plan", planId.ToString(), Outcome.Success, System, null, "system (maintenance)", new Dictionary<string, string> { ["missed"] = missed.ToString(), ["notRecorded"] = dropped.ToString() });
            await db.SaveChangesAsync(ct);
        }
        return made;
    }

    // ================================================================ compliance
    /// <summary>PM-003. Planned visits due between two dates (at most a year), grouped by <c>plan</c>, <c>team</c>, <c>vendor</c> or <c>site</c> (the top-level place).</summary>
    public async Task<ComplianceReport> ComplianceAsync(DateOnly? from, DateOnly? to, string? by, CancellationToken ct = default)
    {
        var (today, _) = await TodayAsync(ct);
        var end = to ?? today; var start = from ?? end.AddDays(-89);
        var e = new Dictionary<string, string>();
        if (end < start) e["to"] = "The end can't be before the start.";
        else if (end.DayNumber - start.DayNumber > 366) e["from"] = "Choose at most a year.";
        var group = (by ?? "plan").Trim().ToLowerInvariant();
        if (group is not ("plan" or "team" or "vendor" or "site")) e["by"] = "Use plan, team, vendor or site.";
        if (e.Count > 0) throw Invalid(e);

        var rows = await VisitRows(db.MaintenanceVisits.AsNoTracking().Where(v => v.DueOn >= start && v.DueOn <= end)).Take(50_000).ToListAsync(ct);
        var planIds = rows.Select(r => r.PlanId).Distinct().ToList();
        var plans = await db.MaintenancePlans.AsNoTracking().Where(p => planIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => new { p.Name, p.TeamId, p.VendorId }, ct);
        var teams = await db.Teams.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.Name, ct);
        var vendors = await db.Vendors.AsNoTracking().ToDictionaryAsync(v => v.Id, v => v.Name, ct);
        var places = group == "site" ? await PlacesAsync(ct) : [];

        (string Key, string Label) KeyOf(VisitRow r)
        {
            switch (group)
            {
                case "team":
                    var team = r.WoTeamId ?? r.TicketTeamId ?? plans.GetValueOrDefault(r.PlanId)?.TeamId;
                    return team is { } t ? (t.ToString(), teams.GetValueOrDefault(t) ?? "—") : ("none", "No team");
                case "vendor":
                    var vendor = r.WorkOrderId is null ? plans.GetValueOrDefault(r.PlanId)?.VendorId : r.VendorId;
                    return vendor is { } v ? (v.ToString(), vendors.GetValueOrDefault(v) ?? "—") : ("none", "In-house");
                case "site":
                    return TopOf(places, r.LocationId) is { } s ? (s.ToString(), places[s].Name) : ("none", "No place");
                default:
                    return (r.PlanId.ToString(), plans.GetValueOrDefault(r.PlanId)?.Name ?? "—");
            }
        }
        var list = rows.GroupBy(KeyOf).Select(g => Summarise(g.Key.Key, g.Key.Label, g.Select(r => State(r, today))))
            .OrderBy(r => r.Percent ?? 101).ThenBy(r => r.Label, StringComparer.OrdinalIgnoreCase).ToList();
        return new ComplianceReport(start, end, group, list, Summarise("total", "Total", rows.Select(r => State(r, today))));
    }
}
