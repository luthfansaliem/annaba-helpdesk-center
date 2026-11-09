using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using AnnabaDigital.Application.Abstractions;

namespace AnnabaDigital.Infrastructure.Security;

/// <summary>
/// AES-256-GCM with versioned keys (rotation: add a new key version, new writes use it, old rows stay readable until re-encrypted).
/// Per-secret random 96-bit nonce; the row id is authenticated as additional data.
/// </summary>
public sealed class AesGcmSecretProtector(IOptions<SecurityOptions> options) : ISecretProtector
{
    private readonly SecurityOptions _o = options.Value;

    private byte[] KeyFor(int version)
    {
        if (!_o.EncryptionKeys.TryGetValue(version.ToString(), out var b64)) throw new CryptographicException($"Encryption key version {version} is not configured.");
        var key = Convert.FromBase64String(b64);
        if (key.Length != 32) throw new CryptographicException("Encryption keys must be 32 bytes (base64).");
        return key;
    }

    public ProtectedSecret Protect(byte[] plaintext, byte[] aad)
    {
        var version = _o.CurrentKeyVersion;
        var nonce = RandomNumberGenerator.GetBytes(12);
        var cipher = new byte[plaintext.Length + 16]; // ciphertext || 16-byte tag
        using var gcm = new AesGcm(KeyFor(version), 16);
        gcm.Encrypt(nonce, plaintext, cipher.AsSpan(0, plaintext.Length), cipher.AsSpan(plaintext.Length, 16), aad);
        return new ProtectedSecret(cipher, nonce, version);
    }

    public byte[] Unprotect(ProtectedSecret s, byte[] aad)
    {
        if (s.Ciphertext.Length < 16) throw new CryptographicException("Ciphertext too short.");
        var len = s.Ciphertext.Length - 16;
        var plain = new byte[len];
        using var gcm = new AesGcm(KeyFor(s.KeyVersion), 16);
        gcm.Decrypt(s.Nonce, s.Ciphertext.AsSpan(0, len), s.Ciphertext.AsSpan(len, 16), plain, aad); // throws on any tampering
        return plain;
    }

    public string KeyedHash(string purpose, string value)
    {
        // Subkey per purpose (HKDF) so a hash computed for one purpose can't be replayed for another.
        var subkey = HKDF.DeriveKey(HashAlgorithmName.SHA256, KeyFor(_o.CurrentKeyVersion), 32, salt: null, info: Encoding.UTF8.GetBytes("annaba_digital:" + purpose));
        return Convert.ToHexString(HMACSHA256.HashData(subkey, Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }
}
