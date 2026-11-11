using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using AnnabaDigital.Application.Abstractions;

namespace AnnabaDigital.Infrastructure.Security;

/// <summary>Single sign-on settings (environment: Sso__Issuer, Sso__ClientId, Sso__ClientSecret, …). Everything blank = SSO off.</summary>
public sealed class SsoOptions
{
    public string? Issuer { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string DisplayName { get; set; } = "Single sign-on";
    public string Scopes { get; set; } = "openid email profile";
    /// <summary>Some providers (e.g. Microsoft Entra) don't send email_verified; set false only for a provider that already restricts who can sign in.</summary>
    public bool RequireVerifiedEmail { get; set; } = true;
    /// <summary>The claim that lists the person's groups (R3-9). Mapped groups give roles at sign-in; a token without the claim leaves roles as they are.</summary>
    public string GroupsClaim { get; set; } = "groups";
}

/// <summary>
/// OpenID Connect relying party: discovery, authorization-code flow with PKCE, and strict id_token validation (signature from the provider's JWKS,
/// issuer, audience, lifetime, nonce; only asymmetric algorithms). The client secret and codes never leave the server.
/// </summary>
public sealed class OidcClient(HttpClient http, IOptions<SsoOptions> options, IClock clock) : IOidcClient
{
    private static readonly string[] Algorithms = ["RS256", "RS384", "RS512", "PS256", "PS384", "PS512", "ES256", "ES384", "ES512"];
    private static readonly TimeSpan CacheFor = TimeSpan.FromHours(1);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Discovery? _doc; private DateTimeOffset _docAt; private JsonWebKeySet? _keys; private DateTimeOffset _keysAt;

    private sealed record Discovery(string Issuer, string AuthorizationEndpoint, string TokenEndpoint, string JwksUri);

    private SsoOptions O => options.Value;
    public bool Enabled => !string.IsNullOrWhiteSpace(O.Issuer) && !string.IsNullOrWhiteSpace(O.ClientId) && !string.IsNullOrWhiteSpace(O.ClientSecret);
    public string DisplayName => string.IsNullOrWhiteSpace(O.DisplayName) ? "Single sign-on" : O.DisplayName.Trim();

    private static bool SafeUrl(string u) => Uri.TryCreate(u, UriKind.Absolute, out var x) && (x.Scheme == Uri.UriSchemeHttps || (x.Scheme == Uri.UriSchemeHttp && x.IsLoopback));

    private async Task<Discovery> DiscoverAsync(CancellationToken ct)
    {
        if (!Enabled) throw new OidcException("Single sign-on is not configured.");
        if (_doc is not null && clock.UtcNow - _docAt < CacheFor) return _doc;
        await _gate.WaitAsync(ct);
        try
        {
            if (_doc is not null && clock.UtcNow - _docAt < CacheFor) return _doc;
            var issuer = O.Issuer!.Trim();
            if (!SafeUrl(issuer)) throw new OidcException("The issuer must be an https URL.");
            using var json = JsonDocument.Parse(await http.GetStringAsync($"{issuer.TrimEnd('/')}/.well-known/openid-configuration", ct));
            string Get(string n) => json.RootElement.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : throw new OidcException($"Discovery document has no {n}.");
            var d = new Discovery(Get("issuer"), Get("authorization_endpoint"), Get("token_endpoint"), Get("jwks_uri"));
            if (d.Issuer.TrimEnd('/') != issuer.TrimEnd('/')) throw new OidcException("The discovery issuer does not match the configured issuer.");
            if (!SafeUrl(d.AuthorizationEndpoint) || !SafeUrl(d.TokenEndpoint) || !SafeUrl(d.JwksUri)) throw new OidcException("Discovery endpoints must be https URLs.");
            _doc = d; _docAt = clock.UtcNow; return d;
        }
        catch (Exception e) when (e is not OidcException) { throw new OidcException("Could not read the provider's discovery document.", e); }
        finally { _gate.Release(); }
    }

    private async Task<JsonWebKeySet> KeysAsync(Discovery d, bool force, CancellationToken ct)
    {
        if (!force && _keys is not null && clock.UtcNow - _keysAt < CacheFor) return _keys;
        var set = new JsonWebKeySet(await http.GetStringAsync(d.JwksUri, ct));
        _keys = set; _keysAt = clock.UtcNow; return set;
    }

