using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Application.Email;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;

namespace AnnabaDigital.Application.Helpdesk;

// ---- policy administration
public sealed record ApproverRef(Guid Id, string Name);
public sealed record ApprovalStepItem(string Name, string Rule, IReadOnlyList<ApproverRef> Users, IReadOnlyList<ApproverRef> Roles, bool RequesterManager = false);
public sealed record ApprovalPolicyItem(Guid ServiceId, string ServiceName, string CategoryName, bool Published, bool HasPolicy, IReadOnlyList<ApprovalStepItem> Steps, Guid? FallbackUserId, string? FallbackName, int Version);
public sealed record SaveApprovalPolicyRequest(ApprovalStepDef[]? Steps, Guid? FallbackUserId, int? Version = null);

// ---- decisions
public sealed record DecideRequest(bool Approve, string? Comment);
public sealed record ReassignRequest(Guid? ToUserId, string? Note);
public sealed record MyApprovalItem(Guid Id, Guid TicketId, string TicketNumber, string TicketSubject, string ServiceName, string? RequesterName, int StepNo, string StepName, string Status, string? Comment, DateTimeOffset CreatedAt, DateTimeOffset? DecidedAt, string? OnBehalfOfName = null);
public sealed record TicketApprovalItem(Guid Id, int StepNo, string StepName, string Rule, Guid ApproverId, string ApproverName, string Status, string? Comment, DateTimeOffset? DecidedAt, string? DecidedVia, string? OnBehalfOfName = null);
public sealed record ApprovalLinkInfo(string TicketNumber, string Subject, string? Description, string ServiceName, string? RequesterName, string StepName, string ApproverName, DateTimeOffset ExpiresAt);
public sealed record ApprovalLinkResult(string Decision, string TicketNumber);

