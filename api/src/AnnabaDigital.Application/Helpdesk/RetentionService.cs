using System.Linq.Expressions;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Application.Users;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;

namespace AnnabaDigital.Application.Helpdesk;

public sealed record RetentionLastRun(DateTimeOffset At, int Tickets, int Files, int Notifications, int FilesFailed = 0);
public sealed record RetentionView(bool Enabled, int? FinishedTicketDays, string TicketAction, int? AttachmentDays, int? NotificationDays, int AuditDays, int Holds, DateTimeOffset? UpdatedAt, RetentionLastRun? LastRun);
public sealed record SaveRetentionRequest(bool Enabled, int? FinishedTicketDays, string? TicketAction, int? AttachmentDays, int? NotificationDays);
/// <summary>What a policy would remove if it ran now: tickets (anonymised or deleted), tickets spared by a legal hold, files and their size, notifications.</summary>
public sealed record RetentionPreview(int Tickets, int HeldTickets, int Files, long FileBytes, int Notifications);
public sealed record RetentionRunResult(int Tickets, int Files, int Notifications, bool More, int FilesFailed = 0);
public sealed record LegalHoldItem(Guid Id, Guid TicketId, string Number, string Subject, string Status, string Reason, string? PlacedBy, DateTimeOffset PlacedAt);
public sealed record PlaceHoldRequest(string? Reason);
/// <summary>What anyone signed in may read about how long their organisation keeps their data.</summary>
public sealed record RetentionNotice(bool Enabled, int? FinishedTicketDays, string TicketAction, int? AttachmentDays, int? NotificationDays, int AuditDays);

/// <summary>
/// ADM-006. Retention periods for tickets, files and notifications, legal holds, and the clean-up that applies them. Nothing is removed until an administrator switches the policy on
/// (and every run is audited with counts). Only <i>finished</i> tickets are aged, from the day they finished; a ticket under legal hold is never touched; the bytes of a file are
/// removed after the database says it is gone, so a crash at worst leaves an unreferenced file and never a reference to a missing one. Safe with several instances: every step only
/// touches rows that still match its conditions.
/// </summary>
public sealed class RetentionService(IAppDb db, IClock clock, AuditWriter audit, SettingsProvider settings, IFileStorage storage, ITenantContext tenant)
{
    public const int Batch = 200, MaxTicketsPerRun = 5000, MaxFilesPerRun = 5000, MaxFileFailures = 100, MaxNotificationBatches = 50, NotificationBatch = 2000;
    public const int ReasonMin = 3, ReasonMax = 300;

    private static readonly Expression<Func<Ticket, bool>> IsFinished = t => t.Status == TicketStatus.Closed || t.Status == TicketStatus.Cancelled || t.Status == TicketStatus.Rejected;

    // ================================================================ the policy
    public async Task<RetentionView> GetAsync(CancellationToken ct = default)
    {
        var p = await db.RetentionPolicies.AsNoTracking().FirstOrDefaultAsync(ct);
        var holds = await db.TicketLegalHolds.CountAsync(ct);
        var auditDays = (await settings.GetAsync(ct)).AuditRetentionDays;
        if (p is null) return new RetentionView(false, null, RetentionRules.Anonymise, null, null, auditDays, holds, null, null);
        return new RetentionView(p.Enabled, p.FinishedTicketDays, p.TicketAction, p.AttachmentDays, p.NotificationDays, auditDays, holds, p.UpdatedAt, LastRunOf(p));
    }

    private static RetentionLastRun? LastRunOf(RetentionPolicy p)
    {
        if (p.LastRunAt is not { } at) return null;
        try
        {
            using var d = JsonDocument.Parse(p.LastRunJson ?? "{}"); var r = d.RootElement;
            int N(string k) => r.TryGetProperty(k, out var v) && v.TryGetInt32(out var i) ? i : 0;
            return new RetentionLastRun(at, N("tickets"), N("files"), N("notifications"), N("filesFailed"));
        }
        catch (JsonException) { return new RetentionLastRun(at, 0, 0, 0); }
    }

