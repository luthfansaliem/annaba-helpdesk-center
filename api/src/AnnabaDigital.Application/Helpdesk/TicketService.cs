using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Application.Users;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Domain.Forms;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Application.Helpdesk;

/// <summary><c>AssetId</c>: what the request is about (from a scanned label); any asset of the organisation that is not retired.</summary>
public sealed record CreateTicketRequest(string? Subject, string? Description, Dictionary<string, JsonElement>? Answers, Guid? LocationId, Guid? OnBehalfOfUserId, Guid? AssetId = null);
public sealed record TransitionRequest(string? Action, string? Reason, string? ResolutionCode, string? ResolutionNote, int? Version);
public sealed record AssignRequest(Guid? AssigneeId, Guid? TeamId, int? Version);
/// <summary>Like the place, the asset is replaced as given: send the current one to keep it, null to clear it.</summary>
public sealed record UpdateTicketRequest(string? Subject, string? Priority, Guid? LocationId, int? Version, Guid? AssetId = null);
public sealed record CommentRequest(string? Body, bool Internal);
public sealed record TicketQuery(string? Q, string? Status, string? Priority, Guid? AssigneeId, Guid? TeamId, bool? Unassigned, string? Sla = null, string? Sort = null, int Page = 1, int PageSize = 20,
    Guid? LocationId = null, Guid? ServiceId = null, DateTimeOffset? From = null, DateTimeOffset? To = null, bool? AssignedToMe = null);

/// <summary>TKT-011. One change applied to many tickets. <c>Action</c> is assign, priority, start, hold, resume, resolve or cancel; the other fields are the ones that action needs.</summary>
public sealed record BulkRequest(Guid[]? Ids, string? Action, Guid? AssigneeId, Guid? TeamId, string? Priority, string? Reason, string? ResolutionCode, string? ResolutionNote);
public sealed record BulkItem(Guid Id, string? Number, bool Ok, string? Error);
public sealed record BulkResult(string Action, int Requested, int Succeeded, IReadOnlyList<BulkItem> Items);

public sealed record TicketListItem(Guid Id, string Number, string Subject, string Status, string Priority, string ServiceName, string? RequesterName, string? AssigneeName, string? TeamName,
    string? LocationName, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, string? SlaState, DateTimeOffset? ResolutionDueAt);
public sealed record TicketAnswer(string Key, string Label, string Type, JsonElement Value, string? Display);
public sealed record TicketEventItem(Guid Id, string Kind, string Actor, bool Internal, string? Body, string? From, string? To, DateTimeOffset At);
public sealed record TicketDetail(Guid Id, string Number, string Subject, string? Description, string Status, string Priority, Guid ServiceId, string ServiceName, int FormVersion,
    Guid RequesterId, string? RequesterName, string? RequesterEmail, string? CreatedByName, Guid? AssigneeId, string? AssigneeName, Guid? TeamId, string? TeamName, Guid? LocationId, string? LocationName,
    string? HoldReason, string? ResolutionCode, string? ResolutionNote, string? CancelReason, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, DateTimeOffset? ResolvedAt, DateTimeOffset? ClosedAt,
    int ReopenCount, int Version, IReadOnlyList<string> Allowed, bool CanComment, bool CanCommentInternal, bool CanEdit, bool CanAssign,
    IReadOnlyList<TicketAnswer> Answers, IReadOnlyList<TicketEventItem> Timeline, TicketSla? Sla, DateTimeOffset? ExpectedBy, IReadOnlyList<TicketApprovalItem> Approvals, Guid? MyApprovalId, IReadOnlyList<AttachmentItem> Attachments, bool CanAttach,
    DateTimeOffset? AnonymisedAt = null, LegalHoldInfo? LegalHold = null, bool CanHold = false, TicketAsset? Asset = null, TicketRatingInfo? Rating = null);
public sealed record TicketAsset(Guid Id, string Tag, string Name, bool Retired);
/// <summary>How a ticket stands against its targets. Shown to staff only; requesters get just the expected date.</summary>
public sealed record TicketSla(string State, string? PolicyName, int FirstResponseMinutes, int ResolutionMinutes, DateTimeOffset? FirstResponseDueAt, DateTimeOffset? FirstRespondedAt, DateTimeOffset? ResolutionDueAt,
    DateTimeOffset? PausedAt, int EscalationLevel, DateTimeOffset? BreachedAt);
public sealed record AssigneeOption(Guid Id, string Name, string Email);

/// <summary>
/// TKT-001/002/004/008. Creates tickets from catalog services and moves them through the state machine in <see cref="TicketWorkflow"/>.
/// Every rule that decides <i>whether</i> a move is allowed lives in the pure workflow; this class decides <i>who</i> the person is, loads and saves, and keeps the timeline and audit trail.
/// Visibility: requesters see their own tickets; staff see tickets of their teams, assigned to them, or everything with <c>tickets.read_all</c>; a ticket you may not see answers 404.
/// </summary>
public sealed class TicketService(IAppDb db, IClock clock, AuditWriter audit, AuthorizationService authz, PortalCatalogService portal, ITenantContext tenant, SlaService sla, AssignmentService routing, TicketApprovalService approvals, NotificationService notifications, AttachmentService attachments, AnnabaDigital.Application.Integrations.IntegrationService integrations)
{
    private const string Prefix = "GA";
    public const int CountCeiling = 10_000;

    private sealed record Viewer(SessionPrincipal Who, IReadOnlySet<string> Perms, List<Guid> TeamIds, List<Guid> Approving)
    {
        public bool Has(string p) => Perms.Contains(p);
        public bool All => Has(Permissions.TicketsReadAll);
        public bool Team => Has(Permissions.TicketsReadTeam);
    }

    private async Task<Viewer> ViewerAsync(SessionPrincipal who, CancellationToken ct)
        => new(who, await authz.GetEffectivePermissionsAsync(who.UserId, ct), await db.TeamMembers.AsNoTracking().Where(m => m.UserId == who.UserId).Select(m => m.TeamId).ToListAsync(ct),
            // tickets the person was asked to approve: found once here (a short, indexed lookup) so the visibility rule below is plain id lists the database can use indexes for
            await db.TicketApprovals.AsNoTracking().Where(a => a.ApproverId == who.UserId).Select(a => a.TicketId).Distinct().Take(1000).ToListAsync(ct));

    /// <summary>Tickets the viewer may see: their own, their teams' (with the permission), everything (with the permission), and any they have been asked to approve.</summary>
    private IQueryable<Ticket> Seen(IQueryable<Ticket> q, Viewer v)
    {
        if (v.All) return q;
        var me = v.Who.UserId; var teams = v.TeamIds; var team = v.Team; var approving = v.Approving;
        return q.Where(t => t.RequesterId == me || t.CreatedById == me || t.AssigneeId == me || (team && t.TeamId != null && teams.Contains(t.TeamId.Value))
            || approving.Contains(t.Id));
    }

    private static TicketActor ActorOf(Ticket t, Viewer v)
        => new(t.RequesterId == v.Who.UserId || t.CreatedById == v.Who.UserId, v.Has(Permissions.TicketsTransition), v.Has(Permissions.TicketsAssign), t.AssigneeId == v.Who.UserId);

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    private static string Lower<T>(T e) where T : struct, Enum => e.ToString().ToLowerInvariant();
    private static string ActionName(TicketAction a) => a.ToString().ToLowerInvariant();

