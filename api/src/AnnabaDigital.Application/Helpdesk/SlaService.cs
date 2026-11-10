using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;

namespace AnnabaDigital.Application.Helpdesk;

public sealed record SlaPolicyItem(Guid Id, string Name, int Position, Guid? ServiceId, string? ServiceName, Guid? CategoryId, string? CategoryName, Guid? LocationId, string? LocationName,
    string? Priority, Guid? CalendarId, string? CalendarName, int FirstResponseMinutes, int ResolutionMinutes, int Escalate1Percent, int Escalate2Percent, bool Archived, int Conditions, int Version);
public sealed record SaveSlaPolicyRequest(string? Name, Guid? ServiceId, Guid? CategoryId, Guid? LocationId, string? Priority, Guid? CalendarId, int FirstResponseMinutes, int ResolutionMinutes,
    int? Escalate1Percent, int? Escalate2Percent, int? Version = null);
public sealed record ReorderRequest(Guid[]? Ids);
public sealed record RulePreviewRequest(Guid? ServiceId, Guid? LocationId, string? Priority);
public sealed record SlaPreview(Guid? PolicyId, string? PolicyName, string? CalendarName, int FirstResponseMinutes, int ResolutionMinutes);

/// <summary>What the ticket facts of a rule or policy look like, built once from the database.</summary>
public static class MatchFactsBuilder
{
    public static async Task<MatchFacts> BuildAsync(IAppDb db, Guid serviceId, Guid? locationId, TicketPriority priority, CancellationToken ct)
    {
        var categoryId = await db.Services.AsNoTracking().Where(s => s.Id == serviceId).Select(s => s.CategoryId).FirstOrDefaultAsync(ct);
        var cats = new HashSet<Guid>();
        if (categoryId != Guid.Empty)
        {
            cats.Add(categoryId);
            if (await db.ServiceCategories.AsNoTracking().Where(c => c.Id == categoryId).Select(c => c.ParentId).FirstOrDefaultAsync(ct) is { } parent) cats.Add(parent);
        }
        var places = new HashSet<Guid>();
        for (Guid? at = locationId; at is { } id && places.Add(id);) at = await db.Locations.AsNoTracking().Where(l => l.Id == id).Select(l => l.ParentId).FirstOrDefaultAsync(ct);
        return new MatchFacts(serviceId, cats, places, priority);
    }
}

/// <summary>
/// SLA-002/004/005/006. Admin of SLA policies, and the one place a ticket's clock is set up and kept up to date. Policies are tried in order and the first match wins;
/// the ticket keeps a copy of the numbers it was given. Escalations are recorded on the ticket's internal timeline and in the audit log (notifications come with their own slice).
/// </summary>
public sealed class SlaService(IAppDb db, IClock clock, AuditWriter audit, ITenantContext tenant, NotificationService notifications)
{
    private readonly Dictionary<Guid, WorkingCalendar> _calendars = [];

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    private static string Lower<T>(T e) where T : struct, Enum => e.ToString().ToLowerInvariant();
    private static Conditions Cond(SlaPolicy p) => new(p.ServiceId, p.CategoryId, p.LocationId, p.Priority);

    // ================================================================ admin
    public async Task<IReadOnlyList<SlaPolicyItem>> ListAsync(bool includeArchived, CancellationToken ct = default)
    {
        var rows = await db.SlaPolicies.AsNoTracking().Where(p => includeArchived || p.ArchivedAt == null)
            .Select(p => new
            {
                p, Service = db.Services.Where(x => x.Id == p.ServiceId).Select(x => x.Name).FirstOrDefault(), Category = db.ServiceCategories.Where(x => x.Id == p.CategoryId).Select(x => x.Name).FirstOrDefault(),
                Location = db.Locations.Where(x => x.Id == p.LocationId).Select(x => x.Name).FirstOrDefault(), Calendar = db.Calendars.Where(x => x.Id == p.CalendarId).Select(x => x.Name).FirstOrDefault(),
            }).ToListAsync(ct);
        return [.. rows.OrderBy(r => r.p.ArchivedAt != null).ThenBy(r => r.p.Position).ThenBy(r => r.p.Name).Select(r => Map(r.p, r.Service, r.Category, r.Location, r.Calendar))];
    }

    private static SlaPolicyItem Map(SlaPolicy p, string? service, string? category, string? location, string? calendar)
        => new(p.Id, p.Name, p.Position, p.ServiceId, service, p.CategoryId, category, p.LocationId, location, p.Priority is { } pr ? Lower(pr) : null, p.CalendarId, calendar,
            p.FirstResponseMinutes, p.ResolutionMinutes, p.Escalate1Percent, p.Escalate2Percent, p.ArchivedAt is not null, Cond(p).Count, p.Version);

