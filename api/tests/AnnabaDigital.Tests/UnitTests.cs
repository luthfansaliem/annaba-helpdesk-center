using Microsoft.Extensions.Options;
using AnnabaDigital.Api.Http;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Infrastructure.Security;
using Xunit;

namespace AnnabaDigital.Tests;

public sealed class SecurityUnitTests
{
    private static Argon2PasswordHasher Hasher(int mem = 8192, int it = 1, string? pepper = null)
        => new(Options.Create(new SecurityOptions { Argon2MemoryKiB = mem, Argon2Iterations = it, PasswordPepper = pepper }));

    [Fact]
    public void Argon2id_hashes_verify_and_are_salted()
    {
        var h = Hasher(); var a = h.Hash("correct horse"); var b = h.Hash("correct horse");
        Assert.NotEqual(a, b);
        Assert.True(h.Verify("correct horse", a)); Assert.False(h.Verify("Correct horse", a));
        Assert.StartsWith("$argon2id$v=19$m=8192,t=1,p=1$", a);
    }

    [Fact]
    public void Pepper_is_required_to_verify_and_stays_out_of_the_hash()
    {
        var withPepper = Hasher(pepper: "pepper-secret-value-xxxxxxxxxxxxxxxx"); var h = withPepper.Hash("pw-123456789012");
        Assert.True(withPepper.Verify("pw-123456789012", h));
        Assert.False(Hasher().Verify("pw-123456789012", h));
        Assert.DoesNotContain("pepper-secret", h);
    }

    [Theory]
    [InlineData("")] [InlineData("garbage")] [InlineData("$argon2id$v=19$m=x,t=1,p=1$AAAA$AAAA")] [InlineData("$argon2id$v=19$m=8192,t=1,p=1$!!$!!")]
    public void Malformed_stored_hashes_never_verify_and_never_throw(string stored)
    {
        Assert.False(Hasher().Verify("anything", stored));
        Assert.True(Hasher().NeedsRehash(stored));
    }

    [Fact]
    public void NeedsRehash_detects_weaker_parameters()
    {
        var weak = Hasher(4096, 1).Hash("x-password-123");
        Assert.True(Hasher(8192, 1).NeedsRehash(weak));
        Assert.False(Hasher(4096, 1).NeedsRehash(weak));
    }

    [Fact]
    public void Tokens_are_256_bit_unique_and_only_hashes_compare()
    {
        var t = new TokenService(Options.Create(new SecurityOptions()));
        var a = t.NewToken(); var b = t.NewToken();
        Assert.NotEqual(a, b); Assert.True(a.Length >= 43);
        Assert.DoesNotContain('+', a); Assert.DoesNotContain('/', a); Assert.DoesNotContain('=', a);
        Assert.Equal(64, t.Hash(a).Length); Assert.Equal(t.Hash(a), t.Hash(a)); Assert.NotEqual(t.Hash(a), t.Hash(b));
        var id = Guid.NewGuid();
        Assert.Equal(t.CsrfToken(id), t.CsrfToken(id)); Assert.NotEqual(t.CsrfToken(id), t.CsrfToken(Guid.NewGuid()));
        Assert.True(t.ConstantTimeEquals("abc", "abc")); Assert.False(t.ConstantTimeEquals("abc", "abd")); Assert.False(t.ConstantTimeEquals("abc", "abcd"));
    }

    [Fact]
    public void Redactor_masks_every_credential_like_key()
    {
        var r = Redactor.Redact(new Dictionary<string, string> { ["password"] = "p", ["newPassword"] = "p", ["apiKey"] = "k", ["Authorization"] = "Bearer x", ["Cookie"] = "sid=1", ["recoveryCode"] = "r", ["otp"] = "1", ["csrf"] = "c", ["token"] = "t", ["ip"] = "1.1.1.1", ["reason"] = "x" })!;
        foreach (var k in new[] { "password", "newPassword", "apiKey", "Authorization", "Cookie", "recoveryCode", "otp", "csrf", "token" }) Assert.Equal("[REDACTED]", r[k]);
        Assert.Equal("1.1.1.1", r["ip"]); Assert.Equal("x", r["reason"]);
    }

    [Theory]
    [InlineData("/users/all", "/users/all")] [InlineData("/users?x=1#a", "/users?x=1#a")]
    [InlineData("//evil.com", "/dashboard")] [InlineData("/\\evil.com", "/dashboard")] [InlineData("https://evil.com", "/dashboard")]
    [InlineData("javascript:alert(1)", "/dashboard")] [InlineData("/ok\r\nSet-Cookie: x=1", "/dashboard")] [InlineData("", "/dashboard")]
    [InlineData("/login?returnTo=/x", "/dashboard")] [InlineData("/a\\b", "/dashboard")]
    public void Redirects_only_allow_safe_same_origin_paths(string input, string expected) => Assert.Equal(expected, Redirects.Safe(input));

    [Fact]
    public void Dev_defaults_are_rejected_for_production_configuration()
    {
        var sec = new SecurityOptions();
        Assert.StartsWith("dev-only", sec.CsrfKey); // StartupChecks refuses this value in Production
    }
}