    // ================================================================ lists
    /// <summary>The requester's own tickets (any signed-in person), or the staff queue (<paramref name="mineOnly"/> = false; needs a ticket read permission).</summary>
    public async Task<PagedResult<TicketListItem>> ListAsync(TicketQuery qy, SessionPrincipal who, bool mineOnly, CancellationToken ct = default)
    {
        var v = await ViewerAsync(who, ct);
        IQueryable<Ticket> q = db.Tickets.AsNoTracking();
        if (mineOnly) { var me = who.UserId; q = q.Where(t => t.RequesterId == me || t.CreatedById == me); }
        else
        {
            if (!v.All && !v.Team) throw DomainException.Denied(Permissions.TicketsReadTeam);
            q = Seen(q, v);
        }

        if (!string.IsNullOrWhiteSpace(qy.Status))
        {
            if (qy.Status.Trim().Equals("open", StringComparison.OrdinalIgnoreCase)) q = q.Where(t => t.Status != TicketStatus.Closed && t.Status != TicketStatus.Cancelled && t.Status != TicketStatus.Rejected);
            else if (Enum.TryParse<TicketStatus>(qy.Status.Trim(), true, out var st) && Enum.IsDefined(st)) q = q.Where(t => t.Status == st);
            else throw DomainException.Validation("Unknown status.", new Dictionary<string, string> { ["status"] = "Choose open, submitted, assigned, inprogress, onhold, resolved, closed, cancelled or reopened." });
        }
        if (!string.IsNullOrWhiteSpace(qy.Priority))
        {
            if (Enum.TryParse<TicketPriority>(qy.Priority.Trim(), true, out var pr) && Enum.IsDefined(pr)) q = q.Where(t => t.Priority == pr);
            else throw DomainException.Validation("Unknown priority.", new Dictionary<string, string> { ["priority"] = "Choose low, medium, high or urgent." });
        }
        if (qy.AssigneeId is { } aid) q = q.Where(t => t.AssigneeId == aid);
        if (qy.TeamId is { } tid) q = q.Where(t => t.TeamId == tid);
        if (qy.Unassigned == true) q = q.Where(t => t.AssigneeId == null);
        if (qy.AssignedToMe == true) { var me = who.UserId; q = q.Where(t => t.AssigneeId == me); }
        if (qy.ServiceId is { } sid) q = q.Where(t => t.ServiceId == sid);
        if (qy.From is { } from) q = q.Where(t => t.CreatedAt >= from);
        if (qy.To is { } to) q = q.Where(t => t.CreatedAt < to);
        if (qy.LocationId is { } lid)
        {
            // a place stands for everything inside it: choosing a building also finds the tickets on its floors and in its rooms
            var tree = await db.Locations.AsNoTracking().Select(l => new { l.Id, l.ParentId }).ToListAsync(ct);
            var inside = new HashSet<Guid> { lid }; var grew = true;
            while (grew) { grew = false; foreach (var l in tree) if (l.ParentId is { } par && inside.Contains(par) && inside.Add(l.Id)) grew = true; }
            var places = inside.ToList();
            q = q.Where(t => t.LocationId != null && places.Contains(t.LocationId.Value));
        }
        if (!string.IsNullOrWhiteSpace(qy.Sla))
        {
            if (Enum.TryParse<SlaState>(qy.Sla.Trim().Replace("_", ""), true, out var ss) && Enum.IsDefined(ss)) q = q.Where(t => t.SlaState == ss);
            else throw DomainException.Validation("Unknown SLA state.", new Dictionary<string, string> { ["sla"] = "Choose none, ontrack, atrisk, breached, paused or met." });
        }
        if (Clean(qy.Q) is { } text)
        {
            // LIKE on lower(column) with a pattern built here, so the trigram indexes (lower(number), lower(subject), lower(description)) can serve it; people are found first
            // (a few thousand rows) and the tickets are matched by requester id, instead of asking the users table once per ticket
            var pat = "%" + text.ToLowerInvariant().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
            var people = await db.Users.AsNoTracking().Where(u => EF.Functions.Like(u.Name.ToLower(), pat, "\\") || EF.Functions.Like(u.Email.ToLower(), pat, "\\")).Select(u => u.Id).Take(500).ToListAsync(ct);
            q = q.Where(t => EF.Functions.Like(t.Number.ToLower(), pat, "\\") || EF.Functions.Like(t.Subject.ToLower(), pat, "\\") || (t.Description != null && EF.Functions.Like(t.Description.ToLower(), pat, "\\"))
                || people.Contains(t.RequesterId));
        }

        var page = Math.Max(1, qy.Page); var size = Math.Clamp(qy.PageSize, 1, 100);
        var ordered = string.Equals(qy.Sort, "due", StringComparison.OrdinalIgnoreCase)
            ? q.OrderBy(t => t.ResolutionDueAt == null).ThenBy(t => t.ResolutionDueAt).ThenByDescending(t => t.Id)       // soonest deadline first, tickets without one last
            : q.OrderByDescending(t => t.UpdatedAt).ThenByDescending(t => t.Id);
        var staff = v.All || v.Team;
        var rows = await ordered.Skip((page - 1) * size).Take(size)
            .Select(t => new
            {
                t.Id, t.Number, t.Subject, t.Status, t.Priority, t.ServiceName, t.CreatedAt, t.UpdatedAt, t.SlaState, t.ResolutionDueAt,
                Requester = db.Users.Where(u => u.Id == t.RequesterId).Select(u => u.Name).FirstOrDefault(),
                Assignee = db.Users.Where(u => u.Id == t.AssigneeId).Select(u => u.Name).FirstOrDefault(),
                Team = db.Teams.Where(x => x.Id == t.TeamId).Select(x => x.Name).FirstOrDefault(),
                Location = db.Locations.Where(l => l.Id == t.LocationId).Select(l => l.Name).FirstOrDefault(),
            }).ToListAsync(ct);
        // a first page that is not full already says how many there are; only longer lists need a count (the expensive part for a common search word)
        // counting stops at a ceiling: "more than 10,000" is as useful to a person as the exact number, and a broad search over a very large tenant should not have to count them all
        var counted = page == 1 && rows.Count < size ? rows.Count : await q.Take(CountCeiling + 1).CountAsync(ct);
        var capped = counted > CountCeiling; var total = Math.Min(counted, CountCeiling);
        return new PagedResult<TicketListItem>([.. rows.Select(r => new TicketListItem(r.Id, r.Number, r.Subject, Lower(r.Status), Lower(r.Priority), r.ServiceName, r.Requester, r.Assignee, r.Team, r.Location, r.CreatedAt, r.UpdatedAt,
            staff ? Lower(r.SlaState) : null, r.ResolutionDueAt))], page, size, total, capped);
    }

