using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Mfa;
using AnnabaDigital.Infrastructure.Security;
using Xunit;

namespace AnnabaDigital.Tests;

public sealed class MfaCryptoTests
{
    // ---- Base32 (RFC 4648 §10 test vectors)
    [Theory]
    [InlineData("", "")] [InlineData("f", "MY")] [InlineData("fo", "MZXQ")] [InlineData("foo", "MZXW6")] [InlineData("foob", "MZXW6YQ")] [InlineData("fooba", "MZXW6YTB")] [InlineData("foobar", "MZXW6YTBOI")]
    public void Base32_matches_rfc4648_vectors_and_round_trips(string plain, string encoded)
    {
        Assert.Equal(encoded, Base32.Encode(Encoding.ASCII.GetBytes(plain)));
        Assert.Equal(plain, Encoding.ASCII.GetString(Base32.Decode(encoded)));
        Assert.Equal(plain, Encoding.ASCII.GetString(Base32.Decode(encoded.ToLowerInvariant() + "====")));
    }

    [Fact]
    public void Base32_decode_tolerates_grouping_and_rejects_garbage()
    {
        Assert.Equal(Base32.Decode("MZXW6YTB"), Base32.Decode("mzxw 6ytb"));
        Assert.Throws<FormatException>(() => Base32.Decode("MZXW1"));
        var s = Totp.NewSecret(); Assert.Equal(s, Base32.Decode(Base32.Encode(s)));
    }

    // ---- RFC 6238 Appendix B (SHA-1, secret "12345678901234567890"; the RFC lists 8-digit codes, 6-digit = last six)
    private static readonly byte[] RfcSecret = Encoding.ASCII.GetBytes("12345678901234567890");

    [Theory]
    [InlineData(59L, "94287082")] [InlineData(1111111109L, "07081804")] [InlineData(1111111111L, "14050471")]
    [InlineData(1234567890L, "89005924")] [InlineData(2000000000L, "69279037")] [InlineData(20000000000L, "65353130")]
    public void Totp_matches_rfc6238_vectors(long unixSeconds, string rfc8Digits)
    {
        var step = unixSeconds / 30;
        Assert.Equal(rfc8Digits, Totp.Compute(RfcSecret, step, 8));
        Assert.Equal(rfc8Digits[2..], Totp.Compute(RfcSecret, step)); // 6-digit = low 6 digits of the same truncated value
    }

