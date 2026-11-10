using System.Text;

namespace AnnabaDigital.Domain;

/// <summary>What a file check concluded: the content type we will serve it as (never the one the sender claimed) and whether browsers may show it inline.</summary>
public sealed record FileVerdict(bool Ok, string? ContentType, bool Inline, string? Error);

/// <summary>
/// TKT-005. Which files are accepted and how they are checked, pure. A file is accepted only when its <b>extension</b> is on the allow-list <i>and</i> its first bytes
/// match what that extension should start with — so a program renamed to <c>.png</c> is refused — and it is then served under the content type <i>we</i> chose for that
/// extension, not the one the sender claimed. Only images are ever shown inline; everything else downloads.
/// </summary>
public static class FileRules
{
    public const long MaxBytes = 8 * 1024 * 1024;          // kept under the web proxy's request-body limit, with room for the multipart envelope
    public const int MaxPerTicket = 20, MaxFileNameLength = 150, HeaderBytes = 4096;

    private sealed record Kind(string ContentType, bool Inline, Func<byte[], bool> Matches);

    private static bool Starts(byte[] h, params byte[] sig) => h.Length >= sig.Length && h.AsSpan(0, sig.Length).SequenceEqual(sig);
    private static bool Ascii(byte[] h, int at, string s) => h.Length >= at + s.Length && Encoding.ASCII.GetString(h, at, s.Length) == s;
    private static readonly byte[] Zip = [0x50, 0x4B, 0x03, 0x04], Ole = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];
    private static bool Text(byte[] h) => h.All(b => b != 0 && (b >= 0x20 || b is 0x09 or 0x0A or 0x0D));   // plain text has no NUL bytes or stray control characters

    private static readonly Dictionary<string, Kind> Kinds = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = new("image/jpeg", true, h => Starts(h, 0xFF, 0xD8, 0xFF)), [".jpeg"] = new("image/jpeg", true, h => Starts(h, 0xFF, 0xD8, 0xFF)),
        [".png"] = new("image/png", true, h => Starts(h, 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A)),
        [".gif"] = new("image/gif", true, h => Ascii(h, 0, "GIF87a") || Ascii(h, 0, "GIF89a")),
        [".webp"] = new("image/webp", true, h => Ascii(h, 0, "RIFF") && Ascii(h, 8, "WEBP")),
        [".pdf"] = new("application/pdf", false, h => Ascii(h, 0, "%PDF-")),
        [".docx"] = new("application/vnd.openxmlformats-officedocument.wordprocessingml.document", false, h => Starts(h, Zip)),
        [".xlsx"] = new("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", false, h => Starts(h, Zip)),
        [".pptx"] = new("application/vnd.openxmlformats-officedocument.presentationml.presentation", false, h => Starts(h, Zip)),
        [".doc"] = new("application/msword", false, h => Starts(h, Ole)), [".xls"] = new("application/vnd.ms-excel", false, h => Starts(h, Ole)), [".ppt"] = new("application/vnd.ms-powerpoint", false, h => Starts(h, Ole)),
        [".txt"] = new("text/plain; charset=utf-8", false, Text), [".csv"] = new("text/csv; charset=utf-8", false, Text),
    };

    public static IReadOnlyCollection<string> AllowedExtensions => Kinds.Keys;

    /// <summary>The name to store and show: no folders, no control characters, at most 150 characters (the extension is kept), never empty.</summary>
    public static string SafeName(string? name)
    {
        var n = (name ?? "").Replace('\\', '/'); n = n[(n.LastIndexOf('/') + 1)..];
        n = new string([.. n.Where(c => !char.IsControl(c) && c is not ('<' or '>' or '"' or ':' or '|' or '?' or '*'))]).Trim().Trim('.');
        if (n.Length == 0) return "file";
        if (n.Length <= MaxFileNameLength) return n;
        var ext = Path.GetExtension(n); var stem = n[..^ext.Length];
        return stem[..Math.Max(1, MaxFileNameLength - ext.Length)] + ext;
    }

    /// <summary>Checks a file by name, size and first bytes. <paramref name="header"/> is the beginning of the content (up to <see cref="HeaderBytes"/>).</summary>
    public static FileVerdict Check(string? fileName, long size, byte[] header)
    {
        if (size <= 0) return new(false, null, false, "The file is empty.");
        if (size > MaxBytes) return new(false, null, false, $"The file is larger than {MaxBytes / 1024 / 1024} MB.");
        var ext = Path.GetExtension(SafeName(fileName));
        if (ext.Length == 0 || !Kinds.TryGetValue(ext, out var kind)) return new(false, null, false, "This type of file is not accepted. Use an image, PDF, Office document, text or CSV file.");
        if (!kind.Matches(header)) return new(false, null, false, "The content does not match the file type.");
        return new(true, kind.ContentType, kind.Inline, null);
    }
}