    /// <summary>The tickets this person may see in the staff views (queue, reports): their teams', assigned to them, or all with <c>tickets.read_all</c>. Anyone without a ticket read permission is refused.</summary>
    public async Task<IQueryable<Ticket>> QueueScopeAsync(SessionPrincipal who, CancellationToken ct = default)
    {
        var v = await ViewerAsync(who, ct);
        if (!v.All && !v.Team) throw DomainException.Denied(Permissions.TicketsReadTeam);
        return Seen(db.Tickets.AsNoTracking(), v);
    }

    // ================================================================ detail
    public async Task<TicketDetail> GetAsync(Guid id, SessionPrincipal who, CancellationToken ct = default)
    {
        var v = await ViewerAsync(who, ct);
        var t = await Seen(db.Tickets.AsNoTracking(), v).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Ticket");
        return await BuildAsync(t, v, ct);
    }

    private async Task<TicketDetail> BuildAsync(Ticket t, Viewer v, CancellationToken ct)
    {
        var ids = new[] { t.RequesterId, t.CreatedById, t.AssigneeId ?? Guid.Empty };
        var people = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).Select(u => new { u.Id, u.Name, u.Email }).ToListAsync(ct);
        string? Name(Guid? g) => g is null ? null : people.FirstOrDefault(p => p.Id == g)?.Name;
        var team = t.TeamId is { } tid ? await db.Teams.AsNoTracking().Where(x => x.Id == tid).Select(x => x.Name).FirstOrDefaultAsync(ct) : null;
        var location = t.LocationId is { } lid ? await db.Locations.AsNoTracking().Where(l => l.Id == lid).Select(l => l.Name).FirstOrDefaultAsync(ct) : null;

