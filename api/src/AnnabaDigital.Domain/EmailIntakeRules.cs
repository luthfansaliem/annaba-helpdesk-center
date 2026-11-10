using System.Text.RegularExpressions;

namespace AnnabaDigital.Domain;

/// <summary>TKT-004 (by e-mail). Pure rules for incoming e-mail: which ticket a reply belongs to, what is the new text of a reply, and which mail is automatic.</summary>
public static partial class EmailIntakeRules
{
    public const long MaxBytes = 10 * 1024 * 1024;
    public const int MaxPerSenderPerHour = 20, MaxPerMailboxPerHour = 500, BodyMax = 4000;

    [GeneratedRegex(@"\b([A-Z]{2,5}-\d{4}-\d{6})\b")] private static partial Regex TicketNumber();
    [GeneratedRegex(@"^\s*(re|fw|fwd|aw|wg|bls|balas|tr)\s*:\s*", RegexOptions.IgnoreCase)] private static partial Regex ReplyPrefix();
    [GeneratedRegex(@"^(On .{4,200} wrote:|Pada .{4,200} menulis:|-{2,}\s*Original Message\s*-{2,}|-{2,}\s*Pesan Asli\s*-{2,}|From:\s.+|Dari:\s.+|_{10,})\s*$", RegexOptions.IgnoreCase)] private static partial Regex QuoteStart();

    /// <summary>The ticket number in a subject such as "Re: New reply on GA-2026-000123".</summary>
    public static string? TicketNumberIn(string? subject) => subject is null ? null : TicketNumber().Match(subject) is { Success: true } m ? m.Groups[1].Value : null;

    /// <summary>The subject without "Re:", "Fwd:" … prefixes (repeated ones too).</summary>
    public static string CleanSubject(string? subject)
    {
        var s = (subject ?? "").Trim();
        for (var i = 0; i < 5 && ReplyPrefix().IsMatch(s); i++) s = ReplyPrefix().Replace(s, "", 1).Trim();
        return s;
    }

    /// <summary>What the person wrote in a reply: the text above the quoted original ("On … wrote:", "-----Original Message-----", "From:" headers, "> " lines) and above the "-- " signature line.</summary>
    public static string NewText(string? text)
    {
        var lines = (text ?? "").Replace("\r\n", "\n").Split('\n');
        var kept = new List<string>();
        foreach (var line in lines)
        {
            if (line == "-- " || line.TrimEnd() == "--" || QuoteStart().IsMatch(line.Trim())) break;
            if (line.TrimStart().StartsWith('>')) continue;
            kept.Add(line.TrimEnd());
        }
        return string.Join('\n', kept).Trim();
    }

    /// <summary>Mail no person wrote: auto-replies, bounces, lists and bulk mail. Answering them would start mail loops.</summary>
    public static bool IsAutomated(string? autoSubmitted, string? precedence, bool hasListId, string fromAddress, bool hasAutoReplyHeader)
    {
        if (!string.IsNullOrWhiteSpace(autoSubmitted) && !autoSubmitted.Trim().Equals("no", StringComparison.OrdinalIgnoreCase)) return true;
        if (precedence?.Trim().ToLowerInvariant() is "bulk" or "junk" or "list" or "auto_reply") return true;
        if (hasListId || hasAutoReplyHeader) return true;
        var local = fromAddress.Split('@')[0].ToLowerInvariant();
        return local is "mailer-daemon" or "postmaster" or "noreply" or "no-reply" or "do-not-reply" or "donotreply" or "bounce" or "bounces";
    }
}