/// <summary>APR-001. Which services need approval and who approves, set per service as ordered steps. Changing a policy never touches requests already waiting: they keep the approvers they were made with.</summary>
public sealed class ApprovalPolicyService(IAppDb db, IClock clock, AuditWriter audit)
{
    public async Task<IReadOnlyList<ApprovalPolicyItem>> ListAsync(CancellationToken ct = default)
    {
        var services = await db.Services.AsNoTracking().Where(s => s.ArchivedAt == null)
            .Select(s => new { s.Id, s.Name, s.IsPublished, Category = db.ServiceCategories.Where(c => c.Id == s.CategoryId).Select(c => c.Name).FirstOrDefault() }).ToListAsync(ct);
        var policies = await db.ApprovalPolicies.AsNoTracking().Where(p => p.ArchivedAt == null).ToListAsync(ct);
        var steps = policies.ToDictionary(p => p.Id, p => ApprovalEngine.Deserialize(p.StepsJson));
        var userIds = steps.Values.SelectMany(x => x).SelectMany(s => s.UserIds).Concat(policies.Where(p => p.FallbackUserId != null).Select(p => p.FallbackUserId!.Value)).Distinct().ToList();
        var roleIds = steps.Values.SelectMany(x => x).SelectMany(s => s.RoleIds).Distinct().ToList();
        var users = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Name, ct);
        var roles = await db.Roles.AsNoTracking().Where(r => roleIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.Name, ct);
        return [.. services.OrderBy(s => s.Category).ThenBy(s => s.Name).Select(s =>
        {
            var p = policies.FirstOrDefault(x => x.ServiceId == s.Id);
            var items = p is null ? [] : steps[p.Id].Select(st => new ApprovalStepItem(st.Name, st.Rule,
                [.. st.UserIds.Select(id => new ApproverRef(id, users.GetValueOrDefault(id) ?? "?"))], [.. st.RoleIds.Select(id => new ApproverRef(id, roles.GetValueOrDefault(id) ?? "?"))], st.RequesterManager)).ToList();
            return new ApprovalPolicyItem(s.Id, s.Name, s.Category ?? "", s.IsPublished, p is not null, items, p?.FallbackUserId, p?.FallbackUserId is { } f ? users.GetValueOrDefault(f) : null, p?.Version ?? 0);
        })];
    }

    public async Task<ApprovalPolicyItem> SetAsync(Guid serviceId, SaveApprovalPolicyRequest req, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var service = await db.Services.AsNoTracking().Where(s => s.Id == serviceId && s.ArchivedAt == null).Select(s => s.Name).FirstOrDefaultAsync(ct) ?? throw DomainException.NotFound("Service");
        var steps = ApprovalEngine.Validate(req.Steps) is { Count: > 0 } bad ? throw DomainException.Validation("The approval steps have problems.", bad) : req.Steps!.ToList();
        var errors = new Dictionary<string, string>();
        for (var i = 0; i < steps.Count; i++)
        {
            var ids = steps[i].UserIds; var rids = steps[i].RoleIds;
            if (ids.Length > 0 && await db.Users.CountAsync(u => ids.Contains(u.Id) && u.Status == UserStatus.Active, ct) != ids.Length) errors[$"steps[{i}].userIds"] = "Only active people of your organisation can approve.";
            else if (rids.Length > 0 && await db.Roles.CountAsync(r => rids.Contains(r.Id) && r.ArchivedAt == null, ct) != rids.Length) errors[$"steps[{i}].userIds"] = "Choose existing roles.";
        }
        if (req.FallbackUserId is { } fb && !await db.Users.AnyAsync(u => u.Id == fb && u.Status == UserStatus.Active, ct)) errors["fallbackUserId"] = "Choose an active person.";
        if (errors.Count > 0) throw DomainException.Validation("The approval steps have problems.", errors);

        var normalized = steps.Select(s => new ApprovalStepDef(s.Name.Trim(), s.Rule, s.UserIds ?? [], s.RoleIds ?? [], s.RequesterManager)).ToList();
        var json = ApprovalEngine.Serialize(normalized);
        var p = await db.ApprovalPolicies.FirstOrDefaultAsync(x => x.ServiceId == serviceId && x.ArchivedAt == null, ct);
        var now = clock.UtcNow;
        if (p is null)
        {
            p = new ApprovalPolicy { ServiceId = serviceId, StepsJson = json, FallbackUserId = req.FallbackUserId, CreatedAt = now, UpdatedAt = now };
            db.ApprovalPolicies.Add(p);
            audit.Record("approval.policy.set", "service", serviceId.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["service"] = service, ["steps"] = normalized.Count.ToString() }, [new AuditDiff("approval", "none", $"{normalized.Count} step(s)")]);
        }
        else
        {
            if (req.Version is { } v && v != p.Version) throw DomainException.Conflict("This policy was changed by someone else. Reload and try again.");
            // jsonb stores its own formatting, so compare the steps themselves, not the text
            if (ApprovalEngine.Serialize(ApprovalEngine.Deserialize(p.StepsJson)) == json && p.FallbackUserId == req.FallbackUserId) return (await ListAsync(ct)).First(x => x.ServiceId == serviceId); // nothing changed
            var before = ApprovalEngine.Deserialize(p.StepsJson).Count;
            p.StepsJson = json; p.FallbackUserId = req.FallbackUserId; p.Version++; p.UpdatedAt = now;
            audit.Record("approval.policy.set", "service", serviceId.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["service"] = service, ["steps"] = normalized.Count.ToString() }, [new AuditDiff("approval", $"{before} step(s)", $"{normalized.Count} step(s)")]);
        }
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw DomainException.Conflict("This policy was changed by someone else. Reload and try again."); }
        catch (DbUpdateException) { throw DomainException.Conflict("This service already has an approval policy. Reload and try again."); }
        return (await ListAsync(ct)).First(x => x.ServiceId == serviceId);
    }

    /// <summary>Requests that need no approval again. Requests already waiting keep their approvers.</summary>
    public async Task<ApprovalPolicyItem> RemoveAsync(Guid serviceId, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var p = await db.ApprovalPolicies.FirstOrDefaultAsync(x => x.ServiceId == serviceId && x.ArchivedAt == null, ct) ?? throw DomainException.NotFound("Approval policy");
        p.ArchivedAt = clock.UtcNow; p.UpdatedAt = p.ArchivedAt.Value; p.Version++;
        audit.Record("approval.policy.remove", "service", serviceId.ToString(), Outcome.Success, client, actor.UserId, actor.Email, null, [new AuditDiff("approval", "required", "none")]);
        await db.SaveChangesAsync(ct);
        return (await ListAsync(ct)).First(x => x.ServiceId == serviceId);
    }
}

