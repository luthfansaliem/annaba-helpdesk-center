using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Application.Helpdesk;

public sealed record StockAt(Guid LocationId, string Place, int Quantity, bool Low);
public sealed record InventoryItemDto(Guid Id, string Sku, string Name, string Category, string Unit, int ReorderLevel, int? ReorderQuantity, decimal? UnitCost, string? Description,
    bool Archived, int Total, bool Low, IReadOnlyList<StockAt> Stock, int Version);
public sealed record SaveInventoryItemRequest(string? Sku, string? Name, string? Category, string? Unit, int? ReorderLevel, int? ReorderQuantity, decimal? UnitCost, string? Description, int? Version = null);
public sealed record MovementDto(Guid Id, Guid ItemId, string Sku, string Item, Guid LocationId, string Place, string Kind, int Quantity, int BalanceAfter, decimal? UnitCost, string? Reference, string? By, DateTimeOffset At);
/// <summary><c>kind</c>: receipt (needs <c>inventory.manage</c>), issue (<c>inventory.manage</c>), adjustment (<c>inventory.adjust</c>, signed quantity, reason required) or transfer (<c>inventory.manage</c>, to <c>toLocationId</c>).</summary>
public sealed record MoveStockRequest(string? Kind, Guid? LocationId, int? Quantity, decimal? UnitCost, string? Reference, Guid? ToLocationId);
public sealed record ReorderLine(Guid ItemId, string Sku, string Name, string Unit, Guid LocationId, string Place, int Quantity, int ReorderLevel, int Suggested, decimal? UnitCost, decimal? Cost);
public sealed record InventorySummary(int Items, int LowLines, decimal? StockValue, int Issued30, int Received30, IReadOnlyList<CountItem> ByCategory, IReadOnlyList<CountItem> TopIssued, IReadOnlyList<DayCount> Issued30Days);
public sealed record StockTakeDto(Guid Id, Guid LocationId, string Place, string? Note, string? StartedBy, DateTimeOffset StartedAt, DateTimeOffset? ClosedAt, IReadOnlyList<StockTakeLineDto> Lines, int Counted, int Variances);
public sealed record StockTakeLineDto(Guid ItemId, string Sku, string Name, string Unit, int Expected, int? Counted, int? Variance);
public sealed record StartStockTakeRequest(Guid? LocationId, string? Note);
public sealed record CountRequest(Dictionary<Guid, int?>? Counts);

/// <summary>
/// INV-001/003/004 and RPT-005 (inventory). Items and the stock of each at each place, changed only through ledger movements: receipts, issues, adjustments with a
/// reason, transfers between places and stock-take corrections. A balance can never go below zero (the database refuses it). When stock at a place falls to the
/// reorder level the people who keep stock are told once; the suggested purchase list and the stock value follow. Reading needs <c>inventory.read</c>.
/// </summary>
public sealed class InventoryService(IAppDb db, IClock clock, AuditWriter audit, NotificationService notifications)
{
    private static string? Clean(string? s) { var t = (s ?? "").Trim(); return t.Length == 0 ? null : t; }
    private static DomainException Invalid(Dictionary<string, string> e) => DomainException.Validation("Please correct the highlighted fields.", e);
    private static string KindName(StockMovementKind k) => k switch
    {
        StockMovementKind.Receipt => "receipt", StockMovementKind.Issue => "issue", StockMovementKind.Adjustment => "adjustment", StockMovementKind.TransferOut => "transfer_out",
        StockMovementKind.TransferIn => "transfer_in", _ => "count",
    };
    private sealed record Place(Guid Id, Guid? ParentId, string Name);
    private async Task<Dictionary<Guid, Place>> PlacesAsync(CancellationToken ct) => await db.Locations.AsNoTracking().Select(l => new Place(l.Id, l.ParentId, l.Name)).ToDictionaryAsync(l => l.Id, ct);
    private static string PathOf(Dictionary<Guid, Place> places, Guid id)
    {
        var parts = new List<string>();
        for (Guid? c = id; c is { } k && places.TryGetValue(k, out var p) && parts.Count < 6; c = p.ParentId) parts.Insert(0, p.Name);
        return string.Join(" › ", parts);
    }