    private async Task<SlaPolicyItem> GetAsync(Guid id, CancellationToken ct) => (await ListAsync(true, ct)).FirstOrDefault(p => p.Id == id) ?? throw DomainException.NotFound("SLA policy");

    private async Task<(string Name, TicketPriority? Priority)> ValidateAsync(SaveSlaPolicyRequest r, Guid? selfId, Dictionary<string, string> errors, CancellationToken ct)
    {
        var name = (r.Name ?? "").Trim();
        if (name.Length is < 2 or > 100) errors["name"] = "Use 2–100 characters.";
        TicketPriority? priority = null;
        if (Clean(r.Priority) is { } ps) { if (Enum.TryParse<TicketPriority>(ps, true, out var pr) && Enum.IsDefined(pr)) priority = pr; else errors["priority"] = "Choose low, medium, high or urgent."; }
        if (r.FirstResponseMinutes is < 1 or > 525600) errors["firstResponseMinutes"] = "Use at least 1 minute.";
        if (r.ResolutionMinutes is < 1 or > 525600) errors["resolutionMinutes"] = "Use at least 1 minute.";
        else if (r.ResolutionMinutes < r.FirstResponseMinutes && !errors.ContainsKey("firstResponseMinutes")) errors["resolutionMinutes"] = "Resolution can't be sooner than the first response.";
        var e1 = r.Escalate1Percent ?? 50; var e2 = r.Escalate2Percent ?? 80;
        if (e1 is < 1 or > 98) errors["escalate1Percent"] = "Use 1–98.";
        if (e2 is < 2 or > 99 || e2 <= e1) errors["escalate2Percent"] = "Use a value above the first threshold, at most 99.";
        if (r.ServiceId is { } sid && !await db.Services.AnyAsync(x => x.Id == sid, ct)) errors["serviceId"] = "Choose an existing service.";
        if (r.CategoryId is { } cid && !await db.ServiceCategories.AnyAsync(x => x.Id == cid, ct)) errors["categoryId"] = "Choose an existing category.";
        if (r.LocationId is { } lid && !await db.Locations.AnyAsync(x => x.Id == lid && x.ArchivedAt == null, ct)) errors["locationId"] = "Choose an active location.";
        if (r.CalendarId is { } kid && !await db.Calendars.AnyAsync(x => x.Id == kid && x.ArchivedAt == null, ct)) errors["calendarId"] = "Choose an active calendar.";
        if (!errors.ContainsKey("name") && await db.SlaPolicies.AnyAsync(x => x.Name == name && x.ArchivedAt == null && x.Id != selfId, ct)) errors["name"] = "A policy with this name already exists.";
        return (name, priority);
    }

