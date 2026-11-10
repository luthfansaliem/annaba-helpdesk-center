using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Application.Monitoring;
using AnnabaDigital.Application.Users;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Application.Helpdesk;

public sealed record CostSettingsDto(string Currency, decimal? LabourRatePerHour);
public sealed record CostRow(string Key, string Label, decimal Part, decimal Labour, decimal Service, decimal Invoice, decimal Other, decimal Time, decimal Total, int WorkOrders);
public sealed record CostReport(DateOnly From, DateOnly To, string By, string Currency, decimal? LabourRatePerHour, IReadOnlyList<CostRow> Rows, CostRow Total);
public sealed record CostCenterItem(Guid Id, string Code, string Name, bool Archived, IReadOnlyList<VendorSiteItem> Places, int Version);
public sealed record SaveCostCenterRequest(string? Code, string? Name, Guid[]? LocationIds, int? Version = null);
public sealed record BudgetItem(Guid Id, string Scope, Guid ScopeId, string ScopeName, int Year, decimal Amount, decimal Spent, int Percent, string State, string? Note, int Version);
public sealed record BudgetList(int Year, string Currency, IReadOnlyList<BudgetItem> Items, bool CanManage);
public sealed record SaveBudgetRequest(string? Scope, Guid? ScopeId, int Year, decimal Amount, string? Note, int? Version = null);

/// <summary>
/// CST-001/002/003/004, RPT-003. Costs are the work orders' cost lines (part, labour, service, vendor invoice, other) plus, when the organisation sets an hourly rate, the logged
/// time (minutes × rate ÷ 60). Every amount is a decimal with two places, rounded half away from zero per line; nothing is floating point. Costs are attributed through the
/// work order's ticket: its place (site = top-level place, cost centre = nearest place mapped to one), its service and category, its requester's department, the work
/// order's vendor and asset. Budgets (a year, per cost centre, site or category) are compared with the costs of that year; 80 % and 100 % alert the budget managers once each.
/// </summary>
public sealed partial class CostService(IAppDb db, IClock clock, AuditWriter audit, AuthorizationService authz, ITenantContext tenant, NotificationService notifications, IAppLinks links)
{
    public const int MaxRows = 200_000, MaxRecipients = 20;
    private static DomainException Invalid(Dictionary<string, string> e) => DomainException.Validation("Please correct the highlighted fields.", e);
    private static string? Clean(string? s) { var t = (s ?? "").Trim(); return t.Length == 0 ? null : t; }
    public static decimal Money(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);
    [GeneratedRegex("^[A-Z]{3}$")] private static partial Regex CurrencyShape();
    [GeneratedRegex("^[A-Za-z0-9._-]{1,20}$")] private static partial Regex CodeShape();

    // ================================================================ settings
    public async Task<CostSettingsDto> SettingsAsync(CancellationToken ct = default)
        => await db.CostSettings.AsNoTracking().Select(s => new CostSettingsDto(s.Currency, s.LabourRatePerHour)).FirstOrDefaultAsync(ct) ?? new CostSettingsDto("IDR", null);

    public async Task<CostSettingsDto> SaveSettingsAsync(CostSettingsDto r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var e = new Dictionary<string, string>();
        var currency = (r.Currency ?? "").Trim().ToUpperInvariant();
        if (!CurrencyShape().IsMatch(currency)) e["currency"] = "Use a three-letter currency code such as IDR.";
        if (r.LabourRatePerHour is { } rate && (rate < 0 || rate > 1_000_000_000m || Money(rate) != rate)) e["labourRatePerHour"] = "Use an amount of 0 or more with at most two decimals.";
        if (e.Count > 0) throw Invalid(e);
        var s = await db.CostSettings.FirstOrDefaultAsync(ct);
        if (s is null) { s = new CostSettings(); db.CostSettings.Add(s); }
        s.Currency = currency; s.LabourRatePerHour = r.LabourRatePerHour; s.UpdatedAt = clock.UtcNow;
        audit.Record("cost.settings.update", "cost_settings", tenant.TenantId.ToString(), Outcome.Success, client, actor.UserId, actor.Email,
            new Dictionary<string, string> { ["currency"] = currency, ["labourRate"] = r.LabourRatePerHour?.ToString(CultureInfo.InvariantCulture) ?? "" });
        await db.SaveChangesAsync(ct);
        return await SettingsAsync(ct);
    }

