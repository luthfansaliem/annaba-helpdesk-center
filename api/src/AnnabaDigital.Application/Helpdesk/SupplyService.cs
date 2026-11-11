using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Application.Helpdesk;

public sealed record SupplyCatalogItem(Guid Id, string Sku, string Name, string Category, string Unit, int Available, decimal? UnitCost);
public sealed record SupplyLineDto(Guid ItemId, string Sku, string Name, string Unit, int Quantity, int Issued, int BackOrder, int Available, decimal? UnitCost);
public sealed record SupplyRequestDto(Guid Id, string Number, Guid RequesterId, string? Requester, Guid LocationId, string Place, string? Note, string Status, decimal EstimatedCost,
    string? Approver, string? DecisionNote, DateTimeOffset CreatedAt, DateTimeOffset? FulfilledAt, IReadOnlyList<SupplyLineDto> Lines, bool CanCancel, bool CanDecide, bool CanFulfil, int Version);
public sealed record SupplyLineInput(Guid? ItemId, int? Quantity);
public sealed record CreateSupplyRequest(Guid? LocationId, string? Note, SupplyLineInput[]? Lines);
public sealed record FulfilSupplyRequest(Dictionary<Guid, int>? Lines);
public sealed record SupplySettingsDto(decimal? ApprovalAbove, Guid? DefaultLocationId);
public sealed record SupplySummary(int Pending, int Open, int PartlyFulfilled, int Fulfilled30, decimal Issued30Cost, IReadOnlyList<CountItem> TopItems);
public sealed record PagedSupplies(IReadOnlyList<SupplyRequestDto> Data, int Page, int PageSize, int Total);

/// <summary>
/// INV-002 (workflow §7.5). Anyone signed in asks for stock items (like making a request). Above the organisation's approval amount the request waits for the
/// requester's manager (or, without one, for the people who fulfil). People with <c>supplies.fulfil</c> issue the items from the chosen stock place through the
/// ledger, partly if stock is short (the rest is a back-order), and close what will not be issued. The requester is told when items are ready to collect.
/// </summary>
public sealed class SupplyService(IAppDb db, IClock clock, AuditWriter audit, AuthorizationService authz, NotificationService notifications, InventoryService inventory)
{
    private static string? Clean(string? s) { var t = (s ?? "").Trim(); return t.Length == 0 ? null : t; }
    private static DomainException Invalid(Dictionary<string, string> e) => DomainException.Validation("Please correct the highlighted fields.", e);
    public static string StatusName(SupplyStatus s) => s switch
    {
        SupplyStatus.Pending => "pending", SupplyStatus.PartlyFulfilled => "partly_fulfilled", SupplyStatus.Fulfilled => "fulfilled", SupplyStatus.Rejected => "rejected",
        SupplyStatus.Cancelled => "cancelled", SupplyStatus.Closed => "closed", _ => "open",
    };
    private async Task<bool> FulfillerAsync(Guid userId, CancellationToken ct) => (await authz.GetEffectivePermissionsAsync(userId, ct)).Contains(Permissions.SuppliesFulfil);

    private sealed record Place(Guid Id, Guid? ParentId, string Name);
    private async Task<Dictionary<Guid, Place>> PlacesAsync(CancellationToken ct) => await db.Locations.AsNoTracking().Select(l => new Place(l.Id, l.ParentId, l.Name)).ToDictionaryAsync(l => l.Id, ct);
    private static string PathOf(Dictionary<Guid, Place> places, Guid id)
    {
        var parts = new List<string>();
        for (Guid? c = id; c is { } k && places.TryGetValue(k, out var p) && parts.Count < 6; c = p.ParentId) parts.Insert(0, p.Name);
        return string.Join(" › ", parts);
    }