    public async Task<SlaPolicyItem> CreateAsync(SaveSlaPolicyRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var errors = new Dictionary<string, string>(); var (name, priority) = await ValidateAsync(r, null, errors, ct);
        if (errors.Count > 0) throw DomainException.Validation("Please correct the highlighted fields.", errors);
        var now = clock.UtcNow;
        var last = await db.SlaPolicies.Where(p => p.ArchivedAt == null).Select(p => (int?)p.Position).MaxAsync(ct) ?? 0;
        var p = new SlaPolicy
        {
            Name = name, Position = last + 10, ServiceId = r.ServiceId, CategoryId = r.CategoryId, LocationId = r.LocationId, Priority = priority, CalendarId = r.CalendarId,
            FirstResponseMinutes = r.FirstResponseMinutes, ResolutionMinutes = r.ResolutionMinutes, Escalate1Percent = r.Escalate1Percent ?? 50, Escalate2Percent = r.Escalate2Percent ?? 80, CreatedAt = now, UpdatedAt = now,
        };
        db.SlaPolicies.Add(p);
        audit.Record("sla.policy.create", "sla_policy", p.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["name"] = name });
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateException) { throw DomainException.Conflict("A policy with this name already exists."); }
        return await GetAsync(p.Id, ct);
    }

    public async Task<SlaPolicyItem> UpdateAsync(Guid id, SaveSlaPolicyRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var p = await db.SlaPolicies.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("SLA policy");
        if (p.ArchivedAt is not null) throw DomainException.Conflict("This policy is archived. Restore it before editing.");
        if (r.Version is { } v && v != p.Version) throw DomainException.Conflict("This policy was changed by someone else. Reload and try again.");
        var errors = new Dictionary<string, string>(); var (name, priority) = await ValidateAsync(r, id, errors, ct);
        if (errors.Count > 0) throw DomainException.Validation("Please correct the highlighted fields.", errors);
        var e1 = r.Escalate1Percent ?? 50; var e2 = r.Escalate2Percent ?? 80;
        var diff = new List<AuditDiff>();
        void D(string f, object? a, object? b) { if (!Equals(a, b)) diff.Add(new(f, a?.ToString() ?? "—", b?.ToString() ?? "—")); }
        D("name", p.Name, name); D("service", p.ServiceId, r.ServiceId); D("category", p.CategoryId, r.CategoryId); D("location", p.LocationId, r.LocationId); D("priority", p.Priority, priority);
        D("calendar", p.CalendarId, r.CalendarId); D("firstResponseMinutes", p.FirstResponseMinutes, r.FirstResponseMinutes); D("resolutionMinutes", p.ResolutionMinutes, r.ResolutionMinutes);
        D("escalate1Percent", p.Escalate1Percent, e1); D("escalate2Percent", p.Escalate2Percent, e2);
        if (diff.Count > 0)
        {
            p.Name = name; p.ServiceId = r.ServiceId; p.CategoryId = r.CategoryId; p.LocationId = r.LocationId; p.Priority = priority; p.CalendarId = r.CalendarId;
            p.FirstResponseMinutes = r.FirstResponseMinutes; p.ResolutionMinutes = r.ResolutionMinutes; p.Escalate1Percent = e1; p.Escalate2Percent = e2; p.Version++; p.UpdatedAt = clock.UtcNow;
            audit.Record("sla.policy.update", "sla_policy", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["name"] = name }, diff);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { throw DomainException.Conflict("This policy was changed by someone else. Reload and try again."); }
            catch (DbUpdateException) { throw DomainException.Conflict("A policy with this name already exists."); }
        }
        return await GetAsync(id, ct);
    }

    public async Task<SlaPolicyItem> ArchiveAsync(Guid id, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var p = await db.SlaPolicies.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("SLA policy");
        if (p.ArchivedAt is not null) throw DomainException.Conflict("This policy is already archived.");
        p.ArchivedAt = clock.UtcNow; p.UpdatedAt = p.ArchivedAt.Value; p.Version++;
        audit.Record("sla.policy.archive", "sla_policy", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["name"] = p.Name });
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<SlaPolicyItem> RestoreAsync(Guid id, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var p = await db.SlaPolicies.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("SLA policy");
        if (p.ArchivedAt is null) throw DomainException.Conflict("This policy is not archived.");
        if (await db.SlaPolicies.AnyAsync(x => x.Name == p.Name && x.ArchivedAt == null, ct)) throw DomainException.Conflict("A policy with this name exists. Rename that one first.");
        p.Position = (await db.SlaPolicies.Where(x => x.ArchivedAt == null).Select(x => (int?)x.Position).MaxAsync(ct) ?? 0) + 10; p.ArchivedAt = null; p.UpdatedAt = clock.UtcNow; p.Version++;
        audit.Record("sla.policy.restore", "sla_policy", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["name"] = p.Name });
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>Order matters (first match wins), so the new order is saved as a whole: it must list every live policy exactly once.</summary>
    public async Task<IReadOnlyList<SlaPolicyItem>> ReorderAsync(ReorderRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var live = await db.SlaPolicies.Where(p => p.ArchivedAt == null).ToListAsync(ct);
        var ids = r.Ids ?? [];
        if (ids.Length != live.Count || ids.Distinct().Count() != ids.Length || ids.Any(i => live.All(p => p.Id != i)))
            throw DomainException.Validation("The order must list every active policy once.", new Dictionary<string, string> { ["ids"] = "List every active policy exactly once." });
        var now = clock.UtcNow;
        for (var i = 0; i < ids.Length; i++) { var p = live.First(x => x.Id == ids[i]); if (p.Position != (i + 1) * 10) { p.Position = (i + 1) * 10; p.UpdatedAt = now; } }
        audit.Record("sla.policy.reorder", "sla_policy", null, Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["count"] = ids.Length.ToString() });
        await db.SaveChangesAsync(ct);
        return await ListAsync(false, ct);
    }

    /// <summary>"Which policy would this ticket get?" — the same matching a new ticket goes through.</summary>
    public async Task<SlaPreview> PreviewAsync(RulePreviewRequest r, CancellationToken ct = default)
    {
        var errors = new Dictionary<string, string>();
        if (r.ServiceId is null || !await db.Services.AnyAsync(s => s.Id == r.ServiceId, ct)) errors["serviceId"] = "Choose a service.";
        var priority = TicketPriority.Medium;
        if (Clean(r.Priority) is { } ps && !(Enum.TryParse(ps, true, out priority) && Enum.IsDefined(priority))) errors["priority"] = "Choose low, medium, high or urgent.";
        if (errors.Count > 0) throw DomainException.Validation("Please correct the highlighted fields.", errors);
        var facts = await MatchFactsBuilder.BuildAsync(db, r.ServiceId!.Value, r.LocationId, priority, ct);
        var policy = await MatchAsync(facts, ct);
        if (policy is null) return new SlaPreview(null, null, null, 0, 0);
        var calId = await CalendarIdAsync(policy, r.LocationId, ct);
        var calName = calId is null ? null : await db.Calendars.AsNoTracking().Where(c => c.Id == calId).Select(c => c.Name).FirstOrDefaultAsync(ct);
        return new SlaPreview(policy.Id, policy.Name, calName, policy.FirstResponseMinutes, policy.ResolutionMinutes);
    }

    // ================================================================ the clock of a ticket
    private async Task<SlaPolicy?> MatchAsync(MatchFacts facts, CancellationToken ct)
    {
        var live = await db.SlaPolicies.AsNoTracking().Where(p => p.ArchivedAt == null).OrderBy(p => p.Position).ThenBy(p => p.Id).ToListAsync(ct);
        return Routing.FirstMatch(live, Cond, facts);
    }

    /// <summary>The calendar for a policy: its own, else the nearest one on the ticket's place or above it, else the organisation's default.</summary>
    private async Task<Guid?> CalendarIdAsync(SlaPolicy policy, Guid? locationId, CancellationToken ct)
    {
        if (policy.CalendarId is { } own) return own;
        var seen = new HashSet<Guid>();
        for (Guid? at = locationId; at is { } id && seen.Add(id);)
        {
            var l = await db.Locations.AsNoTracking().Where(x => x.Id == id).Select(x => new { x.CalendarId, x.ParentId }).FirstOrDefaultAsync(ct);
            if (l?.CalendarId is { } c) return c;
            at = l?.ParentId;
        }
        return await db.Calendars.AsNoTracking().Where(c => c.IsDefault && c.ArchivedAt == null).Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct);
    }

    private async Task<WorkingCalendar> EngineAsync(Guid? calendarId, CancellationToken ct)
    {
        if (calendarId is not { } id) return WorkingCalendar.Always();
        if (_calendars.TryGetValue(id, out var cached)) return cached;
        var c = await db.Calendars.AsNoTracking().Include(x => x.Periods).Include(x => x.Holidays).AsSplitQuery().FirstOrDefaultAsync(x => x.Id == id, ct);
        return _calendars[id] = c is null ? WorkingCalendar.Always() : CalendarService.ToEngine(c);
    }

    /// <summary>
    /// (Re)selects the ticket's policy and copies its numbers onto the ticket, then refreshes the clock. Called when a ticket is created and when something the
    /// policy depends on changes (priority, place). Time already paused and the clock's start are kept, so a recalculation never gives a ticket back time it used.
    /// </summary>
    public async Task ApplyAsync(Ticket t, DateTimeOffset now, CancellationToken ct = default)
    {
        var facts = await MatchFactsBuilder.BuildAsync(db, t.ServiceId, t.LocationId, t.Priority, ct);
        var policy = await MatchAsync(facts, ct);
        if (policy is null) { t.SlaPolicyId = null; t.SlaCalendarId = null; SlaClock.Refresh(t, WorkingCalendar.Always(), now); return; }
        t.SlaPolicyId = policy.Id; t.SlaCalendarId = await CalendarIdAsync(policy, t.LocationId, ct); t.SlaStartAt ??= t.CreatedAt;
        t.SlaFirstResponseMinutes = policy.FirstResponseMinutes; t.SlaResolutionMinutes = policy.ResolutionMinutes; t.SlaEscalate1Percent = policy.Escalate1Percent; t.SlaEscalate2Percent = policy.Escalate2Percent;
        await RefreshAsync(t, now, ct);
    }

    /// <summary>Recomputes due times, state and escalation; records an escalation on the ticket's internal timeline when the level rose.</summary>
    public async Task RefreshAsync(Ticket t, DateTimeOffset now, CancellationToken ct = default)
    {
        var rose = SlaClock.Refresh(t, await EngineAsync(t.SlaCalendarId, ct), now);
        if (rose == 0) return;
        var firstMissed = t.FirstRespondedAt is null && t.FirstResponseDueAt is { } f && now > f;
        var text = rose switch
        {
            1 => $"SLA warning: {t.SlaEscalate1Percent}% of the time allowed has been used.",
            2 => $"SLA at risk: {t.SlaEscalate2Percent}% of the time allowed has been used.",
            _ => firstMissed ? "SLA breached: nobody has responded within the first-response target." : "SLA breached: the resolution target has been missed.",
        };
        db.TicketEvents.Add(new TicketEvent { TenantId = t.TenantId, TicketId = t.Id, Kind = TicketEventKind.Escalation, ActorLabel = "System", Internal = true, Body = text, CreatedAt = now });
        audit.Record("ticket.sla_escalation", "ticket", t.Id.ToString(), Outcome.Success, new ClientInfo(null, null, null), null, "system (SLA)",
            new Dictionary<string, string> { ["number"] = t.Number, ["level"] = rose.ToString() }, [new AuditDiff("slaState", "", Lower(t.SlaState))]);
        await notifications.EscalationAsync(t, rose, ct);
    }

    /// <summary>Restarts a held ticket's clock, adding the working time spent on hold to both deadlines.</summary>
    public async Task ResumeAsync(Ticket t, DateTimeOffset now, CancellationToken ct = default)
    {
        SlaClock.Resume(t, await EngineAsync(t.SlaCalendarId, ct), now);
        await RefreshAsync(t, now, ct);
    }

    // ================================================================ periodic check
    /// <summary>
    /// Re-reads the clock of every open, running ticket across all organisations: updates the state, and records an escalation when a threshold has been crossed.
    /// Idempotent (a level is only recorded when it rises), so overlapping runs and several instances are harmless. Returns how many tickets changed.
    /// </summary>
    public async Task<int> RunDueAsync(CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var open = new[] { TicketStatus.Submitted, TicketStatus.Assigned, TicketStatus.InProgress, TicketStatus.Reopened };
        var due = await db.Tickets.IgnoreQueryFilters().AsNoTracking().Where(t => t.SlaPolicyId != null && open.Contains(t.Status) && t.SlaPausedAt == null)
            .OrderBy(t => t.ResolutionDueAt).Take(2000).Select(t => new { t.Id, t.TenantId }).ToListAsync(ct);
        var changed = 0;
        foreach (var group in due.GroupBy(d => d.TenantId))
        {
            using (tenant.Switch(group.Key))
            {
                db.ResetTracking(); _calendars.Clear();
                var ids = group.Select(g => g.Id).ToList();
                var tickets = await db.Tickets.Where(t => ids.Contains(t.Id)).ToListAsync(ct);
                foreach (var t in tickets)
                {
                    var before = (t.SlaState, t.SlaEscalationLevel, t.ResolutionDueAt);
                    await RefreshAsync(t, now, ct);
                    if (before != (t.SlaState, t.SlaEscalationLevel, t.ResolutionDueAt)) changed++;
                }
                try { await db.SaveChangesAsync(ct); }
                catch (DbUpdateConcurrencyException) { db.ResetTracking(); } // someone acted on a ticket meanwhile: the next run picks it up
            }
        }
        return changed;
    }
}

/// <summary>Default policies every organisation starts with, so tickets have targets from day one. Idempotent: only when the organisation has no policy at all.</summary>
public static class SlaDefaults
{
    public static async Task SeedAsync(IAppDb db, Guid tenantId, DateTimeOffset now, CancellationToken ct = default)
    {
        if (await db.SlaPolicies.IgnoreQueryFilters().AnyAsync(p => p.TenantId == tenantId, ct)) return;
        // working minutes on the organisation's calendar: first response, resolution
        var defaults = new (string Name, TicketPriority Priority, int First, int Resolve)[]
        {
            ("Urgent requests", TicketPriority.Urgent, 15, 240), ("High priority requests", TicketPriority.High, 60, 480),
            ("Standard requests", TicketPriority.Medium, 240, 1440), ("Low priority requests", TicketPriority.Low, 480, 2880),
        };
        var pos = 0;
        foreach (var d in defaults)
            db.SlaPolicies.Add(new SlaPolicy { TenantId = tenantId, Name = d.Name, Position = pos += 10, Priority = d.Priority, FirstResponseMinutes = d.First, ResolutionMinutes = d.Resolve, CreatedAt = now, UpdatedAt = now });
        await db.SaveChangesAsync(ct);
    }
}