    // ================================================================ the cost lines, attributed
    private sealed record Line(DateOnly Date, string Kind, decimal Amount, Guid WorkOrderId, Guid? LocationId, Guid ServiceId, Guid RequesterId, Guid? VendorId, Guid? AssetId);
    private sealed record Place(Guid Id, Guid? ParentId, string Name);

    private async Task<List<Line>> LinesAsync(DateOnly first, DateOnly last, decimal? rate, CancellationToken ct)
    {
        var start = new DateTimeOffset(first.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero); var end = new DateTimeOffset(last.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var lines = await (from c in db.WorkOrderCosts.AsNoTracking()
                           join w in db.WorkOrders.AsNoTracking() on c.WorkOrderId equals w.Id
                           join t in db.Tickets.AsNoTracking() on w.TicketId equals t.Id
                           where c.CreatedAt >= start && c.CreatedAt < end
                           select new { c.CreatedAt, c.Kind, c.Quantity, c.UnitCost, WorkOrderId = w.Id, t.LocationId, t.ServiceId, t.RequesterId, w.VendorId, w.AssetId }).Take(MaxRows).ToListAsync(ct);
        var list = lines.Select(x => new Line(DateOnly.FromDateTime(x.CreatedAt.UtcDateTime), x.Kind, Money(x.Quantity * x.UnitCost), x.WorkOrderId, x.LocationId, x.ServiceId, x.RequesterId, x.VendorId, x.AssetId)).ToList();
        if (rate is { } r && r > 0)
        {
            var times = await (from m in db.WorkOrderTimes.AsNoTracking()
                               join w in db.WorkOrders.AsNoTracking() on m.WorkOrderId equals w.Id
                               join t in db.Tickets.AsNoTracking() on w.TicketId equals t.Id
                               where m.WorkDate >= first && m.WorkDate <= last
                               select new { m.WorkDate, m.Minutes, WorkOrderId = w.Id, t.LocationId, t.ServiceId, t.RequesterId, w.VendorId, w.AssetId }).Take(MaxRows).ToListAsync(ct);
            list.AddRange(times.Select(x => new Line(x.WorkDate, "time", Money(x.Minutes * r / 60m), x.WorkOrderId, x.LocationId, x.ServiceId, x.RequesterId, x.VendorId, x.AssetId)));
        }
        return list;
    }

    private sealed class Lookup
    {
        public required Dictionary<Guid, Place> Places { get; init; }
        public required Dictionary<Guid, Guid> CenterOfPlace { get; init; }
        public required Dictionary<Guid, (string Code, string Name)> Centers { get; init; }
        public required Dictionary<Guid, (string Name, Guid CategoryId)> Services { get; init; }
        public required Dictionary<Guid, string> Categories { get; init; }
        public Guid? Top(Guid? id) { Guid? top = null; for (var (c, n) = (id, 0); c is { } k && Places.TryGetValue(k, out var p) && n < 10; c = p.ParentId, n++) top = k; return top; }
        public Guid? Center(Guid? id) { for (var (c, n) = (id, 0); c is { } k && Places.TryGetValue(k, out var p) && n < 10; c = p.ParentId, n++) if (CenterOfPlace.TryGetValue(k, out var cc)) return cc; return null; }
    }

    private async Task<Lookup> LookupAsync(CancellationToken ct) => new()
    {
        Places = await db.Locations.AsNoTracking().Select(l => new Place(l.Id, l.ParentId, l.Name)).ToDictionaryAsync(l => l.Id, ct),
        CenterOfPlace = await db.CostCenterPlaces.AsNoTracking().ToDictionaryAsync(p => p.LocationId, p => p.CostCenterId, ct),
        Centers = (await db.CostCenters.AsNoTracking().Select(c => new { c.Id, c.Code, c.Name }).ToListAsync(ct)).ToDictionary(c => c.Id, c => (c.Code, c.Name)),
        Services = (await db.Services.AsNoTracking().Select(s => new { s.Id, s.Name, s.CategoryId }).ToListAsync(ct)).ToDictionary(s => s.Id, s => (s.Name, s.CategoryId)),
        Categories = await db.ServiceCategories.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name, ct),
    };

