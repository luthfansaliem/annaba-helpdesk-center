using AnnabaDigital.Application.Profile;
using AnnabaDigital.Domain;
using Xunit;

namespace AnnabaDigital.Tests;

public sealed class ImageInspectorTests
{
    // --- tiny synthetic headers: only the bytes the inspector reads, followed by padding
    public static byte[] Png(int w, int h, int pad = 20)
    {
        var b = new List<byte> { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R' };
        b.AddRange([(byte)(w >> 24), (byte)(w >> 16), (byte)(w >> 8), (byte)w, (byte)(h >> 24), (byte)(h >> 16), (byte)(h >> 8), (byte)h, 8, 6, 0, 0, 0]);
        b.AddRange(new byte[pad]); return [.. b];
    }
    public static byte[] Jpeg(int w, int h)
    {
        var b = new List<byte> { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10 }; b.AddRange("JFIF\0"u8.ToArray()); b.AddRange(new byte[] { 1, 1, 0, 0, 1, 0, 1, 0, 0 }); // APP0 (16 bytes)
        b.AddRange(new byte[] { 0xFF, 0xC0, 0x00, 0x11, 8, (byte)(h >> 8), (byte)h, (byte)(w >> 8), (byte)w, 3, 1, 0x22, 0, 2, 0x11, 1, 3, 0x11, 1, 0xFF, 0xD9 });
        return [.. b];
    }
    public static byte[] WebPLossy(int w, int h)
    {
        var b = new List<byte>("RIFF"u8.ToArray()); b.AddRange([0, 0, 0, 0]); b.AddRange("WEBP"u8.ToArray()); b.AddRange("VP8 "u8.ToArray()); b.AddRange([20, 0, 0, 0]);
        b.AddRange([0, 0, 0, 0x9D, 0x01, 0x2A, (byte)w, (byte)(w >> 8), (byte)h, (byte)(h >> 8)]); b.AddRange(new byte[20]); return [.. b];
    }
    public static byte[] WebPLossless(int w, int h)
    {
        var bits = (w - 1) | ((h - 1) << 14);
        var b = new List<byte>("RIFF"u8.ToArray()); b.AddRange([0, 0, 0, 0]); b.AddRange("WEBP"u8.ToArray()); b.AddRange("VP8L"u8.ToArray()); b.AddRange([10, 0, 0, 0]);
        b.AddRange([0x2F, (byte)bits, (byte)(bits >> 8), (byte)(bits >> 16), (byte)(bits >> 24)]); b.AddRange(new byte[20]); return [.. b];
    }
    public static byte[] WebPExtended(int w, int h)
    {
        var b = new List<byte>("RIFF"u8.ToArray()); b.AddRange([0, 0, 0, 0]); b.AddRange("WEBP"u8.ToArray()); b.AddRange("VP8X"u8.ToArray()); b.AddRange([10, 0, 0, 0]);
        b.AddRange([0, 0, 0, 0, (byte)(w - 1), (byte)((w - 1) >> 8), (byte)((w - 1) >> 16), (byte)(h - 1), (byte)((h - 1) >> 8), (byte)((h - 1) >> 16)]); b.AddRange(new byte[10]); return [.. b];
    }

    [Fact]
    public void Accepts_real_headers_and_reports_type_and_dimensions()
    {
        Assert.Equal(("image/png", 200, 120), Tuple(ImageInspector.Inspect(Png(200, 120))));
        Assert.Equal(("image/jpeg", 640, 480), Tuple(ImageInspector.Inspect(Jpeg(640, 480))));
        Assert.Equal(("image/webp", 300, 300), Tuple(ImageInspector.Inspect(WebPLossy(300, 300))));
        Assert.Equal(("image/webp", 256, 128), Tuple(ImageInspector.Inspect(WebPLossless(256, 128))));
        Assert.Equal(("image/webp", 500, 400), Tuple(ImageInspector.Inspect(WebPExtended(500, 400))));
    }
    private static (string, int, int) Tuple(ImageInfo i) => (i.ContentType, i.Width, i.Height);

    [Fact]
    public void Rejects_everything_that_is_not_a_sane_png_jpeg_or_webp()
    {
        var html = "<html><script>alert(1)</script></html>"u8.ToArray();
        byte[] polyglot = [.. Png(100, 100), .. "<script>alert(1)</script>"u8.ToArray()]; // a *valid* PNG header with trailing HTML is still just served as image/png, nosniff
        var cases = new (string Name, byte[] Data)[]
        {
            ("empty", []), ("html", html), ("svg", "<svg xmlns='http://www.w3.org/2000/svg' width='64' height='64'/>"u8.ToArray()), ("gif", [.. "GIF89a"u8.ToArray(), 64, 0, 64, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]),
            ("exe", [.. "MZ"u8.ToArray(), .. new byte[100]]), ("png-no-ihdr", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'X', (byte)'X', (byte)'X', (byte)'X', .. new byte[30]]),
            ("png-truncated", Png(100, 100)[..20]), ("jpeg-no-sof", [0xFF, 0xD8, 0xFF, 0xE0, 0, 4, 0, 0, 0xFF, 0xD9]), ("riff-but-wave", [.. "RIFF"u8.ToArray(), 0, 0, 0, 0, .. "WAVEfmt "u8.ToArray(), .. new byte[30]]),
            ("too-small", Png(8, 8)), ("too-large-dims", Png(5000, 5000)), ("zero-dims", Png(0, 0)), ("webp-bad-start-code", [.. WebPLossy(100, 100)[..23], 0, 0, 0, .. new byte[20]]),
        };
        foreach (var (name, data) in cases)
        {
            var e = Assert.Throws<DomainException>(() => ImageInspector.Inspect(data));
            Assert.True(e.Code == "validation" && e.Status == 422, name);
        }
        Assert.Equal("image/png", ImageInspector.Inspect(polyglot).ContentType); // accepted as an image, but only ever delivered as image/png + nosniff + CSP sandbox (checked in the API tests)
    }

    [Fact]
    public void Enforces_the_size_limit_exactly()
    {
        var ok = Png(100, 100, pad: ImageInspector.MaxBytes - 29); Assert.Equal(ImageInspector.MaxBytes, ok.Length); Assert.Equal("image/png", ImageInspector.Inspect(ok).ContentType);
        var tooBig = Png(100, 100, pad: ImageInspector.MaxBytes - 28); Assert.Equal(ImageInspector.MaxBytes + 1, tooBig.Length);
        Assert.Contains("256 KB", Assert.Throws<DomainException>(() => ImageInspector.Inspect(tooBig)).Message);
    }

    [Fact]
    public void Never_trusts_a_lying_extension_or_header_only_the_bytes()
    {
        // JPEG bytes are judged as JPEG regardless of what the client claimed; garbage after a valid magic number is rejected where it matters
        Assert.Equal("image/jpeg", ImageInspector.Inspect(Jpeg(100, 100)).ContentType);
        Assert.Throws<DomainException>(() => ImageInspector.Inspect([0xFF, 0xD8, 0xFF]));
        Assert.Throws<DomainException>(() => ImageInspector.Inspect([0xFF, 0xD8, 0xFF, 0xC0, 0xFF, 0xFF, 0xFF, 0xFF]));
    }
}
