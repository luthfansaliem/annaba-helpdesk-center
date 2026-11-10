using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Application.Helpdesk;

public sealed record SaveViewRequest(string? Name, Dictionary<string, string>? Filter, bool? Shared);
public sealed record TicketViewItem(Guid Id, string Name, IReadOnlyDictionary<string, string> Filter, bool Shared, bool Mine, string? OwnerName, bool CanEdit);

/// <summary>
/// TKT-011. Saved queue filters. A view is only a set of filter values (the same names the queue takes), validated on the way in so a stored view can always be applied.
/// Everyone who reads the queue keeps personal views; people who may assign tickets can also share one with everyone who reads the queue.
/// </summary>
public sealed class TicketViewService(IAppDb db, IClock clock, AuditWriter audit, AuthorizationService authz)
{
    public const int MaxPersonal = 30, MaxShared = 50;
    private static readonly string[] Statuses = ["open", "submitted", "assigned", "inprogress", "onhold", "resolved", "closed", "cancelled", "reopened", "pendingapproval", "rejected"];
    private static readonly string[] Priorities = ["low", "medium", "high", "urgent"];
    private static readonly string[] SlaStates = ["none", "ontrack", "atrisk", "breached", "paused", "met"];
    private static readonly string[] GuidKeys = ["assigneeId", "teamId", "locationId", "serviceId"];
    private static readonly string[] BoolKeys = ["unassigned", "assignedToMe"];
    private static readonly string[] DateKeys = ["from", "to"];

    private async Task<IReadOnlySet<string>> RequireQueueAsync(SessionPrincipal who, CancellationToken ct)
    {
        var p = await authz.GetEffectivePermissionsAsync(who.UserId, ct);
        if (!p.Contains(Permissions.TicketsReadTeam) && !p.Contains(Permissions.TicketsReadAll)) throw DomainException.Denied(Permissions.TicketsReadTeam);
        return p;
    }

    private static Dictionary<string, string> ParseFilter(string json)
    {
        try { return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? []; } catch (JsonException) { return []; }
    }

    private static DomainException Bad(string key, string message) => DomainException.Validation("Please correct the highlighted fields.", new Dictionary<string, string> { [key] = message });