    private async Task<SupplyRequestDto> DtoAsync(SupplyRequest r, Guid viewer, bool fulfiller, CancellationToken ct)
    {
        var ids = r.Lines.Select(l => l.ItemId).ToList();
        var items = await db.InventoryItems.AsNoTracking().Where(i => ids.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        var stock = await db.StockBalances.AsNoTracking().Where(b => ids.Contains(b.ItemId) && b.LocationId == r.LocationId).ToDictionaryAsync(b => b.ItemId, b => b.Quantity, ct);
        var people = new[] { r.RequesterId, r.ApproverId ?? Guid.Empty }.Where(x => x != Guid.Empty).ToList();
        var names = await db.Users.AsNoTracking().Where(u => people.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Name, ct);
        var places = await PlacesAsync(ct);
        var mine = r.RequesterId == viewer;
        var live = r.Status is SupplyStatus.Open or SupplyStatus.PartlyFulfilled;
        return new SupplyRequestDto(r.Id, r.Number, r.RequesterId, names.GetValueOrDefault(r.RequesterId), r.LocationId, places.ContainsKey(r.LocationId) ? PathOf(places, r.LocationId) : "—", r.Note,
            StatusName(r.Status), r.EstimatedCost, r.ApproverId is { } a ? names.GetValueOrDefault(a) : null, r.DecisionNote, r.CreatedAt, r.FulfilledAt,
            [.. r.Lines.OrderBy(l => items[l.ItemId].Name).Select(l => new SupplyLineDto(l.ItemId, items[l.ItemId].Sku, items[l.ItemId].Name, items[l.ItemId].Unit, l.Quantity, l.Issued, live ? l.Quantity - l.Issued : 0,
                stock.GetValueOrDefault(l.ItemId), l.UnitCost))],
            mine && r.Status is SupplyStatus.Pending or SupplyStatus.Open && r.Lines.All(l => l.Issued == 0),
            r.Status == SupplyStatus.Pending && (r.ApproverId == viewer || (r.ApproverId is null && fulfiller)),
            fulfiller && live, r.Version);
    }

    private async Task<SupplyRequestDto> GetForAsync(Guid id, SessionPrincipal p, CancellationToken ct)
    {
        var fulfiller = await FulfillerAsync(p.UserId, ct);
        var r = await db.SupplyRequests.AsNoTracking().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Request");
        if (!(r.RequesterId == p.UserId || r.ApproverId == p.UserId || fulfiller)) throw DomainException.NotFound("Request");
        return await DtoAsync(r, p.UserId, fulfiller, ct);
    }
    public Task<SupplyRequestDto> GetAsync(Guid id, SessionPrincipal p, CancellationToken ct = default) => GetForAsync(id, p, ct);

    // ================================================================ asking
    /// <summary>Active items with what is available at the place (the organisation's default place when none is given).</summary>
    public async Task<IReadOnlyList<SupplyCatalogItem>> CatalogAsync(Guid? locationId, string? q, CancellationToken ct = default)
    {
        var loc = locationId ?? (await SettingsAsync(ct)).DefaultLocationId;
        var query = db.InventoryItems.AsNoTracking().Where(i => i.ArchivedAt == null);
        if (Clean(q) is { } text)
        {
            var like = "%" + text.ToLowerInvariant().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            query = query.Where(i => EF.Functions.Like(i.Name.ToLower(), like, "\\") || EF.Functions.Like(i.Sku.ToLower(), like, "\\"));
        }
        var items = await query.OrderBy(i => i.Category).ThenBy(i => i.Name).Take(500).ToListAsync(ct);
        var stock = loc is null ? [] : await db.StockBalances.AsNoTracking().Where(b => b.LocationId == loc).ToDictionaryAsync(b => b.ItemId, b => b.Quantity, ct);
        return [.. items.Select(i => new SupplyCatalogItem(i.Id, i.Sku, i.Name, i.Category, i.Unit, stock.GetValueOrDefault(i.Id), i.UnitCost))];
    }

    public async Task<SupplyRequestDto> CreateAsync(CreateSupplyRequest req, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var settings = await SettingsAsync(ct);
        var e = new Dictionary<string, string>();
        var loc = req.LocationId ?? settings.DefaultLocationId;
        if (loc is null || !await db.Locations.AnyAsync(l => l.Id == loc && l.ArchivedAt == null, ct)) e["locationId"] = "Choose where to collect from.";
        var lines = (req.Lines ?? []).Where(l => l.ItemId is not null).GroupBy(l => l.ItemId!.Value).Select(g => (Item: g.Key, Qty: g.Sum(x => x.Quantity ?? 0))).ToList();
        if (lines.Count == 0) e["lines"] = "Add at least one item.";
        else if (lines.Count > SupplyRules.MaxLines) e["lines"] = $"Ask for at most {SupplyRules.MaxLines} different items.";
        else if (lines.Any(l => l.Qty is < 1 or > SupplyRules.MaxQuantity)) e["lines"] = "Use a quantity of 1 or more for every item.";
        var ids = lines.Select(l => l.Item).ToList();
        var items = await db.InventoryItems.AsNoTracking().Where(i => ids.Contains(i.Id) && i.ArchivedAt == null).ToDictionaryAsync(i => i.Id, ct);
        if (!e.ContainsKey("lines") && items.Count != ids.Count) e["lines"] = "One of the items cannot be asked for.";
        var note = Clean(req.Note); if (note is { Length: > 500 }) e["note"] = "Use at most 500 characters.";
        if (e.Count > 0) throw Invalid(e);

        var estimate = Math.Round(lines.Sum(l => l.Qty * (items[l.Item].UnitCost ?? 0)), 2);
        var needsApproval = settings.ApprovalAbove is { } limit && estimate >= limit;
        var manager = needsApproval ? await db.Profiles.AsNoTracking().Where(p => p.UserId == actor.UserId).Select(p => p.ManagerId).FirstOrDefaultAsync(ct) : null;
        if (manager is { } m && !await db.Users.AnyAsync(u => u.Id == m && u.Status == UserStatus.Active, ct)) manager = null;
        var now = clock.UtcNow;
        var r = new SupplyRequest
        {
            RequesterId = actor.UserId, LocationId = loc!.Value, Note = note, EstimatedCost = estimate, Status = needsApproval ? SupplyStatus.Pending : SupplyStatus.Open,
            ApproverId = manager, CreatedAt = now, UpdatedAt = now,
            Lines = [.. lines.Select(l => new SupplyRequestLine { ItemId = l.Item, Quantity = l.Qty, UnitCost = items[l.Item].UnitCost })],
        };
        foreach (var l in r.Lines) l.RequestId = r.Id;
        for (var attempt = 0; ; attempt++)
        {
            var year = now.Year;
            var count = await db.SupplyRequests.CountAsync(x => x.CreatedAt >= new DateTimeOffset(year, 1, 1, 0, 0, 0, TimeSpan.Zero), ct);
            r.Number = $"SR-{year}-{count + 1 + attempt:0000}";
            if (attempt == 0)
            {
                db.SupplyRequests.Add(r);
                audit.Record("supply.request", "supply_request", r.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email,
                    new Dictionary<string, string> { ["lines"] = r.Lines.Count.ToString(), ["estimate"] = estimate.ToString(System.Globalization.CultureInfo.InvariantCulture), ["approval"] = needsApproval ? "yes" : "no" });
                var title = $"{actor.Name} asks for supplies";
                var body = string.Join(", ", r.Lines.Take(5).Select(l => $"{l.Quantity} × {items[l.ItemId].Name}"));
                if (needsApproval && manager is { } who) await notifications.SendAsync(who, "supply.approval", $"{title}: your approval is needed", body, "/supplies", null, ct: ct);
                else await TellFulfillersAsync(needsApproval ? $"{title}: approval needed" : title, body, actor.UserId, ct);
            }
            try { await db.SaveChangesAsync(ct); break; }
            catch (DbUpdateException) when (attempt < 3) { }
        }
        return await GetForAsync(r.Id, actor, ct);
    }

    public async Task<SupplyRequestDto> CancelAsync(Guid id, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var r = await db.SupplyRequests.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id && x.RequesterId == actor.UserId, ct) ?? throw DomainException.NotFound("Request");
        if (r.Status is not (SupplyStatus.Pending or SupplyStatus.Open) || r.Lines.Any(l => l.Issued > 0)) throw DomainException.Conflict("Only a request nothing was issued for can be cancelled.");
        r.Status = SupplyStatus.Cancelled; r.UpdatedAt = clock.UtcNow; r.Version++;
        audit.Record("supply.cancel", "supply_request", r.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["number"] = r.Number });
        await db.SaveChangesAsync(ct);
        return await GetForAsync(id, actor, ct);
    }

    // ================================================================ deciding and fulfilling
    public async Task<SupplyRequestDto> DecideAsync(Guid id, bool approve, string? noteText, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var fulfiller = await FulfillerAsync(actor.UserId, ct);
        var r = await db.SupplyRequests.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Request");
        if (!(r.ApproverId == actor.UserId || (r.ApproverId is null && fulfiller))) throw (fulfiller || r.RequesterId == actor.UserId ? DomainException.DeniedBecause("Only the requester's manager decides this request.") : DomainException.NotFound("Request"));
        if (r.Status != SupplyStatus.Pending) throw DomainException.Conflict("This request is not waiting for approval.");
        var note = Clean(noteText);
        if (!approve && note is null) throw Invalid(new Dictionary<string, string> { ["note"] = "Say why it is not approved." });
        if (note is { Length: > 500 }) throw Invalid(new Dictionary<string, string> { ["note"] = "Use at most 500 characters." });
        r.Status = approve ? SupplyStatus.Open : SupplyStatus.Rejected; r.DecidedById = actor.UserId; r.DecidedAt = clock.UtcNow; r.DecisionNote = note; r.UpdatedAt = clock.UtcNow; r.Version++;
        audit.Record(approve ? "supply.approve" : "supply.reject", "supply_request", r.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["number"] = r.Number });
        await notifications.SendAsync(r.RequesterId, "supply.update", approve ? $"{r.Number} is approved" : $"{r.Number} is not approved", note, "/supplies", null, ct: ct);
        if (approve) await TellFulfillersAsync($"{r.Number} is approved and waits to be issued", null, actor.UserId, ct);
        await db.SaveChangesAsync(ct);
        return await GetForAsync(id, actor, ct);
    }

    /// <summary>Issues the given quantities (at most what is still outstanding per line) from the request's place in one transaction. When stock is short the
    /// whole fulfilment is refused with what is left, so it can be repeated with smaller quantities; what stays outstanding is the back-order.</summary>
    public async Task<SupplyRequestDto> FulfilAsync(Guid id, FulfilSupplyRequest req, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var r = await db.SupplyRequests.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Request");
        if (r.Status is not (SupplyStatus.Open or SupplyStatus.PartlyFulfilled)) throw DomainException.Conflict(r.Status == SupplyStatus.Pending ? "This request waits for approval first." : "This request is no longer open.");
        var give = (req.Lines ?? []).Where(kv => kv.Value > 0).ToList();
        if (give.Count == 0) throw Invalid(new Dictionary<string, string> { ["lines"] = "Give a quantity for at least one item." });
        foreach (var (itemId, qty) in give)
        {
            var line = r.Lines.FirstOrDefault(l => l.ItemId == itemId) ?? throw Invalid(new Dictionary<string, string> { ["lines"] = "An item is not part of this request." });
            if (qty > line.Quantity - line.Issued) throw Invalid(new Dictionary<string, string> { [$"lines.{itemId}"] = $"At most {line.Quantity - line.Issued} is still outstanding." });
        }
        var ids = give.Select(g => g.Key).ToList();
        var items = await db.InventoryItems.AsNoTracking().Where(i => ids.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        var who = await db.Users.AsNoTracking().Where(u => u.Id == r.RequesterId).Select(u => u.Name).FirstAsync(ct);
        await db.InTransactionAsync(async () =>
        {
            foreach (var (itemId, qty) in give)
            {
                await inventory.PostAsync(items[itemId], r.LocationId, -qty, StockMovementKind.Issue, items[itemId].UnitCost, $"{r.Number} · {who}", r.Id, actor.UserId, ct);
                r.Lines.First(l => l.ItemId == itemId).Issued += qty;
            }
            var done = r.Lines.All(l => l.Issued >= l.Quantity);
            r.Status = done ? SupplyStatus.Fulfilled : SupplyStatus.PartlyFulfilled;
            if (done) r.FulfilledAt = clock.UtcNow;
            r.UpdatedAt = clock.UtcNow; r.Version++;
            audit.Record("supply.fulfil", "supply_request", r.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email,
                new Dictionary<string, string> { ["number"] = r.Number, ["lines"] = give.Count.ToString(), ["complete"] = done ? "yes" : "no" });
            var body = string.Join(", ", give.Select(g => $"{g.Value} × {items[g.Key].Name}"));
            await notifications.SendAsync(r.RequesterId, "supply.update", done ? $"{r.Number} is ready to collect" : $"Part of {r.Number} is ready to collect", body, "/supplies", null, ct: ct);
            await db.SaveChangesAsync(ct);
            return true;
        }, ct);
        return await GetForAsync(id, actor, ct);
    }

    /// <summary>Ends a partly fulfilled request: the back-order will not be issued.</summary>
    public async Task<SupplyRequestDto> CloseAsync(Guid id, string? noteText, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var r = await db.SupplyRequests.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Request");
        if (r.Status is not (SupplyStatus.Open or SupplyStatus.PartlyFulfilled)) throw DomainException.Conflict("This request is no longer open.");
        var note = Clean(noteText); if (note is null) throw Invalid(new Dictionary<string, string> { ["note"] = "Say why the rest will not be issued." });
        r.Status = r.Lines.Any(l => l.Issued > 0) ? SupplyStatus.Closed : SupplyStatus.Rejected; r.DecisionNote = note; r.UpdatedAt = clock.UtcNow; r.Version++;
        audit.Record("supply.close", "supply_request", r.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["number"] = r.Number });
        await notifications.SendAsync(r.RequesterId, "supply.update", $"{r.Number} is closed", note, "/supplies", null, ct: ct);
        await db.SaveChangesAsync(ct);
        return await GetForAsync(id, actor, ct);
    }

    // ================================================================ lists, settings, summary
    /// <summary><c>scope</c>: <c>mine</c>, <c>approvals</c> (waiting for me) or <c>desk</c> (everything, <c>supplies.fulfil</c>).</summary>
    public async Task<PagedSupplies> ListAsync(string? scope, string? status, int page, int pageSize, SessionPrincipal p, CancellationToken ct = default)
    {
        var fulfiller = await FulfillerAsync(p.UserId, ct);
        var s = (scope ?? "mine").Trim().ToLowerInvariant();
        if (s == "desk" && !fulfiller) throw DomainException.Denied(Permissions.SuppliesFulfil);
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100);
        var q = db.SupplyRequests.AsNoTracking().Include(x => x.Lines).AsQueryable();
        q = s switch
        {
            "desk" => q,
            "approvals" => q.Where(r => r.Status == SupplyStatus.Pending && (r.ApproverId == p.UserId || (fulfiller && r.ApproverId == null))),
            _ => q.Where(r => r.RequesterId == p.UserId),
        };
        if (Clean(status)?.ToLowerInvariant() is { } st)
        {
            if (st == "active") q = q.Where(r => r.Status == SupplyStatus.Pending || r.Status == SupplyStatus.Open || r.Status == SupplyStatus.PartlyFulfilled);
            else
            {
                var want = Enum.GetValues<SupplyStatus>().Where(x => StatusName(x) == st).ToList();
                if (want.Count == 0) throw Invalid(new Dictionary<string, string> { ["status"] = "Use active, pending, open, partly_fulfilled, fulfilled, rejected, cancelled or closed." });
                q = q.Where(r => want.Contains(r.Status));
            }
        }
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(r => r.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var list = new List<SupplyRequestDto>();
        foreach (var r in rows) list.Add(await DtoAsync(r, p.UserId, fulfiller, ct));
        return new PagedSupplies(list, page, pageSize, total);
    }

    public async Task<SupplySettingsDto> SettingsAsync(CancellationToken ct = default)
    {
        var s = await db.SupplySettings.AsNoTracking().FirstOrDefaultAsync(ct);
        return new SupplySettingsDto(s?.ApprovalAbove, s?.DefaultLocationId);
    }

    public async Task<SupplySettingsDto> SaveSettingsAsync(SupplySettingsDto req, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var e = new Dictionary<string, string>();
        if (req.ApprovalAbove is < 0) e["approvalAbove"] = "Use 0 or more, or leave it empty for no approval.";
        if (req.DefaultLocationId is { } l && !await db.Locations.AnyAsync(x => x.Id == l && x.ArchivedAt == null, ct)) e["defaultLocationId"] = "Choose an active place.";
        if (e.Count > 0) throw Invalid(e);
        var s = await db.SupplySettings.FirstOrDefaultAsync(ct);
        if (s is null) { s = new SupplySettings(); db.SupplySettings.Add(s); }
        s.ApprovalAbove = req.ApprovalAbove; s.DefaultLocationId = req.DefaultLocationId; s.UpdatedAt = clock.UtcNow;
        audit.Record("supply.settings", "settings", "supplies", Outcome.Success, client, actor.UserId, actor.Email,
            new Dictionary<string, string> { ["approvalAbove"] = req.ApprovalAbove?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none" });
        await db.SaveChangesAsync(ct);
        return await SettingsAsync(ct);
    }

    public async Task<SupplySummary> SummaryAsync(CancellationToken ct = default)
    {
        var since = clock.UtcNow.AddDays(-30);
        var reqs = await db.SupplyRequests.AsNoTracking().GroupBy(r => r.Status).Select(g => new { g.Key, N = g.Count(), Recent = g.Count(r => r.FulfilledAt >= since) }).ToListAsync(ct);
        int Of(SupplyStatus s) => reqs.Where(x => x.Key == s).Sum(x => x.N);
        var issued = await (from m in db.StockMovements.AsNoTracking() join r in db.SupplyRequests.AsNoTracking() on m.GroupId equals (Guid?)r.Id
                            where m.At >= since && m.Kind == StockMovementKind.Issue select new { m.ItemId, m.Quantity, m.UnitCost }).ToListAsync(ct);
        var ids = issued.Select(x => x.ItemId).Distinct().ToList();
        var names = await db.InventoryItems.AsNoTracking().Where(i => ids.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.Name, ct);
        return new SupplySummary(Of(SupplyStatus.Pending), Of(SupplyStatus.Open), Of(SupplyStatus.PartlyFulfilled), reqs.Where(x => x.Key == SupplyStatus.Fulfilled).Sum(x => x.Recent),
            Math.Round(issued.Sum(x => -x.Quantity * (x.UnitCost ?? 0)), 2),
            [.. issued.GroupBy(x => x.ItemId).Select(g => new CountItem(g.Key.ToString(), names.GetValueOrDefault(g.Key) ?? "—", -g.Sum(x => x.Quantity))).OrderByDescending(x => x.Count).Take(8)]);
    }

    private async Task TellFulfillersAsync(string title, string? body, Guid except, CancellationToken ct)
    {
        var people = await db.Users.AsNoTracking().Where(u => u.Status == UserStatus.Active && u.Id != except
                && u.UserRoles.Any(ur => ur.Role!.ArchivedAt == null && ur.Role.Permissions.Any(p => p.PermissionKey == Permissions.SuppliesFulfil)))
            .OrderBy(u => u.Id).Select(u => u.Id).Take(20).ToListAsync(ct);
        foreach (var uid in people) await notifications.SendAsync(uid, "supply.new", title, body, "/supplies", null, ct: ct);
    }
}