    public async Task<RetentionNotice> NoticeAsync(CancellationToken ct = default)
    {
        var v = await GetAsync(ct);
        return new RetentionNotice(v.Enabled, v.FinishedTicketDays, v.TicketAction, v.AttachmentDays, v.NotificationDays, v.AuditDays);
    }

    public async Task<RetentionView> UpdateAsync(SaveRetentionRequest req, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        Guards.RequireRecentReauthForCritical(actor, await settings.GetAsync(ct), clock); // it decides what is destroyed, so a fresh password check
        var action = RetentionRules.Normalise(req.TicketAction);
        var f = RetentionRules.Validate(req.Enabled, req.FinishedTicketDays, action, req.AttachmentDays, req.NotificationDays);
        if (f.Count > 0) throw DomainException.Validation("Please correct the highlighted fields.", f);

        var now = clock.UtcNow;
        var p = await db.RetentionPolicies.FirstOrDefaultAsync(ct);
        var before = p is null ? "off" : Describe(p.Enabled, p.FinishedTicketDays, p.TicketAction, p.AttachmentDays, p.NotificationDays);
        if (p is null) { p = new RetentionPolicy { TenantId = tenant.TenantId }; db.RetentionPolicies.Add(p); }
        p.Enabled = req.Enabled; p.FinishedTicketDays = req.FinishedTicketDays; p.TicketAction = action; p.AttachmentDays = req.AttachmentDays; p.NotificationDays = req.NotificationDays;
        p.UpdatedAt = now; p.UpdatedBy = actor.UserId;
        var after = Describe(p.Enabled, p.FinishedTicketDays, p.TicketAction, p.AttachmentDays, p.NotificationDays);
        audit.Record("retention.policy.update", "tenant", tenant.TenantId.ToString(), Outcome.Success, client, actor.UserId, actor.Email, diff: [new AuditDiff("policy", before, after)]);
        audit.Security("retention.change", Severity.Medium, $"Retention policy changed: {after}", actor.UserId);
        await db.SaveChangesAsync(ct);
        return await GetAsync(ct);
    }

    private static string Describe(bool enabled, int? tickets, string action, int? files, int? notifications)
        => $"{(enabled ? "on" : "off")}; tickets {(tickets is { } t ? $"{action} after {t} days" : "kept")}; files {(files is { } a ? $"removed after {a} days" : "kept")}; notifications {(notifications is { } n ? $"removed after {n} days" : "kept")}";

    // ================================================================ what is in scope
    private IQueryable<Ticket> Finished(int days, DateTimeOffset now)
    {
        var cutoff = now.AddDays(-days);
        return db.Tickets.Where(IsFinished).Where(t => t.UpdatedAt < cutoff);
    }

    /// <summary>The finished tickets that the ticket period applies to and that no hold protects.</summary>
    private IQueryable<Ticket> TicketsDue(int days, string action, DateTimeOffset now)
    {
        var q = Finished(days, now).Where(t => !db.TicketLegalHolds.Any(h => h.TicketId == t.Id));
        return action == RetentionRules.Anonymise ? q.Where(t => t.AnonymisedAt == null) : q;
    }

    /// <summary>A file goes when the file period is over — or the ticket period, whichever comes first (the ticket period is never shorter than the file period, so this is the later cutoff).</summary>
    private IQueryable<Attachment> FilesDue(int? fileDays, int? ticketDays, DateTimeOffset now)
    {
        var days = new[] { fileDays, ticketDays }.Where(d => d.HasValue).Select(d => d!.Value).DefaultIfEmpty(-1).Min();
        if (days < 0) return db.Attachments.Where(_ => false);
        var cutoff = now.AddDays(-days);
        return db.Attachments.Where(a => a.DeletedAt == null && a.TicketId != null
            && db.Tickets.Where(IsFinished).Any(t => t.Id == a.TicketId && t.UpdatedAt < cutoff)
            && !db.TicketLegalHolds.Any(h => h.TicketId == a.TicketId));
    }

