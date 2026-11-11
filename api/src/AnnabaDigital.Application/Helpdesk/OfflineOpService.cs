using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;

namespace AnnabaDigital.Application.Helpdesk;

/// <summary>
/// ADR-GA-07. Remembers the <c>Idempotency-Key</c> of each change a person's device replayed (field mode), for 7 days, so a change whose answer was lost on a
/// bad connection is applied once even when the device sends it again. Keys are per person: one person's key never affects another's.
/// </summary>
public sealed class OfflineOpService(IAppDb db, IClock clock)
{
    public const int MaxKey = 100, KeepDays = 7;

    public static string? Key(string? raw)
    {
        var k = (raw ?? "").Trim();
        if (k.Length == 0) return null;
        if (k.Length > MaxKey || !k.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or ':' or '.'))
            throw DomainException.Validation("The Idempotency-Key is not valid.", new Dictionary<string, string> { ["Idempotency-Key"] = $"Use at most {MaxKey} letters, digits, - _ : ." });
        return k;
    }

    public Task<bool> SeenAsync(Guid userId, string key, CancellationToken ct) => db.OfflineOps.AnyAsync(o => o.UserId == userId && o.Key == key, ct);

    public async Task RememberAsync(Guid userId, string key, CancellationToken ct)
    {
        var cutoff = clock.UtcNow.AddDays(-KeepDays);
        await db.OfflineOps.Where(o => o.UserId == userId && o.CreatedAt < cutoff).ExecuteDeleteAsync(ct);
        db.OfflineOps.Add(new OfflineOp { UserId = userId, Key = key, CreatedAt = clock.UtcNow });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { db.ResetTracking(); }       // the same key arrived twice at once: one is enough
    }
}
