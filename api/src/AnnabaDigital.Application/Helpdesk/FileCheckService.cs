using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;

namespace AnnabaDigital.Application.Helpdesk;

public sealed record MissingFile(Guid AttachmentId, string FileName, Guid? TicketId, string? Number);
/// <summary>Files: database rows that should have bytes. Missing: rows whose bytes are not on the volume (typically after restoring a database without its files). Orphans: bytes with no live row (an upload that never finished, a crash between two steps, or files newer than the restored database).</summary>
public sealed record FileCheck(int Files, int Missing, IReadOnlyList<MissingFile> MissingSample, int Orphans, long OrphanBytes, DateTimeOffset CheckedAt);
public sealed record FileClean(int Removed, long Bytes, int Failed);

/// <summary>
/// Compares this organisation's attachment rows with the bytes on the volume. After a restore, or any time files and database may have drifted apart, it shows what is missing and
/// can remove the bytes nothing refers to. Bytes younger than <see cref="OrphanGrace"/> are never touched: an upload stores its bytes just before its row is saved.
/// </summary>
public sealed class FileCheckService(IAppDb db, IClock clock, IFileStorage storage, ITenantContext tenant, SettingsProvider settings, AuditWriter audit)
{
    public static readonly TimeSpan OrphanGrace = TimeSpan.FromHours(1);
    public const int SampleSize = 50;

    private string Prefix => tenant.TenantId.ToString("N") + "/";

    private async Task<(int Files, List<MissingFile> Missing, int MissingCount, List<StoredFile> Orphans)> ScanAsync(CancellationToken ct)
    {
        var stored = new Dictionary<string, StoredFile>();
        await foreach (var f in storage.ListAsync(Prefix, ct)) stored[f.Key] = f;

        var live = new HashSet<string>(); var missing = new List<MissingFile>(); var missingCount = 0; var files = 0;
        await foreach (var a in db.Attachments.AsNoTracking().Where(x => x.DeletedAt == null).Select(x => new { x.Id, x.FileName, x.StorageKey, x.TicketId, Number = db.Tickets.Where(t => t.Id == x.TicketId).Select(t => t.Number).FirstOrDefault() }).AsAsyncEnumerable().WithCancellation(ct))
        {
            files++; live.Add(a.StorageKey);
            if (stored.ContainsKey(a.StorageKey)) continue;
            missingCount++;
            if (missing.Count < SampleSize) missing.Add(new MissingFile(a.Id, a.FileName, a.TicketId, a.Number));
        }
        // knowledge-base files too
        await foreach (var d in db.KbFiles.AsNoTracking().Where(x => x.DeletedAt == null).Select(x => new { x.Id, x.FileName, x.StorageKey }).AsAsyncEnumerable().WithCancellation(ct))
        {
            files++; live.Add(d.StorageKey);
            if (stored.ContainsKey(d.StorageKey)) continue;
            missingCount++;
            if (missing.Count < SampleSize) missing.Add(new MissingFile(d.Id, d.FileName, null, null));
        }
        // vendor documents live on the same volume
        await foreach (var d in db.VendorDocuments.AsNoTracking().Where(x => x.DeletedAt == null).Select(x => new { x.Id, x.FileName, x.StorageKey }).AsAsyncEnumerable().WithCancellation(ct))
        {
            files++; live.Add(d.StorageKey);
            if (stored.ContainsKey(d.StorageKey)) continue;
            missingCount++;
            if (missing.Count < SampleSize) missing.Add(new MissingFile(d.Id, d.FileName, null, null));
        }
        var cutoff = clock.UtcNow - OrphanGrace;
        var orphans = stored.Values.Where(f => !live.Contains(f.Key) && f.ModifiedAt < cutoff).ToList();
        return (files, missing, missingCount, orphans);
    }

    public async Task<FileCheck> CheckAsync(CancellationToken ct = default)
    {
        var (files, missing, missingCount, orphans) = await ScanAsync(ct);
        return new FileCheck(files, missingCount, missing, orphans.Count, orphans.Sum(o => o.Size), clock.UtcNow);
    }

    /// <summary>Deletes the bytes nothing refers to. Fresh password check; audited with counts. A file the volume refuses to delete is counted, not fatal.</summary>
    public async Task<FileClean> CleanAsync(SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        Guards.RequireRecentReauthForCritical(actor, await settings.GetAsync(ct), clock);
        var (_, _, _, orphans) = await ScanAsync(ct);
        int removed = 0, failed = 0; long bytes = 0;
        foreach (var o in orphans)
        {
            try { await storage.DeleteAsync(o.Key, ct); removed++; bytes += o.Size; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { failed++; }
        }
        audit.Record("files.orphans.clean", "tenant", tenant.TenantId.ToString(), failed > 0 ? Outcome.Failure : Outcome.Success, client, actor.UserId, actor.Email,
            new Dictionary<string, string> { ["removed"] = removed.ToString(), ["bytes"] = bytes.ToString(), ["failed"] = failed.ToString() });
        await db.SaveChangesAsync(ct);
        return new FileClean(removed, bytes, failed);
    }
}
