using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using AnnabaDigital.Application.Abstractions;

namespace AnnabaDigital.Infrastructure.Security;

public sealed class TokenService(IOptions<SecurityOptions> options) : ITokenService
{
    public string NewToken() => Base64Url(RandomNumberGenerator.GetBytes(32));

    public string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    public string CsrfToken(Guid sessionId)
    {
        using var h = new HMACSHA256(Encoding.UTF8.GetBytes(options.Value.CsrfKey));
        return Base64Url(h.ComputeHash(sessionId.ToByteArray()));
    }

    public bool ConstantTimeEquals(string a, string b)
        => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

public sealed class SystemClock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }
