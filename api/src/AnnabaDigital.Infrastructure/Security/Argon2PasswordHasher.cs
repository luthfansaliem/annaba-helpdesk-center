using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;
using Microsoft.Extensions.Options;
using AnnabaDigital.Application.Abstractions;

namespace AnnabaDigital.Infrastructure.Security;

public sealed class SecurityOptions
{
    /// <summary>Argon2id memory in KiB. OWASP minimum is 19456 (19 MiB) with t=2, p=1.</summary>
    public int Argon2MemoryKiB { get; set; } = 19456;
    public int Argon2Iterations { get; set; } = 2;
    public int Argon2Parallelism { get; set; } = 1;
    /// <summary>Optional secret pepper mixed into every hash (kept outside the database).</summary>
    public string? PasswordPepper { get; set; }
    /// <summary>HMAC key for CSRF tokens. Required in production.</summary>
    public string CsrfKey { get; set; } = "dev-only-csrf-key-change-me-0123456789abcdef";
    /// <summary>Base64 32-byte AES keys by version, e.g. Security:EncryptionKeys:1. The built-in dev key is refused in Production.</summary>
    public Dictionary<string, string> EncryptionKeys { get; set; } = new() { [DevKeyVersion] = DevEncryptionKey };
    public int CurrentKeyVersion { get; set; } = 1;
    public const string DevKeyVersion = "1";
    /// <summary>Public, well-known development key — never valid in Production (StartupChecks).</summary>
    public const string DevEncryptionKey = "ZGV2LW9ubHktZW5jcnlwdGlvbi1rZXktMzItYnl0ZXM=";
}

/// <summary>Argon2id with PHC-style encoding: $argon2id$v=19$m=…,t=…,p=…$salt$hash.</summary>
public sealed class Argon2PasswordHasher(IOptions<SecurityOptions> options) : IPasswordHasher
{
    private readonly SecurityOptions _o = options.Value;
    private const int SaltBytes = 16, HashBytes = 32;
    private static readonly string DummyHashSeed = "dummy-password-for-timing";
    private string? _dummy;

    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Compute(password, salt, _o.Argon2MemoryKiB, _o.Argon2Iterations, _o.Argon2Parallelism);
        return $"$argon2id$v=19$m={_o.Argon2MemoryKiB},t={_o.Argon2Iterations},p={_o.Argon2Parallelism}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public bool Verify(string password, string encoded)
    {
        if (!TryParse(encoded, out var m, out var t, out var p, out var salt, out var expected)) return false;
        var actual = Compute(password, salt, m, t, p);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    public bool NeedsRehash(string encoded)
        => !TryParse(encoded, out var m, out var t, out var p, out _, out _) || m < _o.Argon2MemoryKiB || t < _o.Argon2Iterations || p < _o.Argon2Parallelism;

    public void VerifyDummy(string password)
    {
        _dummy ??= Hash(DummyHashSeed);
        Verify(password, _dummy);
    }

    private byte[] Compute(string password, byte[] salt, int memKiB, int iterations, int parallelism)
    {
        using var a = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt, MemorySize = memKiB, Iterations = iterations, DegreeOfParallelism = parallelism,
            KnownSecret = string.IsNullOrEmpty(_o.PasswordPepper) ? null : Encoding.UTF8.GetBytes(_o.PasswordPepper),
        };
        return a.GetBytes(HashBytes);
    }

    private static bool TryParse(string encoded, out int m, out int t, out int p, out byte[] salt, out byte[] hash)
    {
        m = t = p = 0; salt = hash = [];
        var parts = encoded.Split('$', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 5 || parts[0] != "argon2id") return false;
        try
        {
            var kv = parts[2].Split(',').Select(x => x.Split('=')).ToDictionary(x => x[0], x => int.Parse(x[1]));
            m = kv["m"]; t = kv["t"]; p = kv["p"];
            salt = Convert.FromBase64String(parts[3]); hash = Convert.FromBase64String(parts[4]);
            return m > 0 && t > 0 && p > 0;
        }
        catch (Exception e) when (e is FormatException or KeyNotFoundException or IndexOutOfRangeException or OverflowException) { return false; }
    }
}
