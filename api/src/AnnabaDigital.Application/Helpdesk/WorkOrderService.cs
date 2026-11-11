using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Application.Users;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Application.Helpdesk;

public sealed record WoTaskItem(Guid Id, int Position, string Text, bool Required, DateTimeOffset? DoneAt, string? DoneBy, string? Note);
public sealed record WoTimeItem(Guid Id, Guid UserId, string Person, int Minutes, DateOnly Date, string? Note, bool CanDelete);
public sealed record WoCostItem(Guid Id, string Kind, string Description, decimal Quantity, decimal UnitCost, decimal Total, string By, bool CanDelete, bool FromStock = false);
public sealed record WoFile(Guid Id, string FileName, string ContentType, string? Stage, bool Inline, DateTimeOffset CreatedAt);
public sealed record WorkOrderListItem(Guid Id, string Number, string Title, string Status, bool Required, Guid? AssigneeId, string? AssigneeName, string? TeamName, Guid TicketId, string TicketNumber, string TicketSubject,
    DateTimeOffset? DueAt, int TasksDone, int TasksTotal, DateTimeOffset UpdatedAt, string? VendorName = null);
public sealed record WorkOrderDetail(Guid Id, string Number, string Title, string? Instructions, string Status, bool Required, bool RequireAfterPhoto, Guid? AssigneeId, string? AssigneeName, Guid? TeamId, string? TeamName,
    Guid TicketId, string TicketNumber, string TicketSubject, string TicketStatus, Guid? AssetId, string? AssetTag, string? AssetName, DateTimeOffset? DueAt, string? HoldReason, DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt,
    string? CompletionNote, string? CancelReason, IReadOnlyList<WoTaskItem> Tasks, IReadOnlyList<WoTimeItem> Times, int TotalMinutes, IReadOnlyList<WoCostItem>? Costs, decimal? CostTotal, IReadOnlyList<WoFile> Files,
    IReadOnlyList<string> Allowed, bool CanEdit, bool CanWork, bool CanCosts, IReadOnlyList<string> Missing, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, int Version,
    Guid? VendorId = null, string? VendorName = null, string? VendorPhone = null, string? PurchaseOrder = null, decimal? LabourCost = null);
public sealed record TicketWorkOrders(bool ResolveTicket, bool CanManage, IReadOnlyList<WorkOrderListItem> Items);
public sealed record TaskInput(Guid? Id, string? Text, bool Required = true);
public sealed record SaveWorkOrderRequest(string? Title, string? Instructions, Guid? AssigneeId, Guid? TeamId, DateTimeOffset? DueAt, bool Required = true, bool RequireAfterPhoto = false,
    TaskInput[]? Tasks = null, Guid? TemplateId = null, bool? ResolveTicket = null, int? Version = null, Guid? VendorId = null, string? PurchaseOrder = null);
public sealed record WorkOrderActionRequest(string? Action, string? Reason, string? Note, int? Version);
public sealed record TickRequest(bool Done, string? Note);
public sealed record LogTimeRequest(int Minutes, DateOnly? Date, string? Note);
public sealed record AddCostRequest(string? Kind, string? Description, decimal Quantity, decimal UnitCost);
public sealed record AddStockPartRequest(Guid? ItemId, Guid? LocationId, int? Quantity);
public sealed record ResolveSettingRequest(bool ResolveTicket);
public sealed record OfflineSnapshot(DateTimeOffset TakenAt, IReadOnlyList<WorkOrderDetail> Items);
public sealed record TemplateLine(string Text, bool Required = true);
public sealed record ChecklistTemplateItem(Guid Id, string Name, IReadOnlyList<TemplateLine> Items, bool Archived, int Version);
public sealed record SaveTemplateRequest(string? Name, TemplateLine[]? Items, int? Version = null);

/// <summary>
/// TKT-006/007/014. Work orders spawned from tickets. Who sees one: people who may read work orders and can see its ticket, and the person it is given to. Who changes it:
/// <c>workorders.manage</c> (create, edit, assign, cancel). Who does it: the person it is given to — or anyone allowed to do work orders when nobody is, who then takes it —
/// with <c>workorders.complete</c> (start, hold, checklist, time, photos, complete); managers may too. Money is behind <c>workorders.costs</c>. Every step is a staff-only line
/// on the ticket's timeline and an audit row. When the ticket asks for it, completing its last required work order resolves the ticket.
/// </summary>
public sealed class WorkOrderService(IAppDb db, IClock clock, AuditWriter audit, AuthorizationService authz, TicketService tickets, NotificationService notifications, AttachmentService attachments, IAppLinks links, AnnabaDigital.Application.Integrations.IntegrationService integrations, InventoryService inventory)
{
    public const int MaxPerTicket = 50, MaxTimeDaysBack = 90;

    /// <summary><see cref="VendorId"/>: set for a vendor's person (R2-11), who sees and works only that vendor's work orders.</summary>
    private sealed record Who(SessionPrincipal P, IReadOnlySet<string> Perms, Guid? VendorId = null)
    {
        public bool Has(string p) => Perms.Contains(p);
        public Guid Id => P.UserId;
    }
    private async Task<Who> WhoAsync(SessionPrincipal p, CancellationToken ct)
        => new(p, await authz.GetEffectivePermissionsAsync(p.UserId, ct), await db.VendorUsers.AsNoTracking().Where(v => v.UserId == p.UserId).Select(v => (Guid?)v.VendorId).FirstOrDefaultAsync(ct));
    private static DomainException Invalid(Dictionary<string, string> e) => DomainException.Validation("Please correct the highlighted fields.", e);
    private static string? Clean(string? s) { var t = (s ?? "").Trim(); return t.Length == 0 ? null : t; }
    private static string Name(WorkOrderStatus s) => WorkOrderRules.StatusName(s);
    private string Url(string path) => links.Origin.TrimEnd('/') + path;

    /// <summary>The work orders this person may see. Read-only: the ticket scope inside makes the whole query untracked, so changes load the row again by id.</summary>
    private async Task<IQueryable<WorkOrder>> VisibleAsync(Who w, CancellationToken ct)
    {
        if (w.VendorId is { } vendor) return db.WorkOrders.AsNoTracking().Where(x => x.VendorId == vendor);   // a vendor's person: their company's work only
        var seen = (await tickets.SeenTicketsAsync(w.P, ct)).Select(t => t.Id);
        var me = w.Id;
        return db.WorkOrders.AsNoTracking().Where(x => x.AssigneeId == me || seen.Contains(x.TicketId));
    }

    private static bool CanWork(Who w, WorkOrder x) => w.Has(Permissions.WorkOrdersManage)
        || (w.Has(Permissions.WorkOrdersComplete) && (w.VendorId is { } v ? x.VendorId == v : x.AssigneeId == w.Id || x.AssigneeId is null));

    // ================================================================ reading
    public async Task<TicketWorkOrders> ForTicketAsync(Guid ticketId, SessionPrincipal p, CancellationToken ct = default)
    {
        var w = await WhoAsync(p, ct);
        var t = await (await tickets.SeenTicketsAsync(p, ct)).Where(x => x.Id == ticketId).Select(x => new { x.Id, x.ResolveWithWorkOrders }).FirstOrDefaultAsync(ct) ?? throw DomainException.NotFound("Ticket");
        var rows = await ListQuery(db.WorkOrders.AsNoTracking().Where(x => x.TicketId == t.Id).OrderBy(x => x.Seq)).ToListAsync(ct);
        return new TicketWorkOrders(t.ResolveWithWorkOrders, w.Has(Permissions.WorkOrdersManage), rows);
    }

