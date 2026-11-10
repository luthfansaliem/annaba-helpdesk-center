using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Application.Users;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;

namespace AnnabaDigital.Application.Helpdesk;

public sealed record InboundFile(string Name, string ContentType, byte[] Content);
public sealed record InboundMail(string? MessageId, string FromAddress, string? FromName, string Subject, string Text, IReadOnlyList<InboundFile> Files, bool Automated);
/// <summary>Reads raw MIME (implemented with MimeKit in Infrastructure). Null when it is not an e-mail with a sender.</summary>
public interface IInboundMailParser { Task<InboundMail?> ParseAsync(Stream raw, CancellationToken ct = default); }

public sealed record InboundResult(string Outcome, string? Reason, string? TicketNumber);
public sealed record InboundMessageItem(Guid Id, DateTimeOffset ReceivedAt, string FromAddress, string Subject, string Outcome, string? Reason, Guid? TicketId, string? TicketNumber);
public sealed record MailboxDetail(bool Configured, bool Enabled, string? Address, Guid? DefaultServiceId, string? DefaultServiceName, string? TokenHint, IReadOnlyList<InboundMessageItem> Recent, int Version);
public sealed record SaveMailboxRequest(string? Address, Guid? DefaultServiceId, bool Enabled, int? Version = null);
public sealed record MailboxToken(string Path, string TokenHint);

