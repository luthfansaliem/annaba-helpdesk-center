using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;

namespace AnnabaDigital.Application.Helpdesk;

public sealed record AttachmentItem(Guid Id, string FileName, string ContentType, long Size, bool Inline, Guid UploadedById, string? UploadedBy, DateTimeOffset CreatedAt, bool CanDelete);
public sealed record AttachmentDownload(Stream Content, string FileName, string ContentType, bool Inline);

/// <summary>
/// TKT-005. Stores, lists, serves and removes attachment files. It knows nothing about who may see a ticket — <see cref="TicketService"/> checks that first and then asks this class.
/// Every upload is checked (<see cref="FileRules"/>: allow-listed type whose content matches, at most 8 MB), scanned (<see cref="IAttachmentScanner"/>) and fingerprinted before a
/// byte is stored; the stored name is generated, never taken from the sender. A file uploaded for a form that has not been submitted has no ticket until the ticket claims it.
/// </summary>
public sealed class AttachmentService(IAppDb db, IClock clock, AuditWriter audit, IFileStorage storage, IAttachmentScanner scanner, ITenantContext tenant)
{
    public static readonly TimeSpan OrphanLifetime = TimeSpan.FromHours(24);
    public const int MaxPendingUploads = 30;

    private static DomainException Bad(string message) => DomainException.Validation(message, new Dictionary<string, string> { ["file"] = message });
    private static string KeyOf(Guid tenantId, Guid id) => $"{tenantId:N}/{id:N}";

    // ================================================================ storing
    /// <summary>Checks, scans and stores one file. <paramref name="ticketId"/> null = an upload for a form that is not submitted yet. The caller has already checked access to the ticket.</summary>
    /// <summary>A file that passed the checks and the scan, held in memory until it is stored. Dispose it.</summary>
    public sealed record CheckedFile(string Name, string ContentType, long Size, string Sha256, MemoryStream Content) : IDisposable { public void Dispose() => Content.Dispose(); }

    /// <summary>
    /// Reads at most one byte past the limit, checks the name, type and content (<see cref="FileRules"/>) and scans it. A refusal by the scan is audited against
    /// <paramref name="targetType"/>/<paramref name="targetId"/>. Shared by ticket files and other documents (vendors).
    /// </summary>
    public async Task<CheckedFile> CheckAsync(string? fileName, Stream content, string targetType, string? targetId, SessionPrincipal who, ClientInfo client, CancellationToken ct = default)
    {
        var buffer = new MemoryStream();
        try
        {
            var limit = FileRules.MaxBytes + 1; var chunk = new byte[81920]; int n;
            while (buffer.Length < limit && (n = await content.ReadAsync(chunk.AsMemory(0, (int)Math.Min(chunk.Length, limit - buffer.Length)), ct)) > 0) buffer.Write(chunk, 0, n);
            var bytes = buffer.GetBuffer(); var size = buffer.Length;
            var header = bytes.AsSpan(0, (int)Math.Min(size, FileRules.HeaderBytes)).ToArray();
            var name = FileRules.SafeName(fileName);
            var verdict = FileRules.Check(name, size, header);
            if (!verdict.Ok) throw Bad(verdict.Error!);
            buffer.Position = 0;
            var scan = await scanner.ScanAsync(buffer, name, ct);
            if (!scan.Clean) { audit.Record("attachment.rejected", targetType, targetId, Outcome.Denied, client, who.UserId, who.Email, new Dictionary<string, string> { ["file"] = name, ["reason"] = scan.Reason ?? "scan" }); await db.SaveChangesAsync(ct); throw Bad("The file was rejected by the security scan."); }
            buffer.Position = 0;
            return new CheckedFile(name, verdict.ContentType!, size, Convert.ToHexString(SHA256.HashData(bytes.AsSpan(0, (int)size))).ToLowerInvariant(), buffer);
        }
        catch { buffer.Dispose(); throw; }
    }

    /// <summary>The storage key for a new file of this organisation.</summary>
    public string NewKey(Guid id) => KeyOf(tenant.TenantId, id);

    public async Task<Attachment> StoreAsync(Guid? ticketId, string? fileName, Stream content, SessionPrincipal who, ClientInfo client, CancellationToken ct = default)
    {
        using var file = await CheckAsync(fileName, content, "ticket", ticketId?.ToString(), who, client, ct);
        var buffer = file.Content; var name = file.Name; var size = file.Size;

        if (ticketId is { } tid && await db.Attachments.CountAsync(a => a.TicketId == tid && a.DeletedAt == null, ct) >= FileRules.MaxPerTicket) throw Bad($"A ticket can have at most {FileRules.MaxPerTicket} files.");
        if (ticketId is null && await db.Attachments.CountAsync(a => a.TicketId == null && a.DeletedAt == null && a.UploadedById == who.UserId, ct) >= MaxPendingUploads) throw Bad("Too many files are waiting to be attached. Submit your request or try again later.");

        buffer.Position = 0;
        var a = new Attachment { TicketId = ticketId, FileName = name, ContentType = file.ContentType, Size = size, Sha256 = file.Sha256, UploadedById = who.UserId, CreatedAt = clock.UtcNow };
        a.StorageKey = KeyOf(tenant.TenantId, a.Id);
        buffer.Position = 0;
        await storage.SaveAsync(a.StorageKey, buffer, ct);
        db.Attachments.Add(a);
        audit.Record("attachment.add", "ticket", ticketId?.ToString(), Outcome.Success, client, who.UserId, who.Email, new Dictionary<string, string> { ["file"] = name, ["bytes"] = size.ToString(), ["sha256"] = a.Sha256 });
        try { await db.SaveChangesAsync(ct); }
        catch { await storage.DeleteAsync(a.StorageKey, CancellationToken.None); throw; }
        return a;
    }