    // ================================================================ items
    public async Task<IReadOnlyList<InventoryItemDto>> ListAsync(string? q, string? category, Guid? locationId, bool lowOnly, bool includeArchived, CancellationToken ct = default)
    {
        var query = db.InventoryItems.AsNoTracking().AsQueryable();
        if (!includeArchived) query = query.Where(i => i.ArchivedAt == null);
        if (Clean(category)?.ToLowerInvariant() is { } c) query = query.Where(i => i.Category == c);
        if (Clean(q) is { } text)
        {
            var like = "%" + text.ToLowerInvariant().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            query = query.Where(i => EF.Functions.Like(i.Name.ToLower(), like, "\\") || EF.Functions.Like(i.Sku.ToLower(), like, "\\"));
        }
        var items = await query.OrderBy(i => i.Category).ThenBy(i => i.Name).Take(1000).ToListAsync(ct);
        var ids = items.Select(i => i.Id).ToList();
        var balances = await db.StockBalances.AsNoTracking().Where(b => ids.Contains(b.ItemId)).ToListAsync(ct);
        var places = await PlacesAsync(ct);
        var list = items.Select(i => ToDto(i, balances.Where(b => b.ItemId == i.Id && (locationId == null || b.LocationId == locationId)).ToList(), places)).ToList();
        return lowOnly ? [.. list.Where(x => x.Low)] : list;
    }

    private static InventoryItemDto ToDto(InventoryItem i, List<StockBalance> bs, Dictionary<Guid, Place> places)
    {
        var stock = bs.OrderBy(b => places.TryGetValue(b.LocationId, out var p) ? p.Name : "").Select(b => new StockAt(b.LocationId, places.ContainsKey(b.LocationId) ? PathOf(places, b.LocationId) : "—", b.Quantity, b.Quantity <= i.ReorderLevel)).ToList();
        return new InventoryItemDto(i.Id, i.Sku, i.Name, i.Category, i.Unit, i.ReorderLevel, i.ReorderQuantity, i.UnitCost, i.Description, i.ArchivedAt != null, stock.Sum(s => s.Quantity),
            i.ArchivedAt == null && stock.Any(s => s.Low), stock, i.Version);
    }