    // ================================================================ report (RPT-003)
    public static readonly string[] Groupings = ["month", "site", "cost_center", "category", "service", "department", "vendor", "asset"];

    public async Task<CostReport> ReportAsync(DateOnly? from, DateOnly? to, string? by, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var end = to ?? today; var start = from ?? new DateOnly(end.Year, end.Month, 1).AddMonths(-11);
        var e = new Dictionary<string, string>();
        if (end < start) e["to"] = "The end can't be before the start.";
        else if (end.DayNumber - start.DayNumber > 731) e["from"] = "Choose at most two years.";
        var group = (by ?? "month").Trim().ToLowerInvariant();
        if (!Groupings.Contains(group)) e["by"] = "Use month, site, cost_center, category, service, department, vendor or asset.";
        if (e.Count > 0) throw Invalid(e);

        var settings = await SettingsAsync(ct);
        var lines = await LinesAsync(start, end, settings.LabourRatePerHour, ct);
        var look = await LookupAsync(ct);
        Dictionary<Guid, string> departments = [], vendors = [], assets = [];
        if (group == "department")
        {
            var ids = lines.Select(l => l.RequesterId).Distinct().ToList();
            departments = await db.Profiles.AsNoTracking().Where(p => ids.Contains(p.UserId) && p.Department != null).ToDictionaryAsync(p => p.UserId, p => p.Department!, ct);
        }
        if (group == "vendor") vendors = await db.Vendors.AsNoTracking().ToDictionaryAsync(v => v.Id, v => v.Name, ct);
        if (group == "asset") { var ids = lines.Where(l => l.AssetId != null).Select(l => l.AssetId!.Value).Distinct().ToList(); assets = await db.Assets.AsNoTracking().Where(a => ids.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.Tag + " " + a.Name, ct); }

        (string Key, string Label) KeyOf(Line l)
        {
            switch (group)
            {
                case "month": var m = new DateOnly(l.Date.Year, l.Date.Month, 1); return (m.ToString("yyyy-MM"), m.ToString("yyyy-MM"));
                case "site": return look.Top(l.LocationId) is { } s ? (s.ToString(), look.Places[s].Name) : ("none", "No place");
                case "cost_center": return look.Center(l.LocationId) is { } c && look.Centers.TryGetValue(c, out var cc) ? (c.ToString(), $"{cc.Code} · {cc.Name}") : ("none", "No cost centre");
                case "category": return look.Services.TryGetValue(l.ServiceId, out var sv) ? (sv.CategoryId.ToString(), look.Categories.GetValueOrDefault(sv.CategoryId) ?? "—") : ("none", "—");
                case "service": return (l.ServiceId.ToString(), look.Services.TryGetValue(l.ServiceId, out var s2) ? s2.Name : "—");
                case "department": return departments.TryGetValue(l.RequesterId, out var d) ? (d.ToLowerInvariant(), d) : ("none", "No department");
                case "vendor": return l.VendorId is { } v ? (v.ToString(), vendors.GetValueOrDefault(v) ?? "—") : ("none", "In-house");
                default: return l.AssetId is { } a ? (a.ToString(), assets.GetValueOrDefault(a) ?? "—") : ("none", "No asset");
            }
        }
        static CostRow Row(string key, string label, IReadOnlyCollection<Line> ls)
        {
            decimal Sum(string k) => ls.Where(l => l.Kind == k).Sum(l => l.Amount);
            return new CostRow(key, label, Sum("part"), Sum("labour"), Sum("service"), Sum("invoice"), Sum("other"), Sum("time"), ls.Sum(l => l.Amount), ls.Select(l => l.WorkOrderId).Distinct().Count());
        }
        var rows = lines.GroupBy(KeyOf).Select(g => Row(g.Key.Key, g.Key.Label, [.. g])).ToList();
        rows = group == "month" ? [.. rows.OrderBy(r => r.Key)] : [.. rows.OrderByDescending(r => r.Total).ThenBy(r => r.Label, StringComparer.OrdinalIgnoreCase)];
        return new CostReport(start, end, group, settings.Currency, settings.LabourRatePerHour, rows, Row("total", "Total", lines));
    }

