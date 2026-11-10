using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Application.Monitoring;
using AnnabaDigital.Application.Users;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Domain.Forms;

namespace AnnabaDigital.Application.Helpdesk;

public sealed record AssetTypeItem(Guid Id, string Name, string? Description, IReadOnlyList<FormField> Fields, bool Archived, int Assets, int Version);
public sealed record SaveAssetTypeRequest(string? Name, string? Description, FormField[]? Fields, int? Version = null);

public sealed record AssetQuery(string? Q = null, Guid? TypeId = null, string? Status = null, Guid? LocationId = null, Guid? AssignedToId = null, int? WarrantyWithinDays = null, int Page = 1, int PageSize = 25, bool? Frequent = null);
public sealed record AssetListItem(Guid Id, string Tag, string Name, Guid TypeId, string TypeName, string Status, string? Serial, string? LocationPath, string? AssignedToName, DateOnly? WarrantyEnd, DateTimeOffset UpdatedAt);
public sealed record AssetEventItem(Guid Id, string Kind, string Actor, string? Body, DateTimeOffset At);
public sealed record AssetTicketItem(Guid Id, string Number, string Subject, string Status, DateTimeOffset CreatedAt);
public sealed record AssetDetail(Guid Id, string Tag, string Name, Guid TypeId, string TypeName, string? Serial, string Status, Guid? LocationId, string? LocationPath, Guid? AssignedToId, string? AssignedToName,
    string? Department, DateOnly? PurchaseDate, decimal? PurchaseCost, DateOnly? WarrantyEnd, string? VendorName, string? Notes, IReadOnlyList<FormField> Fields, JsonElement Values,
    IReadOnlyList<AssetEventItem> Events, IReadOnlyList<AssetTicketItem> Tickets, int TicketCount, DateTimeOffset? RetiredAt, string? RetireReason, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, int Version, bool CanManage,
    int RequestsLastYear = 0, string? RepairHint = null);
/// <summary>What anyone in the organisation may see after scanning an asset label: enough to report a problem about it.</summary>
public sealed record AssetCard(Guid Id, string Tag, string Name, string TypeName, Guid? LocationId, string? LocationPath, string Status, bool Retired, bool CanOpen);
public sealed record AssetLabel(Guid Id, string Tag, string Name, string TypeName, string? LocationPath);
public sealed record SaveAssetRequest(string? Tag, string? Name, Guid? TypeId, string? Serial, string? Status, Guid? LocationId, Guid? AssignedToId, string? Department,
    DateOnly? PurchaseDate, decimal? PurchaseCost, DateOnly? WarrantyEnd, string? VendorName, string? Notes, Dictionary<string, JsonElement>? Fields, int? Version = null);
public sealed record RetireAssetRequest(string? Reason, int? Version = null);
public sealed record AssetPick(Guid Id, string Tag, string Name, string TypeName, string? LocationPath);

public sealed record AssetExport(string Content, string FileName, int Rows, bool Truncated);
public sealed record ImportProblem(int Line, string? Tag, IReadOnlyDictionary<string, string> Errors);
public sealed record ImportReport(int Rows, int Creates, int Updates, IReadOnlyList<ImportProblem> Problems, bool Applied);
public sealed record ImportRequest(string? Csv, bool DryRun = true);

/// <summary>
/// AST-001/003/004/006. The asset register: types with their own questions, assets, their lifecycle (every move, hand-over, status change and retirement is a timeline entry and an audit
/// row), the requests about an asset (only those the reader may see in the queue), and CSV export/import (import is all-or-nothing, with a report per line; a dry run changes nothing).
/// </summary>
public sealed class AssetService(IAppDb db, IClock clock, AuditWriter audit, AuthorizationService authz, ITenantContext tenant, TicketService tickets, NotificationService notifications, IAppLinks links)
{
    public const int MaxImportRows = 5000, MaxImportChars = 2_000_000, MaxExportRows = 20_000, HistoryShown = 50, MaxLabels = 500;
    /// <summary>AST-003. This many requests about one asset within a year marks it as failing often (the "replace rather than repair?" hint).</summary>
    public const int FrequentThreshold = 3;
    public const int WarrantyNoticeDays = 30, WarrantyDigestAbove = 5, MaxReminderRecipients = 20;
    private DateTimeOffset YearAgo => clock.UtcNow.AddDays(-365);
    public static readonly string[] Columns = ["tag", "name", "type", "serial", "status", "location_code", "assigned_to_email", "department", "purchase_date", "purchase_cost", "warranty_end", "vendor", "notes"];

    // ================================================================ helpers
    private static string Lower(AssetStatus s) => AssetRules.StatusName(s);
    private static Dictionary<string, string> Errors() => new();
    private static DomainException Invalid(Dictionary<string, string> e) => DomainException.Validation("Please correct the highlighted fields.", e);
    private static string? Clean(string? s) { var t = (s ?? "").Trim(); return t.Length == 0 ? null : t; }

    private sealed record Place(Guid Id, Guid? ParentId, string Name, string Code, bool Archived);
    private async Task<Dictionary<Guid, Place>> PlacesAsync(CancellationToken ct)
        => await db.Locations.AsNoTracking().Select(l => new Place(l.Id, l.ParentId, l.Name, l.Code, l.ArchivedAt != null)).ToDictionaryAsync(l => l.Id, ct);
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

    private static List<FormField> FieldsOf(AssetType t) => [.. FormEngine.Deserialize(t.FieldsJson)];

    // ================================================================ types
    public async Task<IReadOnlyList<AssetTypeItem>> TypesAsync(bool includeArchived, CancellationToken ct = default)
    {
        var rows = await db.AssetTypes.AsNoTracking().Where(t => includeArchived || t.ArchivedAt == null)
            .Select(t => new { t, Count = db.Assets.Count(a => a.AssetTypeId == t.Id && a.RetiredAt == null) }).ToListAsync(ct);
        return [.. rows.OrderBy(r => r.t.Name, StringComparer.OrdinalIgnoreCase).Select(r => new AssetTypeItem(r.t.Id, r.t.Name, r.t.Description, FieldsOf(r.t), r.t.ArchivedAt != null, r.Count, r.t.Version))];
    }

