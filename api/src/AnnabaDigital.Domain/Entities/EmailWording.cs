namespace AnnabaDigital.Domain.Entities;

/// <summary>
/// NTF-002. How one organisation's e-mails read, on top of the product's own texts: the name in the sign-off, a prefix on every subject (handy for inbox rules) and a closing
/// paragraph per language (for example who to call). Plain text only; it is escaped wherever it is shown. One row per organisation; no row means the product's wording.
/// </summary>
public class EmailWording : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string? SenderName { get; set; }
    public string? SubjectPrefix { get; set; }
    public string? FooterEn { get; set; }
    public string? FooterId { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}

public static class EmailWordingRules
{
    public const int SenderMax = 60, PrefixMax = 30, FooterMax = 400, FooterLinesMax = 6;

    /// <summary>Trims; empty becomes null. Line breaks are normalised to \n.</summary>
    public static string? Clean(string? s) { var t = (s ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Trim(); return t.Length == 0 ? null : t; }

    private static bool HasControl(string s, bool allowNewline) => s.Any(c => char.IsControl(c) && !(allowNewline && c == '\n'));

    /// <summary>The problems with the proposed wording, by field.</summary>
    public static Dictionary<string, string> Validate(string? sender, string? prefix, string? footerEn, string? footerId)
    {
        var f = new Dictionary<string, string>();
        if (sender is not null && (sender.Length > SenderMax || HasControl(sender, false) || sender.Contains('\n'))) f["senderName"] = $"Use one line of at most {SenderMax} characters.";
        if (prefix is not null && (prefix.Length > PrefixMax || HasControl(prefix, false) || prefix.Contains('\n'))) f["subjectPrefix"] = $"Use one line of at most {PrefixMax} characters.";
        foreach (var (key, v) in new[] { ("footerEn", footerEn), ("footerId", footerId) })
            if (v is not null && (v.Length > FooterMax || HasControl(v, true) || v.Count(c => c == '\n') + 1 > FooterLinesMax)) f[key] = $"Use at most {FooterMax} characters on {FooterLinesMax} lines.";
        return f;
    }
}