/// <summary>
/// APR-003/004/006. Runs a request through its approval steps. A new request waits as <c>PendingApproval</c> (its SLA clock stopped, nothing assigned) until the last step
/// is approved, then it is released to the team exactly like a request that never needed approval; any rejection ends it. Approvers decide in the app or through a
/// one-time, expiring e-mail link bound to them and the step. Nobody — creator or requester — can approve their own request; if that leaves a step empty the policy's
/// fallback person decides, and without one the request cannot be made. Every decision is kept as an immutable row (who, when, what, from where).
/// </summary>
public sealed class TicketApprovalService(IAppDb db, IClock clock, AuditWriter audit, ITokenService tokens, NotificationService notifications, SlaService sla, AssignmentService routing,
    IAppLinks links, IRateLimiter limiter, ITenantContext tenant)
{
    public static readonly TimeSpan LinkLifetime = TimeSpan.FromDays(7);
    public const int MaxApproversPerStep = 25;

    private string Url(string path) => links.Origin.TrimEnd('/') + path;
    private static string Lower<T>(T e) where T : struct, Enum => e.ToString().ToLowerInvariant();

    public async Task<ApprovalPolicy?> PolicyForAsync(Guid serviceId, CancellationToken ct = default)
        => await db.ApprovalPolicies.AsNoTracking().FirstOrDefaultAsync(p => p.ServiceId == serviceId && p.ArchivedAt == null, ct);

    // ================================================================ starting
    /// <summary>Who is away right now and who covers for them (one level only: a delegate's own delegation is not followed).</summary>
    private async Task<Dictionary<Guid, Guid>> ActiveDelegatesAsync(IReadOnlyCollection<Guid> userIds, DateTimeOffset now, CancellationToken ct)
        => userIds.Count == 0 ? [] : (await db.ApprovalDelegations.AsNoTracking().Where(d => userIds.Contains(d.UserId) && d.RevokedAt == null && d.StartsAt <= now && d.EndsAt > now)
            .OrderBy(d => d.CreatedAt).Select(d => new { d.UserId, d.DelegateId }).ToListAsync(ct)).GroupBy(d => d.UserId).ToDictionary(g => g.Key, g => g.First().DelegateId);

    private async Task<List<(Guid Approver, Guid? For)>> ResolveAsync(ApprovalStepDef step, ApprovalPolicy policy, Ticket t, DateTimeOffset now, CancellationToken ct)
    {
        var roleUsers = step.RoleIds.Length == 0 ? [] : await db.UserRoles.AsNoTracking()
            .Where(ur => step.RoleIds.Contains(ur.RoleId) && ur.Role!.ArchivedAt == null && ur.User!.Status == UserStatus.Active).Select(ur => ur.UserId).Distinct().OrderBy(i => i).Take(MaxApproversPerStep).ToListAsync(ct);
        var named = step.UserIds.Length == 0 ? [] : await db.Users.AsNoTracking().Where(u => step.UserIds.Contains(u.Id) && u.Status == UserStatus.Active).Select(u => u.Id).ToListAsync(ct);
        if (step.RequesterManager)
        {
            // APR-002: the requester's manager, when they have one who is active
            var mgr = await db.Profiles.AsNoTracking().Where(pr => pr.UserId == t.RequesterId).Select(pr => pr.ManagerId).FirstOrDefaultAsync(ct);
            if (mgr is { } m && await db.Users.AnyAsync(u => u.Id == m && u.Status == UserStatus.Active, ct)) named.Add(m);
        }
        var all = named.Concat(roleUsers).Distinct().Where(i => i != t.RequesterId && i != t.CreatedById).Take(MaxApproversPerStep).ToList();
        if (all.Count == 0 && policy.FallbackUserId is { } fb && fb != t.RequesterId && fb != t.CreatedById
            && await db.Users.AnyAsync(u => u.Id == fb && u.Status == UserStatus.Active, ct)) all.Add(fb);
        // APR-004: someone who is away is replaced by their delegate, unless the delegate may not approve this request or is already a named approver of the step
        var away = await ActiveDelegatesAsync(all, now, ct); var result = new List<(Guid Approver, Guid? For)>();
        foreach (var who in all)
        {
            if (away.TryGetValue(who, out var d) && d != t.RequesterId && d != t.CreatedById && await db.Users.AnyAsync(u => u.Id == d && u.Status == UserStatus.Active, ct))
            {
                if (all.Contains(d)) continue;                        // their cover is already a named approver of this step, so the absent one adds nothing
                if (result.All(r => r.Approver != d)) result.Add((d, who));
            }
            else if (result.All(r => r.Approver != who)) result.Add((who, null));
        }
        return result;
    }

    /// <summary>
    /// Sets up the approval rows for a new ticket and tells the first step's approvers. Throws (so nothing is saved) when a step has nobody who may approve.
    /// The caller has already added the ticket; this adds the approvals, notifications and the timeline entry.
    /// </summary>
    public async Task StartAsync(Ticket t, ApprovalPolicy policy, SessionPrincipal creator, CancellationToken ct = default)
    {
        var now = clock.UtcNow; var steps = ApprovalEngine.Deserialize(policy.StepsJson);
        var plan = new List<(int No, ApprovalStepDef Step, List<(Guid Approver, Guid? For)> People)>();
        for (var i = 0; i < steps.Count; i++)
        {
            var people = await ResolveAsync(steps[i], policy, t, now, ct);
            if (people.Count == 0) throw DomainException.Invariant($"No approver is available for the step “{steps[i].Name}”. Ask your administrator to set a fallback approver.");
            plan.Add((i + 1, steps[i], people));
        }
        foreach (var (no, step, people) in plan)
            foreach (var (who, onBehalf) in people)
                db.TicketApprovals.Add(new TicketApproval { TicketId = t.Id, StepNo = no, StepName = step.Name, Rule = step.Rule, ApproverId = who, OnBehalfOfId = onBehalf, Status = no == 1 ? ApprovalStatus.Pending : ApprovalStatus.Waiting, CreatedAt = now });
        t.Status = TicketStatus.PendingApproval;
        AddEvent(t, "System", null, $"Waiting for approval: {plan[0].Step.Name}.", null, TicketStatus.PendingApproval, now);
        await ActivateAsync(t, 1, now, ct);
    }

    /// <summary>Makes a step's approvers pending: gives each a one-time link and tells them (in the app and by e-mail, which they always get).</summary>
    private async Task ActivateAsync(Ticket t, int stepNo, DateTimeOffset now, CancellationToken ct)
    {
        var rows = db.TicketApprovals.Local.Where(a => a.TicketId == t.Id && a.StepNo == stepNo).ToList(); // rows added in this unit of work are not in the database yet
        if (rows.Count == 0) rows = await db.TicketApprovals.Where(a => a.TicketId == t.Id && a.StepNo == stepNo).ToListAsync(ct);
        var requester = await db.Users.AsNoTracking().Where(u => u.Id == t.RequesterId).Select(u => u.Name).FirstOrDefaultAsync(ct) ?? "Someone";
        foreach (var a in rows)
        {
            if (a.Status == ApprovalStatus.Waiting)
            {
                a.Status = ApprovalStatus.Pending;
                // the person may have gone away since the request was made: their cover takes over now that it is their turn
                if (a.OnBehalfOfId is null && (await ActiveDelegatesAsync([a.ApproverId], now, ct)).TryGetValue(a.ApproverId, out var cover) && await CanTakeOverAsync(a, t, cover, ct)) { a.OnBehalfOfId = a.ApproverId; a.ApproverId = cover; }
            }
            await NotifyNeededAsync(a, t, requester, now, ct);
        }
    }

    /// <summary>A one-time link for this approval and the message that tells the approver (in the app and by e-mail, which they always get).</summary>
    private async Task NotifyNeededAsync(TicketApproval a, Ticket t, string requester, DateTimeOffset now, CancellationToken ct)
    {
        var raw = tokens.NewToken(); a.TokenHash = tokens.Hash(raw); a.TokenExpiresAt = now + LinkLifetime;
        var link = Url($"/approve?token={Uri.EscapeDataString(raw)}");
        var forName = a.OnBehalfOfId is { } f ? await db.Users.AsNoTracking().Where(u => u.Id == f).Select(u => u.Name).FirstOrDefaultAsync(ct) : null;
        var body = forName is null ? $"{requester}: {t.Subject}" : $"{requester}: {t.Subject} (for {forName})";
        await notifications.SendAsync(a.ApproverId, "approval.needed", $"Approval needed: {t.Number}", body, "/approvals", t.Id,
            (to, name) => EmailTemplates.ApprovalNeeded(to, name, requester, t.Number, t.Subject, t.ServiceName, link), alwaysMail: true, ct: ct);
    }

    /// <summary>Whether <paramref name="to"/> may take this approval over: an active person who is not the requester or creator and is not already an approver of the same step.</summary>
    private async Task<bool> CanTakeOverAsync(TicketApproval a, Ticket t, Guid to, CancellationToken ct)
        => to != t.RequesterId && to != t.CreatedById && to != a.ApproverId
           && await db.Users.AnyAsync(u => u.Id == to && u.Status == UserStatus.Active, ct)
           && !await db.TicketApprovals.AnyAsync(x => x.TicketId == a.TicketId && x.StepNo == a.StepNo && x.ApproverId == to, ct)
           && !db.TicketApprovals.Local.Any(x => x.TicketId == a.TicketId && x.StepNo == a.StepNo && x.ApproverId == to);

    // ================================================================ handing over (APR-004)
    /// <summary>Hands one waiting approval to someone else. The trail keeps the original approver; the old link stops working; the new approver is told.</summary>
    private async Task MoveAsync(TicketApproval a, Ticket t, Guid to, string actorName, Guid? actorId, DateTimeOffset now, CancellationToken ct)
    {
        var from = a.ApproverId;
        var names = await db.Users.AsNoTracking().Where(u => u.Id == from || u.Id == to || u.Id == t.RequesterId).ToDictionaryAsync(u => u.Id, u => u.Name, ct);
        a.OnBehalfOfId ??= from; a.ApproverId = to;
        AddEvent(t, actorName, actorId, $"Approval handed over from {names.GetValueOrDefault(from) ?? "?"} to {names.GetValueOrDefault(to) ?? "?"}.", null, null, now);
        t.UpdatedAt = now; t.Version++;
        await NotifyNeededAsync(a, t, names.GetValueOrDefault(t.RequesterId) ?? "Someone", now, ct);   // a fresh link: the old one is void
    }

    /// <summary>Someone (or an administrator for them) hands a waiting approval to another person.</summary>
    public async Task ReassignAsync(Guid approvalId, ReassignRequest req, SessionPrincipal who, bool canManage, ClientInfo client, CancellationToken ct = default)
    {
        var a = await db.TicketApprovals.FirstOrDefaultAsync(x => x.Id == approvalId && (x.ApproverId == who.UserId || canManage), ct) ?? throw DomainException.NotFound("Approval");
        if (a.Status != ApprovalStatus.Pending) throw DomainException.Conflict("Only an approval that is waiting for a decision can be handed over.");
        var t = await db.Tickets.FirstOrDefaultAsync(x => x.Id == a.TicketId, ct) ?? throw DomainException.NotFound("Ticket");
        if (t.Status != TicketStatus.PendingApproval) throw DomainException.Conflict("This request is no longer waiting for approval.");
        if (req.ToUserId is not { } to) throw DomainException.Validation("Choose who takes over.", new Dictionary<string, string> { ["toUserId"] = "Choose a person." });
        if (to == t.RequesterId || to == t.CreatedById) throw DomainException.Validation("Nobody can approve their own request.", new Dictionary<string, string> { ["toUserId"] = "This person made the request and cannot approve it." });
        if (!await CanTakeOverAsync(a, t, to, ct)) throw DomainException.Validation("This person cannot take it over.", new Dictionary<string, string> { ["toUserId"] = "Choose an active person who is not already an approver of this step." });
        var previous = a.ApproverId;
        await MoveAsync(a, t, to, who.Name, who.UserId, clock.UtcNow, ct);
        audit.Record("approval.reassign", "ticket", t.Id.ToString(), Outcome.Success, client, who.UserId, who.Email,
            new Dictionary<string, string> { ["number"] = t.Number, ["step"] = a.StepName, ["from"] = previous.ToString(), ["to"] = to.ToString() });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { throw DomainException.Conflict("This approval was changed by someone else. Reload and try again."); }
    }

    /// <summary>When a delegation that is already running is created, what is waiting for the person moves to their cover at once. Returns how many moved and how many had to stay.</summary>
    public async Task<(int Moved, int Kept)> HandOverPendingAsync(Guid fromUserId, Guid toUserId, string actorName, DateTimeOffset now, CancellationToken ct = default)
    {
        var rows = await db.TicketApprovals.Where(a => a.ApproverId == fromUserId && a.Status == ApprovalStatus.Pending).ToListAsync(ct);
        int moved = 0, kept = 0;
        foreach (var a in rows)
        {
            var t = await db.Tickets.FirstOrDefaultAsync(x => x.Id == a.TicketId, ct);
            if (t is null || t.Status != TicketStatus.PendingApproval || !await CanTakeOverAsync(a, t, toUserId, ct)) { kept++; continue; }
            await MoveAsync(a, t, toUserId, actorName, fromUserId, now, ct); moved++;
        }
        return (moved, kept);
    }

    private void AddEvent(Ticket t, string actor, Guid? actorId, string? body, TicketStatus? from, TicketStatus? to, DateTimeOffset now, TicketEventKind kind = TicketEventKind.Approval)
        => db.TicketEvents.Add(new TicketEvent { TenantId = t.TenantId, TicketId = t.Id, Kind = kind, ActorId = actorId, ActorLabel = actor, Body = body, FromStatus = from, ToStatus = to, CreatedAt = now });

    // ================================================================ deciding
    /// <summary>Decision made signed in, from the Approvals screen or the ticket.</summary>
    public async Task DecideAsync(Guid approvalId, DecideRequest req, SessionPrincipal who, ClientInfo client, CancellationToken ct = default)
    {
        var a = await db.TicketApprovals.FirstOrDefaultAsync(x => x.Id == approvalId && x.ApproverId == who.UserId, ct) ?? throw DomainException.NotFound("Approval"); // someone else's is "not found"
        await ApplyAsync(a, req, who.UserId, who.Name, who.Email, "app", client, ct);
    }

    private async Task ApplyAsync(TicketApproval a, DecideRequest req, Guid userId, string name, string? emailAddr, string via, ClientInfo client, CancellationToken ct)
    {
        if (a.Status != ApprovalStatus.Pending) throw DomainException.Conflict(a.Status is ApprovalStatus.Approved or ApprovalStatus.Rejected ? "You have already decided this." : "This approval is no longer needed.");
        var comment = string.IsNullOrWhiteSpace(req.Comment) ? null : req.Comment.Trim();
        if (comment is { Length: > 1000 }) throw DomainException.Validation("Please correct the highlighted fields.", new Dictionary<string, string> { ["comment"] = "Use at most 1000 characters." });
        if (!req.Approve && (comment is null || comment.Length < 3)) throw DomainException.Validation("Please say why.", new Dictionary<string, string> { ["comment"] = "A rejection needs a comment of at least 3 characters." });

        var t = await db.Tickets.FirstOrDefaultAsync(x => x.Id == a.TicketId, ct) ?? throw DomainException.NotFound("Ticket");
        if (t.Status != TicketStatus.PendingApproval) throw DomainException.Conflict("This request is no longer waiting for approval.");
        if (a.ApproverId == t.RequesterId || a.ApproverId == t.CreatedById) throw DomainException.DeniedBecause("Nobody can approve their own request.");

        var now = clock.UtcNow;
        a.Status = req.Approve ? ApprovalStatus.Approved : ApprovalStatus.Rejected; a.Comment = comment; a.DecidedAt = now; a.DecidedVia = via;
        a.Ip = client.Ip; a.Device = client.UserAgent is null ? null : DeviceInfo.Describe(client.UserAgent) is var dev ? $"{dev.Device} · {dev.Browser}" : null;
        a.TokenHash = null; a.TokenExpiresAt = null; // a link works once
        AddEvent(t, name, userId, $"{(req.Approve ? "Approved" : "Rejected")}{(comment is null ? "" : $": {comment}")}", null, null, now);

        var step = await db.TicketApprovals.Where(x => x.TicketId == t.Id && x.StepNo == a.StepNo).ToListAsync(ct);
        var outcome = ApprovalEngine.Evaluate(a.Rule, [.. step.Select(x => x.Status)]);
        var requesterId = t.RequesterId;
        if (outcome == StepOutcome.Rejected)
        {
            foreach (var x in await db.TicketApprovals.Where(x => x.TicketId == t.Id && x.Id != a.Id && (x.Status == ApprovalStatus.Pending || x.Status == ApprovalStatus.Waiting)).ToListAsync(ct)) { x.Status = ApprovalStatus.Cancelled; x.TokenHash = null; x.TokenExpiresAt = null; } // everyone else is released (the rejecting row is already decided)
            var from = t.Status;
            t.Status = TicketStatus.Rejected; t.CancelReason = comment; t.CancelledAt = now; t.UpdatedAt = now; t.Version++;
            AddEvent(t, "System", null, comment, from, TicketStatus.Rejected, now, TicketEventKind.Status);
            await sla.RefreshAsync(t, now, ct);
            await notifications.SendAsync(requesterId, "approval.rejected", $"{t.Number} was rejected", comment, $"/tickets/{t.Id}", t.Id, (to, n) => EmailTemplates.ApprovalDecided(to, n, false, t.Number, t.Subject, comment, Url($"/tickets/{t.Id}")), ct: ct);
        }
        else if (outcome == StepOutcome.Approved)
        {
            foreach (var x in step.Where(x => x.Status == ApprovalStatus.Pending)) { x.Status = ApprovalStatus.Skipped; x.TokenHash = null; x.TokenExpiresAt = null; } // an "any" step is settled
            var next = await db.TicketApprovals.AnyAsync(x => x.TicketId == t.Id && x.StepNo == a.StepNo + 1, ct);
            if (next)
            {
                t.UpdatedAt = now; t.Version++;
                AddEvent(t, "System", null, $"Waiting for approval: {(await db.TicketApprovals.Where(x => x.TicketId == t.Id && x.StepNo == a.StepNo + 1).Select(x => x.StepName).FirstAsync(ct))}.", null, null, now);
                await ActivateAsync(t, a.StepNo + 1, now, ct);
            }
            else await ReleaseAsync(t, now, ct);
        }
        else { t.UpdatedAt = now; t.Version++; } // still waiting for the others of an "all" step

        var meta = new Dictionary<string, string> { ["number"] = t.Number, ["step"] = a.StepName, ["via"] = via };
        if (a.OnBehalfOfId is { } onBehalf) meta["onBehalfOf"] = onBehalf.ToString();
        audit.Record(req.Approve ? "approval.approve" : "approval.reject", "ticket", t.Id.ToString(), Outcome.Success, client, userId, emailAddr ?? name, meta, [new AuditDiff("approval", "pending", req.Approve ? "approved" : "rejected")]);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw DomainException.Conflict("This request was changed by someone else. Reload and try again."); }
        catch (DbUpdateException) { throw DomainException.Conflict("This request was changed by someone else. Reload and try again."); }
    }

    /// <summary>The last step is approved: the request becomes an ordinary submitted ticket, is routed, and its SLA clock restarts (the wait does not count against the team).</summary>
    private async Task ReleaseAsync(Ticket t, DateTimeOffset now, CancellationToken ct)
    {
        var from = t.Status;
        t.Status = TicketStatus.Submitted; t.UpdatedAt = now; t.Version++;
        AddEvent(t, "System", null, "Approved. The request is released to the team.", from, TicketStatus.Submitted, now, TicketEventKind.Status);
        var service = await db.Services.AsNoTracking().Where(s => s.Id == t.ServiceId).Select(s => s.OwningTeamId).FirstOrDefaultAsync(ct);
        await routing.ApplyRoutingAsync(t, service, now, ct);
        await sla.ResumeAsync(t, now, ct);
        await notifications.SendAsync(t.RequesterId, "approval.approved", $"{t.Number} was approved", t.Subject, $"/tickets/{t.Id}", t.Id,
            (to, n) => EmailTemplates.ApprovalDecided(to, n, true, t.Number, t.Subject, null, Url($"/tickets/{t.Id}")), ct: ct);
        if (t.AssigneeId is { } who) await notifications.AssignedAsync(t, who, null, ct);
    }

    /// <summary>The requester withdrew a request that was still waiting: nobody needs to decide any more.</summary>
    public async Task CancelPendingAsync(Ticket t, CancellationToken ct = default)
    {
        foreach (var x in await db.TicketApprovals.Where(x => x.TicketId == t.Id && (x.Status == ApprovalStatus.Pending || x.Status == ApprovalStatus.Waiting)).ToListAsync(ct)) { x.Status = ApprovalStatus.Cancelled; x.TokenHash = null; x.TokenExpiresAt = null; }
    }

    // ================================================================ one-time e-mail link
    private async Task<TicketApproval> ByTokenAsync(string? token, ClientInfo client, CancellationToken ct)
    {
        var rate = await limiter.HitAsync($"approval-link:{client.Ip ?? "unknown"}", 30, TimeSpan.FromMinutes(1), ct);
        if (!rate.Allowed) throw DomainException.RateLimited(rate.RetryAfter);
        const string gone = "This link has expired or was already used.";
        if (string.IsNullOrWhiteSpace(token) || token.Length > 200) throw DomainException.Expired(gone);
        var hash = tokens.Hash(token);
        var a = await db.TicketApprovals.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.TokenHash == hash, ct);
        if (a is null || a.Status != ApprovalStatus.Pending || a.TokenExpiresAt is not { } exp || exp <= clock.UtcNow) throw DomainException.Expired(gone); // unknown, used and expired look the same
        return a;
    }

    /// <summary>What the person sees before deciding. Reading never decides (mail scanners open links).</summary>
    public async Task<ApprovalLinkInfo> LookupAsync(string? token, ClientInfo client, CancellationToken ct = default)
    {
        var a = await ByTokenAsync(token, client, ct);
        using (tenant.Switch(a.TenantId))
        {
            var t = await db.Tickets.AsNoTracking().FirstAsync(x => x.Id == a.TicketId, ct);
            var requester = await db.Users.AsNoTracking().Where(u => u.Id == t.RequesterId).Select(u => u.Name).FirstOrDefaultAsync(ct);
            var approver = await db.Users.AsNoTracking().Where(u => u.Id == a.ApproverId).Select(u => u.Name).FirstAsync(ct);
            return new ApprovalLinkInfo(t.Number, t.Subject, t.Description, t.ServiceName, requester, a.StepName, approver, a.TokenExpiresAt!.Value);
        }
    }

    public async Task<ApprovalLinkResult> DecideByLinkAsync(string? token, DecideRequest req, ClientInfo client, CancellationToken ct = default)
    {
        var a = await ByTokenAsync(token, client, ct);
        using (tenant.Switch(a.TenantId))
        {
            db.ResetTracking();
            var tracked = await db.TicketApprovals.FirstAsync(x => x.Id == a.Id, ct);
            var user = await db.Users.AsNoTracking().Where(u => u.Id == tracked.ApproverId && u.Status == UserStatus.Active).Select(u => new { u.Name, u.Email }).FirstOrDefaultAsync(ct)
                ?? throw DomainException.Expired("This link has expired or was already used.");
            await ApplyAsync(tracked, req, tracked.ApproverId, user.Name, user.Email, "email", client, ct);
            var number = await db.Tickets.AsNoTracking().Where(x => x.Id == tracked.TicketId).Select(x => x.Number).FirstAsync(ct);
            return new ApprovalLinkResult(req.Approve ? "approved" : "rejected", number);
        }
    }

    // ================================================================ reading
    public async Task<IReadOnlyList<MyApprovalItem>> MineAsync(Guid userId, CancellationToken ct = default)
    {
        var rows = await db.TicketApprovals.AsNoTracking().Where(a => a.ApproverId == userId && (a.Status == ApprovalStatus.Pending || a.DecidedAt != null))
            .OrderBy(a => a.Status == ApprovalStatus.Pending ? 0 : 1).ThenByDescending(a => a.DecidedAt ?? a.CreatedAt).Take(100)
            .Select(a => new
            {
                a.Id, a.TicketId, a.StepNo, a.StepName, a.Status, a.Comment, a.CreatedAt, a.DecidedAt, For = db.Users.Where(u => u.Id == a.OnBehalfOfId).Select(u => u.Name).FirstOrDefault(),
                Ticket = db.Tickets.Where(t => t.Id == a.TicketId).Select(t => new { t.Number, t.Subject, t.ServiceName, t.RequesterId }).FirstOrDefault(),
            }).ToListAsync(ct);
        var ids = rows.Select(r => r.Ticket?.RequesterId).Where(i => i != null).Select(i => i!.Value).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Name, ct);
        return [.. rows.Where(r => r.Ticket is not null).Select(r => new MyApprovalItem(r.Id, r.TicketId, r.Ticket!.Number, r.Ticket.Subject, r.Ticket.ServiceName, names.GetValueOrDefault(r.Ticket.RequesterId), r.StepNo, r.StepName, Lower(r.Status), r.Comment, r.CreatedAt, r.DecidedAt, r.For))];
    }

    public async Task<int> PendingCountAsync(Guid userId, CancellationToken ct = default) => await db.TicketApprovals.CountAsync(a => a.ApproverId == userId && a.Status == ApprovalStatus.Pending, ct);

    public async Task<IReadOnlyList<TicketApprovalItem>> ForTicketAsync(Guid ticketId, CancellationToken ct = default)
    {
        var rows = await db.TicketApprovals.AsNoTracking().Where(a => a.TicketId == ticketId)
            .Select(a => new { a.Id, a.StepNo, a.StepName, a.Rule, a.ApproverId, a.Status, a.Comment, a.DecidedAt, a.DecidedVia, Name = db.Users.Where(u => u.Id == a.ApproverId).Select(u => u.Name).FirstOrDefault(), For = db.Users.Where(u => u.Id == a.OnBehalfOfId).Select(u => u.Name).FirstOrDefault() }).ToListAsync(ct);
        return [.. rows.OrderBy(r => r.StepNo).ThenBy(r => r.Name).Select(r => new TicketApprovalItem(r.Id, r.StepNo, r.StepName, r.Rule, r.ApproverId, r.Name ?? "?", Lower(r.Status), r.Comment, r.DecidedAt, r.DecidedVia, r.For))];
    }
}