    public async Task<string> BuildAuthorizeUrlAsync(string redirectUri, string state, string nonce, string codeChallenge, CancellationToken ct = default)
    {
        var d = await DiscoverAsync(ct);
        var q = new Dictionary<string, string>
        {
            ["response_type"] = "code", ["client_id"] = O.ClientId!, ["redirect_uri"] = redirectUri, ["scope"] = O.Scopes, ["state"] = state, ["nonce"] = nonce,
            ["code_challenge"] = codeChallenge, ["code_challenge_method"] = "S256",
        };
        var sep = d.AuthorizationEndpoint.Contains('?') ? '&' : '?';
        return d.AuthorizationEndpoint + sep + string.Join('&', q.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
    }

    public async Task<OidcIdentity> ExchangeAsync(string redirectUri, string code, string codeVerifier, string nonce, CancellationToken ct = default)
    {
        var d = await DiscoverAsync(ct);
        string idToken;
        try
        {
            using var res = await http.PostAsync(d.TokenEndpoint, new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code", ["code"] = code, ["redirect_uri"] = redirectUri, ["client_id"] = O.ClientId!, ["client_secret"] = O.ClientSecret!, ["code_verifier"] = codeVerifier,
            }), ct);
            if (!res.IsSuccessStatusCode) throw new OidcException($"The token endpoint answered {(int)res.StatusCode}.");
            using var json = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
            idToken = json.RootElement.TryGetProperty("id_token", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString()! : throw new OidcException("The provider returned no id_token.");
        }
        catch (Exception e) when (e is not OidcException) { throw new OidcException("The token request failed.", e); }

        var handler = new JsonWebTokenHandler();
        async Task<TokenValidationResult> Validate(bool refreshKeys) => await handler.ValidateTokenAsync(idToken, new TokenValidationParameters
        {
            ValidIssuer = d.Issuer, ValidAudience = O.ClientId, IssuerSigningKeys = (await KeysAsync(d, refreshKeys, ct)).GetSigningKeys(),
            ValidateIssuer = true, ValidateAudience = true, ValidateLifetime = true, ValidateIssuerSigningKey = true, RequireSignedTokens = true, RequireExpirationTime = true,
            ValidAlgorithms = Algorithms, ClockSkew = TimeSpan.FromMinutes(2), LifetimeValidator = (nb, exp, _, _) => exp is not null && exp > clock.UtcNow.UtcDateTime.AddMinutes(-2) && (nb is null || nb < clock.UtcNow.UtcDateTime.AddMinutes(2)),
        });
        var result = await Validate(false);
        if (!result.IsValid && result.Exception is SecurityTokenSignatureKeyNotFoundException) result = await Validate(true); // provider rotated its keys
        if (!result.IsValid) throw new OidcException("The id_token is not valid.", result.Exception);

        var c = result.Claims;
        string? Str(string k) => c.TryGetValue(k, out var v) ? v?.ToString() : null;
        if (Str("nonce") != nonce) throw new OidcException("The id_token nonce does not match.");
        var sub = Str("sub"); if (string.IsNullOrEmpty(sub)) throw new OidcException("The id_token has no subject.");
        var verified = c.TryGetValue("email_verified", out var ev) && (ev is true || string.Equals(ev?.ToString(), "true", StringComparison.OrdinalIgnoreCase));
        List<string>? groups = null;
        if (!string.IsNullOrWhiteSpace(O.GroupsClaim) && c.TryGetValue(O.GroupsClaim, out var gv) && gv is not null)
            groups = gv switch
            {
                string one => [one],
                IEnumerable<object> many => [.. many.Select(x => x?.ToString() ?? "").Where(x => x.Length > 0)],
                System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.Array } arr => [.. arr.EnumerateArray().Select(x => x.ToString()).Where(x => x.Length > 0)],
                _ => [gv.ToString() ?? ""],
            };
        return new OidcIdentity(d.Issuer, sub, Str("email"), verified, Str("name"), groups);
    }
}
