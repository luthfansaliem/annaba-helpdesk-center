using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Application.Users;

public sealed record StartAccessReview(string? Name, int? DueInDays);
public sealed record ReviewDecisionRequest(string? Decision, string? Note);
public sealed record ReviewScheduleView(bool Enabled, int EveryDays, int DueInDays, DateTimeOffset? NextRunAt, DateTimeOffset? LastRunAt);
public sealed record SaveReviewSchedule(bool? Enabled, int? EveryDays, int? DueInDays);
public sealed record AccessReviewSummary(Guid Id, string Name, string Status, DateTimeOffset CreatedAt, DateTimeOffset DueAt, DateTimeOffset? ClosedAt, string? CreatedByName,
    int Total, int Kept, int Revoked, int Pending, bool Overdue);
public sealed record AccessReviewItemView(Guid Id, Guid UserId, string UserName, string UserEmail, RoleRef Role, string Decision, string? ReviewerName, DateTimeOffset? DecidedAt, string? Note);

/// <summary>
/// USR-008. A review snapshots every active user's privileged roles (roles with at least one permission); reviewers keep or revoke each one. Revoking applies
/// at once through the normal role-assignment path (re-authentication, hierarchy rule, audit). Nobody reviews their own access, and one review is open at a time.
/// </summary>
public sealed class AccessReviewService(IAppDb db, IClock clock, UserAdminService users, ApprovalService approvals, ITenantContext tenant, AuditWriter audit)
{
    public const int MaxNote = 500;

    public async Task<List<AccessReviewSummary>> ListAsync(CancellationToken ct = default)
    {
        var rows = await db.AccessReviews.AsNoTracking().OrderByDescending(r => r.CreatedAt).Take(50).ToListAsync(ct);
        var ids = rows.Select(r => r.Id).ToList();
        var counts = await db.AccessReviewItems.AsNoTracking().Where(i => ids.Contains(i.ReviewId)).GroupBy(i => new { i.ReviewId, i.Decision }).Select(g => new { g.Key.ReviewId, g.Key.Decision, N = g.Count() }).ToListAsync(ct);
        var names = await db.Users.AsNoTracking().Where(u => rows.Select(r => r.CreatedBy).Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Name, ct);
        int N(Guid id, ReviewDecision d) => counts.Where(c => c.ReviewId == id && c.Decision == d).Sum(c => c.N);
        return [.. rows.Select(r =>
        {
            var k = N(r.Id, ReviewDecision.Keep); var v = N(r.Id, ReviewDecision.Revoke); var p = N(r.Id, ReviewDecision.Pending);
            return new AccessReviewSummary(r.Id, r.Name, r.Status.ToString().ToLowerInvariant(), r.CreatedAt, r.DueAt, r.ClosedAt, r.CreatedBy == Guid.Empty ? "Scheduled (automatic)" : names.GetValueOrDefault(r.CreatedBy), k + v + p, k, v, p, r.Status == AccessReviewStatus.Open && r.DueAt < clock.UtcNow);
        })];
    }

    public async Task<AccessReviewSummary> StartAsync(StartAccessReview req, SessionPrincipal actor, ClientInfo client, AuditWriter audit, CancellationToken ct = default)
    {
        var name = (req.Name ?? "").Trim(); var days = req.DueInDays ?? 14;
        var fields = new Dictionary<string, string>();
        if (name.Length < 3 || name.Length > 200) fields["name"] = "Give the review a name (3–200 characters).";
        if (days is < 1 or > 90) fields["dueInDays"] = "Choose between 1 and 90 days.";
        if (fields.Count > 0) throw DomainException.Validation("Please correct the highlighted fields.", fields);
        var review = await CreateAsync(name, days, actor.UserId, actor.Email, client, audit, ct) ?? throw DomainException.Conflict("There are no privileged role assignments to review.");
        await approvals.NotifyReviewStartedAsync(review.Name, review.DueAt, actor.UserId, ct);
        return (await ListAsync(ct)).Single(r => r.Id == review.Id);
    }

