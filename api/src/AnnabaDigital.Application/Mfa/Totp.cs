using System.Security.Cryptography;
using System.Text;

namespace AnnabaDigital.Application.Mfa;

/// <summary>RFC 4648 Base32 (no padding on output; tolerant on input: case, spaces, dashes, padding).</summary>
public static class Base32
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string Encode(ReadOnlySpan<byte> data)
    {
        var sb = new StringBuilder((data.Length * 8 + 4) / 5);
        int buffer = 0, bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b; bits += 8;
            while (bits >= 5) { sb.Append(Alphabet[(buffer >> (bits - 5)) & 31]); bits -= 5; }
        }
        if (bits > 0) sb.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        return sb.ToString();
    }

    public static byte[] Decode(string text)
    {
        var clean = new string(text.Where(c => c is not ('=' or ' ' or '-')).ToArray()).ToUpperInvariant();
        var bytes = new List<byte>(clean.Length * 5 / 8);
        int buffer = 0, bits = 0;
        foreach (var c in clean)
        {
            var v = Alphabet.IndexOf(c);
            if (v < 0) throw new FormatException("Invalid Base32 character.");
            buffer = (buffer << 5) | v; bits += 5;
            if (bits >= 8) { bytes.Add((byte)((buffer >> (bits - 8)) & 0xFF)); bits -= 8; }
        }
        return [.. bytes];
    }
}

/// <summary>RFC 6238 TOTP (HMAC-SHA1, 6 digits, 30 s) — the profile every authenticator app supports.</summary>
public static class Totp
{
    public const int Digits = 6;
    public const int PeriodSeconds = 30;
    public const int SecretBytes = 20; // 160-bit, as recommended by RFC 4226

    public static byte[] NewSecret() => RandomNumberGenerator.GetBytes(SecretBytes);

    public static long StepAt(DateTimeOffset t) => t.ToUnixTimeSeconds() / PeriodSeconds;

    /// <summary>Code for a given time step (RFC 4226 dynamic truncation).</summary>
    public static string Compute(byte[] secret, long step, int digits = Digits)
    {
        Span<byte> counter = stackalloc byte[8];
        for (var i = 7; i >= 0; i--) { counter[i] = (byte)(step & 0xFF); step >>= 8; }
        var hash = HMACSHA1.HashData(secret, counter);
        var offset = hash[^1] & 0x0F;
        var bin = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        var mod = 1; for (var i = 0; i < digits; i++) mod *= 10;
        return (bin % mod).ToString().PadLeft(digits, '0');
    }

    /// <summary>
    /// Accepts the previous, current or next step (±30 s clock skew) but only a step strictly greater than <paramref name="lastUsedStep"/>,
    /// so a code can never be used twice (replay protection). Compares all candidates in constant time.
    /// </summary>
    public static bool TryVerify(byte[] secret, string? code, DateTimeOffset now, long lastUsedStep, out long matchedStep)
    {
        matchedStep = 0;
        if (code is null || code.Length != Digits || !code.All(char.IsAsciiDigit)) return false;
        var current = StepAt(now);
        var given = Encoding.ASCII.GetBytes(code);
        var ok = false;
        for (var s = current - 1; s <= current + 1; s++)
        {
            var match = CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Compute(secret, s)), given) & s > lastUsedStep;
            if (match && !ok) { ok = true; matchedStep = s; }
        }
        return ok;
    }

    public static string OtpAuthUri(string issuer, string account, string secretBase32)
        => $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(account)}?secret={secretBase32}&issuer={Uri.EscapeDataString(issuer)}&algorithm=SHA1&digits={Digits}&period={PeriodSeconds}";
}

/// <summary>Single-use recovery codes: 80 random bits shown as XXXX-XXXX-XXXX-XXXX (ambiguous characters removed).</summary>
public static class RecoveryCodes
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // 32 symbols → 5 bits each
    public const int Count = 10;

    public static string New()
    {
        Span<byte> b = stackalloc byte[16];
        RandomNumberGenerator.Fill(b);
        var chars = new char[19]; var j = 0;
        for (var i = 0; i < 16; i++) { if (i > 0 && i % 4 == 0) chars[j++] = '-'; chars[j++] = Alphabet[b[i] & 31]; }
        return new string(chars);
    }

    /// <summary>Canonical form used for hashing: upper-case, no separators.</summary>
    public static string Normalize(string? input) => new((input ?? "").Where(c => c is not ('-' or ' ')).Select(char.ToUpperInvariant).ToArray());

    public static bool LooksLikeCode(string? input) { var n = Normalize(input); return n.Length == 16 && n.All(Alphabet.Contains); }
}
