using System.Net;
using System.Text.RegularExpressions;
using MimeKit;
using AnnabaDigital.Application.Helpdesk;
using AnnabaDigital.Domain;

namespace AnnabaDigital.Infrastructure.Email;

/// <summary>Reads a raw RFC 5322 message with MimeKit: sender, subject, plain text (or text made from the HTML part), attachments, and the headers that mark automatic mail.</summary>
public sealed partial class InboundMailParser : IInboundMailParser
{
    public const int MaxAttachments = 10;
    [GeneratedRegex("<(script|style)[^>]*>.*?</\\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline)] private static partial Regex ScriptOrStyle();
    [GeneratedRegex("<br\\s*/?>|</p>|</div>|</li>|</tr>|</h[1-6]>", RegexOptions.IgnoreCase)] private static partial Regex LineBreaks();
    [GeneratedRegex("<[^>]+>")] private static partial Regex Tags();
    [GeneratedRegex("\\n{3,}")] private static partial Regex BlankRuns();

    public async Task<InboundMail?> ParseAsync(Stream raw, CancellationToken ct = default)
    {
        MimeMessage m;
        try { m = await MimeMessage.LoadAsync(raw, ct); }
        catch (FormatException) { return null; }
        var from = m.From.Mailboxes.FirstOrDefault();
        if (from is null || string.IsNullOrWhiteSpace(from.Address) || !from.Address.Contains('@')) return null;

        var text = m.TextBody;
        if (string.IsNullOrWhiteSpace(text) && m.HtmlBody is { } html)
        {
            var t = ScriptOrStyle().Replace(html, "");
            t = LineBreaks().Replace(t, "\n");
            text = WebUtility.HtmlDecode(Tags().Replace(t, ""));
            text = BlankRuns().Replace(text.Replace("\r\n", "\n"), "\n\n");
        }
        var files = new List<InboundFile>();
        foreach (var part in m.Attachments.OfType<MimePart>().Take(MaxAttachments))
        {
            using var ms = new MemoryStream();
            await part.Content.DecodeToAsync(ms, ct);
            files.Add(new InboundFile(part.FileName ?? "attachment", part.ContentType.MimeType, ms.ToArray()));
        }
        var h = m.Headers;
        var automated = EmailIntakeRules.IsAutomated(h[HeaderId.AutoSubmitted], h[HeaderId.Precedence], h.Contains(HeaderId.ListId), from.Address,
            h.Contains("X-Autoreply") || h.Contains("X-Autorespond") || (h["X-Auto-Response-Suppress"] ?? "").Contains("All", StringComparison.OrdinalIgnoreCase) && h.Contains("X-MS-Exchange-Inbox-Rules-Loop"));
        var id = string.IsNullOrWhiteSpace(m.MessageId) ? null : m.MessageId.Trim();
        return new InboundMail(id, from.Address.Trim().ToLowerInvariant(), string.IsNullOrWhiteSpace(from.Name) ? null : from.Name.Trim(), m.Subject ?? "", (text ?? "").Trim(), files, automated);
    }
}