    public async Task<RetentionPreview> PreviewAsync(SaveRetentionRequest req, CancellationToken ct = default)
    {
        var action = RetentionRules.Normalise(req.TicketAction);
        var f = RetentionRules.Validate(false, req.FinishedTicketDays, action, req.AttachmentDays, req.NotificationDays);
        if (f.Count > 0) throw DomainException.Validation("Please correct the highlighted fields.", f);
        var now = clock.UtcNow;
        var tickets = req.FinishedTicketDays is { } td ? await TicketsDue(td, action, now).CountAsync(ct) : 0;
        var held = req.FinishedTicketDays is { } hd ? await Finished(hd, now).Where(t => (action == RetentionRules.Delete || t.AnonymisedAt == null) && db.TicketLegalHolds.Any(h => h.TicketId == t.Id)).CountAsync(ct) : 0;
        var files = FilesDue(req.AttachmentDays, req.FinishedTicketDays, now);
        var fileCount = await files.CountAsync(ct); var bytes = fileCount == 0 ? 0 : await files.SumAsync(a => a.Size, ct);
        var notes = req.NotificationDays is { } nd ? await db.Notifications.CountAsync(n => n.CreatedAt < now.AddDays(-nd), ct) : 0;
        return new RetentionPreview(tickets, held, fileCount, bytes, notes);
    }

    // ================================================================ running it
    /// <summary>An administrator runs the clean-up now for their own organisation (the policy must be on).</summary>
    public async Task<RetentionRunResult> RunNowAsync(SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        Guards.RequireRecentReauthForCritical(actor, await settings.GetAsync(ct), clock);
        var p = await db.RetentionPolicies.FirstOrDefaultAsync(ct);
        if (p is null || !p.Enabled) throw DomainException.Conflict("The retention policy is switched off. Switch it on first.");
        return await RunPolicyAsync(p, actor.UserId, actor.Email, client, "manual", ct);
    }

    /// <summary>The scheduled run: every organisation whose policy is on, each as itself. Returns what was removed in total.</summary>
    public async Task<RetentionRunResult> RunDueAsync(CancellationToken ct = default)
    {
        var tenants = await db.RetentionPolicies.IgnoreQueryFilters().AsNoTracking().Where(p => p.Enabled).Select(p => p.TenantId).ToListAsync(ct);
        int tickets = 0, files = 0, notes = 0, failed = 0; var more = false;
        foreach (var tid in tenants)
        {
            using (tenant.Switch(tid))
            {
                db.ResetTracking();
                var p = await db.RetentionPolicies.FirstOrDefaultAsync(x => x.Enabled, ct);
                if (p is null) continue;
                var r = await RunPolicyAsync(p, null, "system (retention)", new ClientInfo(null, null, null), "schedule", ct);
                tickets += r.Tickets; files += r.Files; notes += r.Notifications; failed += r.FilesFailed; more |= r.More;
            }
        }
        return new RetentionRunResult(tickets, files, notes, more, failed);
    }

    private async Task<RetentionRunResult> RunPolicyAsync(RetentionPolicy p, Guid? actorId, string actorLabel, ClientInfo client, string trigger, CancellationToken ct)
    {
        var now = clock.UtcNow; var policyId = p.Id; var action = p.TicketAction;
        var (files, failed) = await RemoveFilesAsync(p, now, ct);       // the steps reset change tracking between batches, so the policy row is updated with a statement, not through the tracker
        var tickets = await RemoveTicketsAsync(p, now, ct);
        var notes = await RemoveNotificationsAsync(p, now, ct);
        var more = files >= MaxFilesPerRun || tickets >= MaxTicketsPerRun;

        var json = JsonSerializer.Serialize(new { tickets, files, notifications = notes, filesFailed = failed });
        await db.RetentionPolicies.Where(x => x.Id == policyId).ExecuteUpdateAsync(s => s.SetProperty(x => x.LastRunAt, now).SetProperty(x => x.LastRunJson, json), ct);
        if (tickets + files + notes + failed > 0 || trigger == "manual")
            audit.Record("retention.run", "tenant", tenant.TenantId.ToString(), failed > 0 ? Outcome.Failure : Outcome.Success, client, actorId, actorLabel,
                new Dictionary<string, string> { ["trigger"] = trigger, ["action"] = action, ["tickets"] = tickets.ToString(), ["files"] = files.ToString(), ["notifications"] = notes.ToString(), ["filesFailed"] = failed.ToString(), ["more"] = more.ToString().ToLowerInvariant() });
        if (failed > 0) audit.Security("retention.files_failed", Severity.Medium, $"{failed} file(s) could not be removed by the retention policy; they are retried at the next run.", null);
        await db.SaveChangesAsync(ct);
        return new RetentionRunResult(tickets, files, notes, more, failed);
    }