    private static (string Name, string? Description, List<FormField> Fields) CheckType(SaveAssetTypeRequest req, Dictionary<string, string> e)
    {
        var name = (req.Name ?? "").Trim(); var desc = Clean(req.Description);
        if (name.Length is < 2 or > 100) e["name"] = "Use 2–100 characters.";
        if (desc is { Length: > 300 }) e["description"] = "Use at most 300 characters.";
        var fields = FormEngine.Normalize(req.Fields ?? []);
        if (fields.Count > AssetRules.MaxTypeFields) e["fields"] = $"An asset type can ask at most {AssetRules.MaxTypeFields} questions.";
        foreach (var (k, v) in FormEngine.ValidateDefinition(fields)) e[k] = v;
        for (var i = 0; i < fields.Count; i++)
            if (!AssetRules.FieldTypes.Contains(fields[i].Type)) e[$"fields[{i}].type"] = "Asset questions can be text, long text, number, date, a choice or yes/no.";
        return (name, desc, fields);
    }

    public async Task<AssetTypeItem> CreateTypeAsync(SaveAssetTypeRequest req, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var e = Errors(); var (name, desc, fields) = CheckType(req, e);
        if (!e.ContainsKey("name") && await db.AssetTypes.AnyAsync(t => t.ArchivedAt == null && t.Name.ToLower() == name.ToLower(), ct)) e["name"] = "An asset type with this name already exists.";
        if (e.Count > 0) throw Invalid(e);
        var now = clock.UtcNow;
        var t = new AssetType { Name = name, Description = desc, FieldsJson = FormEngine.Serialize(fields), CreatedAt = now, UpdatedAt = now };
        db.AssetTypes.Add(t);
        audit.Record("assettype.create", "asset_type", t.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["name"] = name, ["fields"] = fields.Count.ToString() });
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateException) { throw DomainException.Conflict("An asset type with this name already exists."); }
        return (await TypesAsync(true, ct)).First(x => x.Id == t.Id);
    }

    public async Task<AssetTypeItem> UpdateTypeAsync(Guid id, SaveAssetTypeRequest req, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var t = await db.AssetTypes.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Asset type");
        if (t.ArchivedAt is not null) throw DomainException.Conflict("This asset type is archived. Restore it before editing.");
        if (req.Version is { } v && v != t.Version) throw DomainException.Conflict("This asset type was changed by someone else. Reload and try again.");
        var e = Errors(); var (name, desc, fields) = CheckType(req, e);
        if (!e.ContainsKey("name") && await db.AssetTypes.AnyAsync(x => x.Id != id && x.ArchivedAt == null && x.Name.ToLower() == name.ToLower(), ct)) e["name"] = "An asset type with this name already exists.";
        if (e.Count > 0) throw Invalid(e);
        var json = FormEngine.Serialize(fields); var diff = new List<AuditDiff>();
        if (t.Name != name) diff.Add(new("name", t.Name, name));
        if (t.Description != desc) diff.Add(new("description", t.Description ?? "—", desc ?? "—"));
        if (t.FieldsJson != json) diff.Add(new("fields", FieldsOf(t).Count.ToString(), fields.Count.ToString()));
        if (diff.Count > 0)
        {
            t.Name = name; t.Description = desc; t.FieldsJson = json; t.UpdatedAt = clock.UtcNow; t.Version++;
            audit.Record("assettype.update", "asset_type", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["name"] = name }, diff);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { throw DomainException.Conflict("This asset type was changed by someone else. Reload and try again."); }
            catch (DbUpdateException) { throw DomainException.Conflict("An asset type with this name already exists."); }
        }
        return (await TypesAsync(true, ct)).First(x => x.Id == id);
    }

    /// <summary>An archived type is no longer offered for new assets; its assets keep it.</summary>
    public async Task<AssetTypeItem> SetTypeArchivedAsync(Guid id, bool archived, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var t = await db.AssetTypes.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Asset type");
        if (archived == (t.ArchivedAt is not null)) throw DomainException.Conflict(archived ? "This asset type is already archived." : "This asset type is not archived.");
        if (!archived && await db.AssetTypes.AnyAsync(x => x.Id != id && x.ArchivedAt == null && x.Name.ToLower() == t.Name.ToLower(), ct)) throw DomainException.Conflict("Another active asset type already has this name. Rename one of them first.");
        t.ArchivedAt = archived ? clock.UtcNow : null; t.UpdatedAt = clock.UtcNow; t.Version++;
        audit.Record(archived ? "assettype.archive" : "assettype.restore", "asset_type", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["name"] = t.Name });
        await db.SaveChangesAsync(ct);
        return (await TypesAsync(true, ct)).First(x => x.Id == id);
    }

    // ================================================================ reading
    public async Task<PagedResult<AssetListItem>> ListAsync(AssetQuery q, CancellationToken ct = default)
    {
        var page = Math.Max(1, q.Page); var size = Math.Clamp(q.PageSize, 1, 100);
        var places = await PlacesAsync(ct);
        var query = Filtered(q, places, out var bad);
        if (bad is not null) throw Invalid(bad);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(a => a.Tag).Skip((page - 1) * size).Take(size)
            .Select(a => new { a.Id, a.Tag, a.Name, a.AssetTypeId, Type = db.AssetTypes.Where(t => t.Id == a.AssetTypeId).Select(t => t.Name).FirstOrDefault(), a.Status, a.Serial, a.LocationId,
                Who = db.Users.Where(u => u.Id == a.AssignedToId).Select(u => u.Name).FirstOrDefault(), a.WarrantyEnd, a.UpdatedAt }).ToListAsync(ct);
        return new PagedResult<AssetListItem>([.. rows.Select(r => new AssetListItem(r.Id, r.Tag, r.Name, r.AssetTypeId, r.Type ?? "", Lower(r.Status), r.Serial, r.LocationId is { } l ? PathOf(places, l) : null, r.Who, r.WarrantyEnd, r.UpdatedAt))], page, size, total);
    }

    private IQueryable<Asset> Filtered(AssetQuery q, Dictionary<Guid, Place> places, out Dictionary<string, string>? bad)
    {
        bad = null;
        var query = db.Assets.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(q.Q))
        {
            var pat = "%" + q.Q.Trim().ToLowerInvariant().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            query = query.Where(a => EF.Functions.Like(a.Tag.ToLower(), pat, "\\") || EF.Functions.Like(a.Name.ToLower(), pat, "\\") || (a.Serial != null && EF.Functions.Like(a.Serial.ToLower(), pat, "\\")));
        }
        if (q.TypeId is { } tid) query = query.Where(a => a.AssetTypeId == tid);
        if (!string.IsNullOrWhiteSpace(q.Status) && !q.Status.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            if (q.Status.Equals("active", StringComparison.OrdinalIgnoreCase)) query = query.Where(a => a.RetiredAt == null);
            else if (AssetRules.ParseStatus(q.Status) is { } st && q.Status.Trim().Length > 0) query = query.Where(a => a.Status == st);
            else bad = new Dictionary<string, string> { ["status"] = "Use active, all, in_use, in_repair, stored or retired." };
        }
        else if (string.IsNullOrWhiteSpace(q.Status)) query = query.Where(a => a.RetiredAt == null);   // by default the register shows what is still in service
        if (q.LocationId is { } lid) { var ids = Below(places, lid).ToList(); query = query.Where(a => a.LocationId != null && ids.Contains(a.LocationId.Value)); }
        if (q.AssignedToId is { } uid) query = query.Where(a => a.AssignedToId == uid);
        if (q.Frequent == true) { var since = YearAgo; query = query.Where(a => db.Tickets.Count(t => t.AssetId == a.Id && t.CreatedAt >= since) >= FrequentThreshold); }
        if (q.WarrantyWithinDays is { } days)
        {
            var until = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime).AddDays(Math.Clamp(days, 0, 3650));
            query = query.Where(a => a.RetiredAt == null && a.WarrantyEnd != null && a.WarrantyEnd <= until);
        }
        return query;
    }

    /// <summary>Small list for pickers (ticket edit): active assets matching the text, at most 20.</summary>
    public async Task<IReadOnlyList<AssetPick>> PickAsync(string? q, CancellationToken ct = default)
    {
        var places = await PlacesAsync(ct);
        var rows = await Filtered(new AssetQuery(q), places, out _).OrderBy(a => a.Tag).Take(20)
            .Select(a => new { a.Id, a.Tag, a.Name, Type = db.AssetTypes.Where(t => t.Id == a.AssetTypeId).Select(t => t.Name).FirstOrDefault(), a.LocationId }).ToListAsync(ct);
        return [.. rows.Select(r => new AssetPick(r.Id, r.Tag, r.Name, r.Type ?? "", r.LocationId is { } l ? PathOf(places, l) : null))];
    }

    public async Task<AssetDetail> GetAsync(Guid id, SessionPrincipal who, CancellationToken ct = default)
    {
        var a = await db.Assets.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Asset");
        var type = await db.AssetTypes.AsNoTracking().FirstAsync(t => t.Id == a.AssetTypeId, ct);
        var places = await PlacesAsync(ct);
        var who2 = a.AssignedToId is { } u ? await db.Users.AsNoTracking().Where(x => x.Id == u).Select(x => x.Name).FirstOrDefaultAsync(ct) : null;
        var events = await db.AssetEvents.AsNoTracking().Where(e => e.AssetId == id).OrderByDescending(e => e.CreatedAt).ThenByDescending(e => e.Id).Take(200).ToListAsync(ct);
        var since = YearAgo; var lastYear = await db.Tickets.CountAsync(t => t.AssetId == id && t.CreatedAt >= since, ct);
        var perms = await authz.GetEffectivePermissionsAsync(who.UserId, ct);
        // the requests about this asset, limited to what this person may see in the queue
        List<AssetTicketItem> history = []; var count = 0;
        if (perms.Contains(Domain.Security.Permissions.TicketsReadTeam) || perms.Contains(Domain.Security.Permissions.TicketsReadAll))
        {
            var seen = (await tickets.QueueScopeAsync(who, ct)).Where(t => t.AssetId == id);
            count = await seen.CountAsync(ct);
            history = [.. (await seen.OrderByDescending(t => t.CreatedAt).Take(HistoryShown).Select(t => new { t.Id, t.Number, t.Subject, t.Status, t.CreatedAt }).ToListAsync(ct))
                .Select(t => new AssetTicketItem(t.Id, t.Number, t.Subject, t.Status.ToString().ToLowerInvariant(), t.CreatedAt))];
        }
        return new AssetDetail(a.Id, a.Tag, a.Name, a.AssetTypeId, type.Name, a.Serial, Lower(a.Status), a.LocationId, a.LocationId is { } l ? PathOf(places, l) : null, a.AssignedToId, who2,
            a.Department, a.PurchaseDate, a.PurchaseCost, a.WarrantyEnd, a.VendorName, a.Notes, FieldsOf(type), JsonDocument.Parse(a.FieldsJson).RootElement.Clone(),
            [.. events.Select(e => new AssetEventItem(e.Id, e.Kind.ToString().ToLowerInvariant(), e.ActorLabel, e.Body, e.CreatedAt))], history, count,
            a.RetiredAt, a.RetireReason, a.CreatedAt, a.UpdatedAt, a.Version, perms.Contains(Domain.Security.Permissions.AssetsManage), lastYear, Hint(lastYear, a.WarrantyEnd));
    }

    /// <summary>AST-003. Often-failing assets get a hint; out of warranty it is stronger. Counted over every request about the asset (a number only, never their content).</summary>
    private string? Hint(int lastYear, DateOnly? warrantyEnd)
        => lastYear < FrequentThreshold ? null : warrantyEnd is { } w && w < DateOnly.FromDateTime(clock.UtcNow.UtcDateTime) ? "frequent_out_of_warranty" : "frequent";

    // ================================================================ labels and scanning (AST-002)
    /// <summary>The asset behind a scanned label (by tag) or a link (by id). Any signed-in person of the organisation may read this much: the label is on the thing itself.</summary>
    public async Task<AssetCard> CardAsync(string? tag, Guid? id, SessionPrincipal who, CancellationToken ct = default)
    {
        var norm = AssetRules.NormaliseTag(tag);
        if (norm is null && id is null) throw DomainException.NotFound("Asset");
        var a = await db.Assets.AsNoTracking().Where(x => id != null ? x.Id == id : x.Tag == norm)
            .Select(x => new { x.Id, x.Tag, x.Name, x.LocationId, x.Status, x.RetiredAt, Type = db.AssetTypes.Where(t => t.Id == x.AssetTypeId).Select(t => t.Name).FirstOrDefault() }).FirstOrDefaultAsync(ct)
            ?? throw DomainException.NotFound("Asset");
        var places = await PlacesAsync(ct);
        var canOpen = (await authz.GetEffectivePermissionsAsync(who.UserId, ct)).Contains(Domain.Security.Permissions.AssetsRead);
        return new AssetCard(a.Id, a.Tag, a.Name, a.Type ?? "", a.LocationId, a.LocationId is { } l ? PathOf(places, l) : null, Lower(a.Status), a.RetiredAt != null, canOpen);
    }

    /// <summary>Assets for a sheet of labels: the given ids, or everything matching the filters (in service by default), at most <see cref="MaxLabels"/>.</summary>
    public async Task<IReadOnlyList<AssetLabel>> LabelsAsync(AssetQuery q, IReadOnlyCollection<Guid>? ids, CancellationToken ct = default)
    {
        var places = await PlacesAsync(ct);
        IQueryable<Asset> query;
        if (ids is { Count: > 0 }) { var list = ids.ToList(); query = db.Assets.AsNoTracking().Where(a => list.Contains(a.Id)); }
        else { query = Filtered(q, places, out var bad); if (bad is not null) throw Invalid(bad); }
        var rows = await query.OrderBy(a => a.Tag).Take(MaxLabels)
            .Select(a => new { a.Id, a.Tag, a.Name, a.LocationId, Type = db.AssetTypes.Where(t => t.Id == a.AssetTypeId).Select(t => t.Name).FirstOrDefault() }).ToListAsync(ct);
        return [.. rows.Select(r => new AssetLabel(r.Id, r.Tag, r.Name, r.Type ?? "", r.LocationId is { } l ? PathOf(places, l) : null))];
    }

    // ================================================================ warranty reminders (AST-005)
    /// <summary>
    /// Tells the people who manage assets when a warranty ends within 30 days, and when it has ended: once per warranty date (changing the date starts over). More than a few at once
    /// become one summary. A warranty that ended more than 30 days before anyone could have been told is noted silently. Every organisation, each as itself; safe with several
    /// instances (each reminder is claimed with one conditional update).
    /// </summary>
    public async Task<int> RunWarrantyRemindersAsync(CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime); var soon = today.AddDays(WarrantyNoticeDays); var stale = today.AddDays(-WarrantyNoticeDays);
        var due = await db.Assets.IgnoreQueryFilters().AsNoTracking()
            .Where(a => a.RetiredAt == null && a.WarrantyEnd != null && ((a.WarrantyNotice < 1 && a.WarrantyEnd <= soon && a.WarrantyEnd > today) || (a.WarrantyNotice < 2 && a.WarrantyEnd <= today)))
            .Select(a => new { a.Id, a.TenantId, a.Tag, a.Name, End = a.WarrantyEnd!.Value }).ToListAsync(ct);
        var sent = 0;
        foreach (var org in due.GroupBy(d => d.TenantId))
        {
            using (tenant.Switch(org.Key))
            {
                db.ResetTracking();
                var claimed = new List<(Guid Id, string Tag, string Name, DateOnly End, bool Ended)>();
                foreach (var a in org)
                {
                    var level = a.End <= today ? 2 : 1; var end = a.End;
                    var n = await db.Assets.Where(x => x.Id == a.Id && x.WarrantyNotice < level && x.WarrantyEnd == end).ExecuteUpdateAsync(u => u.SetProperty(x => x.WarrantyNotice, level), ct);
                    if (n == 1 && !(level == 2 && a.End < stale)) claimed.Add((a.Id, a.Tag, a.Name, a.End, level == 2));
                }
                if (claimed.Count == 0) continue;
                var people = await db.Users.AsNoTracking().Where(u => u.Status == UserStatus.Active
                        && u.UserRoles.Any(ur => ur.Role!.ArchivedAt == null && ur.Role.Permissions.Any(p => p.PermissionKey == Domain.Security.Permissions.AssetsManage)))
                    .OrderBy(u => u.Id).Select(u => u.Id).Take(MaxReminderRecipients).ToListAsync(ct);
                string Url(string path) => links.Origin.TrimEnd('/') + path;
                foreach (var uid in people)
                {
                    if (claimed.Count > WarrantyDigestAbove)
                    {
                        var ended = claimed.Count(c => c.Ended); var ending = claimed.Count - ended;
                        var title = $"{claimed.Count} asset warranties end soon or have ended"; var body = $"Ending within {WarrantyNoticeDays} days: {ending}. Ended: {ended}.";
                        await notifications.SendAsync(uid, "asset.warranty", title, body, "/assets?warranty=30", null, (to, name) => Email.EmailTemplates.Notification(to, name, title, body, Url("/assets?warranty=30")), ct: ct);
                    }
                    else foreach (var c in claimed)
                    {
                        var title = c.Ended ? $"Warranty of {c.Tag} has ended" : $"Warranty of {c.Tag} ends on {c.End:yyyy-MM-dd}";
                        await notifications.SendAsync(uid, "asset.warranty", title, c.Name, $"/assets/{c.Id}", null, (to, name) => Email.EmailTemplates.Notification(to, name, title, c.Name, Url($"/assets/{c.Id}")), ct: ct);
                    }
                }
                await db.SaveChangesAsync(ct);
                sent += claimed.Count;
            }
        }
        return sent;
    }

    // ================================================================ checking one asset (shared by the form and the import)
    private sealed record Draft(string? Tag, string Name, Guid TypeId, string? Serial, AssetStatus Status, Guid? LocationId, Guid? AssignedToId, string? Department,
        DateOnly? PurchaseDate, decimal? PurchaseCost, DateOnly? WarrantyEnd, string? VendorName, string? Notes, Dictionary<string, JsonElement> Fields);

    private static void CheckDraft(Draft d, Dictionary<string, string> e)
    {
        if (d.Tag is { } tag && !AssetRules.IsValidTag(tag)) e["tag"] = "Use 1–40 letters, digits and . _ / - (it is stored in capitals).";
        if (d.Name.Length is < AssetRules.NameMin or > AssetRules.NameMax) e["name"] = $"Use {AssetRules.NameMin}–{AssetRules.NameMax} characters.";
        if (d.Serial is { Length: > AssetRules.SerialMax }) e["serial"] = $"Use at most {AssetRules.SerialMax} characters.";
        if (d.Department is { Length: > AssetRules.DepartmentMax }) e["department"] = $"Use at most {AssetRules.DepartmentMax} characters.";
        if (d.VendorName is { Length: > AssetRules.VendorMax }) e["vendorName"] = $"Use at most {AssetRules.VendorMax} characters.";
        if (d.Notes is { Length: > AssetRules.NotesMax }) e["notes"] = $"Use at most {AssetRules.NotesMax} characters.";
        if (d.PurchaseCost is { } c && (c < 0 || c > AssetRules.CostMax || decimal.Round(c, 2) != c)) e["purchaseCost"] = "Use a positive amount with at most two decimals.";
        foreach (var (key, date) in new[] { ("purchaseDate", d.PurchaseDate), ("warrantyEnd", d.WarrantyEnd) })
            if (date is { } v && (v.Year < 1950 || v.Year > 2100)) e[key] = "Use a date between 1950 and 2100.";
        if (d.PurchaseDate is { } p && d.WarrantyEnd is { } w && w < p && !e.ContainsKey("warrantyEnd")) e["warrantyEnd"] = "The warranty can't end before the purchase date.";
        if (d.Status == AssetStatus.Retired) e["status"] = "Retire an asset with the Retire action, which asks for a reason.";
    }

    private static string? EvaluateFields(AssetType type, Dictionary<string, JsonElement> answers, Dictionary<string, string> e)
    {
        var check = FormEngine.Evaluate(FieldsOf(type), answers);
        foreach (var (k, v) in check.Errors) e[$"fields.{k}"] = v;
        return check.IsValid ? JsonSerializer.Serialize(check.Values) : null;
    }

    private async Task<string> NewTagAsync(CancellationToken ct)
    {
        for (var i = 0; i < 100; i++)
        {
            var tag = AssetRules.AutoTag(await db.NextAssetNumberAsync(tenant.TenantId, ct));
            if (!await db.Assets.AnyAsync(a => a.Tag == tag, ct)) return tag;          // someone typed this tag by hand: take the next one
        }
        throw DomainException.Conflict("Could not find a free automatic tag. Give the asset a tag of its own.");
    }

    private void Event(Guid assetId, AssetEventKind kind, SessionPrincipal? who, string label, string? body, DateTimeOffset now)
        => db.AssetEvents.Add(new AssetEvent { AssetId = assetId, Kind = kind, ActorId = who?.UserId, ActorLabel = who?.Name ?? label, Body = body is null ? null : (body.Length > 1000 ? body[..1000] : body), CreatedAt = now });

    private Draft FromRequest(SaveAssetRequest r, Dictionary<string, string> e)
    {
        var status = AssetRules.ParseStatus(r.Status);
        if (status is null) e["status"] = "Use in_use, in_repair or stored.";
        if (r.TypeId is null) e["typeId"] = "Choose the asset type.";
        return new Draft(AssetRules.NormaliseTag(r.Tag), (r.Name ?? "").Trim(), r.TypeId ?? Guid.Empty, Clean(r.Serial), status ?? AssetStatus.InUse, r.LocationId, r.AssignedToId, Clean(r.Department),
            r.PurchaseDate, r.PurchaseCost, r.WarrantyEnd, Clean(r.VendorName), Clean(r.Notes), r.Fields ?? []);
    }

    private async Task CheckReferencesAsync(Draft d, Guid? self, Dictionary<string, string> e, CancellationToken ct)
    {
        if (d.Tag is { } tag && !e.ContainsKey("tag") && await db.Assets.AnyAsync(a => a.Tag == tag && a.Id != self, ct)) e["tag"] = "Another asset already has this tag.";
        if (d.LocationId is { } l && !await db.Locations.AnyAsync(x => x.Id == l && x.ArchivedAt == null, ct)) e["locationId"] = "Choose an active location.";
        if (d.AssignedToId is { } u && !await db.Users.AnyAsync(x => x.Id == u && x.Status == UserStatus.Active, ct)) e["assignedToId"] = "Choose an active person.";
    }

    // ================================================================ writing
    public async Task<AssetDetail> CreateAsync(SaveAssetRequest req, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var e = Errors(); var d = FromRequest(req, e); CheckDraft(d, e);
        var type = d.TypeId == Guid.Empty ? null : await db.AssetTypes.FirstOrDefaultAsync(t => t.Id == d.TypeId && t.ArchivedAt == null, ct);
        if (type is null && !e.ContainsKey("typeId")) e["typeId"] = "Choose an active asset type.";
        await CheckReferencesAsync(d, null, e, ct);
        var fields = type is null ? null : EvaluateFields(type, d.Fields, e);
        if (e.Count > 0) throw Invalid(e);

        var now = clock.UtcNow;
        var a = new Asset
        {
            Tag = d.Tag ?? await NewTagAsync(ct), Name = d.Name, AssetTypeId = type!.Id, Serial = d.Serial, Status = d.Status, LocationId = d.LocationId, AssignedToId = d.AssignedToId, Department = d.Department,
            PurchaseDate = d.PurchaseDate, PurchaseCost = d.PurchaseCost, WarrantyEnd = d.WarrantyEnd, VendorName = d.VendorName, Notes = d.Notes, FieldsJson = fields!, CreatedAt = now, UpdatedAt = now,
        };
        db.Assets.Add(a);
        Event(a.Id, AssetEventKind.Created, actor, "", $"Added to the register as {a.Tag}.", now);
        audit.Record("asset.create", "asset", a.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["tag"] = a.Tag, ["type"] = type.Name });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { throw DomainException.Conflict("Another asset already has this tag."); }   // lost a race for the same tag
        return await GetAsync(a.Id, actor, ct);
    }

    public async Task<AssetDetail> UpdateAsync(Guid id, SaveAssetRequest req, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var a = await db.Assets.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Asset");
        if (a.RetiredAt is not null) throw DomainException.Conflict("This asset is retired. Restore it before editing.");
        if (req.Version is { } v && v != a.Version) throw DomainException.Conflict("This asset was changed by someone else. Reload and try again.");
        var e = Errors(); var d = FromRequest(req, e); CheckDraft(d, e);
        if (d.Tag is null) e["tag"] = "An asset keeps a tag.";
        var type = d.TypeId == Guid.Empty ? null : await db.AssetTypes.FirstOrDefaultAsync(t => t.Id == d.TypeId && (t.ArchivedAt == null || t.Id == a.AssetTypeId), ct);
        if (type is null && !e.ContainsKey("typeId")) e["typeId"] = "Choose an active asset type.";
        await CheckReferencesAsync(d, id, e, ct);
        var fields = type is null ? null : EvaluateFields(type, d.Fields, e);
        if (e.Count > 0) throw Invalid(e);

        var now = clock.UtcNow; var diff = new List<AuditDiff>(); var other = new List<string>();
        var places = await PlacesAsync(ct);
        string PlaceName(Guid? l) => l is { } x && places.ContainsKey(x) ? PathOf(places, x) : "no place";
        async Task<string> Person(Guid? u) => u is { } x ? await db.Users.AsNoTracking().Where(p => p.Id == x).Select(p => p.Name).FirstOrDefaultAsync(ct) ?? "someone" : "nobody";

        if (a.LocationId != d.LocationId) { diff.Add(new("location", a.LocationId?.ToString() ?? "—", d.LocationId?.ToString() ?? "—")); Event(id, AssetEventKind.Moved, actor, "", $"Moved from {PlaceName(a.LocationId)} to {PlaceName(d.LocationId)}.", now); }
        if (a.AssignedToId != d.AssignedToId)
        {
            diff.Add(new("assignedTo", a.AssignedToId?.ToString() ?? "—", d.AssignedToId?.ToString() ?? "—"));
            Event(id, AssetEventKind.Assigned, actor, "", d.AssignedToId is null ? $"Taken back from {await Person(a.AssignedToId)}." : $"Handed to {await Person(d.AssignedToId)}.", now);
        }
        if (a.Status != d.Status) { diff.Add(new("status", Lower(a.Status), Lower(d.Status))); Event(id, AssetEventKind.Status, actor, "", $"Status changed from {Lower(a.Status).Replace('_', ' ')} to {Lower(d.Status).Replace('_', ' ')}.", now); }
        void C<T>(string key, string label, T before, T after) { if (!EqualityComparer<T>.Default.Equals(before, after)) { diff.Add(new(key, before?.ToString() ?? "—", after?.ToString() ?? "—")); other.Add(label); } }
        C("tag", "tag", a.Tag, d.Tag!); C("name", "name", a.Name, d.Name); C("type", "type", a.AssetTypeId, type!.Id); C("serial", "serial number", a.Serial, d.Serial); C("department", "department", a.Department, d.Department);
        C("purchaseDate", "purchase date", a.PurchaseDate, d.PurchaseDate); C("purchaseCost", "purchase cost", a.PurchaseCost, d.PurchaseCost); C("warrantyEnd", "warranty end", a.WarrantyEnd, d.WarrantyEnd);
        C("vendor", "vendor", a.VendorName, d.VendorName); C("notes", "notes", a.Notes, d.Notes);
        if (!JsonEqual(a.FieldsJson, fields!)) { diff.Add(new("fields", "…", "…")); other.Add("details"); }
        if (diff.Count == 0) return await GetAsync(id, actor, ct);
        if (other.Count > 0) Event(id, AssetEventKind.Updated, actor, "", $"Changed: {string.Join(", ", other)}.", now);

        a.Tag = d.Tag!; a.Name = d.Name; a.AssetTypeId = type.Id; a.Serial = d.Serial; a.Status = d.Status; a.LocationId = d.LocationId; a.AssignedToId = d.AssignedToId; a.Department = d.Department;
        if (a.WarrantyEnd != d.WarrantyEnd) a.WarrantyNotice = 0;
        a.PurchaseDate = d.PurchaseDate; a.PurchaseCost = d.PurchaseCost; a.WarrantyEnd = d.WarrantyEnd; a.VendorName = d.VendorName; a.Notes = d.Notes; a.FieldsJson = fields!; a.UpdatedAt = now; a.Version++;
        audit.Record("asset.update", "asset", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["tag"] = a.Tag }, diff);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw DomainException.Conflict("This asset was changed by someone else. Reload and try again."); }
        catch (DbUpdateException) { throw DomainException.Conflict("Another asset already has this tag."); }
        return await GetAsync(id, actor, ct);
    }

    private static bool JsonEqual(string a, string b)
    {
        try { return JsonElement.DeepEquals(JsonDocument.Parse(a).RootElement, JsonDocument.Parse(b).RootElement); } catch (JsonException) { return a == b; }
    }

    public async Task<AssetDetail> RetireAsync(Guid id, RetireAssetRequest req, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var a = await db.Assets.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Asset");
        if (a.RetiredAt is not null) throw DomainException.Conflict("This asset is already retired.");
        if (req.Version is { } v && v != a.Version) throw DomainException.Conflict("This asset was changed by someone else. Reload and try again.");
        var reason = (req.Reason ?? "").Trim();
        if (reason.Length is < AssetRules.ReasonMin or > AssetRules.ReasonMax) throw Invalid(new Dictionary<string, string> { ["reason"] = $"Give a reason of {AssetRules.ReasonMin}–{AssetRules.ReasonMax} characters." });
        var now = clock.UtcNow; var before = a.Status;
        a.Status = AssetStatus.Retired; a.RetiredAt = now; a.RetireReason = reason; a.UpdatedAt = now; a.Version++;
        Event(id, AssetEventKind.Retired, actor, "", $"Retired: {reason}", now);
        audit.Record("asset.retire", "asset", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["tag"] = a.Tag }, [new AuditDiff("status", Lower(before), "retired")]);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, actor, ct);
    }

    /// <summary>Brings a retired asset back into the register as stored (someone then moves or hands it out).</summary>
    public async Task<AssetDetail> RestoreAsync(Guid id, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var a = await db.Assets.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Asset");
        if (a.RetiredAt is null) throw DomainException.Conflict("This asset is not retired.");
        var now = clock.UtcNow;
        a.Status = AssetStatus.Stored; a.RetiredAt = null; a.RetireReason = null; a.UpdatedAt = now; a.Version++;
        Event(id, AssetEventKind.Restored, actor, "", "Back in the register (stored).", now);
        audit.Record("asset.restore", "asset", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["tag"] = a.Tag }, [new AuditDiff("status", "retired", "stored")]);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, actor, ct);
    }

    // ================================================================ CSV export (AST-006)
    private static string FieldColumn(string key) => "x_" + key;

    public async Task<AssetExport> ExportAsync(AssetQuery q, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var places = await PlacesAsync(ct);
        var query = Filtered(q, places, out var bad); if (bad is not null) throw Invalid(bad);
        var rows = await query.OrderBy(a => a.Tag).Take(MaxExportRows + 1)
            .Select(a => new { a, Type = db.AssetTypes.Where(t => t.Id == a.AssetTypeId).Select(t => t.Name).FirstOrDefault(), Email = db.Users.Where(u => u.Id == a.AssignedToId).Select(u => u.Email).FirstOrDefault() }).ToListAsync(ct);
        var truncated = rows.Count > MaxExportRows; if (truncated) rows.RemoveAt(rows.Count - 1);
        // one type chosen: its own questions become columns too, so the file can be edited and imported again
        var typeFields = q.TypeId is { } tid ? FieldsOf(await db.AssetTypes.AsNoTracking().FirstAsync(t => t.Id == tid, ct)) : [];
        var sb = new StringBuilder(string.Join(",", Columns.Concat(typeFields.Select(f => FieldColumn(f.Key))))).Append("\r\n");
        foreach (var r in rows)
        {
            var a = r.a; var values = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(a.FieldsJson) ?? [];
            string?[] cells = [a.Tag, a.Name, r.Type, a.Serial, Lower(a.Status), a.LocationId is { } l && places.TryGetValue(l, out var p) ? p.Code : null, r.Email, a.Department,
                a.PurchaseDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), a.PurchaseCost?.ToString("0.##", CultureInfo.InvariantCulture), a.WarrantyEnd?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), a.VendorName, a.Notes,
                .. typeFields.Select(f => values.TryGetValue(f.Key, out var v) ? CellOf(v) : null)];
            sb.AppendJoin(',', cells.Select(MonitoringService.Csv)).Append("\r\n");
        }
        audit.Record("asset.export", "asset", null, Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["rows"] = rows.Count.ToString(), ["truncated"] = truncated.ToString().ToLowerInvariant() });
        await db.SaveChangesAsync(ct);
        return new AssetExport(sb.ToString(), $"assets-{clock.UtcNow:yyyyMMdd-HHmmss}.csv", rows.Count, truncated);
    }

    private static string? CellOf(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.String => v.GetString(), JsonValueKind.Number => v.GetRawText(), JsonValueKind.True => "yes", JsonValueKind.False => "no",
        JsonValueKind.Array => string.Join("; ", v.EnumerateArray().Select(x => x.ToString())), _ => null,
    };

    // ================================================================ CSV import (AST-006)
    /// <summary>
    /// Reads a CSV file (first line = column names, see <see cref="Columns"/>; <c>x_&lt;key&gt;</c> columns answer the type's questions). A line whose tag exists updates that asset (an empty
    /// cell keeps the current value); any other line creates one (no tag → the next automatic tag). Every line is checked like the form; nothing is written unless every line is
    /// correct and <see cref="ImportRequest.DryRun"/> is false, and then all lines are written in one transaction.
    /// </summary>
    public async Task<ImportReport> ImportAsync(ImportRequest req, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var text = req.Csv ?? "";
        if (text.Length == 0) throw Invalid(new Dictionary<string, string> { ["csv"] = "Choose a CSV file." });
        if (text.Length > MaxImportChars) throw Invalid(new Dictionary<string, string> { ["csv"] = "The file is too large (at most 2 MB)." });
        List<string[]> lines;
        try { lines = AssetRules.ParseCsv(text); } catch (FormatException ex) { throw Invalid(new Dictionary<string, string> { ["csv"] = $"The file can't be read: {ex.Message}" }); }
        if (lines.Count < 2) throw Invalid(new Dictionary<string, string> { ["csv"] = "The file has no rows below the column names." });
        if (lines.Count - 1 > MaxImportRows) throw Invalid(new Dictionary<string, string> { ["csv"] = $"At most {MaxImportRows} rows per file." });

        var header = lines[0].Select(h => h.Trim().ToLowerInvariant()).ToArray();
        var col = new Dictionary<string, int>();
        for (var i = 0; i < header.Length; i++)
        {
            if (header[i].Length == 0) continue;
            if (!Columns.Contains(header[i]) && !header[i].StartsWith("x_")) throw Invalid(new Dictionary<string, string> { ["csv"] = $"Unknown column “{header[i]}”. Use: {string.Join(", ", Columns)}, and x_<question key> for a type's own questions." });
            if (!col.TryAdd(header[i], i)) throw Invalid(new Dictionary<string, string> { ["csv"] = $"The column “{header[i]}” appears twice." });
        }
        if (!col.ContainsKey("name") || !col.ContainsKey("type")) throw Invalid(new Dictionary<string, string> { ["csv"] = "The columns “name” and “type” are required." });

        var types = (await db.AssetTypes.AsNoTracking().Where(t => t.ArchivedAt == null).ToListAsync(ct)).GroupBy(t => t.Name.ToLowerInvariant()).ToDictionary(g => g.Key, g => g.First());
        var places = (await db.Locations.AsNoTracking().Where(l => l.ArchivedAt == null).Select(l => new { l.Id, l.Code }).ToListAsync(ct)).ToDictionary(l => l.Code.ToUpperInvariant(), l => l.Id);
        var emails = new HashSet<string>(lines.Skip(1).Select(r => Cell(r, col, "assigned_to_email")?.ToLowerInvariant()).OfType<string>());
        var people = (await db.Users.AsNoTracking().Where(u => emails.Contains(u.Email) && u.Status == UserStatus.Active).Select(u => new { u.Id, u.Email }).ToListAsync(ct)).ToDictionary(u => u.Email.ToLowerInvariant(), u => u.Id);
        var tags = new HashSet<string>(lines.Skip(1).Select(r => AssetRules.NormaliseTag(Cell(r, col, "tag"))).OfType<string>());
        var existing = (await db.Assets.Where(a => tags.Contains(a.Tag)).ToListAsync(ct)).ToDictionary(a => a.Tag);

        var problems = new List<ImportProblem>(); var plan = new List<(Draft D, Asset? Existing, AssetType Type, string Fields)>(); var seen = new HashSet<string>();
        for (var n = 1; n < lines.Count; n++)
        {
            var r = lines[n]; var e = Errors(); var tag = AssetRules.NormaliseTag(Cell(r, col, "tag"));
            if (r.Length > header.Length) e["csv"] = "This line has more values than there are columns.";
            if (tag is not null && !seen.Add(tag)) e["tag"] = "This tag appears more than once in the file.";
            existing.TryGetValue(tag ?? "", out var cur);
            if (cur?.RetiredAt is not null) e["tag"] = "This asset is retired. Restore it in the app first.";

            var typeName = Cell(r, col, "type"); AssetType? type = null;
            if (typeName is null) { if (cur is not null) type = await db.AssetTypes.AsNoTracking().FirstAsync(t => t.Id == cur.AssetTypeId, ct); else e["type"] = "Give the asset type."; }
            else if (!types.TryGetValue(typeName.ToLowerInvariant(), out type)) e["type"] = $"No active asset type is called “{typeName}”.";

            var statusText = Cell(r, col, "status"); var status = statusText is null ? cur?.Status ?? AssetStatus.InUse : AssetRules.ParseStatus(statusText);
            if (status is null) e["status"] = "Use in_use, in_repair or stored.";
            Guid? loc = cur?.LocationId; var code = Cell(r, col, "location_code");
            if (code is not null) { if (places.TryGetValue(code.ToUpperInvariant(), out var lid)) loc = lid; else e["location_code"] = $"No active location has the code “{code}”."; }
            Guid? person = cur?.AssignedToId; var mail = Cell(r, col, "assigned_to_email");
            if (mail is not null) { if (people.TryGetValue(mail.ToLowerInvariant(), out var pid)) person = pid; else e["assigned_to_email"] = $"No active person has the e-mail “{mail}”."; }
            if (!AssetRules.TryDate(Cell(r, col, "purchase_date"), out var bought)) e["purchase_date"] = "Use yyyy-mm-dd."; else if (Cell(r, col, "purchase_date") is null) bought = cur?.PurchaseDate;
            if (!AssetRules.TryDate(Cell(r, col, "warranty_end"), out var warranty)) e["warranty_end"] = "Use yyyy-mm-dd."; else if (Cell(r, col, "warranty_end") is null) warranty = cur?.WarrantyEnd;
            if (!AssetRules.TryCost(Cell(r, col, "purchase_cost"), out var cost)) e["purchase_cost"] = "Use a number like 1250000 or 1250.50 (dot for decimals, no thousands separators)."; else if (Cell(r, col, "purchase_cost") is null) cost = cur?.PurchaseCost;

            // the type's own questions: what the asset had, overwritten by the file's non-empty cells
            var answers = cur is not null && cur.AssetTypeId == type?.Id ? JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(cur.FieldsJson) ?? [] : new Dictionary<string, JsonElement>();
            if (type is not null)
            {
                var defs = FieldsOf(type).ToDictionary(f => f.Key);
                foreach (var (h, i) in col.Where(c => c.Key.StartsWith("x_")))
                {
                    var key = h[2..]; var raw = i < r.Length ? r[i].Trim() : "";
                    if (raw.Length == 0) continue;
                    if (!defs.TryGetValue(key, out var f)) { e[h] = $"“{type.Name}” has no question “{key}”."; continue; }
                    if (FieldValue(f, raw) is { } jv) answers[key] = jv; else e[h] = f.Type == FieldTypes.YesNo ? "Use yes or no." : f.Type == FieldTypes.Number ? "Enter a number." : "Not one of the choices.";
                }
            }
            var d = new Draft(tag, Cell(r, col, "name") ?? cur?.Name ?? "", type?.Id ?? Guid.Empty, Cell(r, col, "serial") ?? cur?.Serial, status ?? AssetStatus.InUse, loc, person,
                Cell(r, col, "department") ?? cur?.Department, bought, cost, warranty, Cell(r, col, "vendor") ?? cur?.VendorName, Cell(r, col, "notes") ?? cur?.Notes, answers);
            CheckDraft(d, e);
            var fieldsJson = type is null ? null : EvaluateFields(type, answers, e);
            if (e.Count > 0) problems.Add(new ImportProblem(n + 1, tag, e)); else plan.Add((d, cur, type!, fieldsJson!));
        }

        var creates = plan.Count(p => p.Existing is null); var updates = plan.Count - creates;
        if (problems.Count > 0 || req.DryRun) return new ImportReport(lines.Count - 1, creates, updates, problems.Take(500).ToList(), false);

        await db.InTransactionAsync(async () =>
        {
            var now = clock.UtcNow;
            foreach (var (d, cur, type, fields) in plan)
            {
                if (cur is null)
                {
                    var a = new Asset { Tag = d.Tag ?? await NewTagAsync(ct), Name = d.Name, AssetTypeId = type.Id, Serial = d.Serial, Status = d.Status, LocationId = d.LocationId, AssignedToId = d.AssignedToId, Department = d.Department,
                        PurchaseDate = d.PurchaseDate, PurchaseCost = d.PurchaseCost, WarrantyEnd = d.WarrantyEnd, VendorName = d.VendorName, Notes = d.Notes, FieldsJson = fields, CreatedAt = now, UpdatedAt = now };
                    db.Assets.Add(a);
                    Event(a.Id, AssetEventKind.Created, actor, "", $"Imported from a CSV file as {a.Tag}.", now);
                }
                else
                {
                    if (cur.WarrantyEnd != d.WarrantyEnd) cur.WarrantyNotice = 0;
                    cur.Name = d.Name; cur.AssetTypeId = type.Id; cur.Serial = d.Serial; cur.Status = d.Status; cur.LocationId = d.LocationId; cur.AssignedToId = d.AssignedToId; cur.Department = d.Department;
                    cur.PurchaseDate = d.PurchaseDate; cur.PurchaseCost = d.PurchaseCost; cur.WarrantyEnd = d.WarrantyEnd; cur.VendorName = d.VendorName; cur.Notes = d.Notes; cur.FieldsJson = fields; cur.UpdatedAt = now; cur.Version++;
                    Event(cur.Id, AssetEventKind.Updated, actor, "", "Updated from a CSV file.", now);
                }
            }
            audit.Record("asset.import", "asset", null, Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["rows"] = plan.Count.ToString(), ["created"] = creates.ToString(), ["updated"] = updates.ToString() });
            await db.SaveChangesAsync(ct);
            return 0;
        }, ct);
        return new ImportReport(lines.Count - 1, creates, updates, [], true);
    }

    private static string? Cell(string[] row, Dictionary<string, int> col, string name) => col.TryGetValue(name, out var i) && i < row.Length && row[i].Trim().Length > 0 ? row[i].Trim() : null;

    /// <summary>Turns a CSV cell into the JSON the form engine expects for that question; null when it can't be.</summary>
    private static JsonElement? FieldValue(FormField f, string raw)
    {
        switch (f.Type)
        {
            case FieldTypes.Number:
                return decimal.TryParse(raw, NumberStyles.Number & ~NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var n) ? JsonSerializer.SerializeToElement(n) : null;
            case FieldTypes.YesNo:
                var t = raw.ToLowerInvariant();
                return t is "yes" or "true" or "ya" or "1" ? JsonSerializer.SerializeToElement(true) : t is "no" or "false" or "tidak" or "0" ? JsonSerializer.SerializeToElement(false) : null;
            case FieldTypes.Select:
                var o = f.Options?.FirstOrDefault(x => x.Value.Equals(raw, StringComparison.OrdinalIgnoreCase) || x.Label.Equals(raw, StringComparison.OrdinalIgnoreCase));
                return o is null ? null : JsonSerializer.SerializeToElement(o.Value);
            default:
                return JsonSerializer.SerializeToElement(raw);
        }
    }
}