    /// <summary>Snapshots every active user's privileged roles into a new open review. Null when there is nothing to review. One review is open at a time (Conflict).</summary>
    private async Task<AccessReview?> CreateAsync(string name, int days, Guid createdBy, string? actorLabel, ClientInfo client, AuditWriter audit, CancellationToken ct)
    {
        if (await db.AccessReviews.AnyAsync(r => r.Status == AccessReviewStatus.Open, ct)) throw DomainException.Conflict("A review is already open. Close it before starting another.");
        var now = clock.UtcNow;
        var assignments = await db.UserRoles.AsNoTracking().Where(ur => ur.User!.Status == UserStatus.Active && ur.Role!.ArchivedAt == null && ur.Role.Permissions.Any())
            .Select(ur => new { ur.UserId, ur.RoleId }).ToListAsync(ct);
        if (assignments.Count == 0) return null;
        var review = new AccessReview { Name = name, CreatedBy = createdBy, CreatedAt = now, DueAt = now.AddDays(days) };
        foreach (var a in assignments) review.Items.Add(new AccessReviewItem { UserId = a.UserId, RoleId = a.RoleId });
        db.AccessReviews.Add(review);
        audit.Record("access.review.start", "access_review", review.Id.ToString(), Outcome.Success, client, createdBy == Guid.Empty ? null : createdBy, actorLabel,
            new Dictionary<string, string> { ["name"] = name, ["items"] = assignments.Count.ToString(), ["dueDays"] = days.ToString(), ["scheduled"] = (createdBy == Guid.Empty).ToString().ToLowerInvariant() });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { db.ResetTracking(); throw DomainException.Conflict("A review is already open. Close it before starting another."); } // lost a race
        return review;
    }

    // ============================================================ schedule
    public static readonly int[] AllowedEveryDays = [30, 90, 180, 365];

    public async Task<ReviewScheduleView> GetScheduleAsync(CancellationToken ct = default)
    {
        var sc = await db.AccessReviewSchedules.AsNoTracking().FirstOrDefaultAsync(ct);
        return sc is null ? new ReviewScheduleView(false, 90, 14, null, null) : new ReviewScheduleView(sc.Enabled, sc.EveryDays, sc.DueInDays, sc.Enabled ? sc.NextRunAt : null, sc.LastRunAt);
    }

    public async Task<ReviewScheduleView> SaveScheduleAsync(SaveReviewSchedule req, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var sc = await db.AccessReviewSchedules.FirstOrDefaultAsync(ct);
        var enabled = req.Enabled ?? sc?.Enabled ?? false; var every = req.EveryDays ?? sc?.EveryDays ?? 90; var due = req.DueInDays ?? sc?.DueInDays ?? 14;
        var fields = new Dictionary<string, string>();
        if (!AllowedEveryDays.Contains(every)) fields["everyDays"] = "Choose monthly, quarterly, every six months or yearly.";
        if (due is < 1 or > 90) fields["dueInDays"] = "Choose between 1 and 90 days.";
        if (fields.Count > 0) throw DomainException.Validation("Please correct the highlighted fields.", fields);

        var now = clock.UtcNow; var before = sc is null ? "off" : sc.Enabled ? $"every {sc.EveryDays}d" : "off";
        sc ??= db.AccessReviewSchedules.Add(new AccessReviewSchedule()).Entity;
        // The next run is one period from now whenever scheduling is switched on or the period changes; saving other fields leaves the date alone.
        if (enabled && (!sc.Enabled || sc.EveryDays != every || sc.NextRunAt is null)) sc.NextRunAt = now.AddDays(every);
        sc.Enabled = enabled; sc.EveryDays = every; sc.DueInDays = due; sc.UpdatedBy = actor.UserId; sc.UpdatedAt = now;
        audit.Record("access.review.schedule", "access_review_schedule", sc.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email,
            new Dictionary<string, string> { ["before"] = before, ["after"] = enabled ? $"every {every}d, due {due}d" : "off" });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { throw DomainException.Conflict("The schedule was changed at the same time. Reload and try again."); }
        return await GetScheduleAsync(ct);
    }

