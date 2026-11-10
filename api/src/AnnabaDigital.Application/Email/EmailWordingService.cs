using System.Net;
using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;

namespace AnnabaDigital.Application.Email;

public sealed record EmailWordingView(string? SenderName, string? SubjectPrefix, string? FooterEn, string? FooterId, DateTimeOffset? UpdatedAt);
public sealed record SaveEmailWording(string? SenderName, string? SubjectPrefix, string? FooterEn, string? FooterId);
public sealed record EmailPreviewVersion(string Lang, string Subject, string Text);
public sealed record EmailWordingPreview(IReadOnlyList<EmailPreviewVersion> Versions);

/// <summary>
/// NTF-002. Per-organisation wording for outgoing e-mail. It is applied once, where a message is queued (so every kind of mail, helpdesk or account, carries it), after the message is in
/// the recipient's language: a prefix on the subject, the organisation's closing paragraph in that language (the other language if only one was written), and its name in the sign-off.
/// The text is plain and escaped; it never changes links or the product's own sentences.
/// </summary>
public sealed class EmailWordingService(IAppDb db, IClock clock, AuditWriter audit, SettingsProvider settings, ITenantContext tenant)
{
    public const string ProductName = "Annaba Digital";
    private const string SignOffMarker = "\n\n— " + ProductName;

    // ================================================================ applying (pure)
    public static EmailMessage Apply(EmailMessage m, EmailWording? w, string lang)
    {
        if (w is null) return m;
        var footer = lang == "id" ? (w.FooterId ?? w.FooterEn) : (w.FooterEn ?? w.FooterId);
        var sender = w.SenderName; var prefix = w.SubjectPrefix;
        if (footer is null && sender is null && prefix is null) return m;

        var subject = prefix is null ? m.Subject : $"{prefix} {m.Subject}";
        var text = m.Text;
        if (text.EndsWith(SignOffMarker, StringComparison.Ordinal)) text = text[..^SignOffMarker.Length] + (footer is null ? "" : "\n\n" + footer) + "\n\n— " + (sender ?? ProductName);
        else text += (footer is null ? "" : "\n\n" + footer) + (sender is null ? "" : "\n\n— " + sender);

        var html = m.Html;
        var extra = (footer is null ? "" : $"<p style=\"color:#5d6b88;white-space:pre-line\">{WebUtility.HtmlEncode(footer)}</p>") + (sender is null ? "" : $"<p style=\"font-size:12px;color:#5d6b88\">— {WebUtility.HtmlEncode(sender)}</p>");
        var close = html.LastIndexOf("</div>", StringComparison.Ordinal);
        html = close >= 0 ? html.Insert(close, extra) : html + extra;
        return m with { Subject = subject, Text = text, Html = html };
    }

    // ================================================================ the administrator's screen
    public async Task<EmailWordingView> GetAsync(CancellationToken ct = default)
    {
        var w = await db.EmailWordings.AsNoTracking().FirstOrDefaultAsync(ct);
        return w is null ? new EmailWordingView(null, null, null, null, null) : new EmailWordingView(w.SenderName, w.SubjectPrefix, w.FooterEn, w.FooterId, w.UpdatedAt);
    }

    private static (string? Sender, string? Prefix, string? FooterEn, string? FooterId) Clean(SaveEmailWording r)
        => (EmailWordingRules.Clean(r.SenderName), EmailWordingRules.Clean(r.SubjectPrefix), EmailWordingRules.Clean(r.FooterEn), EmailWordingRules.Clean(r.FooterId));

    public async Task<EmailWordingView> UpdateAsync(SaveEmailWording req, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        Guards.RequireRecentReauthForCritical(actor, await settings.GetAsync(ct), clock);
        var (sender, prefix, fen, fid) = Clean(req);
        var f = EmailWordingRules.Validate(sender, prefix, fen, fid);
        if (f.Count > 0) throw DomainException.Validation("Please correct the highlighted fields.", f);

        var w = await db.EmailWordings.FirstOrDefaultAsync(ct);
        var diff = new List<AuditDiff>();
        void D(string field, string? before, string? after) { if (before != after) diff.Add(new AuditDiff(field, before ?? "(default)", after ?? "(default)")); }
        D("senderName", w?.SenderName, sender); D("subjectPrefix", w?.SubjectPrefix, prefix); D("footerEn", w?.FooterEn, fen); D("footerId", w?.FooterId, fid);
        if (diff.Count == 0) throw DomainException.Validation("No changes to save.");

        if (w is null) { w = new EmailWording { TenantId = tenant.TenantId }; db.EmailWordings.Add(w); }
        w.SenderName = sender; w.SubjectPrefix = prefix; w.FooterEn = fen; w.FooterId = fid; w.UpdatedAt = clock.UtcNow; w.UpdatedBy = actor.UserId;
        audit.Record("settings.email_wording.update", "tenant", tenant.TenantId.ToString(), Outcome.Success, client, actor.UserId, actor.Email, diff: diff);
        await db.SaveChangesAsync(ct);
        return await GetAsync(ct);
    }

    /// <summary>What a typical message (a resolved request) would look like with the proposed wording, in both languages. Nothing is saved or sent.</summary>
    public EmailWordingPreview Preview(SaveEmailWording req)
    {
        var (sender, prefix, fen, fid) = Clean(req);
        var f = EmailWordingRules.Validate(sender, prefix, fen, fid);
        if (f.Count > 0) throw DomainException.Validation("Please correct the highlighted fields.", f);
        var w = new EmailWording { SenderName = sender, SubjectPrefix = prefix, FooterEn = fen, FooterId = fid };
        var sample = EmailTemplates.TicketResolved("sample@example.org", "Alex", "GA-2026-000123", "Air conditioning in meeting room 3", "https://helpdesk.example.org/tickets/sample");
        EmailPreviewVersion One(string lang) { var m = Apply(lang == "id" ? sample.Localize!(lang)! : sample, w, lang); return new EmailPreviewVersion(lang, m.Subject, m.Text); }
        return new EmailWordingPreview([One("en"), One("id")]);
    }
}