        var answers = await AnswersAsync(t, ct);
        var trail = await approvals.ForTicketAsync(t.Id, ct);
        var events = await db.TicketEvents.AsNoTracking().Where(e => e.TicketId == t.Id && (!e.Internal || v.Has(Permissions.TicketsCommentInternal))).OrderBy(e => e.CreatedAt).ThenBy(e => e.Id).ToListAsync(ct);
        var actor = ActorOf(t, v);
        var allowed = TicketWorkflow.Allowed(t.Status, t.AssigneeId is not null, t.ClosedAt, clock.UtcNow, actor).Select(ActionName).ToList();
        var open = !TicketWorkflow.IsFinished(t.Status);
        var canHold = v.Has(Permissions.RetentionManage); // the hold and its reason are for the people who manage retention only
        var hold = canHold ? await db.TicketLegalHolds.AsNoTracking().Where(h => h.TicketId == t.Id).Select(h => new LegalHoldInfo(h.Reason, h.PlacedAt, db.Users.Where(u => u.Id == h.PlacedById).Select(u => u.Name).FirstOrDefault())).FirstOrDefaultAsync(ct) : null;
        return new TicketDetail(t.Id, t.Number, t.Subject, t.Description, Lower(t.Status), Lower(t.Priority), t.ServiceId, t.ServiceName, t.FormVersion,
            t.RequesterId, Name(t.RequesterId), people.FirstOrDefault(p => p.Id == t.RequesterId)?.Email, Name(t.CreatedById), t.AssigneeId, Name(t.AssigneeId), t.TeamId, team, t.LocationId, location,
            t.HoldReason, t.ResolutionCode, t.ResolutionNote, t.CancelReason, t.CreatedAt, t.UpdatedAt, t.ResolvedAt, t.ClosedAt, t.ReopenCount, t.Version,
            allowed, open, open && v.Has(Permissions.TicketsCommentInternal), open && v.Has(Permissions.TicketsUpdate), actor.CanAssign && TicketWorkflow.CanAssign(t.Status),
            answers, [.. events.Select(e => new TicketEventItem(e.Id, Lower(e.Kind), e.ActorLabel, e.Internal, e.Body, e.FromStatus is { } f ? Lower(f) : null, e.ToStatus is { } to ? Lower(to) : null, e.CreatedAt))],
            v.All || v.Team ? await SlaOfAsync(t, ct) : null, t.SlaState == SlaState.None ? null : t.ResolutionDueAt, trail, trail.FirstOrDefault(a => a.ApproverId == v.Who.UserId && a.Status == "pending")?.Id,
            await attachments.ListAsync(t.Id, v.Who.UserId, v.Has(Permissions.TicketsUpdate), ct), open, t.AnonymisedAt, hold, canHold,
            t.AssetId is { } aid ? await db.Assets.AsNoTracking().Where(a => a.Id == aid).Select(a => new TicketAsset(a.Id, a.Tag, a.Name, a.RetiredAt != null)).FirstOrDefaultAsync(ct) : null,
            await RatingOfAsync(t, v.Who.UserId, ct));
    }

    /// <summary>TKT-009. The rating as this viewer sees it (the requester may rate a resolved request for 14 days; planned maintenance is never rated).</summary>
    private async Task<TicketRatingInfo> RatingOfAsync(Ticket t, Guid viewer, CancellationToken ct)
    {
        var r = await db.TicketRatings.AsNoTracking().Where(x => x.TicketId == t.Id).Select(x => new { x.Score, x.Comment, x.UpdatedAt }).FirstOrDefaultAsync(ct);
        var can = t.RequesterId == viewer && RatingRules.CanRate(t.Status, t.ResolvedAt, clock.UtcNow, true, await db.MaintenanceVisits.AnyAsync(m => m.TicketId == t.Id, ct));
        return new TicketRatingInfo(r?.Score, r?.Comment, r?.UpdatedAt, can, can ? t.ResolvedAt + RatingRules.Window : null);
    }

    private async Task<TicketSla?> SlaOfAsync(Ticket t, CancellationToken ct)
    {
        if (t.SlaPolicyId is not { } pid) return null;
        var name = await db.SlaPolicies.AsNoTracking().Where(p => p.Id == pid).Select(p => p.Name).FirstOrDefaultAsync(ct);
        return new TicketSla(Lower(t.SlaState), name, t.SlaFirstResponseMinutes, t.SlaResolutionMinutes, t.FirstResponseDueAt, t.FirstRespondedAt, t.ResolutionDueAt, t.SlaPausedAt, t.SlaEscalationLevel, t.SlaBreachedAt);
    }

    /// <summary>The answers labelled with the questions of the form version the ticket was made with; places and people are shown by name.</summary>
    private async Task<IReadOnlyList<TicketAnswer>> AnswersAsync(Ticket t, CancellationToken ct)
    {
        if (t.FormVersion == 0) return [];
        var json = await db.ServiceFormVersions.AsNoTracking().Where(x => x.ServiceId == t.ServiceId && x.Number == t.FormVersion).Select(x => x.FieldsJson).FirstOrDefaultAsync(ct);
        if (json is null) return [];
        var fields = FormEngine.Deserialize(json);
        var values = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(t.AnswersJson) ?? [];

        var locIds = fields.Where(f => f.Type == FieldTypes.Location && values.ContainsKey(f.Key)).Select(f => Guid.TryParse(values[f.Key].GetString(), out var g) ? g : Guid.Empty).ToList();
        var userIds = fields.Where(f => f.Type == FieldTypes.User && values.ContainsKey(f.Key)).Select(f => Guid.TryParse(values[f.Key].GetString(), out var g) ? g : Guid.Empty).ToList();
        var locs = locIds.Count == 0 ? [] : await db.Locations.AsNoTracking().Where(l => locIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, l => l.Name, ct);
        var users = userIds.Count == 0 ? [] : await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Name, ct);

        var fileIds = fields.Where(f => f.Type == FieldTypes.File && values.ContainsKey(f.Key)).SelectMany(f => values[f.Key].EnumerateArray().Select(x => Guid.TryParse(x.GetString(), out var g) ? g : Guid.Empty)).ToList();
        var files = fileIds.Count == 0 ? [] : await db.Attachments.AsNoTracking().Where(a => fileIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.FileName, ct);

        var list = new List<TicketAnswer>();
        foreach (var f in fields)
        {
            if (!values.TryGetValue(f.Key, out var val)) continue;
            string? display = null;
            switch (f.Type)
            {
                case FieldTypes.Select: display = f.Options?.FirstOrDefault(o => o.Value == val.GetString())?.Label ?? val.GetString(); break;
                case FieldTypes.MultiSelect: display = string.Join(", ", val.EnumerateArray().Select(x => f.Options?.FirstOrDefault(o => o.Value == x.GetString())?.Label ?? x.GetString())); break;
                case FieldTypes.Location: display = Guid.TryParse(val.GetString(), out var lg) && locs.TryGetValue(lg, out var ln) ? ln : null; break;
                case FieldTypes.User: display = Guid.TryParse(val.GetString(), out var ug) && users.TryGetValue(ug, out var un) ? un : null; break;
                case FieldTypes.File: display = string.Join(", ", val.EnumerateArray().Select(x => Guid.TryParse(x.GetString(), out var fg) && files.TryGetValue(fg, out var fn) ? fn : "—")); break;
            }
            list.Add(new TicketAnswer(f.Key, f.Label, f.Type, val.Clone(), display));
        }
        return list;
    }

    // ================================================================ create
    public async Task<TicketDetail> CreateAsync(Guid serviceId, CreateTicketRequest req, SessionPrincipal who, ClientInfo client, CancellationToken ct = default)
    {
        var v = await ViewerAsync(who, ct);
        var errors = new Dictionary<string, string>();
        var subject = (req.Subject ?? "").Trim();
        if (subject.Length is < 3 or > 200) errors["subject"] = "Use 3–200 characters.";
        var description = Clean(req.Description);
        if (description is { Length: > 4000 }) errors["description"] = "Use at most 4000 characters.";

        var requesterId = who.UserId;
        if (req.OnBehalfOfUserId is { } ob && ob != who.UserId)
        {
            if (!v.Has(Permissions.TicketsUpdate)) throw DomainException.Denied(Permissions.TicketsUpdate);
            if (!await db.Users.AnyAsync(u => u.Id == ob && u.Status == UserStatus.Active, ct)) errors["onBehalfOfUserId"] = "Choose an active person.";
            requesterId = ob;
        }
        if (req.LocationId is { } lid && !await db.Locations.AnyAsync(l => l.Id == lid && l.ArchivedAt == null, ct)) errors["locationId"] = "Choose an active location.";
        if (req.AssetId is { } asid && !await db.Assets.AnyAsync(a => a.Id == asid && a.RetiredAt == null, ct)) errors["assetId"] = "This asset is no longer in the register.";

        AnswerCheck? check = null;
        try { check = await portal.CheckAnswersAsync(serviceId, new CheckAnswersRequest(req.Answers), who, ct); }
        catch (DomainException e) when (e.Code == ErrorCodes.Validation && e.Fields is not null) { foreach (var (k, m) in e.Fields) errors[$"answers.{k}"] = m; }
        if (errors.Count > 0 || check is null) throw DomainException.Validation("Please correct the highlighted fields.", errors);

        var service = await db.Services.AsNoTracking().FirstAsync(s => s.Id == serviceId, ct);
        var now = clock.UtcNow; var year = now.Year;
        var ticket = new Ticket
        {
            ServiceId = serviceId, ServiceName = service.Name, FormVersion = check.FormVersion, Subject = subject, Description = description,
            AnswersJson = JsonSerializer.Serialize(check.Values), RequesterId = requesterId, CreatedById = who.UserId, LocationId = req.LocationId, AssetId = req.AssetId,
            Priority = service.DefaultPriority, Status = TicketStatus.Submitted, TeamId = service.OwningTeamId, Year = year, CreatedAt = now, UpdatedAt = now,
        };
        var policy = await approvals.PolicyForAsync(serviceId, ct);
        RouteResult? routed = null;
        await db.InTransactionAsync(async () =>
        {
            ticket.Sequence = await db.NextTicketSequenceAsync(tenant.TenantId, year, ct);
            ticket.Number = $"{Prefix}-{year}-{ticket.Sequence:D6}";
            db.Tickets.Add(ticket);
            var behalf = requesterId != who.UserId ? await db.Users.Where(u => u.Id == requesterId).Select(u => u.Name).FirstAsync(ct) : null;
            AddEvent(ticket, TicketEventKind.Created, who, behalf is null ? null : $"Filed on behalf of {behalf}.", false, null, policy is null ? TicketStatus.Submitted : TicketStatus.PendingApproval, now);
            // Either the request goes straight to the team, or it waits for approval and is routed only once approved.
            if (policy is null) routed = await routing.ApplyRoutingAsync(ticket, service.OwningTeamId, now, ct);
            else { ticket.TeamId = service.OwningTeamId; await approvals.StartAsync(ticket, policy, who, ct); }
            await attachments.ClaimAsync(ticket.Id, check.FileIds, who.UserId, ct);      // files uploaded for the form now belong to the ticket
            await sla.ApplyAsync(ticket, now, ct);
            if (policy is not null) { SlaClock.Pause(ticket, now); await sla.RefreshAsync(ticket, now, ct); }   // the clock does not run while it waits for approval
            audit.Record("ticket.create", "ticket", ticket.Id.ToString(), Outcome.Success, client, who.UserId, who.Email,
                new Dictionary<string, string> { ["number"] = ticket.Number, ["service"] = service.Name, ["formVersion"] = check.FormVersion.ToString(), ["rule"] = routed?.RuleName ?? "", ["sla"] = Lower(ticket.SlaState), ["approval"] = policy is null ? "none" : "required" });
            if (routed?.AssigneeId is { } assignee) await notifications.AssignedAsync(ticket, assignee, who.UserId, ct);
            await integrations.PublishAsync("ticket.created", ticket, null, ct);
            await db.SaveChangesAsync(ct);
            return 0;        }, ct);
        return await GetAsync(ticket.Id, who, ct);
    }

    private void AddEvent(Ticket t, TicketEventKind kind, SessionPrincipal? who, string? body, bool isInternal, TicketStatus? from, TicketStatus? to, DateTimeOffset now)
        => db.TicketEvents.Add(new TicketEvent
        {
            TenantId = t.TenantId, TicketId = t.Id, Kind = kind, ActorId = who?.UserId, ActorLabel = who?.Name ?? "System", Internal = isInternal, Body = body, FromStatus = from, ToStatus = to, CreatedAt = now,
        });

    private async Task<(Ticket Ticket, Viewer Viewer)> LoadAsync(Guid id, SessionPrincipal who, int? version, CancellationToken ct)
    {
        var v = await ViewerAsync(who, ct);
        var t = await Seen(db.Tickets, v).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Ticket");
        if (version is { } ver && ver != t.Version) throw DomainException.Conflict("This ticket was changed by someone else. Reload and try again.");
        return (t, v);
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw DomainException.Conflict("This ticket was changed by someone else. Reload and try again."); }
    }

    // ================================================================ attachments
    public async Task<AttachmentItem> AttachAsync(Guid ticketId, string? fileName, Stream content, SessionPrincipal who, ClientInfo client, CancellationToken ct = default)
    {
        var (t, v) = await LoadAsync(ticketId, who, null, ct);
        if (TicketWorkflow.IsFinished(t.Status)) throw DomainException.Conflict($"Files can't be added to a {Lower(t.Status)} ticket.");
        var a = await attachments.StoreAsync(ticketId, fileName, content, who, client, ct);
        var now = clock.UtcNow;
        AddEvent(t, TicketEventKind.Updated, who, $"Attached {a.FileName} ({Size(a.Size)}).", false, null, null, now);
        t.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return (await attachments.ListAsync(ticketId, who.UserId, v.Has(Permissions.TicketsUpdate), ct)).First(x => x.Id == a.Id);
    }

    public async Task<AttachmentDownload> OpenAttachmentAsync(Guid ticketId, Guid id, SessionPrincipal who, CancellationToken ct = default)
    {
        var v = await ViewerAsync(who, ct);
        if (!await Seen(db.Tickets.AsNoTracking(), v).AnyAsync(x => x.Id == ticketId, ct)) throw DomainException.NotFound("Ticket");
        return await attachments.OpenAsync(ticketId, id, ct);
    }

    public async Task RemoveAttachmentAsync(Guid ticketId, Guid id, SessionPrincipal who, ClientInfo client, CancellationToken ct = default)
    {
        var (t, v) = await LoadAsync(ticketId, who, null, ct);
        if (TicketWorkflow.IsFinished(t.Status)) throw DomainException.Conflict($"Files can't be removed from a {Lower(t.Status)} ticket.");
        var name = await attachments.DeleteAsync(ticketId, id, who, v.Has(Permissions.TicketsUpdate), client, ct);
        var now = clock.UtcNow;
        AddEvent(t, TicketEventKind.Updated, who, $"Removed {name}.", false, null, null, now);
        t.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
    }

    private static string Size(long bytes) => bytes < 1024 ? $"{bytes} B" : bytes < 1024 * 1024 ? $"{bytes / 1024} KB" : $"{bytes / 1024.0 / 1024.0:0.#} MB";

    // ================================================================ transitions
    public async Task<TicketDetail> TransitionAsync(Guid id, TransitionRequest req, SessionPrincipal who, ClientInfo client, CancellationToken ct = default)
    {
        if (!Enum.TryParse<TicketAction>(req.Action?.Trim(), true, out var action) || !Enum.IsDefined(action) || action == TicketAction.Assign)
            throw DomainException.Validation("Unknown action.", new Dictionary<string, string> { ["action"] = "Choose start, hold, resume, resolve, cancel, confirm or reopen." });
        var (t, v) = await LoadAsync(id, who, req.Version, ct);
        var now = clock.UtcNow;
        if (t.AnonymisedAt is not null) throw DomainException.Conflict("This ticket's content was removed under your organisation's retention policy, so it can't change any more. Please make a new request.");

        var to = TicketWorkflow.Next(t.Status, action) ?? throw DomainException.Invariant($"This ticket can't be {Past(action)} while it is {Lower(t.Status)}.");
        if (!TicketWorkflow.Allowed(t.Status, t.AssigneeId is not null, t.ClosedAt, now, ActorOf(t, v)).Contains(action))
            throw DomainException.DeniedBecause(action == TicketAction.Reopen && t.Status == TicketStatus.Closed && v.Has(Permissions.TicketsTransition) is false
                ? "The reopening period has passed. Please make a new request." : "You can't do that on this ticket.");

        var errors = new Dictionary<string, string>();
        var reason = Clean(req.Reason); var code = Clean(req.ResolutionCode)?.ToLowerInvariant(); var note = Clean(req.ResolutionNote);
        if (action is TicketAction.Hold or TicketAction.Cancel or TicketAction.Reopen && (reason is null || reason.Length is < 3 or > 500)) errors["reason"] = "Give a reason of 3–500 characters.";
        if (action == TicketAction.Resolve)
        {
            if (code is null || !TicketWorkflow.ResolutionCodes.Contains(code)) errors["resolutionCode"] = "Choose how it was resolved.";
            if (note is null || note.Length is < 3 or > 2000) errors["resolutionNote"] = "Describe what was done (3–2000 characters).";
        }
        if (errors.Count > 0) throw DomainException.Validation("Please correct the highlighted fields.", errors);
        if (action == TicketAction.Resolve)
        {
            var open = await db.WorkOrders.CountAsync(w => w.TicketId == t.Id && w.Required && w.Status != WorkOrderStatus.Done && w.Status != WorkOrderStatus.Cancelled, ct);
            if (open > 0) throw DomainException.Conflict(open == 1 ? "A required work order is still open. Finish or cancel it first." : $"{open} required work orders are still open. Finish or cancel them first.");
        }

        var from = t.Status; string? body = null;
        switch (action)
        {
            case TicketAction.Start: if (t.AssigneeId is null) { t.AssigneeId = who.UserId; t.AssignedAt = now; } t.FirstRespondedAt ??= now; t.HoldReason = null; break;
            case TicketAction.Hold: t.HoldReason = reason; body = reason; SlaClock.Pause(t, now); break;
            case TicketAction.Resume: t.HoldReason = null; break;                                    // the clock restarts below, once the status has changed
            case TicketAction.Resolve: t.ResolutionCode = code; t.ResolutionNote = note; t.ResolvedAt = now; t.FirstRespondedAt ??= now; t.HoldReason = null; body = $"{code}: {note}"; break;
            case TicketAction.Cancel: t.CancelReason = reason; t.CancelledAt = now; t.HoldReason = null; body = reason; break;
            case TicketAction.Confirm: t.ClosedAt = now; break;
            case TicketAction.Reopen: t.ResolutionCode = null; t.ResolutionNote = null; t.ClosedAt = null; t.ReopenCount++; body = reason; SlaClock.Restart(t, now); break;
        }
        t.Status = to; t.UpdatedAt = now; t.Version++;
        if (action == TicketAction.Resume) await sla.ResumeAsync(t, now, ct); else await sla.RefreshAsync(t, now, ct);
        if (action == TicketAction.Cancel && from == TicketStatus.PendingApproval) await approvals.CancelPendingAsync(t, ct);   // nobody needs to decide any more
        if (action == TicketAction.Cancel)                                                                                     // nobody works on a cancelled request
            foreach (var w in await db.WorkOrders.Where(w => w.TicketId == t.Id && w.Status != WorkOrderStatus.Done && w.Status != WorkOrderStatus.Cancelled).ToListAsync(ct))
            { w.Status = WorkOrderStatus.Cancelled; w.CancelReason = "The ticket was cancelled."; w.UpdatedAt = now; w.Version++; }
        await notifications.StatusAsync(t, to, who.UserId, ct);
        AddEvent(t, TicketEventKind.Status, who, body, false, from, to, now);
        audit.Record($"ticket.{ActionName(action)}", "ticket", id.ToString(), Outcome.Success, client, who.UserId, who.Email,
            new Dictionary<string, string> { ["number"] = t.Number }, [new AuditDiff("status", Lower(from), Lower(to))]);
        await integrations.PublishAsync("ticket.status_changed", t, new { from = Lower(from), to = Lower(to), action = ActionName(action) }, ct);
        await SaveAsync(ct);
        return await GetAsync(id, who, ct);
    }

    private static string Past(TicketAction a) => a switch
    {
        TicketAction.Start => "started", TicketAction.Hold => "put on hold", TicketAction.Resume => "resumed", TicketAction.Resolve => "resolved",
        TicketAction.Cancel => "cancelled", TicketAction.Confirm => "confirmed", TicketAction.Reopen => "reopened", _ => "changed",
    };

    // ================================================================ assignment
    public async Task<TicketDetail> AssignAsync(Guid id, AssignRequest req, SessionPrincipal who, ClientInfo client, CancellationToken ct = default)
    {
        var (t, _) = await LoadAsync(id, who, req.Version, ct);
        if (!TicketWorkflow.CanAssign(t.Status)) throw DomainException.Invariant($"A ticket that is {Lower(t.Status)} can't be assigned.");
        var errors = new Dictionary<string, string>();
        if (req.AssigneeId is null && req.TeamId is null) errors["assigneeId"] = "Choose a person or a team.";
        string? assigneeName = null;
        if (req.AssigneeId is { } aid)
        {
            var user = await db.Users.AsNoTracking().Where(u => u.Id == aid && u.Status == UserStatus.Active).Select(u => new { u.Name }).FirstOrDefaultAsync(ct);
            if (user is null) errors["assigneeId"] = "Choose an active person.";
            else if (!(await authz.GetEffectivePermissionsAsync(aid, ct)).Contains(Permissions.TicketsTransition)) errors["assigneeId"] = "That person doesn't work tickets.";
            assigneeName = user?.Name;
        }
        string? teamName = null;
        if (req.TeamId is { } tid)
        {
            var team = await db.Teams.AsNoTracking().Where(x => x.Id == tid).Select(x => new { x.Name, x.ArchivedAt }).FirstOrDefaultAsync(ct);
            if (team is null) errors["teamId"] = "Choose an existing team.";
            else if (team.ArchivedAt is not null) errors["teamId"] = "That team is archived.";
            teamName = team?.Name;
        }
        if (errors.Count > 0) throw DomainException.Validation("Please correct the highlighted fields.", errors);
        if (t.AssigneeId == req.AssigneeId && t.TeamId == req.TeamId) return await GetAsync(id, who, ct); // nothing changes

        var now = clock.UtcNow; var from = t.Status;
        var before = $"{t.AssigneeId?.ToString() ?? "—"} / {t.TeamId?.ToString() ?? "—"}";
        if (req.AssigneeId != t.AssigneeId) t.AssignedAt = req.AssigneeId is null ? null : now;
        t.AssigneeId = req.AssigneeId; t.TeamId = req.TeamId;
        t.Status = TicketWorkflow.AfterAssign(from, req.AssigneeId is not null);
        t.UpdatedAt = now; t.Version++;
        var body = req.AssigneeId is null ? $"Returned to the {teamName} queue." : teamName is null ? $"Assigned to {assigneeName}." : $"Assigned to {assigneeName} ({teamName}).";
        AddEvent(t, TicketEventKind.Assigned, who, body, false, from, t.Status, now);
        if (req.AssigneeId is { } newAssignee) await notifications.AssignedAsync(t, newAssignee, who.UserId, ct);
        audit.Record("ticket.assign", "ticket", id.ToString(), Outcome.Success, client, who.UserId, who.Email, new Dictionary<string, string> { ["number"] = t.Number },
            [new AuditDiff("assignee/team", before, $"{req.AssigneeId?.ToString() ?? "—"} / {req.TeamId?.ToString() ?? "—"}")]);
        await integrations.PublishAsync("ticket.assigned", t, new { assigneeId = req.AssigneeId, teamId = req.TeamId }, ct);
        await SaveAsync(ct);
        return await GetAsync(id, who, ct);
    }

    /// <summary>People who can be given a ticket: active and holding <c>tickets.transition</c>; narrowed to a team's members when one is given.</summary>
    public async Task<IReadOnlyList<AssigneeOption>> AssigneesAsync(Guid? teamId, CancellationToken ct = default)
    {
        var q = db.Users.AsNoTracking().Where(u => u.Status == UserStatus.Active
            && u.UserRoles.Any(ur => ur.Role!.ArchivedAt == null && ur.Role.Permissions.Any(p => p.PermissionKey == Permissions.TicketsTransition)));
        if (teamId is { } tid) q = q.Where(u => db.TeamMembers.Any(m => m.TeamId == tid && m.UserId == u.Id));
        var rows = await q.Select(u => new { u.Id, u.Name, u.Email }).ToListAsync(ct);
        return [.. rows.OrderBy(r => r.Name).Select(r => new AssigneeOption(r.Id, r.Name, r.Email))];
    }

    // ================================================================ edit & comments
    // ================================================================ bulk actions
    public const int MaxBulk = 100;
    private static readonly string[] BulkTransitions = ["start", "hold", "resume", "resolve", "cancel"];

    /// <summary>
    /// TKT-011. Applies one action to up to 100 tickets, one ticket at a time through the same methods a single change uses, so every rule, permission, SLA update, notification
    /// and audit entry is the one-ticket one. A ticket that cannot take the change (not allowed, wrong status, not visible) is reported and skipped; the others still go through.
    /// </summary>
    public async Task<BulkResult> BulkAsync(BulkRequest req, SessionPrincipal who, ClientInfo client, CancellationToken ct = default)
    {
        var v = await ViewerAsync(who, ct);
        if (!v.All && !v.Team) throw DomainException.Denied(Permissions.TicketsReadTeam);
        var ids = (req.Ids ?? []).Distinct().ToList(); var action = (req.Action ?? "").Trim().ToLowerInvariant();
        var errors = new Dictionary<string, string>(); TicketPriority? priority = null;
        if (ids.Count == 0) errors["ids"] = "Choose at least one ticket.";
        else if (ids.Count > MaxBulk) errors["ids"] = $"Choose at most {MaxBulk} tickets at once.";
        if (action == "assign")
        {
            if (!v.Has(Permissions.TicketsAssign)) throw DomainException.Denied(Permissions.TicketsAssign);
            if (req.AssigneeId is null && req.TeamId is null) errors["assigneeId"] = "Choose a person or a team.";
        }
        else if (action == "priority")
        {
            if (!v.Has(Permissions.TicketsUpdate)) throw DomainException.Denied(Permissions.TicketsUpdate);
            if (Enum.TryParse<TicketPriority>((req.Priority ?? "").Trim(), true, out var pr) && Enum.IsDefined(pr)) priority = pr; else errors["priority"] = "Choose low, medium, high or urgent.";
        }
        else if (BulkTransitions.Contains(action))
        {
            if ((action is "hold" or "cancel") && string.IsNullOrWhiteSpace(req.Reason)) errors["reason"] = "Give a reason.";
            if (action == "resolve" && (string.IsNullOrWhiteSpace(req.ResolutionCode) || string.IsNullOrWhiteSpace(req.ResolutionNote))) errors["resolutionNote"] = "Give a resolution code and a note.";
        }
        else errors["action"] = "Choose assign, priority, start, hold, resume, resolve or cancel.";
        if (errors.Count > 0) throw DomainException.Validation("Please correct the highlighted fields.", errors);

        // what the person may see; anything else is reported exactly like a ticket that does not exist
        var known = (await Seen(db.Tickets.AsNoTracking().Where(t => ids.Contains(t.Id)), v).Select(t => new { t.Id, t.Number, t.Subject, t.LocationId, t.Priority }).ToListAsync(ct)).ToDictionary(t => t.Id);
        var items = new List<BulkItem>();
        foreach (var id in ids)
        {
            if (!known.TryGetValue(id, out var t)) { items.Add(new BulkItem(id, null, false, "Ticket not found.")); continue; }
            try
            {
                switch (action)
                {
                    case "assign": await AssignAsync(id, new AssignRequest(req.AssigneeId, req.TeamId, null), who, client, ct); break;
                    case "priority": if (t.Priority != priority) await UpdateAsync(id, new UpdateTicketRequest(t.Subject, Lower(priority!.Value), t.LocationId, null), who, client, ct); break;
                    default: await TransitionAsync(id, new TransitionRequest(action, req.Reason, req.ResolutionCode, req.ResolutionNote, null), who, client, ct); break;
                }
                items.Add(new BulkItem(id, t.Number, true, null));
            }
            catch (DomainException e) { db.ResetTracking(); items.Add(new BulkItem(id, t.Number, false, e.Message)); }
        }
        var ok = items.Count(i => i.Ok);
        audit.Record("ticket.bulk", "ticket", null, Outcome.Success, client, who.UserId, who.Email, new Dictionary<string, string> { ["action"] = action, ["requested"] = ids.Count.ToString(), ["succeeded"] = ok.ToString() });
        await db.SaveChangesAsync(ct);
        return new BulkResult(action, ids.Count, ok, items);
    }

    public async Task<TicketDetail> UpdateAsync(Guid id, UpdateTicketRequest req, SessionPrincipal who, ClientInfo client, CancellationToken ct = default)
    {
        var (t, _) = await LoadAsync(id, who, req.Version, ct);
        if (TicketWorkflow.IsFinished(t.Status)) throw DomainException.Conflict($"A {Lower(t.Status)} ticket can't be edited.");
        var errors = new Dictionary<string, string>();
        var subject = (req.Subject ?? "").Trim();
        if (subject.Length is < 3 or > 200) errors["subject"] = "Use 3–200 characters.";
        var priority = t.Priority;
        if (req.Priority is not null && !(Enum.TryParse(req.Priority.Trim(), true, out priority) && Enum.IsDefined(priority))) errors["priority"] = "Choose low, medium, high or urgent.";
        string? locName = null;
        if (req.LocationId is { } lid)
        {
            locName = await db.Locations.AsNoTracking().Where(l => l.Id == lid && l.ArchivedAt == null).Select(l => l.Name).FirstOrDefaultAsync(ct);
            if (locName is null) errors["locationId"] = "Choose an active location.";
        }
        string? assetLabel = null;
        if (req.AssetId is { } asid && asid != t.AssetId)
        {
            assetLabel = await db.Assets.AsNoTracking().Where(a => a.Id == asid && a.RetiredAt == null).Select(a => a.Tag + " " + a.Name).FirstOrDefaultAsync(ct);
            if (assetLabel is null) errors["assetId"] = "Choose an asset that is still in the register.";
        }
        if (errors.Count > 0) throw DomainException.Validation("Please correct the highlighted fields.", errors);

        var diff = new List<AuditDiff>(); var lines = new List<string>();
        if (t.Subject != subject) { diff.Add(new("subject", t.Subject, subject)); lines.Add($"Subject changed to “{subject}”."); }
        if (t.Priority != priority) { diff.Add(new("priority", Lower(t.Priority), Lower(priority))); lines.Add($"Priority changed from {Lower(t.Priority)} to {Lower(priority)}."); }
        if (t.LocationId != req.LocationId) { diff.Add(new("location", t.LocationId?.ToString() ?? "—", req.LocationId?.ToString() ?? "—")); lines.Add(locName is null ? "Location cleared." : $"Location changed to {locName}."); }
        if (t.AssetId != req.AssetId) { diff.Add(new("asset", t.AssetId?.ToString() ?? "—", req.AssetId?.ToString() ?? "—")); lines.Add(assetLabel is null ? "Asset cleared." : $"Asset set to {assetLabel}."); }
        if (diff.Count == 0) return await GetAsync(id, who, ct);

        var now = clock.UtcNow;
        var policyInputsChanged = t.Priority != priority || t.LocationId != req.LocationId;
        t.Subject = subject; t.Priority = priority; t.LocationId = req.LocationId; t.AssetId = req.AssetId; t.UpdatedAt = now; t.Version++;
        if (policyInputsChanged) await sla.ApplyAsync(t, now, ct);   // a new priority or place may mean a different policy; time already used is kept
        AddEvent(t, TicketEventKind.Updated, who, string.Join(" ", lines), false, null, null, now);
        audit.Record("ticket.update", "ticket", id.ToString(), Outcome.Success, client, who.UserId, who.Email, new Dictionary<string, string> { ["number"] = t.Number }, diff);
        await SaveAsync(ct);
        return await GetAsync(id, who, ct);
    }

    public async Task<TicketDetail> CommentAsync(Guid id, CommentRequest req, SessionPrincipal who, ClientInfo client, CancellationToken ct = default)
    {
        var (t, v) = await LoadAsync(id, who, null, ct);
        var body = (req.Body ?? "").Trim();
        if (body.Length is < 1 or > 4000) throw DomainException.Validation("Please correct the highlighted fields.", new Dictionary<string, string> { ["body"] = "Write 1–4000 characters." });
        if (req.Internal && !v.Has(Permissions.TicketsCommentInternal)) throw DomainException.Denied(Permissions.TicketsCommentInternal);
        if (TicketWorkflow.IsFinished(t.Status)) throw DomainException.Conflict($"A {Lower(t.Status)} ticket can't be commented on. Reopen it or make a new request.");
        var now = clock.UtcNow;
        AddEvent(t, TicketEventKind.Comment, who, body, req.Internal, null, null, now);
        if (!req.Internal && v.Has(Permissions.TicketsTransition) && t.RequesterId != who.UserId && t.FirstRespondedAt is null)
        {
            t.FirstRespondedAt = now;                       // the first public answer from staff is the first response
            await sla.RefreshAsync(t, now, ct);
        }
        await notifications.CommentAsync(t, who.UserId, who.Name, req.Internal, body, ct);
        t.UpdatedAt = now; // a comment is activity, but not a change to the ticket's state, so the version stays
        audit.Record(req.Internal ? "ticket.note" : "ticket.comment", "ticket", id.ToString(), Outcome.Success, client, who.UserId, who.Email, new Dictionary<string, string> { ["number"] = t.Number });
        if (!req.Internal) await integrations.PublishAsync("ticket.commented", t, new { body, author = who.Name, authorId = who.UserId }, ct);   // internal notes never leave the organisation
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, who, ct);
    }

    // ================================================================ work orders (TKT-006)
    /// <summary>The tickets this person may see (requester, creator, assignee, their teams, approvals, or all). Used to scope work orders to their tickets.</summary>
    public async Task<IQueryable<Ticket>> SeenTicketsAsync(SessionPrincipal who, CancellationToken ct = default) => Seen(db.Tickets.AsNoTracking(), await ViewerAsync(who, ct));

    /// <summary>
    /// Resolves a (tracked) ticket because its last required work order is done, as the system. Only when the ticket's state allows resolving; the caller saves.
    /// Returns whether it did.
    /// </summary>
    public async Task<bool> ResolveFromWorkOrdersAsync(Ticket t, IReadOnlyList<string> numbers, DateTimeOffset now, CancellationToken ct = default)
    {
        // work done on a ticket nobody had started (a planned visit, or work given out while the ticket was only assigned) means it was worked: start it on the way to resolved
        var to = TicketWorkflow.Next(t.Status, TicketAction.Resolve) ?? (TicketWorkflow.Next(t.Status, TicketAction.Start) is { } started ? TicketWorkflow.Next(started, TicketAction.Resolve) : null);
        if (to is null || t.AnonymisedAt is not null) return false;
        var from = t.Status; var note = $"All required work orders are done ({string.Join(", ", numbers)}).";
        t.ResolutionCode = "fixed"; t.ResolutionNote = note; t.ResolvedAt = now; t.FirstRespondedAt ??= now; t.HoldReason = null;
        t.Status = to.Value; t.UpdatedAt = now; t.Version++;
        await sla.RefreshAsync(t, now, ct);
        await notifications.StatusAsync(t, to.Value, null, ct);
        AddEvent(t, TicketEventKind.Status, null, $"fixed: {note}", false, from, to, now);
        await integrations.PublishAsync("ticket.status_changed", t, new { from = Lower(from), to = Lower(to.Value), action = "resolve" }, ct);
        audit.Record("ticket.auto_resolve", "ticket", t.Id.ToString(), Outcome.Success, new ClientInfo(null, null, null), null, "system (work orders)",
            new Dictionary<string, string> { ["number"] = t.Number }, [new AuditDiff("status", Lower(from), Lower(to.Value))]);
        return true;
    }

    /// <summary>Adds an internal timeline line to a (tracked) ticket about one of its work orders.</summary>
    public void NoteWorkOrder(Ticket t, SessionPrincipal? who, string body, DateTimeOffset now) => AddEvent(t, TicketEventKind.Updated, who, body, true, null, null, now);

    // ================================================================ planned maintenance
    /// <summary>
    /// R2-5. The ticket of one planned maintenance visit, added to the caller's unit of work (the caller saves, inside its transaction). Filed for the plan's owner on the plan's
    /// service, with no form, no approval and no SLA clock (the work order's due date is the target, and a resolution clock started days ahead would always breach).
    /// The plan's person, else its team, else the normal assignment rules decide who gets it. It resolves itself when its work order is done.
    /// </summary>
    public async Task<Ticket> AddPlannedAsync(ServiceItem service, string subject, string? description, Guid ownerId, Guid? locationId, Guid? assetId, Guid? teamId, Guid? assigneeId, DateTimeOffset now, CancellationToken ct = default)
    {
        var year = now.Year;
        var t = new Ticket
        {
            ServiceId = service.Id, ServiceName = service.Name, FormVersion = 0, Subject = subject, Description = description, AnswersJson = "{}", RequesterId = ownerId, CreatedById = ownerId,
            LocationId = locationId, AssetId = assetId, Priority = service.DefaultPriority, Status = TicketStatus.Submitted, TeamId = teamId ?? service.OwningTeamId, ResolveWithWorkOrders = true,
            Year = year, CreatedAt = now, UpdatedAt = now,
        };
        t.Sequence = await db.NextTicketSequenceAsync(tenant.TenantId, year, ct);
        t.Number = $"{Prefix}-{year}-{t.Sequence:D6}";
        db.Tickets.Add(t);
        AddEvent(t, TicketEventKind.Created, null, "Planned maintenance, made by its maintenance plan.", false, null, TicketStatus.Submitted, now);
        await integrations.PublishAsync("ticket.created", t, null, ct);
        if (assigneeId is { } a)
        {
            var name = await db.Users.AsNoTracking().Where(u => u.Id == a).Select(u => u.Name).FirstOrDefaultAsync(ct);
            t.AssigneeId = a; t.Status = TicketStatus.Assigned; t.AssignedAt = now;
            AddEvent(t, TicketEventKind.Assigned, null, $"Assigned to {name} by the maintenance plan.", false, TicketStatus.Submitted, TicketStatus.Assigned, now);
            await notifications.AssignedAsync(t, a, null, ct);
        }
        else if (teamId is null && (await routing.ApplyRoutingAsync(t, service.OwningTeamId, now, ct)).AssigneeId is { } routed) await notifications.AssignedAsync(t, routed, null, ct);
        return t;
    }

    // ================================================================ automatic closing
    /// <summary>Closes tickets that have been resolved for <see cref="TicketWorkflow.AutoCloseAfter"/> without the requester confirming or reopening. Idempotent; safe with several instances.</summary>
    public async Task<int> AutoCloseDueAsync(CancellationToken ct = default)
    {
        var now = clock.UtcNow; var cutoff = now - TicketWorkflow.AutoCloseAfter;
        var due = await db.Tickets.IgnoreQueryFilters().AsNoTracking().Where(t => t.Status == TicketStatus.Resolved && t.ResolvedAt != null && t.ResolvedAt <= cutoff).Select(t => new { t.Id, t.TenantId }).ToListAsync(ct);
        var closed = 0;
        foreach (var d in due)
        {
            using (tenant.Switch(d.TenantId))
            {
                db.ResetTracking();
                var t = await db.Tickets.FirstOrDefaultAsync(x => x.Id == d.Id && x.Status == TicketStatus.Resolved, ct);
                if (t is null || t.ResolvedAt is null || t.ResolvedAt > cutoff) continue;
                t.Status = TicketStatus.Closed; t.ClosedAt = now; t.UpdatedAt = now; t.Version++;
                AddEvent(t, TicketEventKind.Status, null, "Closed automatically: the resolution was not confirmed or reopened in time.", false, TicketStatus.Resolved, TicketStatus.Closed, now);
                audit.Record("ticket.auto_close", "ticket", t.Id.ToString(), Outcome.Success, new ClientInfo(null, null, null), null, "system (ticket auto-close)",
                    new Dictionary<string, string> { ["number"] = t.Number }, [new AuditDiff("status", "resolved", "closed")]);
                try { await db.SaveChangesAsync(ct); closed++; }
                catch (DbUpdateConcurrencyException) { /* someone confirmed or reopened it just now: leave it */ }
            }
        }
        return closed;
    }
}