    /// <summary>
    /// Run by the scheduler (no signed-in person, so no tenant is set): opens a review for every organisation whose schedule is due, as that organisation.
    /// A schedule is claimed by moving its next run forward in one UPDATE, so several instances never open the same review twice. If a review is still open
    /// the run is skipped (reviews never stack) and the next one is a period away. Suspended organisations are skipped the same way.
    /// </summary>
    public async Task<int> RunDueAsync(CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var due = await db.AccessReviewSchedules.IgnoreQueryFilters().AsNoTracking().Where(x => x.Enabled && x.NextRunAt != null && x.NextRunAt <= now)
            .Select(x => new { x.Id, x.TenantId, x.EveryDays, x.DueInDays, x.NextRunAt }).ToListAsync(ct);
        var opened = 0;
        foreach (var d in due)
        {
            var next = now.AddDays(d.EveryDays);
            var claimed = await db.AccessReviewSchedules.IgnoreQueryFilters().Where(x => x.Id == d.Id && x.NextRunAt == d.NextRunAt)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.NextRunAt, next).SetProperty(x => x.LastRunAt, now), ct);
            if (claimed == 0) continue; // another instance took it
            var active = await db.Tenants.AsNoTracking().AnyAsync(t => t.Id == d.TenantId && t.Status == TenantStatus.Active, ct);
            if (!active) continue;
            using (tenant.Switch(d.TenantId))
            {
                try
                {
                    db.ResetTracking();
                    var review = await CreateAsync($"Scheduled access review {now:yyyy-MM-dd}", d.DueInDays, Guid.Empty, "system (scheduled)", new ClientInfo(null, null, null), audit, ct);
                    if (review is null) continue;
                    await approvals.NotifyReviewStartedAsync(review.Name, review.DueAt, Guid.Empty, ct);
                    opened++;
                }
                catch (DomainException e) when (e.Code == ErrorCodes.Conflict) { /* a review is still open: skip this period */ }
            }
        }
        return opened;
    }

    public async Task<PagedResult<AccessReviewItemView>> ItemsAsync(Guid reviewId, string? decision, int page, int pageSize, CancellationToken ct = default)
    {
        if (!await db.AccessReviews.AnyAsync(r => r.Id == reviewId, ct)) throw DomainException.NotFound("Access review");
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100);
        var q = db.AccessReviewItems.AsNoTracking().Where(i => i.ReviewId == reviewId);
        if (!string.IsNullOrWhiteSpace(decision) && !decision.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            if (!Enum.TryParse<ReviewDecision>(decision, true, out var d)) throw DomainException.Validation("Unknown decision.", new Dictionary<string, string> { ["decision"] = "Use pending, keep, revoke or all." });
            q = q.Where(i => i.Decision == d);
        }
        var total = await q.CountAsync(ct);
        var rows = await q.OrderBy(i => i.User!.Name).ThenBy(i => i.Role!.Name).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(i => new { i.Id, i.UserId, UserName = i.User!.Name, i.User.Email, RoleId = i.Role!.Id, i.Role.Key, RoleName = i.Role.Name, i.Decision, i.ReviewerId, i.DecidedAt, i.Note }).ToListAsync(ct);
        var rv = rows.Where(r => r.ReviewerId != null).Select(r => r.ReviewerId!.Value).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => rv.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Name, ct);
        return new PagedResult<AccessReviewItemView>([.. rows.Select(r => new AccessReviewItemView(r.Id, r.UserId, r.UserName, r.Email, new RoleRef(r.RoleId, r.Key, r.RoleName), r.Decision.ToString().ToLowerInvariant(),
            r.ReviewerId is { } x ? names.GetValueOrDefault(x) : null, r.DecidedAt, r.Note))], page, pageSize, total);
    }

    public async Task DecideAsync(Guid reviewId, Guid itemId, ReviewDecisionRequest req, SessionPrincipal actor, ClientInfo client, AuditWriter audit, CancellationToken ct = default)
    {
        if (!Enum.TryParse<ReviewDecision>(req.Decision, true, out var decision) || decision == ReviewDecision.Pending)
            throw DomainException.Validation("Choose keep or revoke.", new Dictionary<string, string> { ["decision"] = "Use keep or revoke." });
        var note = req.Note?.Trim();
        if (note is { Length: > MaxNote }) throw DomainException.Validation("The note is too long.", new Dictionary<string, string> { ["note"] = $"Use at most {MaxNote} characters." });
        var item = await db.AccessReviewItems.AsNoTracking().Include(i => i.Review).Include(i => i.Role).Include(i => i.User).FirstOrDefaultAsync(i => i.Id == itemId && i.ReviewId == reviewId, ct)
            ?? throw DomainException.NotFound("Review item");
        if (item.Review!.Status != AccessReviewStatus.Open) throw DomainException.Conflict("This review is closed.");
        if (item.Decision != ReviewDecision.Pending) throw DomainException.Conflict("This item was already decided.");
        var action = decision == ReviewDecision.Keep ? "access.review.keep" : "access.review.revoke";
        if (item.UserId == actor.UserId)
        {
            audit.Record(action, "user", item.UserId.ToString(), Outcome.Denied, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["reason"] = "own access" });
            await db.SaveChangesAsync(ct);
            throw DomainException.DeniedBecause("You can't review your own access. Ask another administrator.");
        }
        if (decision == ReviewDecision.Revoke)
        {
            var held = await db.UserRoles.Where(r => r.UserId == item.UserId).Select(r => r.RoleId).ToListAsync(ct);
            if (held.Contains(item.RoleId))
            {
                var keep = held.Where(r => r != item.RoleId).ToList();
                if (keep.Count == 0) keep.Add(await db.Roles.Where(r => r.Key == SystemRoles.User).Select(r => r.Id).SingleAsync(ct)); // never leave someone with no role
                await users.SetRolesAsync(item.UserId, [.. keep], actor, client, ct); // re-auth, hierarchy, last-Super-Admin guard, audit
            } // already gone since the snapshot: nothing to remove, the decision still records
        }
        var now = clock.UtcNow;
        var rows = await db.AccessReviewItems.Where(i => i.Id == itemId && i.Decision == ReviewDecision.Pending)
            .ExecuteUpdateAsync(u => u.SetProperty(i => i.Decision, decision).SetProperty(i => i.ReviewerId, actor.UserId).SetProperty(i => i.DecidedAt, now).SetProperty(i => i.Note, note), ct);
        if (rows == 0) throw DomainException.Conflict("This item was already decided.");
        audit.Record(action, "user", item.UserId.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["review"] = item.Review.Name, ["role"] = item.Role!.Key, ["user"] = item.User!.Email });
        await db.SaveChangesAsync(ct);
    }

    public async Task CloseAsync(Guid reviewId, SessionPrincipal actor, ClientInfo client, AuditWriter audit, CancellationToken ct = default)
    {
        var review = await db.AccessReviews.FirstOrDefaultAsync(r => r.Id == reviewId, ct) ?? throw DomainException.NotFound("Access review");
        if (review.Status == AccessReviewStatus.Closed) throw DomainException.Conflict("This review is already closed.");
        var pending = await db.AccessReviewItems.CountAsync(i => i.ReviewId == reviewId && i.Decision == ReviewDecision.Pending, ct);
        review.Status = AccessReviewStatus.Closed; review.ClosedAt = clock.UtcNow; review.ClosedBy = actor.UserId;
        audit.Record("access.review.close", "access_review", reviewId.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["name"] = review.Name, ["unreviewed"] = pending.ToString() });
        await db.SaveChangesAsync(ct);
    }
}