    // ================================================================ reading
    public async Task<IReadOnlyList<AttachmentItem>> ListAsync(Guid ticketId, Guid viewerId, bool canManage, CancellationToken ct = default)
    {
        var rows = await db.Attachments.AsNoTracking().Where(a => a.TicketId == ticketId && a.DeletedAt == null).OrderBy(a => a.CreatedAt).ThenBy(a => a.Id)
            .Select(a => new { a.Id, a.FileName, a.ContentType, a.Size, a.UploadedById, a.CreatedAt, By = db.Users.Where(u => u.Id == a.UploadedById).Select(u => u.Name).FirstOrDefault() }).ToListAsync(ct);
        return [.. rows.Select(r => new AttachmentItem(r.Id, r.FileName, r.ContentType, r.Size, r.ContentType.StartsWith("image/"), r.UploadedById, r.By, r.CreatedAt, canManage || r.UploadedById == viewerId))];
    }

    public async Task<AttachmentDownload> OpenAsync(Guid ticketId, Guid id, CancellationToken ct = default)
    {
        var a = await db.Attachments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.TicketId == ticketId && x.DeletedAt == null, ct) ?? throw DomainException.NotFound("File");
        var stream = await storage.OpenAsync(a.StorageKey, ct) ?? throw DomainException.NotFound("File");
        return new AttachmentDownload(stream, a.FileName, a.ContentType, a.ContentType.StartsWith("image/"));
    }

    // ================================================================ removing
    /// <summary>The uploader or someone who may manage the ticket removes a file: the bytes go, the row stays (soft delete) for the trail.</summary>
    public async Task<string> DeleteAsync(Guid ticketId, Guid id, SessionPrincipal who, bool canManage, ClientInfo client, CancellationToken ct = default)
    {
        var a = await db.Attachments.FirstOrDefaultAsync(x => x.Id == id && x.TicketId == ticketId && x.DeletedAt == null, ct) ?? throw DomainException.NotFound("File");
        if (a.UploadedById != who.UserId && !canManage) throw DomainException.DeniedBecause("Only the person who attached a file, or staff who manage the ticket, can remove it.");
        if (await db.TicketLegalHolds.AnyAsync(h => h.TicketId == ticketId, ct)) throw DomainException.Conflict("This ticket is under legal hold, so its files can't be removed.");
        a.DeletedAt = clock.UtcNow;
        audit.Record("attachment.delete", "ticket", ticketId.ToString(), Outcome.Success, client, who.UserId, who.Email, new Dictionary<string, string> { ["file"] = a.FileName });
        await db.SaveChangesAsync(ct);
        await storage.DeleteAsync(a.StorageKey, ct);
        return a.FileName;
    }

    // ================================================================ uploads for a form not yet submitted
    /// <summary>The ids that are NOT usable by this person: unknown, someone else's, already attached to a ticket, or removed.</summary>
    public async Task<IReadOnlyList<Guid>> UnusableAsync(IReadOnlyCollection<Guid> ids, Guid userId, CancellationToken ct = default)
    {
        if (ids.Count == 0) return [];
        var ok = await db.Attachments.AsNoTracking().Where(a => ids.Contains(a.Id) && a.UploadedById == userId && a.TicketId == null && a.DeletedAt == null).Select(a => a.Id).ToListAsync(ct);
        return [.. ids.Except(ok)];
    }

    /// <summary>Attaches the person's unclaimed uploads to the new ticket. Call inside the unit of work that creates it; throws if any is no longer usable.</summary>
    public async Task ClaimAsync(Guid ticketId, IReadOnlyCollection<Guid> ids, Guid userId, CancellationToken ct = default)
    {
        if (ids.Count == 0) return;
        var rows = await db.Attachments.Where(a => ids.Contains(a.Id) && a.UploadedById == userId && a.TicketId == null && a.DeletedAt == null).ToListAsync(ct);
        if (rows.Count != ids.Count) throw DomainException.Validation("An uploaded file is no longer available. Please upload it again.", new Dictionary<string, string> { ["files"] = "An uploaded file is no longer available." });
        foreach (var a in rows) a.TicketId = ticketId;
    }

    /// <summary>Removes uploads nobody claimed within a day (bytes and rows), across all organisations. Idempotent.</summary>
    public async Task<int> PurgeOrphansAsync(CancellationToken ct = default)
    {
        var cutoff = clock.UtcNow - OrphanLifetime;
        var old = await db.Attachments.IgnoreQueryFilters().Where(a => a.TicketId == null && a.DeletedAt == null && a.CreatedAt < cutoff).Take(500).ToListAsync(ct);
        foreach (var a in old) await storage.DeleteAsync(a.StorageKey, ct);
        db.Attachments.RemoveRange(old);
        await db.SaveChangesAsync(ct);
        return old.Count;
    }
}