    private IQueryable<WorkOrderListItem> ListQuery(IQueryable<WorkOrder> q) => q.Select(x => new WorkOrderListItem(x.Id, x.Number, x.Title,
        x.Status == WorkOrderStatus.InProgress ? "in_progress" : x.Status == WorkOrderStatus.OnHold ? "on_hold" : x.Status == WorkOrderStatus.Open ? "open" : x.Status == WorkOrderStatus.Done ? "done" : "cancelled",
        x.Required, x.AssigneeId, db.Users.Where(u => u.Id == x.AssigneeId).Select(u => u.Name).FirstOrDefault(), db.Teams.Where(tm => tm.Id == x.TeamId).Select(tm => tm.Name).FirstOrDefault(),
        x.TicketId, db.Tickets.Where(t => t.Id == x.TicketId).Select(t => t.Number).FirstOrDefault()!, db.Tickets.Where(t => t.Id == x.TicketId).Select(t => t.Subject).FirstOrDefault()!,
        x.DueAt, db.WorkOrderTasks.Count(k => k.WorkOrderId == x.Id && k.DoneAt != null), db.WorkOrderTasks.Count(k => k.WorkOrderId == x.Id), x.UpdatedAt,
        db.Vendors.Where(v => v.Id == x.VendorId).Select(v => v.Name).FirstOrDefault()));

    /// <summary>A vendor's work orders that this person may see (for the vendor's page), newest first, at most 50.</summary>
    public async Task<IReadOnlyList<WorkOrderListItem>> ForVendorAsync(Guid vendorId, SessionPrincipal p, CancellationToken ct = default)
    {
        var w = await WhoAsync(p, ct);
        return await ListQuery((await VisibleAsync(w, ct)).Where(x => x.VendorId == vendorId).OrderByDescending(x => x.UpdatedAt).Take(50)).ToListAsync(ct);
    }

