using AnnabaDigital.Domain;

namespace AnnabaDigital.Application.Profile;

public sealed record ImageInfo(string ContentType, int Width, int Height);

/// <summary>
/// Strict avatar validation without a decoding library: magic bytes, bounded size, and dimensions parsed from the container headers.
/// A file is accepted only if its bytes really are a PNG, JPEG or WebP header of sane dimensions — the declared Content-Type is never trusted.
/// (Files are not re-encoded; they are served with an exact image type, nosniff and a locked-down CSP.)
/// </summary>
public static class ImageInspector
{
    public const int MaxBytes = 256 * 1024;
    public const int MinSide = 32, MaxSide = 2048;

    public static ImageInfo Inspect(byte[] b)
    {
        if (b.Length == 0) throw Bad("Choose an image to upload.");
        if (b.Length > MaxBytes) throw Bad("The image must be 256 KB or smaller.");
        var info = Png(b) ?? Jpeg(b) ?? WebP(b) ?? throw Bad("Use a PNG, JPEG or WebP image.");
        if (info.Width < MinSide || info.Height < MinSide || info.Width > MaxSide || info.Height > MaxSide)
            throw Bad($"The image must be between {MinSide}×{MinSide} and {MaxSide}×{MaxSide} pixels.");
        return info;
    }

    private static DomainException Bad(string msg) => DomainException.Validation(msg, new Dictionary<string, string> { ["file"] = msg });
    private static int BE32(byte[] b, int o) => (b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3];

    private static ImageInfo? Png(byte[] b)
    {
        ReadOnlySpan<byte> sig = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        if (b.Length < 33 || !b.AsSpan(0, 8).SequenceEqual(sig)) return null;
        if (!(b[12] == 'I' && b[13] == 'H' && b[14] == 'D' && b[15] == 'R') || BE32(b, 8) != 13) return null; // first chunk must be a 13-byte IHDR
        var w = BE32(b, 16); var h = BE32(b, 20);
        return w <= 0 || h <= 0 ? null : new ImageInfo("image/png", w, h);
    }

    private static ImageInfo? Jpeg(byte[] b)
    {
        if (b.Length < 4 || b[0] != 0xFF || b[1] != 0xD8 || b[2] != 0xFF) return null;
        var i = 2;
        while (i + 3 < b.Length)
        {
            if (b[i] != 0xFF) { i++; continue; }
            var marker = b[i + 1];
            if (marker == 0xFF) { i++; continue; }
            if (marker is 0xD8 or 0x01 || (marker >= 0xD0 && marker <= 0xD7)) { i += 2; continue; }
            if (i + 4 > b.Length) return null;
            var len = (b[i + 2] << 8) | b[i + 3]; if (len < 2) return null;
            var isSof = marker is >= 0xC0 and <= 0xCF && marker is not (0xC4 or 0xC8 or 0xCC);
            if (isSof) { if (i + 9 > b.Length) return null; var h = (b[i + 5] << 8) | b[i + 6]; var w = (b[i + 7] << 8) | b[i + 8]; return w > 0 && h > 0 ? new ImageInfo("image/jpeg", w, h) : null; }
            i += 2 + len;
        }
        return null;
    }

    private static ImageInfo? WebP(byte[] b)
    {
        if (b.Length < 30 || !(b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F') || !(b[8] == 'W' && b[9] == 'E' && b[10] == 'B' && b[11] == 'P')) return null;
        string tag = $"{(char)b[12]}{(char)b[13]}{(char)b[14]}{(char)b[15]}";
        switch (tag)
        {
            case "VP8 ": // lossy: 3-byte frame tag, start code 9D 01 2A, then 14-bit width/height
                if (b[23] != 0x9D || b[24] != 0x01 || b[25] != 0x2A) return null;
                return new ImageInfo("image/webp", (b[26] | (b[27] << 8)) & 0x3FFF, (b[28] | (b[29] << 8)) & 0x3FFF);
            case "VP8L": // lossless: signature 0x2F then 14+14 bits
                if (b[20] != 0x2F) return null;
                var bits = b[21] | (b[22] << 8) | (b[23] << 16) | (b[24] << 24);
                return new ImageInfo("image/webp", (bits & 0x3FFF) + 1, ((bits >> 14) & 0x3FFF) + 1);
            case "VP8X": // extended: 24-bit canvas width-1 / height-1
                return new ImageInfo("image/webp", (b[24] | (b[25] << 8) | (b[26] << 16)) + 1, (b[27] | (b[28] << 8) | (b[29] << 16)) + 1);
            default: return null;
        }
    }
}
