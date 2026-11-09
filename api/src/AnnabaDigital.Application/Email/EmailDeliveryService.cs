using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;

namespace AnnabaDigital.Application.Email;

public sealed record EmailDeliveryRow(Guid Id, string To, string Subject, string Status, int Attempts, DateTimeOffset CreatedAt, DateTimeOffset? SentAt, DateTimeOffset? FailedAt, DateTimeOffset? NextAttemptAt, string? LastError);
public sealed record EmailDeliverySummary(string Provider, bool Configured, int Queued, int Sent24h, int Failed7d);
public sealed record EmailDeliveryPage(EmailDeliverySummary Summary, IReadOnlyList<EmailDeliveryRow> Data, int Page, int PageSize, int Total);

/// <summary>
/// Read-only view of the e-mail outbox for administrators (settings.manage). Recipients and subjects only: bodies hold one-time links
/// and are wiped as soon as a message is sent or abandoned, so they are never exposed here. Rows older than the retention purge are gone.
/// </summary>
public sealed class EmailDeliveryService(IAppDb db, IClock clock, IEmailInfo info)
{
    public async Task<EmailDeliveryPage> ListAsync(string? status, int page, int pageSize, CancellationToken ct = default)
    {
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100);
        var now = clock.UtcNow;
        var rows = db.OutboxEmails.AsNoTracking();
        var queued = await rows.CountAsync(r => r.SentAt == null && r.FailedAt == null, ct);
        var sent = await rows.CountAsync(r => r.SentAt != null && r.SentAt > now.AddHours(-24), ct);
        var failed = await rows.CountAsync(r => r.FailedAt != null && r.FailedAt > now.AddDays(-7), ct);

        var q = (status?.Trim().ToLowerInvariant()) switch
        {
            "queued" => rows.Where(r => r.SentAt == null && r.FailedAt == null),
            "sent" => rows.Where(r => r.SentAt != null),
            "failed" => rows.Where(r => r.FailedAt != null),
            _ => rows,
        };
        var total = await q.CountAsync(ct);
        var data = await q.OrderByDescending(r => r.CreatedAt).ThenBy(r => r.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(r => new EmailDeliveryRow(r.Id, r.ToAddress, r.Subject, r.SentAt != null ? "sent" : r.FailedAt != null ? "failed" : "queued", r.Attempts, r.CreatedAt, r.SentAt, r.FailedAt, r.SentAt == null && r.FailedAt == null ? r.NextAttemptAt : null, r.LastError))
            .ToListAsync(ct);
        return new EmailDeliveryPage(new EmailDeliverySummary(info.Provider, info.Configured, queued, sent, failed), data, page, pageSize, total);
    }
}
