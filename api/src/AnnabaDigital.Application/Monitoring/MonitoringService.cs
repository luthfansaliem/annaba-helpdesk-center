using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Application.Users;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;

namespace AnnabaDigital.Application.Monitoring;

/// <summary>AUD-001 / SEC-001 / SEC-002. Read-only evidence plus acknowledgement of security events and the retention purge.</summary>
public sealed class MonitoringService(IAppDb db, IClock clock, SettingsProvider settings, AuditWriter audit)
{
    // ---------------------------------------------------------------- audit log
    private IQueryable<AuditEvent> AuditFiltered(AuditQuery q)
    {
        var query = db.AuditEvents.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(q.Q)) { var t = q.Q.Trim().ToLowerInvariant(); query = query.Where(a => a.Action.ToLower().Contains(t) || a.ActorLabel.ToLower().Contains(t) || (a.TargetId != null && a.TargetId.ToLower().Contains(t))); }
        if (!string.IsNullOrWhiteSpace(q.Outcome))
        {
            if (!Enum.TryParse<Outcome>(q.Outcome, true, out var o)) throw DomainException.Validation("Unknown outcome.", new Dictionary<string, string> { ["outcome"] = "Use success, failure or denied." });
            query = query.Where(a => a.Outcome == o);
        }
        if (!string.IsNullOrWhiteSpace(q.Category)) { var c = q.Category.Trim().ToLowerInvariant() + "."; query = query.Where(a => a.Action.ToLower().StartsWith(c)); }
        if (q.ActorId is { } actor) query = query.Where(a => a.ActorId == actor);
        if (q.From is { } from) query = query.Where(a => a.Ts >= from);
        if (q.To is { } to) query = query.Where(a => a.Ts <= to);
        return query;
    }

    public async Task<PagedResult<AuditListItem>> AuditAsync(AuditQuery q, CancellationToken ct = default)
    {
        var page = Math.Max(1, q.Page); var size = Math.Clamp(q.PageSize, 1, 100);
        var query = AuditFiltered(q);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(a => a.Ts).ThenByDescending(a => a.Id).Skip((page - 1) * size).Take(size).ToListAsync(ct);
        return new PagedResult<AuditListItem>([.. rows.Select(a => new AuditListItem(a.Id, a.Ts, a.ActorId, a.ActorLabel, a.Action, a.TargetType, a.TargetId, a.Outcome.ToString().ToLowerInvariant()))], page, size, total);
    }

    /// <summary>Most rows one export may contain; the response says when it was cut short.</summary>
    public const int ExportMaxRows = 10_000;

    /// <summary>
    /// AUD-003. Exports the rows matching the same filters as the list (newest first, capped). The export itself is audited, and CSV cells that a
    /// spreadsheet would treat as a formula are neutralised.
    /// </summary>
    public async Task<AuditExport> ExportAsync(AuditQuery q, string? format, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var fmt = (format ?? "csv").Trim().ToLowerInvariant();
        if (fmt is not ("csv" or "json")) throw DomainException.Validation("Unknown export format.", new Dictionary<string, string> { ["format"] = "Use csv or json." });
        var rows = await AuditFiltered(q).OrderByDescending(a => a.Ts).ThenByDescending(a => a.Id).Take(ExportMaxRows + 1).ToListAsync(ct);
        var truncated = rows.Count > ExportMaxRows; if (truncated) rows.RemoveAt(rows.Count - 1);
        var stamp = clock.UtcNow.ToString("yyyyMMdd-HHmmss");
        string content;
        if (fmt == "json")
            content = JsonSerializer.Serialize(rows.Select(a => new { a.Id, a.Ts, actorId = a.ActorId, actor = a.ActorLabel, a.Action, a.TargetType, a.TargetId, outcome = a.Outcome.ToString().ToLowerInvariant(), a.Ip, a.RequestId, meta = Parse(a.MetaJson), diff = Parse(a.DiffJson) }), new JsonSerializerOptions { WriteIndented = true });
        else
        {
            var sb = new System.Text.StringBuilder("id,timestamp,actor_id,actor,action,target_type,target_id,outcome,ip,request_id,meta\r\n");
            foreach (var a in rows)
                sb.AppendJoin(',', new string?[] { a.Id.ToString(), a.Ts.UtcDateTime.ToString("o"), a.ActorId?.ToString(), a.ActorLabel, a.Action, a.TargetType, a.TargetId, a.Outcome.ToString().ToLowerInvariant(), a.Ip, a.RequestId, a.MetaJson }.Select(Csv)).Append("\r\n");
            content = sb.ToString();
        }
        audit.Record("audit.export", "audit", null, Outcome.Success, client, actor.UserId, actor.Email,
            new Dictionary<string, string> { ["format"] = fmt, ["rows"] = rows.Count.ToString(), ["truncated"] = truncated.ToString().ToLowerInvariant(), ["filters"] = $"q={q.Q};outcome={q.Outcome};category={q.Category};actor={q.ActorId};from={q.From:o};to={q.To:o}" });
        await db.SaveChangesAsync(ct);
        return new AuditExport(content, fmt == "json" ? "application/json" : "text/csv", $"audit-{stamp}.{fmt}", rows.Count, truncated);
    }

    /// <summary>RFC 4180 quoting plus CSV-injection defence: a leading = + - @ tab or CR would be run as a formula by Excel/Sheets.</summary>
    public static string Csv(string? v)
    {
        if (string.IsNullOrEmpty(v)) return "";
        if (v[0] is '=' or '+' or '-' or '@' or '\t' or '\r') v = "'" + v;
        return v.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
    }

    private static JsonElement? Parse(string? json) => json is null ? null : JsonDocument.Parse(json).RootElement.Clone();

    public async Task<AuditDetail> AuditDetailAsync(long id, CancellationToken ct = default)
    {
        var a = await db.AuditEvents.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Audit event");
        return new AuditDetail(a.Id, a.Ts, a.ActorId, a.ActorLabel, a.Action, a.TargetType, a.TargetId, a.Outcome.ToString().ToLowerInvariant(), a.Ip, a.UserAgent, a.RequestId, Parse(a.MetaJson), Parse(a.DiffJson));
    }

    // ---------------------------------------------------------------- security events
    public async Task<PagedResult<SecurityEventItem>> SecurityAsync(SecurityQuery q, CancellationToken ct = default)
    {
        var page = Math.Max(1, q.Page); var size = Math.Clamp(q.PageSize, 1, 100);
        var query = db.SecurityEvents.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(q.Severity))
        {
            if (!Enum.TryParse<Severity>(q.Severity, true, out var s)) throw DomainException.Validation("Unknown severity.", new Dictionary<string, string> { ["severity"] = "Use low, medium or high." });
            query = query.Where(e => e.Severity == s);
        }
        if (q.Acknowledged is { } ack) query = ack ? query.Where(e => e.AcknowledgedAt != null) : query.Where(e => e.AcknowledgedAt == null);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(e => e.Ts).Skip((page - 1) * size).Take(size).ToListAsync(ct);
        var uids = rows.Where(r => r.UserId != null).Select(r => r.UserId!.Value).Concat(rows.Where(r => r.AcknowledgedBy != null).Select(r => r.AcknowledgedBy!.Value)).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => uids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Name, ct);
        return new PagedResult<SecurityEventItem>([.. rows.Select(e => new SecurityEventItem(e.Id, e.Ts, e.Type, e.Severity.ToString().ToLowerInvariant(), e.UserId, e.UserId is { } u ? names.GetValueOrDefault(u) : null, e.Detail,
            e.AcknowledgedAt != null, e.AcknowledgedBy is { } b ? names.GetValueOrDefault(b) : null, e.AcknowledgedAt))], page, size, total);
    }

    public async Task AcknowledgeAsync(Guid id, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var e = await db.SecurityEvents.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Security event");
        if (e.AcknowledgedAt is not null) throw DomainException.Conflict("This event was already acknowledged.");
        var rows = await db.SecurityEvents.Where(x => x.Id == id && x.AcknowledgedAt == null).ExecuteUpdateAsync(u => u.SetProperty(x => x.AcknowledgedAt, now).SetProperty(x => x.AcknowledgedBy, actor.UserId), ct);
        if (rows == 0) throw DomainException.Conflict("This event was already acknowledged."); // someone else acknowledged it first
        audit.Record("security.ack", "security_event", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["type"] = e.Type });
        await db.SaveChangesAsync(ct);
    }

    // ---------------------------------------------------------------- retention (SEC-002)
    /// <summary>
    /// Deletes evidence older than <c>auditRetentionDays</c> and housekeeps expired credentials. Audit rows are append-only at the DB level; the purge
    /// opens the explicit maintenance context (<c>SET LOCAL annabadigital.allow_audit_purge</c>) for exactly this transaction and records that it ran.
    /// </summary>
    public async Task<RetentionResult> PurgeAsync(CancellationToken ct = default)
    {
        var s = await settings.GetAsync(ct); var now = clock.UtcNow; var cutoff = now.AddDays(-s.AuditRetentionDays);
        return await db.InTransactionAsync(async () =>
        {
            await db.ExecuteSqlAsync($"SET LOCAL annabadigital.allow_audit_purge = 'on'", ct);
            var auditRows = await db.AuditEvents.Where(a => a.Ts < cutoff && a.Action != "retention.purge").ExecuteDeleteAsync(ct);
            var sec = await db.SecurityEvents.Where(e => e.Ts < cutoff).ExecuteDeleteAsync(ct);
            var tokenCut = now.AddDays(-7); var sessCut = now.AddDays(-30); var bucketCut = now.AddDays(-1);
            var tokens = await db.EmailVerificationTokens.Where(t => t.ExpiresAt < tokenCut || (t.ConsumedAt != null && t.ConsumedAt < tokenCut)).ExecuteDeleteAsync(ct)
                       + await db.PasswordResetTokens.Where(t => t.ExpiresAt < tokenCut || (t.ConsumedAt != null && t.ConsumedAt < tokenCut)).ExecuteDeleteAsync(ct);
            var sessions = await db.Sessions.Where(x => (x.RevokedAt != null && x.RevokedAt < sessCut) || x.AbsoluteExpiresAt < sessCut).ExecuteDeleteAsync(ct);
            var buckets = await db.RateLimitBuckets.Where(b => b.WindowStart < bucketCut).ExecuteDeleteAsync(ct);
            db.AuditEvents.Add(new AuditEvent { Ts = now, ActorLabel = "system", Action = "retention.purge", TargetType = "system", Outcome = Outcome.Success,
                MetaJson = JsonSerializer.Serialize(new { retentionDays = s.AuditRetentionDays, audit = auditRows, security = sec, tokens, sessions, rateBuckets = buckets }) });
            await db.SaveChangesAsync(ct);
            return new RetentionResult(auditRows, sec, tokens, sessions, buckets);
        }, ct);
    }
}