    public async Task<InventoryItemDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var i = await db.InventoryItems.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Item");
        return ToDto(i, await db.StockBalances.AsNoTracking().Where(b => b.ItemId == id).ToListAsync(ct), await PlacesAsync(ct));
    }

    public async Task<InventoryItemDto> SaveAsync(Guid? id, SaveInventoryItemRequest req, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var e = new Dictionary<string, string>();
        var sku = Clean(req.Sku)?.ToUpperInvariant();
        if (sku is null) e["sku"] = "Give a code (SKU)."; else if (sku.Length > 40 || !sku.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.')) e["sku"] = "Use letters, digits, - _ . (at most 40).";
        var name = Clean(req.Name); if (name is null) e["name"] = "Give a name."; else if (name.Length > 150) e["name"] = "Use at most 150 characters.";
        var cat = Clean(req.Category)?.ToLowerInvariant() ?? "other"; if (!InventoryRules.Categories.Contains(cat)) e["category"] = "Choose a category.";
        var unit = Clean(req.Unit)?.ToLowerInvariant() ?? "pcs"; if (unit.Length > 20) e["unit"] = "Use at most 20 characters.";
        if (req.ReorderLevel is < 0 or > InventoryRules.MaxQuantity) e["reorderLevel"] = "Use 0 or more.";
        if (req.ReorderQuantity is < 1 or > InventoryRules.MaxQuantity) e["reorderQuantity"] = "Use 1 or more, or leave it empty.";
        if (req.UnitCost is < 0) e["unitCost"] = "Use 0 or more.";
        var description = Clean(req.Description); if (description is { Length: > 1000 }) e["description"] = "Use at most 1000 characters.";
        if (sku is not null && await db.InventoryItems.AnyAsync(i => i.Sku == sku && i.Id != (id ?? Guid.Empty), ct)) e["sku"] = "Another item already uses this code.";
        if (e.Count > 0) throw Invalid(e);
        var now = clock.UtcNow;
        InventoryItem item;
        if (id is null) { item = new InventoryItem { CreatedAt = now }; db.InventoryItems.Add(item); }
        else
        {
            item = await db.InventoryItems.FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw DomainException.NotFound("Item");
            if (req.Version is { } v && v != item.Version) throw DomainException.Conflict("This item was changed by someone else. Reload and try again.");
            item.Version++;
        }
        item.Sku = sku!; item.Name = name!; item.Category = cat; item.Unit = unit; item.ReorderLevel = req.ReorderLevel ?? 0; item.ReorderQuantity = req.ReorderQuantity; item.UnitCost = req.UnitCost;
        item.Description = description; item.UpdatedAt = now;
        audit.Record(id is null ? "inventory.item_create" : "inventory.item_update", "inventory_item", item.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["sku"] = item.Sku });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw DomainException.Conflict("This item was changed by someone else. Reload and try again."); }
        catch (DbUpdateException) { throw Invalid(new Dictionary<string, string> { ["sku"] = "Another item already uses this code." }); }
        return await GetAsync(item.Id, ct);
    }

    public async Task<InventoryItemDto> ArchiveAsync(Guid id, bool archive, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var item = await db.InventoryItems.FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw DomainException.NotFound("Item");
        if (archive && await db.StockBalances.AnyAsync(b => b.ItemId == id && b.Quantity > 0, ct)) throw DomainException.Conflict("There is still stock of this item. Issue or adjust it to zero first.");
        item.ArchivedAt = archive ? clock.UtcNow : null; item.UpdatedAt = clock.UtcNow; item.Version++;
        audit.Record(archive ? "inventory.item_archive" : "inventory.item_restore", "inventory_item", item.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["sku"] = item.Sku });
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    // ================================================================ movements (the ledger)
    /// <summary>Adds a signed quantity to an item's balance at a place and writes the ledger line, in the caller's transaction. Refuses (409) when there is not enough.
    /// The low-stock alert goes out when the balance falls to the reorder level and is re-armed when it rises above it.</summary>
    internal async Task<StockMovement> PostAsync(InventoryItem item, Guid locationId, int delta, StockMovementKind kind, decimal? unitCost, string? reference, Guid? groupId, Guid byId, CancellationToken ct)
    {
        var now = clock.UtcNow;
        if (!await db.StockBalances.AnyAsync(b => b.ItemId == item.Id && b.LocationId == locationId, ct))
        {
            if (delta < 0) throw DomainException.Conflict($"There is no {item.Name} at this place.");
            db.StockBalances.Add(new StockBalance { ItemId = item.Id, LocationId = locationId, Quantity = 0, UpdatedAt = now });
            await db.SaveChangesAsync(ct);
        }
        var changed = await db.StockBalances.Where(b => b.ItemId == item.Id && b.LocationId == locationId && b.Quantity + delta >= 0)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.Quantity, b => b.Quantity + delta).SetProperty(b => b.UpdatedAt, now), ct);
        if (changed == 0)
        {
            var left = await db.StockBalances.AsNoTracking().Where(b => b.ItemId == item.Id && b.LocationId == locationId).Select(b => b.Quantity).FirstAsync(ct);
            throw DomainException.Conflict($"Only {left} {item.Unit} of {item.Name} left at this place.");
        }
        var balance = await db.StockBalances.AsNoTracking().FirstAsync(b => b.ItemId == item.Id && b.LocationId == locationId, ct);
        var m = new StockMovement { ItemId = item.Id, LocationId = locationId, Kind = kind, Quantity = delta, BalanceAfter = balance.Quantity, UnitCost = unitCost, Reference = reference, GroupId = groupId, ById = byId, At = now };
        db.StockMovements.Add(m);
        if (balance.Quantity <= item.ReorderLevel && balance.LowNotifiedAt is null && item.ArchivedAt is null)
        {
            if (await db.StockBalances.Where(b => b.ItemId == item.Id && b.LocationId == locationId && b.LowNotifiedAt == null).ExecuteUpdateAsync(s => s.SetProperty(b => b.LowNotifiedAt, now), ct) > 0)
            {
                var place = (await db.Locations.AsNoTracking().Where(l => l.Id == locationId).Select(l => l.Name).FirstOrDefaultAsync(ct)) ?? "—";
                var keepers = await db.Users.AsNoTracking().Where(u => u.Status == UserStatus.Active && u.UserRoles.Any(ur => ur.Role!.ArchivedAt == null && ur.Role.Permissions.Any(p => p.PermissionKey == Permissions.InventoryManage)))
                    .OrderBy(u => u.Id).Select(u => u.Id).Take(20).ToListAsync(ct);
                foreach (var uid in keepers) await notifications.SendAsync(uid, "stock.low", $"{item.Name} is low at {place}", $"{balance.Quantity} {item.Unit} left (reorder at {item.ReorderLevel}).", $"/inventory/{item.Id}", null, ct: ct);
            }
        }
        else if (balance.Quantity > item.ReorderLevel && balance.LowNotifiedAt is not null)
            await db.StockBalances.Where(b => b.ItemId == item.Id && b.LocationId == locationId).ExecuteUpdateAsync(s => s.SetProperty(b => b.LowNotifiedAt, (DateTimeOffset?)null), ct);
        return m;
    }

    public async Task<IReadOnlyList<MovementDto>> MoveAsync(Guid itemId, MoveStockRequest req, bool mayManage, bool mayAdjust, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var item = await db.InventoryItems.AsNoTracking().FirstOrDefaultAsync(i => i.Id == itemId, ct) ?? throw DomainException.NotFound("Item");
        var kind = (req.Kind ?? "").Trim().ToLowerInvariant();
        var e = new Dictionary<string, string>();
        if (kind is not ("receipt" or "issue" or "adjustment" or "transfer")) e["kind"] = "Use receipt, issue, adjustment or transfer.";
        else if (kind == "adjustment" ? !mayAdjust : !mayManage) throw DomainException.Denied(kind == "adjustment" ? Permissions.InventoryAdjust : Permissions.InventoryManage);
        if (item.ArchivedAt is not null && kind is "receipt" or "transfer") e["kind"] = "This item is archived.";
        if (req.LocationId is not { } loc || !await db.Locations.AnyAsync(l => l.Id == loc && l.ArchivedAt == null, ct)) e["locationId"] = "Choose an active place.";
        var qty = req.Quantity ?? 0;
        if (kind == "adjustment" ? qty == 0 || Math.Abs(qty) > InventoryRules.MaxQuantity : qty < 1 || qty > InventoryRules.MaxQuantity) e["quantity"] = kind == "adjustment" ? "Use a number other than 0 (minus takes stock away)." : "Use 1 or more.";
        if (req.UnitCost is < 0) e["unitCost"] = "Use 0 or more.";
        var reference = Clean(req.Reference); if (reference is { Length: > 300 }) e["reference"] = "Use at most 300 characters.";
        if (kind == "adjustment" && reference is null) e["reference"] = "Say why the stock is adjusted.";
        if (kind == "transfer" && (req.ToLocationId is not { } to || to == req.LocationId || !await db.Locations.AnyAsync(l => l.Id == to && l.ArchivedAt == null, ct))) e["toLocationId"] = "Choose another active place.";
        if (e.Count > 0) throw Invalid(e);
        var group = kind == "transfer" ? Guid.CreateVersion7() : (Guid?)null;
        var made = await db.InTransactionAsync(async () =>
        {
            var list = new List<StockMovement>();
            switch (kind)
            {
                case "receipt":
                    list.Add(await PostAsync(item, req.LocationId!.Value, qty, StockMovementKind.Receipt, req.UnitCost ?? item.UnitCost, reference, null, actor.UserId, ct));
                    if (req.UnitCost is { } cost) await db.InventoryItems.Where(i => i.Id == item.Id).ExecuteUpdateAsync(s => s.SetProperty(i => i.UnitCost, cost), ct);
                    break;
                case "issue": list.Add(await PostAsync(item, req.LocationId!.Value, -qty, StockMovementKind.Issue, item.UnitCost, reference, null, actor.UserId, ct)); break;
                case "adjustment": list.Add(await PostAsync(item, req.LocationId!.Value, qty, StockMovementKind.Adjustment, item.UnitCost, reference, null, actor.UserId, ct)); break;
                default:
                    list.Add(await PostAsync(item, req.LocationId!.Value, -qty, StockMovementKind.TransferOut, item.UnitCost, reference, group, actor.UserId, ct));
                    list.Add(await PostAsync(item, req.ToLocationId!.Value, qty, StockMovementKind.TransferIn, item.UnitCost, reference, group, actor.UserId, ct));
                    break;
            }
            audit.Record("inventory." + kind, "inventory_item", item.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email,
                new Dictionary<string, string> { ["sku"] = item.Sku, ["quantity"] = qty.ToString(), ["place"] = req.LocationId!.Value.ToString() });
            await db.SaveChangesAsync(ct);
            return list;
        }, ct);
        var ids = made.Select(m => m.Id).ToList();
        return await MovementsAsync(itemId, null, 1, 50, ct, ids);
    }

    public async Task<IReadOnlyList<MovementDto>> MovementsAsync(Guid? itemId, Guid? locationId, int page, int pageSize, CancellationToken ct = default, IReadOnlyList<Guid>? only = null)
    {
        var q = db.StockMovements.AsNoTracking().AsQueryable();
        if (itemId is { } id) q = q.Where(m => m.ItemId == id);
        if (locationId is { } l) q = q.Where(m => m.LocationId == l);
        if (only is not null) q = q.Where(m => only.Contains(m.Id));
        var rows = await q.OrderByDescending(m => m.At).ThenByDescending(m => m.Id).Skip((Math.Max(1, page) - 1) * Math.Clamp(pageSize, 1, 200)).Take(Math.Clamp(pageSize, 1, 200)).ToListAsync(ct);
        var itemIds = rows.Select(m => m.ItemId).Distinct().ToList();
        var items = await db.InventoryItems.AsNoTracking().Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        var people = rows.Select(m => m.ById).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => people.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Name, ct);
        var places = await PlacesAsync(ct);
        return [.. rows.Select(m => new MovementDto(m.Id, m.ItemId, items[m.ItemId].Sku, items[m.ItemId].Name, m.LocationId, places.ContainsKey(m.LocationId) ? PathOf(places, m.LocationId) : "—",
            KindName(m.Kind), m.Quantity, m.BalanceAfter, m.UnitCost, m.Reference, names.GetValueOrDefault(m.ById), m.At))];
    }

    // ================================================================ reorder list and summary
    /// <summary>INV-004. Every active item at or below its reorder level at a place, with a suggested quantity (its reorder quantity, else enough to reach twice the level).</summary>
    public async Task<IReadOnlyList<ReorderLine>> ReorderAsync(CancellationToken ct = default)
    {
        var rows = await (from b in db.StockBalances.AsNoTracking() join i in db.InventoryItems.AsNoTracking() on b.ItemId equals i.Id
                          where i.ArchivedAt == null && b.Quantity <= i.ReorderLevel select new { i, b }).ToListAsync(ct);
        var places = await PlacesAsync(ct);
        return [.. rows.OrderBy(x => x.i.Name).Select(x =>
        {
            var suggested = x.i.ReorderQuantity ?? Math.Max(1, 2 * x.i.ReorderLevel - x.b.Quantity);
            return new ReorderLine(x.i.Id, x.i.Sku, x.i.Name, x.i.Unit, x.b.LocationId, places.ContainsKey(x.b.LocationId) ? PathOf(places, x.b.LocationId) : "—", x.b.Quantity, x.i.ReorderLevel, suggested,
                x.i.UnitCost, x.i.UnitCost is { } c ? Math.Round(c * suggested, 2) : null);
        })];
    }

    public async Task<InventorySummary> SummaryAsync(CancellationToken ct = default)
    {
        var now = clock.UtcNow; var from = now.AddDays(-30);
        var items = await db.InventoryItems.AsNoTracking().Where(i => i.ArchivedAt == null).ToListAsync(ct);
        var balances = await db.StockBalances.AsNoTracking().ToListAsync(ct);
        var byId = items.ToDictionary(i => i.Id);
        var live = balances.Where(b => byId.ContainsKey(b.ItemId)).ToList();
        var moves = await db.StockMovements.AsNoTracking().Where(m => m.At >= from && (m.Kind == StockMovementKind.Issue || m.Kind == StockMovementKind.Receipt)).Select(m => new { m.ItemId, m.Kind, m.Quantity, m.At }).ToListAsync(ct);
        var valued = live.Where(b => byId[b.ItemId].UnitCost is not null).ToList();
        var issues = moves.Where(m => m.Kind == StockMovementKind.Issue).ToList();
        var start = new DateTimeOffset(now.UtcDateTime.Date.AddDays(-29), TimeSpan.Zero);
        return new InventorySummary(items.Count, live.Count(b => b.Quantity <= byId[b.ItemId].ReorderLevel),
            valued.Count == 0 ? null : Math.Round(valued.Sum(b => b.Quantity * byId[b.ItemId].UnitCost!.Value), 2),
            -issues.Sum(m => m.Quantity), moves.Where(m => m.Kind == StockMovementKind.Receipt).Sum(m => m.Quantity),
            [.. items.GroupBy(i => i.Category).Select(g => new CountItem(g.Key, g.Key, g.Count())).OrderByDescending(x => x.Count)],
            [.. issues.GroupBy(m => m.ItemId).Where(g => byId.ContainsKey(g.Key)).Select(g => new CountItem(g.Key.ToString(), byId[g.Key].Name, -g.Sum(m => m.Quantity))).OrderByDescending(x => x.Count).Take(8)],
            [.. Enumerable.Range(0, 30).Select(i => start.AddDays(i)).Select(d => new DayCount(d.ToString("yyyy-MM-dd"), -issues.Where(m => m.At >= d && m.At < d.AddDays(1)).Sum(m => m.Quantity)))]);
    }

    // ================================================================ stock-takes (cycle counts)
    public async Task<StockTakeDto> StartTakeAsync(StartStockTakeRequest req, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        if (req.LocationId is not { } loc || !await db.Locations.AnyAsync(l => l.Id == loc && l.ArchivedAt == null, ct)) throw Invalid(new Dictionary<string, string> { ["locationId"] = "Choose an active place." });
        if (await db.StockTakes.AnyAsync(t => t.LocationId == loc && t.ClosedAt == null, ct)) throw DomainException.Conflict("A count is already open for this place. Close it first.");
        var lines = await (from b in db.StockBalances.AsNoTracking() join i in db.InventoryItems.AsNoTracking() on b.ItemId equals i.Id where b.LocationId == loc && i.ArchivedAt == null select new { b.ItemId, b.Quantity }).ToListAsync(ct);
        if (lines.Count == 0) throw DomainException.Conflict("Nothing is kept at this place yet.");
        var take = new StockTake { LocationId = loc, Note = Clean(req.Note), StartedById = actor.UserId, StartedAt = clock.UtcNow };
        take.Lines = [.. lines.Select(l => new StockTakeLine { StockTakeId = take.Id, ItemId = l.ItemId, Expected = l.Quantity })];
        db.StockTakes.Add(take);
        audit.Record("inventory.count_start", "stock_take", take.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["lines"] = lines.Count.ToString() });
        await db.SaveChangesAsync(ct);
        return await TakeAsync(take.Id, ct);
    }

    public async Task<StockTakeDto> TakeAsync(Guid id, CancellationToken ct = default)
    {
        var t = await db.StockTakes.AsNoTracking().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Count");
        var ids = t.Lines.Select(l => l.ItemId).ToList();
        var items = await db.InventoryItems.AsNoTracking().Where(i => ids.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        var places = await PlacesAsync(ct);
        var by = await db.Users.AsNoTracking().Where(u => u.Id == t.StartedById).Select(u => u.Name).FirstOrDefaultAsync(ct);
        var lines = t.Lines.OrderBy(l => items[l.ItemId].Name).Select(l => new StockTakeLineDto(l.ItemId, items[l.ItemId].Sku, items[l.ItemId].Name, items[l.ItemId].Unit, l.Expected, l.Counted, l.Counted - l.Expected)).ToList();
        return new StockTakeDto(t.Id, t.LocationId, places.ContainsKey(t.LocationId) ? PathOf(places, t.LocationId) : "—", t.Note, by, t.StartedAt, t.ClosedAt, lines, lines.Count(l => l.Counted is not null), lines.Count(l => l.Variance is not null and not 0));
    }

    public async Task<IReadOnlyList<StockTakeDto>> TakesAsync(CancellationToken ct = default)
    {
        var ids = await db.StockTakes.AsNoTracking().OrderByDescending(t => t.StartedAt).Select(t => t.Id).Take(20).ToListAsync(ct);
        var list = new List<StockTakeDto>();
        foreach (var id in ids) list.Add(await TakeAsync(id, ct));
        return list;
    }

    public async Task<StockTakeDto> CountAsync(Guid id, CountRequest req, CancellationToken ct = default)
    {
        var t = await db.StockTakes.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Count");
        if (t.ClosedAt is not null) throw DomainException.Conflict("This count is closed.");
        foreach (var (itemId, counted) in req.Counts ?? [])
        {
            var line = t.Lines.FirstOrDefault(l => l.ItemId == itemId) ?? throw Invalid(new Dictionary<string, string> { ["counts"] = "An item is not part of this count." });
            if (counted is < 0 or > InventoryRules.MaxQuantity) throw Invalid(new Dictionary<string, string> { [$"counts.{itemId}"] = "Use 0 or more." });
            line.Counted = counted;
        }
        await db.SaveChangesAsync(ct);
        return await TakeAsync(id, ct);
    }

    /// <summary>Closes the count: every counted line that differs from what was expected posts a Count movement for the difference. Lines not counted change nothing.</summary>
    public async Task<StockTakeDto> CloseTakeAsync(Guid id, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var t = await db.StockTakes.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Count");
        if (t.ClosedAt is not null) throw DomainException.Conflict("This count is already closed.");
        if (t.Lines.All(l => l.Counted is null)) throw DomainException.Conflict("Nothing has been counted yet.");
        var ids = t.Lines.Select(l => l.ItemId).ToList();
        var items = await db.InventoryItems.AsNoTracking().Where(i => ids.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        await db.InTransactionAsync(async () =>
        {
            foreach (var l in t.Lines.Where(l => l.Counted is { } c && c != l.Expected))
                await PostAsync(items[l.ItemId], t.LocationId, l.Counted!.Value - l.Expected, StockMovementKind.Count, items[l.ItemId].UnitCost, $"Stock-take {t.StartedAt:yyyy-MM-dd}", t.Id, actor.UserId, ct);
            t.ClosedAt = clock.UtcNow; t.ClosedById = actor.UserId;
            audit.Record("inventory.count_close", "stock_take", t.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email,
                new Dictionary<string, string> { ["variances"] = t.Lines.Count(l => l.Counted is { } c && c != l.Expected).ToString() });
            await db.SaveChangesAsync(ct);
            return true;
        }, ct);
        return await TakeAsync(id, ct);
    }
}