    public async Task<string> ReportCsvAsync(DateOnly? from, DateOnly? to, string? by, CancellationToken ct = default)
    {
        var r = await ReportAsync(from, to, by, ct);
        var sb = new StringBuilder();
        string N(decimal d) => d.ToString("0.00", CultureInfo.InvariantCulture);
        sb.Append($"{r.By},parts,labour lines,services,vendor invoices,other,labour from time,total ({r.Currency}),work orders\r\n");
        foreach (var x in r.Rows.Append(r.Total))
            sb.Append(string.Join(',', MonitoringService.Csv(x.Label), N(x.Part), N(x.Labour), N(x.Service), N(x.Invoice), N(x.Other), N(x.Time), N(x.Total), x.WorkOrders.ToString(CultureInfo.InvariantCulture))).Append("\r\n");
        return sb.ToString();
    }

    // ================================================================ cost centres (CST-002)
    public async Task<IReadOnlyList<CostCenterItem>> CentersAsync(CancellationToken ct = default)
    {
        var places = await db.Locations.AsNoTracking().Select(l => new Place(l.Id, l.ParentId, l.Name)).ToDictionaryAsync(l => l.Id, ct);
        string Path(Guid id) { var parts = new List<string>(); for (Guid? c = id; c is { } k && places.TryGetValue(k, out var p) && parts.Count < 6; c = p.ParentId) parts.Insert(0, p.Name); return string.Join(" › ", parts); }
        var maps = await db.CostCenterPlaces.AsNoTracking().ToListAsync(ct);
        return [.. (await db.CostCenters.AsNoTracking().ToListAsync(ct)).OrderBy(c => c.ArchivedAt != null).ThenBy(c => c.Code, StringComparer.OrdinalIgnoreCase)
            .Select(c => new CostCenterItem(c.Id, c.Code, c.Name, c.ArchivedAt != null, [.. maps.Where(m => m.CostCenterId == c.Id && places.ContainsKey(m.LocationId)).Select(m => new VendorSiteItem(m.LocationId, Path(m.LocationId))).OrderBy(p => p.Path)], c.Version))];
    }