    /// <summary>
    /// Files first: drop the bytes, and only then mark the row removed, so a file that cannot be deleted (a locked or unreadable volume) is never reported as gone and is tried again next
    /// run. A crash between the two steps leaves a row pointing at nothing (served as "not found"), never bytes nobody knows about.
    /// </summary>
    private async Task<(int Removed, int Failed)> RemoveFilesAsync(RetentionPolicy p, DateTimeOffset now, CancellationToken ct)
    {
        if (p.AttachmentDays is null && p.FinishedTicketDays is null) return (0, 0);
        var removed = 0; var failed = new HashSet<Guid>();
        while (removed < MaxFilesPerRun && failed.Count < MaxFileFailures)
        {
            db.ResetTracking();
            var skip = failed.ToList();
            var rows = await FilesDue(p.AttachmentDays, p.FinishedTicketDays, now).Where(a => !skip.Contains(a.Id)).OrderBy(a => a.CreatedAt).ThenBy(a => a.Id).Take(Batch).ToListAsync(ct);
            if (rows.Count == 0) break;
            var gone = new List<Attachment>();
            foreach (var a in rows)
            {
                try { await storage.DeleteAsync(a.StorageKey, ct); gone.Add(a); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { failed.Add(a.Id); }
            }
            foreach (var a in gone) a.DeletedAt = now;
            if (gone.Count > 0) await db.SaveChangesAsync(ct);
            removed += gone.Count;
        }
        return (removed, failed.Count);
    }

    private async Task<int> RemoveTicketsAsync(RetentionPolicy p, DateTimeOffset now, CancellationToken ct)
    {
        if (p.FinishedTicketDays is not { } days) return 0;
        var total = 0;
        while (total < MaxTicketsPerRun)
        {
            db.ResetTracking();
            // a ticket whose files could not be removed waits: its content goes only together with its files
            var ids = await TicketsDue(days, p.TicketAction, now).Where(t => !db.Attachments.Any(a => a.TicketId == t.Id && a.DeletedAt == null)).OrderBy(t => t.UpdatedAt).ThenBy(t => t.Id).Select(t => t.Id).Take(Batch).ToListAsync(ct);
            if (ids.Count == 0) break;
            var done = p.TicketAction == RetentionRules.Delete ? await DeleteBatchAsync(ids, now, ct) : await AnonymiseBatchAsync(ids, now, ct);
            if (done == 0) break; // nothing in this batch could be changed (it changed under us): leave it to the next run
            total += done;
        }
        return total;
    }

    /// <summary>Removes everything a person wrote and keeps the ticket as a statistic. Returns how many tickets it changed.</summary>
    private async Task<int> AnonymiseBatchAsync(List<Guid> ids, DateTimeOffset now, CancellationToken ct)
    {
        var keys = new List<string>(); var changed = 0;
        await db.InTransactionAsync(async () =>
        {
            // the conditions are repeated here: a ticket reopened or put under hold since it was selected is left alone
            changed = await db.Tickets.Where(t => ids.Contains(t.Id) && t.AnonymisedAt == null && !db.TicketLegalHolds.Any(h => h.TicketId == t.Id))
                .Where(IsFinished)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.Subject, RetentionRules.RemovedSubject).SetProperty(t => t.Description, (string?)null).SetProperty(t => t.AnswersJson, "{}")
                    .SetProperty(t => t.ResolutionNote, (string?)null).SetProperty(t => t.HoldReason, (string?)null).SetProperty(t => t.CancelReason, (string?)null).SetProperty(t => t.AnonymisedAt, now), ct);
            if (changed == 0) return 0;
            var done = await db.Tickets.Where(t => ids.Contains(t.Id) && t.AnonymisedAt != null).Select(t => t.Id).ToListAsync(ct);
            await db.TicketEvents.Where(e => done.Contains(e.TicketId))
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.Body, (string?)null).SetProperty(e => e.ActorLabel, e => e.ActorLabel == "System" ? "System" : RetentionRules.RemovedActor), ct);
            await db.TicketApprovals.Where(a => done.Contains(a.TicketId))
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.Comment, (string?)null).SetProperty(a => a.Ip, (string?)null).SetProperty(a => a.Device, (string?)null).SetProperty(a => a.TokenHash, (string?)null).SetProperty(a => a.TokenExpiresAt, (DateTimeOffset?)null), ct);
            await db.Notifications.Where(n => n.TicketId != null && done.Contains(n.TicketId.Value)).ExecuteDeleteAsync(ct);
            keys = await db.Attachments.Where(a => a.TicketId != null && done.Contains(a.TicketId.Value) && a.DeletedAt == null).Select(a => a.StorageKey).ToListAsync(ct);
            await db.Attachments.Where(a => a.TicketId != null && done.Contains(a.TicketId.Value) && a.DeletedAt == null).ExecuteUpdateAsync(s => s.SetProperty(a => a.DeletedAt, now), ct);
            return 0;
        }, ct);
        foreach (var k in keys) await TryDeleteAsync(k, ct);
        return changed;
    }

    /// <summary>Removes the tickets with their timeline, approvals, notifications and file rows (the database cascades); then the bytes of their files.</summary>
    private async Task<int> DeleteBatchAsync(List<Guid> ids, DateTimeOffset now, CancellationToken ct)
    {
        var deleted = 0; List<(Guid TicketId, string Key)> files = [];
        await db.InTransactionAsync(async () =>
        {
            var pairs = await db.Attachments.Where(a => a.TicketId != null && ids.Contains(a.TicketId.Value) && a.DeletedAt == null).Select(a => new { TicketId = a.TicketId!.Value, a.StorageKey }).ToListAsync(ct);
            files = [.. pairs.Select(x => (x.TicketId, x.StorageKey))];
            deleted = await db.Tickets.Where(t => ids.Contains(t.Id) && !db.TicketLegalHolds.Any(h => h.TicketId == t.Id)).Where(IsFinished).ExecuteDeleteAsync(ct);
            return 0;
        }, ct);
        if (files.Count > 0)
        {
            var still = (await db.Tickets.Where(t => ids.Contains(t.Id)).Select(t => t.Id).ToListAsync(ct)).ToHashSet();
            foreach (var f in files.Where(f => !still.Contains(f.TicketId))) await TryDeleteAsync(f.Key, ct);
        }
        return deleted;
    }

    /// <summary>For the rare file that appeared between the file step and the ticket step: a failure leaves the bytes (their row is already gone or marked), which the next file sweep cannot see, so it is only tolerated here.</summary>
    private async Task TryDeleteAsync(string key, CancellationToken ct)
    {
        try { await storage.DeleteAsync(key, ct); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* the volume refused; nothing more can be done from here */ }
    }

    private async Task<int> RemoveNotificationsAsync(RetentionPolicy p, DateTimeOffset now, CancellationToken ct)
    {
        if (p.NotificationDays is not { } days) return 0;
        var cutoff = now.AddDays(-days); var total = 0;
        for (var i = 0; i < MaxNotificationBatches; i++)
        {
            var ids = await db.Notifications.Where(n => n.CreatedAt < cutoff).Select(n => n.Id).Take(NotificationBatch).ToListAsync(ct);
            if (ids.Count == 0) break;
            total += await db.Notifications.Where(n => ids.Contains(n.Id)).ExecuteDeleteAsync(ct);
        }
        return total;
    }

    // ================================================================ legal holds
    public async Task<PagedResult<LegalHoldItem>> HoldsAsync(int page, int pageSize, CancellationToken ct = default)
    {
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100);
        var total = await db.TicketLegalHolds.CountAsync(ct);
        var rows = await db.TicketLegalHolds.AsNoTracking().OrderByDescending(h => h.PlacedAt).ThenByDescending(h => h.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(h => new
            {
                h.Id, h.TicketId, h.Reason, h.PlacedAt, h.PlacedById,
                Number = db.Tickets.Where(t => t.Id == h.TicketId).Select(t => t.Number).FirstOrDefault(),
                Subject = db.Tickets.Where(t => t.Id == h.TicketId).Select(t => t.Subject).FirstOrDefault(),
                Status = db.Tickets.Where(t => t.Id == h.TicketId).Select(t => t.Status).FirstOrDefault(),
                By = db.Users.Where(u => u.Id == h.PlacedById).Select(u => u.Name).FirstOrDefault(),
            }).ToListAsync(ct);
        return new PagedResult<LegalHoldItem>([.. rows.Select(r => new LegalHoldItem(r.Id, r.TicketId, r.Number ?? "", r.Subject ?? "", r.Status.ToString().ToLowerInvariant(), r.Reason, r.By, r.PlacedAt))], page, pageSize, total);
    }

    public async Task PlaceHoldAsync(Guid ticketId, PlaceHoldRequest req, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        Guards.RequireRecentReauthForCritical(actor, await settings.GetAsync(ct), clock);
        var reason = (req.Reason ?? "").Trim();
        if (reason.Length < ReasonMin || reason.Length > ReasonMax) throw DomainException.Validation("Give a reason.", new Dictionary<string, string> { ["reason"] = $"Use {ReasonMin}–{ReasonMax} characters." });
        var number = await db.Tickets.AsNoTracking().Where(t => t.Id == ticketId).Select(t => t.Number).FirstOrDefaultAsync(ct) ?? throw DomainException.NotFound("Ticket");
        if (await db.TicketLegalHolds.AnyAsync(h => h.TicketId == ticketId, ct)) throw DomainException.Conflict("This ticket is already under legal hold.");
        db.TicketLegalHolds.Add(new TicketLegalHold { TenantId = tenant.TenantId, TicketId = ticketId, Reason = reason, PlacedById = actor.UserId, PlacedAt = clock.UtcNow });
        audit.Record("retention.hold.place", "ticket", ticketId.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["number"] = number });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { throw DomainException.Conflict("This ticket is already under legal hold."); } // lost a race
    }

    public async Task ReleaseHoldAsync(Guid ticketId, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        Guards.RequireRecentReauthForCritical(actor, await settings.GetAsync(ct), clock);
        var number = await db.Tickets.AsNoTracking().Where(t => t.Id == ticketId).Select(t => t.Number).FirstOrDefaultAsync(ct) ?? throw DomainException.NotFound("Ticket");
        var n = await db.TicketLegalHolds.Where(h => h.TicketId == ticketId).ExecuteDeleteAsync(ct);
        if (n == 0) throw DomainException.Conflict("This ticket is not under legal hold.");
        audit.Record("retention.hold.release", "ticket", ticketId.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["number"] = number });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>The hold on one ticket, for the ticket page of someone who may manage holds.</summary>
    public async Task<LegalHoldInfo?> HoldOfAsync(Guid ticketId, CancellationToken ct = default)
        => await db.TicketLegalHolds.AsNoTracking().Where(h => h.TicketId == ticketId)
            .Select(h => new LegalHoldInfo(h.Reason, h.PlacedAt, db.Users.Where(u => u.Id == h.PlacedById).Select(u => u.Name).FirstOrDefault())).FirstOrDefaultAsync(ct);

    public Task<bool> IsHeldAsync(Guid ticketId, CancellationToken ct = default) => db.TicketLegalHolds.AnyAsync(h => h.TicketId == ticketId, ct);
}

public sealed record LegalHoldInfo(string Reason, DateTimeOffset PlacedAt, string? PlacedBy);