    [Fact]
    public void Verify_accepts_one_step_of_skew_but_not_two()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_010);
        var step = Totp.StepAt(now);
        foreach (var s in new[] { step - 1, step, step + 1 }) Assert.True(Totp.TryVerify(RfcSecret, Totp.Compute(RfcSecret, s), now, 0, out var m) && m == s);
        Assert.False(Totp.TryVerify(RfcSecret, Totp.Compute(RfcSecret, step + 2), now, 0, out _));
        Assert.False(Totp.TryVerify(RfcSecret, Totp.Compute(RfcSecret, step - 2), now, 0, out _));
    }

    [Fact]
    public void Verify_blocks_replay_of_a_used_step_and_older_steps()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_010); var step = Totp.StepAt(now);
        var code = Totp.Compute(RfcSecret, step);
        Assert.True(Totp.TryVerify(RfcSecret, code, now, step - 1, out var used));
        Assert.False(Totp.TryVerify(RfcSecret, code, now, used, out _)); // same code again
        Assert.False(Totp.TryVerify(RfcSecret, Totp.Compute(RfcSecret, step - 1), now, used, out _)); // older step after a newer one was used
        Assert.True(Totp.TryVerify(RfcSecret, Totp.Compute(RfcSecret, step + 1), now, used, out _)); // a newer step is fine
    }

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("12345")] [InlineData("1234567")] [InlineData("12 456")] [InlineData("abcdef")] [InlineData("١٢٣٤٥٦")]
    public void Verify_rejects_malformed_codes(string? code) => Assert.False(Totp.TryVerify(RfcSecret, code, DateTimeOffset.UtcNow, 0, out _));

    [Fact]
    public void Otpauth_uri_has_the_fields_authenticator_apps_need()
    {
        var uri = Totp.OtpAuthUri("Annaba Digital", "sarah@annabadigital.io", "JBSWY3DPEHPK3PXP");
        Assert.StartsWith("otpauth://totp/Annaba%20Digital:sarah%40annabadigital.io?secret=JBSWY3DPEHPK3PXP", uri);
        Assert.Contains("issuer=Annaba%20Digital", uri); Assert.Contains("algorithm=SHA1", uri); Assert.Contains("digits=6", uri); Assert.Contains("period=30", uri);
    }

    // ---- recovery codes
    [Fact]
    public void Recovery_codes_have_80_bits_a_stable_format_and_normalise_user_input()
    {
        var codes = Enumerable.Range(0, 200).Select(_ => RecoveryCodes.New()).ToList();
        Assert.Equal(200, codes.Distinct().Count());
        Assert.All(codes, c => { Assert.Matches("^[A-HJ-NP-Z2-9]{4}(-[A-HJ-NP-Z2-9]{4}){3}$", c); Assert.True(RecoveryCodes.LooksLikeCode(c)); });
        var one = codes[0];
        Assert.Equal(RecoveryCodes.Normalize(one), RecoveryCodes.Normalize(one.ToLowerInvariant().Replace("-", " ")));
        Assert.False(RecoveryCodes.LooksLikeCode("123456")); Assert.False(RecoveryCodes.LooksLikeCode("ABCD-0000-1111-2222")); // 0/1 are excluded characters
    }

    // ---- secret protector
    private static AesGcmSecretProtector Protector(Dictionary<string, string>? keys = null, int version = 1)
        => new(Options.Create(new SecurityOptions { EncryptionKeys = keys ?? new() { ["1"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) }, CurrentKeyVersion = version }));

    [Fact]
    public void Protector_round_trips_uses_fresh_nonces_and_never_stores_plaintext()
    {
        var p = Protector(); var secret = Totp.NewSecret(); var aad = Guid.NewGuid().ToByteArray();
        var a = p.Protect(secret, aad); var b = p.Protect(secret, aad);
        Assert.NotEqual(a.Nonce, b.Nonce); Assert.NotEqual(a.Ciphertext, b.Ciphertext);
        Assert.Equal(secret, p.Unprotect(a, aad));
        Assert.False(a.Ciphertext.AsSpan().IndexOf(secret) >= 0);
        Assert.Equal(12, a.Nonce.Length); Assert.Equal(secret.Length + 16, a.Ciphertext.Length);
    }

    [Fact]
    public void Protector_detects_tampering_wrong_row_and_wrong_key()
    {
        var p = Protector(); var secret = Totp.NewSecret(); var aad = Guid.NewGuid().ToByteArray();
        var s = p.Protect(secret, aad);
        var flipped = (byte[])s.Ciphertext.Clone(); flipped[0] ^= 1;
        Assert.ThrowsAny<CryptographicException>(() => p.Unprotect(s with { Ciphertext = flipped }, aad));
        var tag = (byte[])s.Ciphertext.Clone(); tag[^1] ^= 1;
        Assert.ThrowsAny<CryptographicException>(() => p.Unprotect(s with { Ciphertext = tag }, aad));
        Assert.ThrowsAny<CryptographicException>(() => p.Unprotect(s, Guid.NewGuid().ToByteArray())); // swapped to another user's row
        Assert.ThrowsAny<CryptographicException>(() => Protector().Unprotect(s, aad)); // different key
    }

    [Fact]
    public void Key_rotation_keeps_old_secrets_readable_and_new_writes_use_the_new_version()
    {
        var k1 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)); var k2 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var before = Protector(new() { ["1"] = k1 }, 1); var secret = Totp.NewSecret(); var aad = new byte[] { 9 };
        var old = before.Protect(secret, aad); Assert.Equal(1, old.KeyVersion);
        var after = Protector(new() { ["1"] = k1, ["2"] = k2 }, 2);
        Assert.Equal(secret, after.Unprotect(old, aad));
        Assert.Equal(2, after.Protect(secret, aad).KeyVersion);
        Assert.ThrowsAny<CryptographicException>(() => Protector(new() { ["2"] = k2 }, 2).Unprotect(old, aad)); // version 1 key retired ⇒ unreadable
    }

    [Fact]
    public void Keyed_hash_is_deterministic_domain_separated_and_key_dependent()
    {
        var p = Protector(new() { ["1"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) });
        Assert.Equal(p.KeyedHash("recovery", "ABCD"), p.KeyedHash("recovery", "ABCD"));
        Assert.NotEqual(p.KeyedHash("recovery", "ABCD"), p.KeyedHash("other", "ABCD"));
        Assert.NotEqual(p.KeyedHash("recovery", "ABCD"), Protector().KeyedHash("recovery", "ABCD"));
        Assert.Equal(64, p.KeyedHash("recovery", "x").Length);
    }

    [Fact]
    public void Misconfigured_keys_fail_loudly()
    {
        Assert.ThrowsAny<Exception>(() => Protector(new() { ["1"] = Convert.ToBase64String(new byte[16]) }).Protect([1], [1]));
        Assert.ThrowsAny<Exception>(() => Protector(new() { ["1"] = Convert.ToBase64String(new byte[32]) }, 7).Protect([1], [1]));
    }
}