    /// <summary>My work (<paramref name="mine"/>), or every work order I may see. <paramref name="status"/>: <c>open</c> (default: not done or cancelled), <c>all</c>, or one status.</summary>
    public async Task<PagedResult<WorkOrderListItem>> ListAsync(bool mine, string? status, int page, int pageSize, SessionPrincipal p, CancellationToken ct = default)
    {
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100);
        var w = await WhoAsync(p, ct);
        var q = await VisibleAsync(w, ct);
        if (mine && w.VendorId is null) q = q.Where(x => x.AssigneeId == w.Id);   // for a vendor's person "mine" is their company's work (already the whole scope)
        var st = (status ?? "open").Trim().ToLowerInvariant();
        q = st switch
        {
            "open" or "" => q.Where(x => x.Status != WorkOrderStatus.Done && x.Status != WorkOrderStatus.Cancelled),
            "all" => q,
            "in_progress" => q.Where(x => x.Status == WorkOrderStatus.InProgress),
            "on_hold" => q.Where(x => x.Status == WorkOrderStatus.OnHold),
            "done" => q.Where(x => x.Status == WorkOrderStatus.Done),
            "cancelled" => q.Where(x => x.Status == WorkOrderStatus.Cancelled),
            "new" => q.Where(x => x.Status == WorkOrderStatus.Open),
            _ => throw Invalid(new() { ["status"] = "Use open, all, new, in_progress, on_hold, done or cancelled." }),
        };
        var total = await q.CountAsync(ct);
        var rows = await ListQuery(q.OrderBy(x => x.DueAt == null).ThenBy(x => x.DueAt).ThenByDescending(x => x.UpdatedAt)).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<WorkOrderListItem>(rows, page, pageSize, total);
    }

    /// <summary>ADR-GA-07. Field mode's snapshot: at most 50 of the caller's own open work orders, with checklists, without costs.</summary>
    public async Task<OfflineSnapshot> OfflineAsync(SessionPrincipal p, CancellationToken ct = default)
    {
        var w = await WhoAsync(p, ct);
        var rows = await (await VisibleAsync(w, ct)).Where(x => x.AssigneeId == p.UserId && x.Status != WorkOrderStatus.Done && x.Status != WorkOrderStatus.Cancelled)
            .OrderBy(x => x.DueAt == null).ThenBy(x => x.DueAt).ThenBy(x => x.CreatedAt).Take(50).ToListAsync(ct);
        var list = new List<WorkOrderDetail>();
        foreach (var x in rows) list.Add((await BuildAsync(x, w, ct)) with { Costs = null, CostTotal = null, LabourCost = null });
        return new OfflineSnapshot(clock.UtcNow, list);
    }

    public async Task<WorkOrderDetail> GetAsync(Guid id, SessionPrincipal p, CancellationToken ct = default)
    {
        var w = await WhoAsync(p, ct);
        var x = await (await VisibleAsync(w, ct)).FirstOrDefaultAsync(o => o.Id == id, ct) ?? throw DomainException.NotFound("Work order");
        return await BuildAsync(x, w, ct);
    }

    private async Task<WorkOrderDetail> BuildAsync(WorkOrder x, Who w, CancellationToken ct)
    {
        var t = await db.Tickets.AsNoTracking().Where(k => k.Id == x.TicketId).Select(k => new { k.Number, k.Subject, k.Status }).FirstAsync(ct);
        var who = x.AssigneeId is { } a ? await db.Users.AsNoTracking().Where(u => u.Id == a).Select(u => u.Name).FirstOrDefaultAsync(ct) : null;
        var team = x.TeamId is { } tm ? await db.Teams.AsNoTracking().Where(k => k.Id == tm).Select(k => k.Name).FirstOrDefaultAsync(ct) : null;
        var asset = x.AssetId is { } aid ? await db.Assets.AsNoTracking().Where(k => k.Id == aid).Select(k => new { k.Tag, k.Name }).FirstOrDefaultAsync(ct) : null;
        var vendor = x.VendorId is { } vid ? await db.Vendors.AsNoTracking().Where(k => k.Id == vid).Select(k => new { k.Name, k.Phone }).FirstOrDefaultAsync(ct) : null;
        var tasks = await db.WorkOrderTasks.AsNoTracking().Where(k => k.WorkOrderId == x.Id).OrderBy(k => k.Position).ToListAsync(ct);
        var times = await db.WorkOrderTimes.AsNoTracking().Where(k => k.WorkOrderId == x.Id).OrderByDescending(k => k.WorkDate).ThenByDescending(k => k.CreatedAt).ToListAsync(ct);
        var files = await db.Attachments.AsNoTracking().Where(k => k.WorkOrderId == x.Id && k.DeletedAt == null).OrderBy(k => k.CreatedAt).ToListAsync(ct);
        var canCosts = w.Has(Permissions.WorkOrdersCosts);
        var costs = canCosts ? await db.WorkOrderCosts.AsNoTracking().Where(k => k.WorkOrderId == x.Id).OrderBy(k => k.CreatedAt).ToListAsync(ct) : null;
        var finished = WorkOrderRules.IsFinished(x.Status); var ticketDone = TicketWorkflow.IsFinished(t.Status);
        var canWork = CanWork(w, x) && !ticketDone; var canEdit = w.Has(Permissions.WorkOrdersManage) && !finished && !ticketDone;
        var allowed = new List<string>();
        if (!ticketDone)
            foreach (var act in Enum.GetValues<WorkOrderAction>())
                if (WorkOrderRules.Next(x.Status, act) is not null && (act == WorkOrderAction.Cancel ? w.Has(Permissions.WorkOrdersManage) : canWork)) allowed.Add(act.ToString().ToLowerInvariant());
        return new WorkOrderDetail(x.Id, x.Number, x.Title, x.Instructions, Name(x.Status), x.Required, x.RequireAfterPhoto, x.AssigneeId, who, x.TeamId, team, x.TicketId, t.Number, t.Subject, t.Status.ToString().ToLowerInvariant(),
            x.AssetId, asset?.Tag, asset?.Name, x.DueAt, x.HoldReason, x.StartedAt, x.CompletedAt, x.CompletionNote, x.CancelReason,
            [.. tasks.Select(k => new WoTaskItem(k.Id, k.Position, k.Text, k.Required, k.DoneAt, k.DoneByLabel, k.Note))],
            [.. times.Select(k => new WoTimeItem(k.Id, k.UserId, k.UserLabel, k.Minutes, k.WorkDate, k.Note, !finished && !ticketDone && (k.UserId == w.Id || w.Has(Permissions.WorkOrdersManage))))], times.Sum(k => k.Minutes),
            costs?.Select(k => new WoCostItem(k.Id, k.Kind, k.Description, k.Quantity, k.UnitCost, CostService.Money(k.Quantity * k.UnitCost), k.CreatedByLabel, x.Status != WorkOrderStatus.Cancelled, k.StockItemId != null)).ToList(),
            costs?.Sum(k => CostService.Money(k.Quantity * k.UnitCost)),
            [.. files.Select(f => new WoFile(f.Id, f.FileName, f.ContentType, f.Stage, f.ContentType.StartsWith("image/"), f.CreatedAt))],
            allowed, canEdit, canWork && !finished, canCosts && x.Status != WorkOrderStatus.Cancelled && !ticketDone,
            x.Status == WorkOrderStatus.InProgress ? WorkOrderRules.MissingForCompletion(x, tasks, files.Count(f => f.Stage == "after")) : [], x.CreatedAt, x.UpdatedAt, x.Version,
            x.VendorId, vendor?.Name, vendor?.Phone, x.PurchaseOrder,
            canCosts && await db.CostSettings.AsNoTracking().Select(s => s.LabourRatePerHour).FirstOrDefaultAsync(ct) is { } rate ? times.Sum(k => CostService.Money(k.Minutes * rate / 60m)) : null);
    }

    // ================================================================ checklist templates
    private static List<TemplateLine> Lines(string json) => JsonSerializer.Deserialize<List<TemplateLine>>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? [];

    public async Task<IReadOnlyList<ChecklistTemplateItem>> TemplatesAsync(bool includeArchived, CancellationToken ct = default)
    {
        var rows = await db.ChecklistTemplates.AsNoTracking().Where(x => includeArchived || x.ArchivedAt == null).ToListAsync(ct);
        return [.. rows.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).Select(x => new ChecklistTemplateItem(x.Id, x.Name, Lines(x.ItemsJson), x.ArchivedAt != null, x.Version))];
    }

    private static (string Name, List<TemplateLine> Items) CheckTemplate(SaveTemplateRequest r, Dictionary<string, string> e)
    {
        var name = (r.Name ?? "").Trim();
        if (name.Length is < 2 or > 100) e["name"] = "Use 2–100 characters.";
        var items = (r.Items ?? []).Select(i => new TemplateLine((i.Text ?? "").Trim(), i.Required)).ToList();
        if (items.Count == 0) e["items"] = "Add at least one line.";
        else if (items.Count > WorkOrderRules.MaxTasks) e["items"] = $"At most {WorkOrderRules.MaxTasks} lines.";
        for (var i = 0; i < items.Count; i++) if (items[i].Text.Length is < 2 or > WorkOrderRules.TaskMax) e[$"items[{i}]"] = $"Use 2–{WorkOrderRules.TaskMax} characters.";
        return (name, items);
    }

    public async Task<ChecklistTemplateItem> SaveTemplateAsync(Guid? id, SaveTemplateRequest req, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var e = new Dictionary<string, string>(); var (name, items) = CheckTemplate(req, e);
        if (!e.ContainsKey("name") && await db.ChecklistTemplates.AnyAsync(x => x.Id != id && x.ArchivedAt == null && x.Name.ToLower() == name.ToLower(), ct)) e["name"] = "A checklist with this name already exists.";
        if (e.Count > 0) throw Invalid(e);
        var now = clock.UtcNow; ChecklistTemplate t;
        if (id is { } tid)
        {
            t = await db.ChecklistTemplates.FirstOrDefaultAsync(x => x.Id == tid, ct) ?? throw DomainException.NotFound("Checklist");
            if (t.ArchivedAt is not null) throw DomainException.Conflict("This checklist is archived. Restore it before editing.");
            if (req.Version is { } v && v != t.Version) throw DomainException.Conflict("This checklist was changed by someone else. Reload and try again.");
            t.Name = name; t.ItemsJson = JsonSerializer.Serialize(items, new JsonSerializerOptions(JsonSerializerDefaults.Web)); t.UpdatedAt = now; t.Version++;
        }
        else { t = new ChecklistTemplate { Name = name, ItemsJson = JsonSerializer.Serialize(items, new JsonSerializerOptions(JsonSerializerDefaults.Web)), CreatedAt = now, UpdatedAt = now }; db.ChecklistTemplates.Add(t); }
        audit.Record(id is null ? "checklist.create" : "checklist.update", "checklist_template", t.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["name"] = name, ["lines"] = items.Count.ToString() });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw DomainException.Conflict("This checklist was changed by someone else. Reload and try again."); }
        catch (DbUpdateException) { throw DomainException.Conflict("A checklist with this name already exists."); }
        return new ChecklistTemplateItem(t.Id, t.Name, items, false, t.Version);
    }

    public async Task SetTemplateArchivedAsync(Guid id, bool archived, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var t = await db.ChecklistTemplates.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Checklist");
        if (archived == (t.ArchivedAt is not null)) throw DomainException.Conflict(archived ? "This checklist is already archived." : "This checklist is not archived.");
        if (!archived && await db.ChecklistTemplates.AnyAsync(x => x.Id != id && x.ArchivedAt == null && x.Name.ToLower() == t.Name.ToLower(), ct)) throw DomainException.Conflict("Another active checklist already has this name.");
        t.ArchivedAt = archived ? clock.UtcNow : null; t.UpdatedAt = clock.UtcNow; t.Version++;
        audit.Record(archived ? "checklist.archive" : "checklist.restore", "checklist_template", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["name"] = t.Name });
        await db.SaveChangesAsync(ct);
    }

    // ================================================================ creating and changing
    private async Task CheckPeopleAsync(Guid? assignee, Guid? team, Dictionary<string, string> e, CancellationToken ct, Guid? vendor = null, Guid? keepVendor = null, string? po = null)
    {
        if (vendor is { } v && v != keepVendor && !await db.Vendors.AnyAsync(x => x.Id == v && x.BlockedAt == null, ct)) e["vendorId"] = "Choose a vendor that is not blocked.";
        if (po is { Length: > 50 }) e["purchaseOrder"] = "Use at most 50 characters.";
        if (assignee is { } a && !await db.Users.AnyAsync(u => u.Id == a && u.Status == UserStatus.Active, ct)) e["assigneeId"] = "Choose an active person.";
        if (team is { } tm && !await db.Teams.AnyAsync(k => k.Id == tm && k.ArchivedAt == null, ct)) e["teamId"] = "Choose an active team.";
    }

    private static (string Title, string? Instructions, List<TaskInput> Tasks) CheckText(SaveWorkOrderRequest r, IEnumerable<TaskInput> fromTemplate, Dictionary<string, string> e)
    {
        var title = (r.Title ?? "").Trim(); var instr = Clean(r.Instructions);
        if (title.Length is < WorkOrderRules.TitleMin or > WorkOrderRules.TitleMax) e["title"] = $"Use {WorkOrderRules.TitleMin}–{WorkOrderRules.TitleMax} characters.";
        if (instr is { Length: > WorkOrderRules.InstructionsMax }) e["instructions"] = $"Use at most {WorkOrderRules.InstructionsMax} characters.";
        var tasks = fromTemplate.Concat(r.Tasks ?? []).Select(k => k with { Text = (k.Text ?? "").Trim() }).ToList();
        if (tasks.Count > WorkOrderRules.MaxTasks) e["tasks"] = $"At most {WorkOrderRules.MaxTasks} checklist lines.";
        for (var i = 0; i < tasks.Count; i++) if (tasks[i].Text!.Length is < 2 or > WorkOrderRules.TaskMax) e[$"tasks[{i}]"] = $"Use 2–{WorkOrderRules.TaskMax} characters.";
        return (title, instr, tasks);
    }

    public async Task<WorkOrderDetail> CreateAsync(Guid ticketId, SaveWorkOrderRequest req, SessionPrincipal p, ClientInfo client, CancellationToken ct = default)
    {
        var w = await WhoAsync(p, ct);
        var visible = await (await tickets.SeenTicketsAsync(p, ct)).AnyAsync(x => x.Id == ticketId, ct);
        var t = visible ? await db.Tickets.FirstOrDefaultAsync(x => x.Id == ticketId, ct) : null;
        if (t is null) throw DomainException.NotFound("Ticket");
        if (TicketWorkflow.IsFinished(t.Status) || t.AnonymisedAt is not null) throw DomainException.Conflict("Work orders can't be added to a finished ticket.");
        if (await db.WorkOrders.CountAsync(x => x.TicketId == ticketId, ct) >= MaxPerTicket) throw DomainException.Conflict($"A ticket can have at most {MaxPerTicket} work orders.");
        var e = new Dictionary<string, string>();
        List<TaskInput> fromTemplate = [];
        if (req.TemplateId is { } tid)
        {
            var tpl = await db.ChecklistTemplates.AsNoTracking().FirstOrDefaultAsync(x => x.Id == tid && x.ArchivedAt == null, ct);
            if (tpl is null) e["templateId"] = "Choose an active checklist."; else fromTemplate = [.. Lines(tpl.ItemsJson).Select(l => new TaskInput(null, l.Text, l.Required))];
        }
        var (title, instr, tasks) = CheckText(req, fromTemplate, e);
        await CheckPeopleAsync(req.AssigneeId, req.TeamId, e, ct, req.VendorId, null, Clean(req.PurchaseOrder));
        if (e.Count > 0) throw Invalid(e);

        var now = clock.UtcNow;
        var seq = (await db.WorkOrders.Where(x => x.TicketId == ticketId).MaxAsync(x => (int?)x.Seq, ct) ?? 0) + 1;
        var wo = new WorkOrder
        {
            TicketId = ticketId, Seq = seq, Number = $"{t.Number}-W{seq}", Title = title, Instructions = instr, AssigneeId = req.AssigneeId, TeamId = req.TeamId, AssetId = t.AssetId, Required = req.Required,
            RequireAfterPhoto = req.RequireAfterPhoto, DueAt = req.DueAt, CreatedById = p.UserId, CreatedAt = now, UpdatedAt = now, VendorId = req.VendorId, PurchaseOrder = Clean(req.PurchaseOrder),
        };
        db.WorkOrders.Add(wo);
        for (var i = 0; i < tasks.Count; i++) db.WorkOrderTasks.Add(new WorkOrderTask { WorkOrderId = wo.Id, Position = i + 1, Text = tasks[i].Text!, Required = tasks[i].Required });
        if (req.ResolveTicket is { } rt && rt != t.ResolveWithWorkOrders) t.ResolveWithWorkOrders = rt;
        var who = req.AssigneeId is { } a ? await db.Users.AsNoTracking().Where(u => u.Id == a).Select(u => u.Name).FirstOrDefaultAsync(ct) : null;
        tickets.NoteWorkOrder(t, p, $"Work order {wo.Number} created: {title}{(who is null ? "" : $" (given to {who})")}.", now);
        audit.Record("workorder.create", "work_order", wo.Id.ToString(), Outcome.Success, client, p.UserId, p.Email, new Dictionary<string, string> { ["number"] = wo.Number, ["ticket"] = t.Number, ["tasks"] = tasks.Count.ToString() });
        if (req.AssigneeId is { } aid && aid != p.UserId) await NotifyAssignedAsync(wo, aid, ct);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { throw DomainException.Conflict("Another work order was added to this ticket at the same moment. Try again."); }
        return await BuildAsync(wo, w, ct);
    }

    /// <summary>R2-5. The one work order of a planned maintenance visit's ticket, added to the caller's unit of work (the caller saves). Required, so finishing it resolves the ticket.</summary>
    public async Task<WorkOrder> AddPlannedAsync(Ticket t, string title, string? instructions, IReadOnlyList<TemplateLine> lines, Guid? assignee, Guid? team, Guid? vendor, bool requireAfterPhoto,
        DateTimeOffset dueAt, Guid createdBy, DateTimeOffset now, CancellationToken ct = default)
    {
        var wo = new WorkOrder
        {
            TicketId = t.Id, Seq = 1, Number = $"{t.Number}-W1", Title = title, Instructions = instructions, AssigneeId = assignee, TeamId = team, AssetId = t.AssetId, VendorId = vendor, Required = true,
            RequireAfterPhoto = requireAfterPhoto, DueAt = dueAt, CreatedById = createdBy, CreatedAt = now, UpdatedAt = now,
        };
        db.WorkOrders.Add(wo);
        var tasks = lines.Take(WorkOrderRules.MaxTasks).ToList();
        for (var i = 0; i < tasks.Count; i++) db.WorkOrderTasks.Add(new WorkOrderTask { WorkOrderId = wo.Id, Position = i + 1, Text = tasks[i].Text, Required = tasks[i].Required });
        tickets.NoteWorkOrder(t, null, $"Work order {wo.Number} created by the maintenance plan: {title}.", now);
        if (assignee is { } a) await NotifyAssignedAsync(wo, a, ct);
        return wo;
    }

    /// <summary>R2-5. The checklist lines of a saved checklist (empty when it is gone).</summary>
    public async Task<IReadOnlyList<TemplateLine>> TemplateLinesAsync(Guid? id, CancellationToken ct = default)
        => id is { } tid && await db.ChecklistTemplates.AsNoTracking().Where(x => x.Id == tid).Select(x => x.ItemsJson).FirstOrDefaultAsync(ct) is { } json ? Lines(json) : [];

    private Task NotifyAssignedAsync(WorkOrder wo, Guid to, CancellationToken ct)
        => notifications.SendAsync(to, "workorder.assigned", $"Work order {wo.Number} was given to you", wo.Title, $"/work-orders/{wo.Id}", wo.TicketId,
            (mail, name) => Email.EmailTemplates.Notification(mail, name, $"Work order {wo.Number} was given to you", wo.Title, Url($"/work-orders/{wo.Id}")), ct: ct);

    private async Task<(WorkOrder Wo, Ticket Ticket, Who Who)> LoadAsync(Guid id, SessionPrincipal p, int? version, CancellationToken ct)
    {
        var w = await WhoAsync(p, ct);
        if (!await (await VisibleAsync(w, ct)).AnyAsync(x => x.Id == id, ct)) throw DomainException.NotFound("Work order");
        var wo = await db.WorkOrders.FirstAsync(x => x.Id == id, ct);
        if (version is { } v && v != wo.Version) throw DomainException.Conflict("This work order was changed by someone else. Reload and try again.");
        var t = await db.Tickets.FirstAsync(x => x.Id == wo.TicketId, ct);
        if (TicketWorkflow.IsFinished(t.Status)) throw DomainException.Conflict($"The ticket is {t.Status.ToString().ToLowerInvariant()}, so its work orders can't change.");
        return (wo, t, w);
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw DomainException.Conflict("This work order was changed by someone else. Reload and try again."); }
    }

    public async Task<WorkOrderDetail> UpdateAsync(Guid id, SaveWorkOrderRequest req, SessionPrincipal p, ClientInfo client, CancellationToken ct = default)
    {
        var (wo, t, w) = await LoadAsync(id, p, req.Version, ct);
        if (!w.Has(Permissions.WorkOrdersManage)) throw DomainException.Denied(Permissions.WorkOrdersManage);
        if (WorkOrderRules.IsFinished(wo.Status)) throw DomainException.Conflict("A finished work order can't be changed.");
        var e = new Dictionary<string, string>(); var (title, instr, tasks) = CheckText(req, [], e);
        await CheckPeopleAsync(req.AssigneeId, req.TeamId, e, ct, req.VendorId, wo.VendorId, Clean(req.PurchaseOrder));
        if (e.Count > 0) throw Invalid(e);
        var now = clock.UtcNow; var reassigned = wo.AssigneeId != req.AssigneeId;
        var existing = await db.WorkOrderTasks.Where(k => k.WorkOrderId == id).ToListAsync(ct);
        var keep = tasks.Where(k => k.Id is not null).Select(k => k.Id!.Value).ToHashSet();
        foreach (var gone in existing.Where(k => !keep.Contains(k.Id))) db.WorkOrderTasks.Remove(gone);
        for (var i = 0; i < tasks.Count; i++)
        {
            var row = tasks[i].Id is { } tid ? existing.FirstOrDefault(k => k.Id == tid) : null;
            if (row is null) db.WorkOrderTasks.Add(new WorkOrderTask { WorkOrderId = id, Position = i + 1, Text = tasks[i].Text!, Required = tasks[i].Required });
            else { row.Position = i + 1; row.Text = tasks[i].Text!; row.Required = tasks[i].Required; }   // a ticked line stays ticked
        }
        wo.VendorId = req.VendorId; wo.PurchaseOrder = Clean(req.PurchaseOrder);
        wo.Title = title; wo.Instructions = instr; wo.AssigneeId = req.AssigneeId; wo.TeamId = req.TeamId; wo.DueAt = req.DueAt; wo.Required = req.Required; wo.RequireAfterPhoto = req.RequireAfterPhoto;
        wo.UpdatedAt = now; wo.Version++;
        if (req.ResolveTicket is { } rt) t.ResolveWithWorkOrders = rt;
        if (reassigned)
        {
            var who = req.AssigneeId is { } a ? await db.Users.AsNoTracking().Where(u => u.Id == a).Select(u => u.Name).FirstOrDefaultAsync(ct) : null;
            tickets.NoteWorkOrder(t, p, who is null ? $"Work order {wo.Number} is no longer given to anyone." : $"Work order {wo.Number} given to {who}.", now);
            if (req.AssigneeId is { } aid && aid != p.UserId) await NotifyAssignedAsync(wo, aid, ct);
        }
        audit.Record("workorder.update", "work_order", id.ToString(), Outcome.Success, client, p.UserId, p.Email, new Dictionary<string, string> { ["number"] = wo.Number });
        await SaveAsync(ct);
        return await BuildAsync(wo, w, ct);
    }

    public async Task<TicketWorkOrders> SetResolveAsync(Guid ticketId, ResolveSettingRequest req, SessionPrincipal p, ClientInfo client, CancellationToken ct = default)
    {
        if (!await (await tickets.SeenTicketsAsync(p, ct)).AnyAsync(x => x.Id == ticketId, ct)) throw DomainException.NotFound("Ticket");
        var t = await db.Tickets.FirstAsync(x => x.Id == ticketId, ct);
        if (t.ResolveWithWorkOrders != req.ResolveTicket)
        {
            t.ResolveWithWorkOrders = req.ResolveTicket;
            audit.Record("ticket.workorder_resolve", "ticket", ticketId.ToString(), Outcome.Success, client, p.UserId, p.Email, new Dictionary<string, string> { ["number"] = t.Number, ["resolveTicket"] = req.ResolveTicket.ToString().ToLowerInvariant() });
            await db.SaveChangesAsync(ct);
        }
        return await ForTicketAsync(ticketId, p, ct);
    }

    // ================================================================ doing the work
    public async Task<WorkOrderDetail> ActAsync(Guid id, WorkOrderActionRequest req, SessionPrincipal p, ClientInfo client, CancellationToken ct = default)
    {
        if (!Enum.TryParse<WorkOrderAction>(req.Action?.Trim(), true, out var action) || !Enum.IsDefined(action))
            throw Invalid(new() { ["action"] = "Choose start, hold, resume, complete or cancel." });
        var (wo, t, w) = await LoadAsync(id, p, req.Version, ct);
        if (action == WorkOrderAction.Cancel ? !w.Has(Permissions.WorkOrdersManage) : !CanWork(w, wo))
            throw DomainException.DeniedBecause(action == WorkOrderAction.Cancel ? "Only people who manage work orders can cancel one." : "This work order is given to someone else.");
        var to = WorkOrderRules.Next(wo.Status, action) ?? throw DomainException.Invariant($"A work order that is {Name(wo.Status).Replace('_', ' ')} can't be {Past(action)}.");
        var reason = Clean(req.Reason); var note = Clean(req.Note); var now = clock.UtcNow;
        if (action is WorkOrderAction.Hold or WorkOrderAction.Cancel && (reason is null || reason.Length is < WorkOrderRules.ReasonMin or > WorkOrderRules.ReasonMax))
            throw Invalid(new() { ["reason"] = $"Give a reason of {WorkOrderRules.ReasonMin}–{WorkOrderRules.ReasonMax} characters." });
        if (action == WorkOrderAction.Complete)
        {
            if (note is null || note.Length is < 3 or > WorkOrderRules.NoteMax) throw Invalid(new() { ["note"] = $"Say what was done (3–{WorkOrderRules.NoteMax} characters)." });
            var tasks = await db.WorkOrderTasks.AsNoTracking().Where(k => k.WorkOrderId == id).ToListAsync(ct);
            var after = await db.Attachments.CountAsync(a => a.WorkOrderId == id && a.Stage == "after" && a.DeletedAt == null, ct);
            var missing = WorkOrderRules.MissingForCompletion(wo, tasks, after);
            if (missing.Count > 0) throw DomainException.Validation("This work order can't be completed yet.", new Dictionary<string, string> { ["complete"] = string.Join(" ", missing) });
        }

        var from = wo.Status; string line;
        switch (action)
        {
            case WorkOrderAction.Start:
                if (wo.AssigneeId is null && w.VendorId is null) wo.AssigneeId = p.UserId;   // taking unassigned work makes it yours (a vendor's person works it without taking it)
                wo.StartedAt ??= now; line = $"Work order {wo.Number} started."; break;
            case WorkOrderAction.Hold: wo.HoldReason = reason; line = $"Work order {wo.Number} on hold: {reason}"; break;
            case WorkOrderAction.Resume: wo.HoldReason = null; line = $"Work order {wo.Number} resumed."; break;
            case WorkOrderAction.Complete: wo.CompletedAt = now; wo.CompletionNote = note; wo.HoldReason = null; line = $"Work order {wo.Number} done: {note}"; break;
            default: wo.CancelReason = reason; wo.HoldReason = null; line = $"Work order {wo.Number} cancelled: {reason}"; break;
        }
        wo.Status = to; wo.UpdatedAt = now; wo.Version++;
        tickets.NoteWorkOrder(t, p, line, now);
        audit.Record($"workorder.{action.ToString().ToLowerInvariant()}", "work_order", id.ToString(), Outcome.Success, client, p.UserId, p.Email,
            new Dictionary<string, string> { ["number"] = wo.Number }, [new AuditDiff("status", Name(from), Name(to))]);

        if (to is WorkOrderStatus.Done or WorkOrderStatus.Cancelled)
        {
            if (to == WorkOrderStatus.Done) await integrations.PublishAsync("workorder.completed", t, new { workOrder = new { wo.Id, wo.Number, wo.Title, wo.VendorId, wo.CompletedAt } }, ct);
            if (to == WorkOrderStatus.Done && t.AssigneeId is { } owner && owner != p.UserId)
                await notifications.SendAsync(owner, "workorder.done", $"Work order {wo.Number} is done", wo.Title, $"/tickets/{t.Id}", t.Id, ct: ct);
            // the last required work order finished: the ticket resolves itself if it asked to
            var others = await db.WorkOrders.Where(x => x.TicketId == t.Id && x.Id != id).Select(x => new { x.Number, x.Required, x.Status }).ToListAsync(ct);
            var stillOpen = others.Any(x => x.Required && !WorkOrderRules.IsFinished(x.Status));
            var doneRequired = others.Where(x => x.Required && x.Status == WorkOrderStatus.Done).Select(x => x.Number).ToList();
            if (wo.Required && to == WorkOrderStatus.Done) doneRequired.Add(wo.Number);
            if (t.ResolveWithWorkOrders && !stillOpen && doneRequired.Count > 0) await tickets.ResolveFromWorkOrdersAsync(t, [.. doneRequired.OrderBy(n => n.Length).ThenBy(n => n)], now, ct);
        }
        await SaveAsync(ct);
        return await BuildAsync(wo, w, ct);
    }

    private static string Past(WorkOrderAction a) => a switch { WorkOrderAction.Start => "started", WorkOrderAction.Hold => "put on hold", WorkOrderAction.Resume => "resumed", WorkOrderAction.Complete => "completed", _ => "cancelled" };

    private async Task<(WorkOrder Wo, Ticket Ticket, Who Who)> LoadForWorkAsync(Guid id, SessionPrincipal p, CancellationToken ct)
    {
        var r = await LoadAsync(id, p, null, ct);
        if (!CanWork(r.Who, r.Wo)) throw DomainException.DeniedBecause("This work order is given to someone else.");
        if (WorkOrderRules.IsFinished(r.Wo.Status)) throw DomainException.Conflict("This work order is finished.");
        return r;
    }

    public async Task<WorkOrderDetail> TickAsync(Guid id, Guid taskId, TickRequest req, SessionPrincipal p, ClientInfo client, CancellationToken ct = default)
    {
        var (wo, _, w) = await LoadForWorkAsync(id, p, ct);
        if (wo.Status != WorkOrderStatus.InProgress) throw DomainException.Conflict("Start the work order before ticking its checklist.");
        var k = await db.WorkOrderTasks.FirstOrDefaultAsync(x => x.Id == taskId && x.WorkOrderId == id, ct) ?? throw DomainException.NotFound("Checklist line");
        var note = Clean(req.Note);
        if (note is { Length: > 500 }) throw Invalid(new() { ["note"] = "Use at most 500 characters." });
        if (req.Done) { k.DoneAt = clock.UtcNow; k.DoneById = p.UserId; k.DoneByLabel = p.Name; k.Note = note; }
        else { k.DoneAt = null; k.DoneById = null; k.DoneByLabel = null; k.Note = note; }
        wo.UpdatedAt = clock.UtcNow;
        await SaveAsync(ct);
        return await BuildAsync(wo, w, ct);
    }

    public async Task<WorkOrderDetail> LogTimeAsync(Guid id, LogTimeRequest req, SessionPrincipal p, ClientInfo client, CancellationToken ct = default)
    {
        var (wo, _, w) = await LoadForWorkAsync(id, p, ct);
        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime); var date = req.Date ?? today; var note = Clean(req.Note);
        var e = new Dictionary<string, string>();
        if (req.Minutes is < 1 or > WorkOrderRules.MaxMinutes) e["minutes"] = "Use 1 minute to 24 hours.";
        if (date > today || date < today.AddDays(-MaxTimeDaysBack)) e["date"] = $"Use a day within the last {MaxTimeDaysBack} days, not in the future.";
        if (note is { Length: > 500 }) e["note"] = "Use at most 500 characters.";
        if (e.Count > 0) throw Invalid(e);
        db.WorkOrderTimes.Add(new WorkOrderTime { WorkOrderId = id, UserId = p.UserId, UserLabel = p.Name, Minutes = req.Minutes, WorkDate = date, Note = note, CreatedAt = clock.UtcNow });
        wo.UpdatedAt = clock.UtcNow;
        audit.Record("workorder.time", "work_order", id.ToString(), Outcome.Success, client, p.UserId, p.Email, new Dictionary<string, string> { ["number"] = wo.Number, ["minutes"] = req.Minutes.ToString() });
        await SaveAsync(ct);
        return await BuildAsync(wo, w, ct);
    }

    public async Task<WorkOrderDetail> DeleteTimeAsync(Guid id, Guid timeId, SessionPrincipal p, ClientInfo client, CancellationToken ct = default)
    {
        var (wo, _, w) = await LoadAsync(id, p, null, ct);
        if (WorkOrderRules.IsFinished(wo.Status)) throw DomainException.Conflict("This work order is finished.");
        var row = await db.WorkOrderTimes.FirstOrDefaultAsync(x => x.Id == timeId && x.WorkOrderId == id, ct) ?? throw DomainException.NotFound("Time entry");
        if (row.UserId != p.UserId && !w.Has(Permissions.WorkOrdersManage)) throw DomainException.DeniedBecause("Only the person who logged the time, or a manager, can remove it.");
        db.WorkOrderTimes.Remove(row);
        audit.Record("workorder.time.delete", "work_order", id.ToString(), Outcome.Success, client, p.UserId, p.Email, new Dictionary<string, string> { ["number"] = wo.Number, ["minutes"] = row.Minutes.ToString() });
        await SaveAsync(ct);
        return await BuildAsync(wo, w, ct);
    }

    public async Task<WorkOrderDetail> AddCostAsync(Guid id, AddCostRequest req, SessionPrincipal p, ClientInfo client, CancellationToken ct = default)
    {
        var (wo, _, w) = await LoadAsync(id, p, null, ct);
        if (!w.Has(Permissions.WorkOrdersCosts)) throw DomainException.Denied(Permissions.WorkOrdersCosts);
        if (wo.Status == WorkOrderStatus.Cancelled) throw DomainException.Conflict("This work order is cancelled.");
        var kind = (req.Kind ?? "").Trim().ToLowerInvariant(); var desc = (req.Description ?? "").Trim();
        var e = new Dictionary<string, string>();
        if (!WorkOrderRules.CostKinds.Contains(kind)) e["kind"] = "Use part, labour, service, invoice or other.";
        if (desc.Length is < 2 or > WorkOrderRules.DescriptionMax) e["description"] = $"Use 2–{WorkOrderRules.DescriptionMax} characters.";
        if (req.Quantity <= 0 || req.Quantity > WorkOrderRules.QuantityMax || decimal.Round(req.Quantity, 2) != req.Quantity) e["quantity"] = "Use a positive quantity with at most two decimals.";
        if (req.UnitCost < 0 || req.UnitCost > WorkOrderRules.UnitCostMax || decimal.Round(req.UnitCost, 2) != req.UnitCost) e["unitCost"] = "Use an amount of 0 or more with at most two decimals.";
        if (e.Count > 0) throw Invalid(e);
        db.WorkOrderCosts.Add(new WorkOrderCost { WorkOrderId = id, Kind = kind, Description = desc, Quantity = req.Quantity, UnitCost = req.UnitCost, CreatedById = p.UserId, CreatedByLabel = p.Name, CreatedAt = clock.UtcNow });
        wo.UpdatedAt = clock.UtcNow;
        audit.Record("workorder.cost", "work_order", id.ToString(), Outcome.Success, client, p.UserId, p.Email, new Dictionary<string, string> { ["number"] = wo.Number, ["kind"] = kind, ["total"] = (req.Quantity * req.UnitCost).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) });
        await SaveAsync(ct);
        return await BuildAsync(wo, w, ct);
    }

    public async Task<WorkOrderDetail> DeleteCostAsync(Guid id, Guid costId, SessionPrincipal p, ClientInfo client, CancellationToken ct = default)
    {
        var (wo, _, w) = await LoadAsync(id, p, null, ct);
        if (!w.Has(Permissions.WorkOrdersCosts)) throw DomainException.Denied(Permissions.WorkOrdersCosts);
        if (wo.Status == WorkOrderStatus.Cancelled) throw DomainException.Conflict("This work order is cancelled.");
        var row = await db.WorkOrderCosts.FirstOrDefaultAsync(x => x.Id == costId && x.WorkOrderId == id, ct) ?? throw DomainException.NotFound("Cost line");
        await db.InTransactionAsync(async () =>
        {
            db.WorkOrderCosts.Remove(row);
            audit.Record("workorder.cost.delete", "work_order", id.ToString(), Outcome.Success, client, p.UserId, p.Email, new Dictionary<string, string> { ["number"] = wo.Number, ["description"] = row.Description });
            if (row is { StockItemId: { } itemId, StockLocationId: { } placeId, StockQuantity: > 0 })
            {
                // INV-005: a part drawn from stock goes back to where it came from
                var item = await db.InventoryItems.AsNoTracking().FirstAsync(i => i.Id == itemId, ct);
                await inventory.PostAsync(item, placeId, row.StockQuantity.Value, StockMovementKind.Adjustment, row.UnitCost, $"Returned from {wo.Number}", id, p.UserId, ct);
                audit.Record("workorder.part_returned", "work_order", id.ToString(), Outcome.Success, client, p.UserId, p.Email,
                    new Dictionary<string, string> { ["number"] = wo.Number, ["sku"] = item.Sku, ["quantity"] = row.StockQuantity.Value.ToString() });
            }
            await SaveAsync(ct);
            return true;
        }, ct);
        return await BuildAsync(wo, w, ct);
    }

    /// <summary>INV-005. A part for this work order taken from stock at a place: an issue movement in the ledger (grouped under the work order) and a part cost
    /// line at the item's cost, in one transaction. Not for vendor accounts, who do not see the organisation's stock.</summary>
    public async Task<WorkOrderDetail> AddStockPartAsync(Guid id, AddStockPartRequest req, SessionPrincipal p, ClientInfo client, CancellationToken ct = default)
    {
        var (wo, _, w) = await LoadAsync(id, p, null, ct);
        if (!w.Has(Permissions.WorkOrdersCosts) || w.VendorId is not null) throw DomainException.Denied(Permissions.WorkOrdersCosts);
        if (wo.Status == WorkOrderStatus.Cancelled) throw DomainException.Conflict("This work order is cancelled.");
        var e = new Dictionary<string, string>();
        var item = req.ItemId is { } iid ? await db.InventoryItems.AsNoTracking().FirstOrDefaultAsync(i => i.Id == iid && i.ArchivedAt == null, ct) : null;
        if (item is null) e["itemId"] = "Choose a stock item.";
        if (req.LocationId is not { } loc || !await db.Locations.AnyAsync(l => l.Id == loc, ct)) e["locationId"] = "Choose the place it comes from.";
        if (req.Quantity is not { } qty || qty < 1 || qty > InventoryRules.MaxQuantity) e["quantity"] = "Use 1 or more.";
        if (e.Count > 0) throw Invalid(e);
        var n = req.Quantity!.Value;
        await db.InTransactionAsync(async () =>
        {
            await inventory.PostAsync(item!, req.LocationId!.Value, -n, StockMovementKind.Issue, item!.UnitCost, $"{wo.Number} · {(wo.Title.Length > 200 ? wo.Title[..200] : wo.Title)}", id, p.UserId, ct);
            db.WorkOrderCosts.Add(new WorkOrderCost
            {
                WorkOrderId = id, Kind = "part", Description = $"{item.Name} ({item.Sku})", Quantity = n, UnitCost = item.UnitCost ?? 0, CreatedById = p.UserId, CreatedByLabel = p.Name,
                CreatedAt = clock.UtcNow, StockItemId = item.Id, StockLocationId = req.LocationId, StockQuantity = n,
            });
            wo.UpdatedAt = clock.UtcNow;
            audit.Record("workorder.part_from_stock", "work_order", id.ToString(), Outcome.Success, client, p.UserId, p.Email,
                new Dictionary<string, string> { ["number"] = wo.Number, ["sku"] = item.Sku, ["quantity"] = n.ToString() });
            await SaveAsync(ct);
            return true;
        }, ct);
        return await BuildAsync(wo, w, ct);
    }

    /// <summary>TKT-007. A photo (or document) of the work, marked as showing the situation before or after. Stored with the ticket's files; the same checks and limits apply.</summary>
    public async Task<WorkOrderDetail> AddFileAsync(Guid id, string? stage, string? fileName, Stream content, SessionPrincipal p, ClientInfo client, CancellationToken ct = default)
    {
        var st = (stage ?? "").Trim().ToLowerInvariant();
        if (st is not ("before" or "after")) throw Invalid(new() { ["stage"] = "Say whether the photo shows the situation before or after the work." });
        var (wo, _, w) = await LoadForWorkAsync(id, p, ct);
        var a = await attachments.StoreAsync(wo.TicketId, fileName, content, p, client, ct);
        a.WorkOrderId = id; a.Stage = st; wo.UpdatedAt = clock.UtcNow;
        await SaveAsync(ct);
        return await BuildAsync(wo, w, ct);
    }

    public async Task<AttachmentDownload> OpenFileAsync(Guid id, Guid fileId, SessionPrincipal p, CancellationToken ct = default)
    {
        var w = await WhoAsync(p, ct);
        var wo = await (await VisibleAsync(w, ct)).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Work order");
        if (!await db.Attachments.AnyAsync(a => a.Id == fileId && a.WorkOrderId == id && a.DeletedAt == null, ct)) throw DomainException.NotFound("File");
        return await attachments.OpenAsync(wo.TicketId, fileId, ct);
    }
    // ================================================================ summary (module analytics, docs/52)
    /// <summary>Counts over the work orders this person may see (the list's scope): what is open, late or due this week, what was done in the period
    /// (<paramref name="days"/>, default 30) and how quickly, overall and per service category, created vs done per week (at least 12 weeks, more for a longer
    /// period), and the open work per vendor.</summary>
    public async Task<WorkOrderSummary> SummaryAsync(SessionPrincipal p, int? days = null, CancellationToken ct = default)
    {
        var w = await WhoAsync(p, ct);
        var q = await VisibleAsync(w, ct);
        var period = InsightWindow.Of(days, 30);
        var now = clock.UtcNow; var week = now.AddDays(7); var since = now.AddDays(-period);
        var open = q.Where(x => x.Status != WorkOrderStatus.Done && x.Status != WorkOrderStatus.Cancelled);
        var groups = await open.GroupBy(x => new { x.Status, Vendor = x.VendorId != null }).Select(g => new
        {
            g.Key.Status, g.Key.Vendor, N = g.Count(), Overdue = g.Count(x => x.DueAt != null && x.DueAt < now), Week = g.Count(x => x.DueAt != null && x.DueAt >= now && x.DueAt <= week),
        }).ToListAsync(ct);
        var done = q.Where(x => x.Status == WorkOrderStatus.Done && x.CompletedAt != null);
        var inPeriod = done.Where(x => x.CompletedAt >= since);
        var donePeriod = await inPeriod.GroupBy(x => 1).Select(g => new
        {
            N = g.Count(), OnTime = g.Count(x => x.DueAt != null && x.CompletedAt <= x.DueAt), Late = g.Count(x => x.DueAt != null && x.CompletedAt > x.DueAt),
        }).FirstOrDefaultAsync(ct);
        var avg = await inPeriod.Select(x => (double?)(x.CompletedAt!.Value - x.CreatedAt).TotalHours).AverageAsync(ct);
        var perCategory = await (from x in inPeriod
                                 join t in db.Tickets on x.TicketId equals t.Id
                                 join s in db.Services on t.ServiceId equals s.Id
                                 group x by s.CategoryId into g
                                 select new { g.Key, N = g.Count(), Avg = g.Average(x => (x.CompletedAt!.Value - x.CreatedAt).TotalHours) }).ToListAsync(ct);
        var catIds = perCategory.Select(x => x.Key).ToList();
        var catNames = await db.ServiceCategories.AsNoTracking().Where(c => catIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        var weeks = Math.Clamp((period + 6) / 7, 12, 53);
        var start = new DateTimeOffset(now.UtcDateTime.Date.AddDays(-(weeks * 7 - 1)), TimeSpan.Zero);
        var createdPerWeek = await q.Where(x => x.CreatedAt >= start).GroupBy(x => Math.Floor((x.CreatedAt - start).TotalDays / 7)).Select(g => new { W = g.Key, N = g.Count() }).ToDictionaryAsync(x => (int)x.W, x => x.N, ct);
        var donePerWeek = await done.Where(x => x.CompletedAt >= start).GroupBy(x => Math.Floor((x.CompletedAt!.Value - start).TotalDays / 7)).Select(g => new { W = g.Key, N = g.Count() }).ToDictionaryAsync(x => (int)x.W, x => x.N, ct);
        var weekly = Enumerable.Range(0, weeks).Select(i => new WeekPoint(start.AddDays(7 * i).ToString("yyyy-MM-dd"), createdPerWeek.GetValueOrDefault(i), donePerWeek.GetValueOrDefault(i))).ToList();

        var perVendor = await open.Where(x => x.VendorId != null).GroupBy(x => x.VendorId!.Value).Select(g => new { g.Key, N = g.Count() }).OrderByDescending(x => x.N).Take(6).ToListAsync(ct);
        var vendorIds = perVendor.Select(x => x.Key).ToList();
        var vendorNames = await db.Vendors.AsNoTracking().Where(v => vendorIds.Contains(v.Id)).ToDictionaryAsync(v => v.Id, v => v.Name, ct);

        WorkOrderStatus[] order = [WorkOrderStatus.Open, WorkOrderStatus.InProgress, WorkOrderStatus.OnHold];
        return new WorkOrderSummary(period, groups.Sum(x => x.N), groups.Sum(x => x.Overdue), groups.Sum(x => x.Week), donePeriod?.N ?? 0, donePeriod?.OnTime ?? 0, donePeriod?.Late ?? 0,
            avg is null ? null : Math.Round(avg.Value, 1), groups.Where(x => !x.Vendor).Sum(x => x.N), groups.Where(x => x.Vendor).Sum(x => x.N),
            [.. order.Select(s => new CountItem(Name(s), Name(s), groups.Where(x => x.Status == s).Sum(x => x.N)))], weekly,
            [.. perVendor.Select(x => new CountItem(x.Key.ToString(), vendorNames.GetValueOrDefault(x.Key) ?? "—", x.N))],
            [.. perCategory.OrderByDescending(x => x.Avg).ThenBy(x => catNames.GetValueOrDefault(x.Key), StringComparer.OrdinalIgnoreCase).Take(10)
                .Select(x => new CategoryTime(x.Key.ToString(), catNames.GetValueOrDefault(x.Key) ?? "—", x.N, Math.Round(x.Avg, 1)))]);
    }
}
