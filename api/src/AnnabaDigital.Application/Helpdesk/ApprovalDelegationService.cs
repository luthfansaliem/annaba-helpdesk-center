using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;

namespace AnnabaDigital.Application.Helpdesk;

public sealed record SaveDelegationRequest(Guid? DelegateId, DateTimeOffset? StartsAt, DateTimeOffset? EndsAt, string? Note);
public sealed record DelegationItem(Guid Id, Guid DelegateId, string DelegateName, DateTimeOffset StartsAt, DateTimeOffset EndsAt, string? Note, string State, DateTimeOffset CreatedAt);
public sealed record DelegationResult(DelegationItem Delegation, int HandedOver, int Kept);

/// <summary>
/// APR-004. A person who will be away names someone to approve in their place for a period. From then on, approvals that would go to them go to their cover
/// (the trail shows both); what is already waiting for them moves at once when the period has started. Only the person themselves sets or ends it.
/// </summary>
public sealed class ApprovalDelegationService(IAppDb db, IClock clock, AuditWriter audit, TicketApprovalService approvals, NotificationService notifications)
{
    public static readonly TimeSpan MaxPeriod = TimeSpan.FromDays(180);

    private static DomainException Bad(string key, string message) => DomainException.Validation("Please correct the highlighted fields.", new Dictionary<string, string> { [key] = message });

    private static string StateOf(ApprovalDelegation d, DateTimeOffset now) => d.RevokedAt is not null ? "revoked" : d.EndsAt <= now ? "ended" : d.StartsAt > now ? "upcoming" : "active";

    public async Task<IReadOnlyList<DelegationItem>> MineAsync(Guid userId, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var rows = await db.ApprovalDelegations.AsNoTracking().Where(d => d.UserId == userId).OrderByDescending(d => d.StartsAt).Take(30)
            .Select(d => new { d.Id, d.DelegateId, d.StartsAt, d.EndsAt, d.Note, d.RevokedAt, d.CreatedAt, d.UserId, Name = db.Users.Where(u => u.Id == d.DelegateId).Select(u => u.Name).FirstOrDefault() }).ToListAsync(ct);
        return [.. rows.Select(r => new DelegationItem(r.Id, r.DelegateId, r.Name ?? "?", r.StartsAt, r.EndsAt, r.Note, StateOf(new ApprovalDelegation { RevokedAt = r.RevokedAt, StartsAt = r.StartsAt, EndsAt = r.EndsAt }, now), r.CreatedAt))];
    }

    public async Task<DelegationResult> CreateAsync(SaveDelegationRequest req, SessionPrincipal who, ClientInfo client, CancellationToken ct = default)
    {
        var now = clock.UtcNow; var errors = new Dictionary<string, string>();
        var start = req.StartsAt ?? now; var end = req.EndsAt;
        if (req.DelegateId is not { } to) errors["delegateId"] = "Choose who covers for you.";
        else if (to == who.UserId) errors["delegateId"] = "You cannot cover for yourself.";
        else if (!await db.Users.AnyAsync(u => u.Id == to && u.Status == UserStatus.Active, ct)) errors["delegateId"] = "Choose an active person of your organisation.";
        if (end is null) errors["endsAt"] = "Say when you are back.";
        else if (end <= start) errors["endsAt"] = "The end must be after the start.";
        else if (end <= now) errors["endsAt"] = "The end must be in the future.";
        else if (end - start > MaxPeriod) errors["endsAt"] = "A delegation can last at most 180 days.";
        if (start < now.AddDays(-1)) errors["startsAt"] = "The start cannot be more than a day in the past.";
        if (req.Note is { Length: > 300 }) errors["note"] = "Use at most 300 characters.";
        if (errors.Count > 0) throw DomainException.Validation("Please correct the highlighted fields.", errors);
        if (await db.ApprovalDelegations.AnyAsync(d => d.UserId == who.UserId && d.RevokedAt == null && d.StartsAt < end && d.EndsAt > start, ct)) throw Bad("startsAt", "You already have a delegation in this period. End it first.");

        var d = new ApprovalDelegation { UserId = who.UserId, DelegateId = req.DelegateId!.Value, StartsAt = start, EndsAt = end!.Value, Note = string.IsNullOrWhiteSpace(req.Note) ? null : req.Note.Trim(), CreatedAt = now };
        db.ApprovalDelegations.Add(d);
        var (moved, kept) = start <= now ? await approvals.HandOverPendingAsync(who.UserId, d.DelegateId, who.Name, now, ct) : (0, 0);
        await notifications.SendAsync(d.DelegateId, "approval.delegated", $"{who.Name} asked you to approve for them", $"From {start:yyyy-MM-dd} to {end:yyyy-MM-dd}.", "/approvals", null, ct: ct);
        audit.Record("approval.delegation.create", "user", who.UserId.ToString(), Outcome.Success, client, who.UserId, who.Email,
            new Dictionary<string, string> { ["delegate"] = d.DelegateId.ToString(), ["from"] = start.ToString("yyyy-MM-dd"), ["to"] = end.Value.ToString("yyyy-MM-dd"), ["handedOver"] = moved.ToString() });
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateException) { throw Bad("startsAt", "This delegation could not be saved. Reload and try again."); }
        return new DelegationResult((await MineAsync(who.UserId, ct)).First(x => x.Id == d.Id), moved, kept);
    }

    /// <summary>Ends a delegation now (or cancels one that has not started). What already moved to the cover stays with them.</summary>
    public async Task<DelegationItem> RevokeAsync(Guid id, SessionPrincipal who, ClientInfo client, CancellationToken ct = default)
    {
        var d = await db.ApprovalDelegations.FirstOrDefaultAsync(x => x.Id == id && x.UserId == who.UserId, ct) ?? throw DomainException.NotFound("Delegation");
        var now = clock.UtcNow;
        if (d.RevokedAt is not null) throw DomainException.Conflict("This delegation was already ended.");
        if (d.EndsAt <= now) throw DomainException.Conflict("This delegation has already finished.");
        d.RevokedAt = now;
        audit.Record("approval.delegation.revoke", "user", who.UserId.ToString(), Outcome.Success, client, who.UserId, who.Email, new Dictionary<string, string> { ["delegate"] = d.DelegateId.ToString() });
        await db.SaveChangesAsync(ct);
        return (await MineAsync(who.UserId, ct)).First(x => x.Id == id);
    }
}