    /// <summary>Keeps only known, well-formed, non-empty values. Anything else is refused, so a saved view can always be applied.</summary>
    public static Dictionary<string, string> NormaliseFilter(Dictionary<string, string>? raw)
    {
        var o = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (k, v0) in raw ?? [])
        {
            var v = (v0 ?? "").Trim(); if (v.Length == 0) continue;
            if (k == "q") { if (v.Length > 100) throw Bad("filter", "The search text is too long (100 characters)."); o[k] = v; }
            else if (k == "status") { if (!Statuses.Contains(v.ToLowerInvariant())) throw Bad("filter", "Unknown status."); o[k] = v.ToLowerInvariant(); }
            else if (k == "priority") { if (!Priorities.Contains(v.ToLowerInvariant())) throw Bad("filter", "Unknown priority."); o[k] = v.ToLowerInvariant(); }
            else if (k == "sla") { if (!SlaStates.Contains(v.ToLowerInvariant())) throw Bad("filter", "Unknown SLA state."); o[k] = v.ToLowerInvariant(); }
            else if (k == "sort") { if (v is not ("due" or "updated")) throw Bad("filter", "Unknown sort order."); o[k] = v; }
            else if (GuidKeys.Contains(k)) { if (!Guid.TryParse(v, out var g)) throw Bad("filter", $"'{k}' must be an id."); o[k] = g.ToString("D"); }
            else if (BoolKeys.Contains(k)) { if (v != "true") { if (v == "false") continue; throw Bad("filter", $"'{k}' must be true or false."); } o[k] = "true"; }
            else if (DateKeys.Contains(k)) { if (!DateOnly.TryParseExact(v, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) throw Bad("filter", $"'{k}' must be a date (yyyy-MM-dd)."); o[k] = d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); }
            else throw Bad("filter", $"Unknown filter '{k}'.");
        }
        return o;
    }

    private static string Clean(string? name, out DomainException? error)
    {
        var n = (name ?? "").Trim(); error = null;
        if (n.Length is < 2 or > 60) error = Bad("name", "Use 2–60 characters.");
        return n;
    }

    public async Task<IReadOnlyList<TicketViewItem>> ListAsync(SessionPrincipal who, CancellationToken ct = default)
    {
        var perms = await RequireQueueAsync(who, ct); var me = who.UserId; var canShare = perms.Contains(Permissions.TicketsAssign);
        var rows = await db.TicketViews.AsNoTracking().Where(v => v.OwnerId == me || v.Shared).OrderBy(v => v.Name).ThenBy(v => v.Id)
            .Select(v => new { v.Id, v.Name, v.FilterJson, v.Shared, v.OwnerId, Owner = db.Users.Where(u => u.Id == v.OwnerId).Select(u => u.Name).FirstOrDefault() }).ToListAsync(ct);
        return [.. rows.OrderByDescending(r => r.OwnerId == me).ThenBy(r => r.Name).Select(r => new TicketViewItem(r.Id, r.Name, ParseFilter(r.FilterJson), r.Shared, r.OwnerId == me, r.Owner, r.OwnerId == me || (r.Shared && canShare)))];
    }

    private async Task<TicketViewItem> ItemAsync(Guid id, SessionPrincipal who, CancellationToken ct) => (await ListAsync(who, ct)).First(v => v.Id == id);

    public async Task<TicketViewItem> CreateAsync(SaveViewRequest req, SessionPrincipal who, ClientInfo client, CancellationToken ct = default)
    {
        var perms = await RequireQueueAsync(who, ct);
        var name = Clean(req.Name, out var bad); if (bad is not null) throw bad;
        var filter = NormaliseFilter(req.Filter); var shared = req.Shared == true;
        if (shared && !perms.Contains(Permissions.TicketsAssign)) throw DomainException.Denied(Permissions.TicketsAssign);
        await CheckLimitsAsync(null, who.UserId, name, shared, ct);
        var now = clock.UtcNow;
        var v = new TicketView { OwnerId = who.UserId, Name = name, FilterJson = JsonSerializer.Serialize(filter), Shared = shared, CreatedAt = now, UpdatedAt = now };
        db.TicketViews.Add(v);
        audit.Record("ticket.view.create", "ticket_view", v.Id.ToString(), Outcome.Success, client, who.UserId, who.Email, new Dictionary<string, string> { ["name"] = name, ["shared"] = shared.ToString() });
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateException) { throw Bad("name", "You already have a view with this name."); }
        return await ItemAsync(v.Id, who, ct);
    }

    public async Task<TicketViewItem> UpdateAsync(Guid id, SaveViewRequest req, SessionPrincipal who, ClientInfo client, CancellationToken ct = default)
    {
        var perms = await RequireQueueAsync(who, ct);
        var v = await db.TicketViews.FirstOrDefaultAsync(x => x.Id == id && (x.OwnerId == who.UserId || x.Shared), ct) ?? throw DomainException.NotFound("View");
        if (v.OwnerId != who.UserId && !perms.Contains(Permissions.TicketsAssign)) throw DomainException.DeniedBecause("Only the owner, or staff who assign tickets, can change a shared view.");
        var name = Clean(req.Name, out var bad); if (bad is not null) throw bad;
        var filter = NormaliseFilter(req.Filter); var shared = req.Shared ?? v.Shared;
        if (shared != v.Shared && v.OwnerId != who.UserId) throw DomainException.DeniedBecause("Only the owner can change whether a view is shared.");
        if (shared && !v.Shared && !perms.Contains(Permissions.TicketsAssign)) throw DomainException.Denied(Permissions.TicketsAssign);
        await CheckLimitsAsync(v.Id, v.OwnerId, name, shared, ct);
        v.Name = name; v.FilterJson = JsonSerializer.Serialize(filter); v.Shared = shared; v.UpdatedAt = clock.UtcNow;
        audit.Record("ticket.view.update", "ticket_view", v.Id.ToString(), Outcome.Success, client, who.UserId, who.Email, new Dictionary<string, string> { ["name"] = name, ["shared"] = shared.ToString() });
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateException) { throw Bad("name", "You already have a view with this name."); }
        return await ItemAsync(v.Id, who, ct);
    }

    public async Task DeleteAsync(Guid id, SessionPrincipal who, ClientInfo client, CancellationToken ct = default)
    {
        var perms = await RequireQueueAsync(who, ct);
        var v = await db.TicketViews.FirstOrDefaultAsync(x => x.Id == id && (x.OwnerId == who.UserId || x.Shared), ct) ?? throw DomainException.NotFound("View");
        if (v.OwnerId != who.UserId && !perms.Contains(Permissions.TicketsAssign)) throw DomainException.DeniedBecause("Only the owner, or staff who assign tickets, can remove a shared view.");
        db.TicketViews.Remove(v);
        audit.Record("ticket.view.delete", "ticket_view", id.ToString(), Outcome.Success, client, who.UserId, who.Email, new Dictionary<string, string> { ["name"] = v.Name });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Names are unique per owner (ignoring case) and among shared views; each person and the organisation have a ceiling.</summary>
    private async Task CheckLimitsAsync(Guid? self, Guid owner, string name, bool shared, CancellationToken ct)
    {
        var lower = name.ToLowerInvariant();
        if (await db.TicketViews.AnyAsync(x => x.Id != self && x.OwnerId == owner && x.Name.ToLower() == lower, ct)) throw Bad("name", "You already have a view with this name.");
        if (shared && await db.TicketViews.AnyAsync(x => x.Id != self && x.Shared && x.Name.ToLower() == lower, ct)) throw Bad("name", "A shared view with this name already exists.");
        if (self is null && await db.TicketViews.CountAsync(x => x.OwnerId == owner, ct) >= MaxPersonal) throw Bad("name", $"You can keep at most {MaxPersonal} views. Remove one first.");
        if (shared && await db.TicketViews.CountAsync(x => x.Id != self && x.Shared, ct) >= MaxShared) throw Bad("name", $"At most {MaxShared} views can be shared.");
    }
}
