using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using AnnabaDigital.Application.Abstractions;

namespace AnnabaDigital.Infrastructure;

public sealed class StorageOptions
{
    /// <summary>Folder for attachment bytes. In a container, mount a persistent volume here.</summary>
    public string Path { get; set; } = "data/files";

    /// <summary>Host of a ClamAV daemon (clamd). When set, every upload is scanned by it; when empty, no scanning happens (and Production refuses to start unless <see cref="AllowUnscanned"/>).</summary>
    public string? ClamAvHost { get; set; }
    public int ClamAvPort { get; set; } = 3310;
    public int ClamAvTimeoutSeconds { get; set; } = 30;

    /// <summary>Explicit acknowledgement that Production accepts files without a virus scan.</summary>
    public bool AllowUnscanned { get; set; }
}

/// <summary>
/// Attachment bytes on local disk, one file per attachment under <c>{root}/{tenant}/{id}</c>. Only keys of exactly that shape are accepted, so no input can reach another
/// folder. Writes go to a temporary file and are moved into place, so a half-written file is never served.
/// </summary>
public sealed partial class LocalFileStorage(IOptions<StorageOptions> options) : IFileStorage
{
    [GeneratedRegex("^[0-9a-f]{32}/[0-9a-f]{32}$")] private static partial Regex KeyShape();

    private string PathOf(string key)
    {
        if (!KeyShape().IsMatch(key)) throw new ArgumentException("Invalid storage key.", nameof(key));
        return System.IO.Path.Combine(System.IO.Path.GetFullPath(options.Value.Path), key.Replace('/', System.IO.Path.DirectorySeparatorChar));
    }

    public async Task SaveAsync(string key, Stream content, CancellationToken ct = default)
    {
        var path = PathOf(key);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var f = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true)) await content.CopyToAsync(f, ct);
            File.Move(tmp, path, overwrite: true);
        }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
    }

    public Task<Stream?> OpenAsync(string key, CancellationToken ct = default)
    {
        var path = PathOf(key);
        return Task.FromResult<Stream?>(File.Exists(path) ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true) : null);
    }

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        var path = PathOf(key);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    /// <summary>Only objects of exactly the key shape are listed (a stray temporary or foreign file is ignored), and only below the given folder prefix.</summary>
    public async IAsyncEnumerable<StoredFile> ListAsync(string prefix, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var root = System.IO.Path.GetFullPath(options.Value.Path);
        var folder = prefix.TrimEnd('/');
        if (!Regex.IsMatch(folder, "^[0-9a-f]{32}$")) yield break;
        var dir = System.IO.Path.Combine(root, folder);
        if (!Directory.Exists(dir)) yield break;
        foreach (var file in Directory.EnumerateFiles(dir))
        {
            ct.ThrowIfCancellationRequested();
            var name = System.IO.Path.GetFileName(file); var key = $"{folder}/{name}";
            if (!KeyShape().IsMatch(key)) continue;
            var info = new FileInfo(file);
            yield return new StoredFile(key, info.Length, new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero));
        }
        await Task.CompletedTask;
    }
}

/// <summary>The default scanner: accepts everything. Replace it (see <see cref="IAttachmentScanner"/>) before accepting files from people you do not trust.</summary>
public sealed class NoopAttachmentScanner : IAttachmentScanner
{
    public bool Active => false;
    public Task<ScanResult> ScanAsync(Stream content, string fileName, CancellationToken ct = default) => Task.FromResult(new ScanResult(true));
}

/// <summary>
/// Scans through a ClamAV daemon (<c>clamd</c>) over TCP using its INSTREAM command. Fails closed: if the daemon cannot be reached or answers anything
/// other than a clean verdict, the file is refused, so an outage never lets unscanned files in.
/// </summary>
public sealed class ClamAvAttachmentScanner(IOptions<StorageOptions> options, Microsoft.Extensions.Logging.ILogger<ClamAvAttachmentScanner> log) : IAttachmentScanner
{
    private const int ChunkSize = 64 * 1024;

    public async Task<ScanResult> ScanAsync(Stream content, string fileName, CancellationToken ct = default)
    {
        var o = options.Value;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(o.ClamAvTimeoutSeconds, 1, 300)));
        try
        {
            using var client = new System.Net.Sockets.TcpClient();
            await client.ConnectAsync(o.ClamAvHost!, o.ClamAvPort, timeout.Token);
            await using var net = client.GetStream();
            await net.WriteAsync("zINSTREAM\0"u8.ToArray(), timeout.Token);
            var buf = new byte[ChunkSize]; var len = new byte[4]; int n;
            while ((n = await content.ReadAsync(buf, timeout.Token)) > 0)
            {
                System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(len, n);
                await net.WriteAsync(len, timeout.Token);
                await net.WriteAsync(buf.AsMemory(0, n), timeout.Token);
            }
            await net.WriteAsync(new byte[4], timeout.Token);
            await net.FlushAsync(timeout.Token);

            using var reply = new MemoryStream(); var one = new byte[256];
            while ((n = await net.ReadAsync(one, timeout.Token)) > 0) { reply.Write(one, 0, n); if (reply.Length > 4096) break; }
            var text = System.Text.Encoding.UTF8.GetString(reply.ToArray()).Trim('\0', '\n', ' ');
            if (text.EndsWith(" OK", StringComparison.Ordinal)) return new ScanResult(true);
            if (text.EndsWith(" FOUND", StringComparison.Ordinal)) return new ScanResult(false, text);
            log.LogError("ClamAV answered unexpectedly for an upload: {Reply}", text);
            return new ScanResult(false, "scanner error");
        }
        catch (Exception e) when (e is System.Net.Sockets.SocketException or IOException or OperationCanceledException or ObjectDisposedException)
        {
            if (ct.IsCancellationRequested) throw;
            log.LogError(e, "ClamAV is unreachable; the upload was refused");
            return new ScanResult(false, "scanner unavailable");
        }
    }
}