    public async Task<CostCenterItem> SaveCenterAsync(Guid? id, SaveCostCenterRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var e = new Dictionary<string, string>();
        var code = (r.Code ?? "").Trim().ToUpperInvariant(); var name = (r.Name ?? "").Trim();
        if (!CodeShape().IsMatch(code)) e["code"] = "Use 1–20 letters, digits, dots, dashes or underscores.";
        else if (await db.CostCenters.AnyAsync(c => c.Id != id && c.Code == code, ct)) e["code"] = "Another cost centre has this code.";
        if (name.Length is < 2 or > 100) e["name"] = "Use 2–100 characters.";
        var places = (r.LocationIds ?? []).Distinct().ToList();
        if (places.Count > 500) e["locationIds"] = "At most 500 places.";
        else if (places.Count > 0)
        {
            if (await db.Locations.CountAsync(l => places.Contains(l.Id) && l.ArchivedAt == null, ct) != places.Count) e["locationIds"] = "Choose active places.";
            else if (await db.CostCenterPlaces.AnyAsync(p => places.Contains(p.LocationId) && p.CostCenterId != id, ct)) e["locationIds"] = "A place belongs to one cost centre; some of these already belong to another.";
        }
        if (e.Count > 0) throw Invalid(e);
        CostCenter c;
        if (id is { } cid)
        {
            c = await db.CostCenters.FirstOrDefaultAsync(x => x.Id == cid, ct) ?? throw DomainException.NotFound("Cost centre");
            if (r.Version is { } v && v != c.Version) throw DomainException.Conflict("This cost centre was changed by someone else. Reload and try again.");
            c.Version++;
        }
        else { c = new CostCenter { CreatedAt = clock.UtcNow }; db.CostCenters.Add(c); }
        c.Code = code; c.Name = name;
        var existing = await db.CostCenterPlaces.Where(p => p.CostCenterId == c.Id).ToListAsync(ct);
        db.CostCenterPlaces.RemoveRange(existing.Where(p => !places.Contains(p.LocationId)));
        foreach (var l in places.Where(l => existing.All(p => p.LocationId != l))) db.CostCenterPlaces.Add(new CostCenterPlace { LocationId = l, CostCenterId = c.Id });
        audit.Record(id is null ? "cost.center.create" : "cost.center.update", "cost_center", c.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["code"] = code, ["places"] = places.Count.ToString() });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw DomainException.Conflict("This cost centre was changed by someone else. Reload and try again."); }
        catch (DbUpdateException) { throw Invalid(new() { ["code"] = "Another cost centre has this code, or a place was given to another cost centre at the same moment." }); }
        return (await CentersAsync(ct)).First(x => x.Id == c.Id);
    }

    public async Task SetCenterArchivedAsync(Guid id, bool archived, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var c = await db.CostCenters.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Cost centre");
        c.ArchivedAt = archived ? clock.UtcNow : null; c.Version++;
        audit.Record(archived ? "cost.center.archive" : "cost.center.restore", "cost_center", c.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["code"] = c.Code });
        await db.SaveChangesAsync(ct);
    }

    // ================================================================ budgets (CST-003)
    private static bool TryScope(string? s, out BudgetScope scope)
    {
        BudgetScope? found = (s ?? "").Trim().ToLowerInvariant() switch { "cost_center" => BudgetScope.CostCenter, "site" => BudgetScope.Site, "category" => BudgetScope.Category, _ => null };
        scope = found ?? BudgetScope.CostCenter;
        return found is not null;
    }
    public static string ScopeName(BudgetScope s) => s switch { BudgetScope.CostCenter => "cost_center", BudgetScope.Site => "site", _ => "category" };

    /// <summary>Spent per budget of a year: one pass over that year's costs.</summary>
    private async Task<Dictionary<Guid, decimal>> SpentAsync(IReadOnlyList<Budget> budgets, int year, CancellationToken ct)
    {
        if (budgets.Count == 0) return [];
        var settings = await SettingsAsync(ct);
        var lines = await LinesAsync(new DateOnly(year, 1, 1), new DateOnly(year, 12, 31), settings.LabourRatePerHour, ct);
        var look = await LookupAsync(ct);
        var bySite = new Dictionary<Guid, decimal>(); var byCenter = new Dictionary<Guid, decimal>(); var byCat = new Dictionary<Guid, decimal>();
        foreach (var l in lines)
        {
            if (look.Top(l.LocationId) is { } s) bySite[s] = bySite.GetValueOrDefault(s) + l.Amount;
            if (look.Center(l.LocationId) is { } c) byCenter[c] = byCenter.GetValueOrDefault(c) + l.Amount;
            if (look.Services.TryGetValue(l.ServiceId, out var sv)) byCat[sv.CategoryId] = byCat.GetValueOrDefault(sv.CategoryId) + l.Amount;
        }
        return budgets.ToDictionary(b => b.Id, b => (b.Scope switch { BudgetScope.Site => bySite, BudgetScope.CostCenter => byCenter, _ => byCat }).GetValueOrDefault(b.ScopeId));
    }

    public static int Percent(decimal spent, decimal amount) => amount <= 0 ? (spent > 0 ? 100 : 0) : (int)Math.Floor(spent * 100m / amount);
    public static string State(int percent) => percent >= 100 ? "over" : percent >= 80 ? "near" : "ok";

    public async Task<BudgetList> BudgetsAsync(int? year, SessionPrincipal who, CancellationToken ct = default)
    {
        var y = year ?? clock.UtcNow.Year;
        if (y is < 2000 or > 2100) throw Invalid(new() { ["year"] = "Use a year between 2000 and 2100." });
        var budgets = await db.Budgets.AsNoTracking().Where(b => b.Year == y).ToListAsync(ct);
        var spent = await SpentAsync(budgets, y, ct);
        var look = await LookupAsync(ct);
        string Name(Budget b) => b.Scope switch
        {
            BudgetScope.Site => look.Places.TryGetValue(b.ScopeId, out var p) ? p.Name : "—",
            BudgetScope.CostCenter => look.Centers.TryGetValue(b.ScopeId, out var c) ? $"{c.Code} · {c.Name}" : "—",
            _ => look.Categories.GetValueOrDefault(b.ScopeId) ?? "—",
        };
        var items = budgets.Select(b => { var s = spent.GetValueOrDefault(b.Id); var p = Percent(s, b.Amount); return new BudgetItem(b.Id, ScopeName(b.Scope), b.ScopeId, Name(b), b.Year, b.Amount, s, p, State(p), b.Note, b.Version); })
            .OrderByDescending(b => b.Percent).ThenBy(b => b.ScopeName, StringComparer.OrdinalIgnoreCase).ToList();
        var perms = await authz.GetEffectivePermissionsAsync(who.UserId, ct);
        return new BudgetList(y, (await SettingsAsync(ct)).Currency, items, perms.Contains(Permissions.BudgetsManage));
    }

    public async Task<BudgetList> SaveBudgetAsync(Guid? id, SaveBudgetRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var e = new Dictionary<string, string>();
        if (!TryScope(r.Scope, out var scope)) e["scope"] = "Use cost_center, site or category.";
        else if (r.ScopeId is not { } sid) e["scopeId"] = "Choose what the budget is for.";
        else
        {
            var exists = scope switch
            {
                BudgetScope.CostCenter => await db.CostCenters.AnyAsync(c => c.Id == sid && c.ArchivedAt == null, ct),
                BudgetScope.Site => await db.Locations.AnyAsync(l => l.Id == sid && l.ParentId == null && l.ArchivedAt == null, ct),
                _ => await db.ServiceCategories.AnyAsync(c => c.Id == sid, ct),
            };
            if (!exists) e["scopeId"] = scope == BudgetScope.Site ? "Choose an active site (a top-level place)." : "Choose an active one.";
        }
        if (r.Year is < 2000 or > 2100) e["year"] = "Use a year between 2000 and 2100.";
        if (r.Amount <= 0 || r.Amount > 1_000_000_000_000m || Money(r.Amount) != r.Amount) e["amount"] = "Use an amount above 0 with at most two decimals.";
        var note = Clean(r.Note);
        if (note is { Length: > 300 }) e["note"] = "Use at most 300 characters.";
        if (e.Count == 0 && await db.Budgets.AnyAsync(b => b.Id != id && b.Scope == scope && b.ScopeId == r.ScopeId && b.Year == r.Year, ct)) e["scopeId"] = "There is already a budget for this in that year.";
        if (e.Count > 0) throw Invalid(e);
        Budget b;
        if (id is { } bid)
        {
            b = await db.Budgets.FirstOrDefaultAsync(x => x.Id == bid, ct) ?? throw DomainException.NotFound("Budget");
            if (r.Version is { } v && v != b.Version) throw DomainException.Conflict("This budget was changed by someone else. Reload and try again.");
            b.Version++;
        }
        else { b = new Budget { CreatedAt = clock.UtcNow }; db.Budgets.Add(b); }
        if (b.Amount != r.Amount || b.Scope != scope || b.ScopeId != r.ScopeId || b.Year != r.Year) b.AlertLevel = 0;   // a new amount or target is judged afresh
        b.Scope = scope; b.ScopeId = r.ScopeId!.Value; b.Year = r.Year; b.Amount = r.Amount; b.Note = note; b.UpdatedAt = clock.UtcNow;
        audit.Record(id is null ? "budget.create" : "budget.update", "budget", b.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email,
            new Dictionary<string, string> { ["scope"] = ScopeName(scope), ["year"] = r.Year.ToString(), ["amount"] = r.Amount.ToString(CultureInfo.InvariantCulture) });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw DomainException.Conflict("This budget was changed by someone else. Reload and try again."); }
        catch (DbUpdateException) { throw Invalid(new() { ["scopeId"] = "There is already a budget for this in that year." }); }
        return await BudgetsAsync(r.Year, actor, ct);
    }

    public async Task DeleteBudgetAsync(Guid id, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var b = await db.Budgets.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Budget");
        db.Budgets.Remove(b);
        audit.Record("budget.delete", "budget", b.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["year"] = b.Year.ToString(), ["amount"] = b.Amount.ToString(CultureInfo.InvariantCulture) });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Every organisation's budgets for this year: 80 % and 100 % alert the people who manage budgets, once each (a changed amount starts over).</summary>
    public async Task<int> RunBudgetAlertsAsync(CancellationToken ct = default)
    {
        var year = clock.UtcNow.Year;
        var orgs = await db.Budgets.IgnoreQueryFilters().AsNoTracking().Where(b => b.Year == year && b.AlertLevel < 2).Select(b => b.TenantId).Distinct().ToListAsync(ct);
        var sent = 0;
        foreach (var org in orgs)
        {
            using (tenant.Switch(org))
            {
                db.ResetTracking();
                var budgets = await db.Budgets.AsNoTracking().Where(b => b.Year == year).ToListAsync(ct);
                var spent = await SpentAsync(budgets, year, ct);
                var list = await BudgetsListForAlertsAsync(budgets, ct);
                var currency = (await SettingsAsync(ct)).Currency;
                var people = await db.Users.AsNoTracking().Where(u => u.Status == UserStatus.Active
                        && u.UserRoles.Any(ur => ur.Role!.ArchivedAt == null && ur.Role.Permissions.Any(p => p.PermissionKey == Permissions.BudgetsManage)))
                    .OrderBy(u => u.Id).Select(u => u.Id).Take(MaxRecipients).ToListAsync(ct);
                foreach (var b in budgets)
                {
                    var p = Percent(spent.GetValueOrDefault(b.Id), b.Amount);
                    var level = p >= 100 ? 2 : p >= 80 ? 1 : 0;
                    if (level <= b.AlertLevel) continue;
                    var amount = b.Amount;
                    var n = await db.Budgets.Where(x => x.Id == b.Id && x.AlertLevel < level && x.Amount == amount).ExecuteUpdateAsync(u => u.SetProperty(x => x.AlertLevel, level), ct);
                    if (n == 0) continue;
                    var name = list.GetValueOrDefault(b.Id) ?? "a budget";
                    var title = level == 2 ? $"Budget used up: {name} ({b.Year})" : $"Budget at {p}%: {name} ({b.Year})";
                    var body = $"{spent.GetValueOrDefault(b.Id).ToString("N2", CultureInfo.InvariantCulture)} of {b.Amount.ToString("N2", CultureInfo.InvariantCulture)} {currency} spent.";
                    foreach (var uid in people)
                        await notifications.SendAsync(uid, "budget.threshold", title, body, "/costs/budgets", null, (to, nm) => Email.EmailTemplates.Notification(to, nm, title, body, links.Origin.TrimEnd('/') + "/costs/budgets"), ct: ct);
                    sent++;
                }
                await db.SaveChangesAsync(ct);
            }
        }
        return sent;
    }

    private async Task<Dictionary<Guid, string>> BudgetsListForAlertsAsync(IReadOnlyList<Budget> budgets, CancellationToken ct)
    {
        var look = await LookupAsync(ct);
        return budgets.ToDictionary(b => b.Id, b => b.Scope switch
        {
            BudgetScope.Site => look.Places.TryGetValue(b.ScopeId, out var p) ? p.Name : "—",
            BudgetScope.CostCenter => look.Centers.TryGetValue(b.ScopeId, out var c) ? $"{c.Code} {c.Name}" : "—",
            _ => look.Categories.GetValueOrDefault(b.ScopeId) ?? "—",
        });
    }
}