/// <summary>
/// TKT-004 (by e-mail). Turns e-mail into requests and replies. The mail service posts each message (raw MIME) to <c>/api/inbound/email/{token}</c>; the token picks the
/// organisation. Only active people of that organisation are heard (by their sign-in e-mail); automatic mail, duplicates (same Message-ID) and floods are ignored. A subject
/// with a ticket number the sender may see adds the new text of the reply as a comment; anything else becomes a request on the mailbox's service. Every message is logged.
/// </summary>
public sealed class EmailIntakeService(IAppDb db, IClock clock, AuditWriter audit, ITenantContext tenant, IInboundMailParser parser, TicketService tickets, AttachmentService attachments,
    NotificationService notifications, SettingsProvider settings)
{
    private static readonly ClientInfo Mail = new(null, "inbound e-mail", null);
    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    // ================================================================ settings (settings.manage)
    public async Task<MailboxDetail> GetAsync(CancellationToken ct = default)
    {
        var m = await db.InboundMailboxes.AsNoTracking().FirstOrDefaultAsync(ct);
        if (m is null) return new MailboxDetail(false, false, null, null, null, null, [], 0);
        var svc = m.DefaultServiceId is { } sid ? await db.Services.AsNoTracking().Where(s => s.Id == sid).Select(s => s.Name).FirstOrDefaultAsync(ct) : null;
        var recent = await db.InboundMessages.AsNoTracking().Where(x => x.MailboxId == m.Id).OrderByDescending(x => x.ReceivedAt).Take(50)
            .Select(x => new InboundMessageItem(x.Id, x.ReceivedAt, x.FromAddress, x.Subject, x.Outcome, x.Reason, x.TicketId, db.Tickets.Where(t => t.Id == x.TicketId).Select(t => t.Number).FirstOrDefault())).ToListAsync(ct);
        return new MailboxDetail(true, m.Enabled, m.Address, m.DefaultServiceId, svc, m.TokenHint, recent, m.Version);
    }

    public async Task<MailboxDetail> SaveAsync(SaveMailboxRequest r, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        var e = new Dictionary<string, string>();
        var address = (r.Address ?? "").Trim().ToLowerInvariant();
        if (address.Length is < 3 or > 200 || address.Count(c => c == '@') != 1 || address.StartsWith('@') || address.EndsWith('@') || address.Any(char.IsWhiteSpace)) e["address"] = "Use an e-mail address like help@example.com.";
        if (r.DefaultServiceId is not { } sid || !await db.Services.AnyAsync(s => s.Id == sid && s.ArchivedAt == null && s.IsPublished, ct)) e["defaultServiceId"] = "Choose a published service for new requests.";
        if (e.Count > 0) throw DomainException.Validation("Please correct the highlighted fields.", e);
        var m = await db.InboundMailboxes.FirstOrDefaultAsync(ct);
        var now = clock.UtcNow;
        if (m is null) { m = new InboundMailbox { CreatedAt = now }; db.InboundMailboxes.Add(m); }
        else
        {
            if (r.Version is { } v && v != m.Version) throw DomainException.Conflict("The mailbox was changed by someone else. Reload and try again.");
            m.Version++;
        }
        if (r.Enabled && m.TokenHash.Length == 0) throw DomainException.Conflict("Create the webhook address first, then switch the mailbox on.");
        m.Address = address; m.DefaultServiceId = r.DefaultServiceId; m.Enabled = r.Enabled; m.UpdatedAt = now;
        audit.Record("email.inbound.settings", "inbound_mailbox", m.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["address"] = address, ["enabled"] = r.Enabled.ToString() });
        await db.SaveChangesAsync(ct);
        return await GetAsync(ct);
    }

    /// <summary>A new secret webhook path (the old one stops working). Shown once; needs a recent password re-entry.</summary>
    public async Task<MailboxToken> RotateTokenAsync(SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        Guards.RequireRecentReauthForCritical(actor, await settings.GetAsync(ct), clock);
        var m = await db.InboundMailboxes.FirstOrDefaultAsync(ct);
        if (m is null) { m = new InboundMailbox { CreatedAt = clock.UtcNow }; db.InboundMailboxes.Add(m); } else m.Version++;
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        m.TokenHash = Hash(token); m.TokenHint = token[^4..]; m.UpdatedAt = clock.UtcNow;
        audit.Record("email.inbound.token", "inbound_mailbox", m.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["hint"] = m.TokenHint });
        await db.SaveChangesAsync(ct);
        return new MailboxToken($"/api/inbound/email/{token}", m.TokenHint);
    }

    // ================================================================ receiving
    public async Task<InboundResult> ReceiveAsync(string token, Stream raw, long size, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 100) throw DomainException.NotFound("Mailbox");
        var hash = Hash(token);
        var box = await db.InboundMailboxes.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(x => x.TokenHash == hash, ct);
        if (box is null || !box.Enabled) throw DomainException.NotFound("Mailbox");
        if (size > EmailIntakeRules.MaxBytes) throw new DomainException(ErrorCodes.Validation, "The message is larger than 10 MB.", 413);
        using (tenant.Switch(box.TenantId))
        {
            db.ResetTracking();
            var now = clock.UtcNow;
            if (await db.InboundMessages.CountAsync(x => x.MailboxId == box.Id && x.ReceivedAt > now.AddHours(-1), ct) >= EmailIntakeRules.MaxPerMailboxPerHour)
                throw new DomainException(ErrorCodes.RateLimited, "Too many messages this hour; try again later.", 429);
            var mail = await parser.ParseAsync(raw, ct) ?? throw DomainException.Validation("This is not an e-mail message with a sender.", new Dictionary<string, string> { ["message"] = "Send the raw MIME message." });
            var messageId = (mail.MessageId ?? $"sha:{hash[..16]}:{mail.FromAddress}:{mail.Subject}").Trim();
            if (messageId.Length > 300) messageId = messageId[..300];
            if (await db.InboundMessages.AnyAsync(x => x.MailboxId == box.Id && x.MessageId == messageId, ct)) return new InboundResult("duplicate", "This message was already received.", null);

            var log = new InboundMessage { MailboxId = box.Id, MessageId = messageId, FromAddress = Clip(mail.FromAddress, 200), Subject = Clip(mail.Subject, 200), Size = size, ReceivedAt = now };
            var result = await HandleAsync(box, mail, log, now, ct);
            log.Outcome = result.Outcome; log.Reason = result.Reason is null ? null : Clip(result.Reason, 300);
            db.InboundMessages.Add(log);
            audit.Record("email.inbound", "inbound_message", log.Id.ToString(), result.Outcome is "rejected" ? Outcome.Failure : Outcome.Success, Mail, log.UserId, mail.FromAddress,
                new Dictionary<string, string> { ["outcome"] = result.Outcome, ["reason"] = result.Reason ?? "", ["ticket"] = result.TicketNumber ?? "" });
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException) { return new InboundResult("duplicate", "This message was already received.", null); }
            return result;
        }
    }

    private static string Clip(string s, int max) => s.Length <= max ? s : s[..max];

    private async Task<InboundResult> HandleAsync(InboundMailbox box, InboundMail mail, InboundMessage log, DateTimeOffset now, CancellationToken ct)
    {
        if (mail.Automated) return new("ignored", "Automatic mail (auto-reply, bounce or list) is not answered.", null);
        var from = mail.FromAddress;
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Email.ToLower() == from && u.Status == UserStatus.Active, ct);
        if (user is null) return new("rejected", "The sender is not an active person of this organisation.", null);
        log.UserId = user.Id;
        if (await db.InboundMessages.CountAsync(x => x.MailboxId == box.Id && x.UserId == user.Id && x.ReceivedAt > now.AddHours(-1), ct) >= EmailIntakeRules.MaxPerSenderPerHour)
            return new("rejected", "Too many messages from this sender this hour.", null);
        var who = new SessionPrincipal(Guid.Empty, user.Id, user.Email, user.Name, false, false, null, TenantId: user.TenantId);

        // a reply to a ticket the sender can see
        if (EmailIntakeRules.TicketNumberIn(mail.Subject) is { } number
            && await (await tickets.SeenTicketsAsync(who, ct)).Where(t => t.Number == number).Select(t => new { t.Id, t.Status }).FirstOrDefaultAsync(ct) is { } ticket)
        {
            log.TicketId = ticket.Id;
            var text = EmailIntakeRules.NewText(mail.Text);
            if (text.Length == 0 && mail.Files.Count == 0) return new("ignored", "The reply has no new text.", number);
            if (TicketWorkflow.IsFinished(ticket.Status))
            {
                await notifications.SendAsync(user.Id, "ticket.reply", $"Your e-mail about {number} was not added", "The request is finished. Reopen it or make a new request in the portal.", $"/tickets/{ticket.Id}", ticket.Id, ct: ct);
                return new("rejected", "The ticket is finished.", number);
            }
            var skipped = await AttachAsync(ticket.Id, mail.Files, who, ct);
            var body = text.Length == 0 ? "(attachments only)" : text;
            if (skipped.Count > 0) body += $"\n\n[Not attached: {string.Join(", ", skipped)}]";
            await tickets.CommentAsync(ticket.Id, new CommentRequest(Clip(body + "\n\n(by e-mail)", EmailIntakeRules.BodyMax), false), who, Mail, ct);
            return new("replied", skipped.Count > 0 ? $"Some attachments were refused: {string.Join(", ", skipped)}" : null, number);
        }

        // a new request
        var subject = EmailIntakeRules.CleanSubject(mail.Subject);
        if (subject.Length < 3) subject = $"E-mail from {mail.FromName ?? from}";
        var description = mail.Text.Length == 0 ? null : Clip(mail.Text, EmailIntakeRules.BodyMax);
        TicketDetail created;
        try { created = await tickets.CreateAsync(box.DefaultServiceId!.Value, new CreateTicketRequest(Clip(subject, 200), description, new(), null, null), who, Mail, ct); }
        catch (DomainException ex) when (ex.Code is ErrorCodes.Validation or ErrorCodes.Denied or ErrorCodes.NotFound)
        {
            db.ResetTracking();
            await notifications.SendAsync(user.Id, "ticket.reply", "Your e-mail could not be turned into a request", "Please make the request in the portal: the service needs more details than an e-mail gives.", "/requests/new", null, ct: ct);
            return new("rejected", $"The request could not be made: {ex.Message}", null);
        }
        log.TicketId = created.Id;
        var refused = await AttachAsync(created.Id, mail.Files, who, ct);
        return new("created", refused.Count > 0 ? $"Some attachments were refused: {string.Join(", ", refused)}" : null, created.Number);
    }

    /// <summary>Stores the files on the ticket with the usual checks; returns the names that were refused (type, size, virus, the per-ticket limit).</summary>
    private async Task<List<string>> AttachAsync(Guid ticketId, IReadOnlyList<InboundFile> files, SessionPrincipal who, CancellationToken ct)
    {
        var refused = new List<string>();
        foreach (var f in files)
        {
            try { using var s = new MemoryStream(f.Content); await attachments.StoreAsync(ticketId, f.Name, s, who, Mail, ct); }
            catch (DomainException) { refused.Add(f.Name); }
        }
        return refused;
    }
}
