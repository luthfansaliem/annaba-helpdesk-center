namespace AnnabaDigital.Api.Http;

public sealed class ApiOptions
{
    /// <summary>The browser-visible origin (scheme://host[:port]) that is allowed to make state-changing requests.</summary>
    public string Origin { get; set; } = "http://localhost:3000";
    /// <summary>Shared secret added by the UI proxy. When set, requests without it get 404 (API cannot be reached directly).</summary>
    public string? ProxyKey { get; set; }
    public bool AutoMigrate { get; set; }
    public bool SeedDevUsers { get; set; }
    /// <summary>Optional: when the database has no users, e-mail a super-admin invitation to this address (see Bootstrap).</summary>
    public string? BootstrapAdminEmail { get; set; }
    public string DevPassword { get; set; } = "Passw0rd!demo";

    public bool SecureCookies => Origin.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
    /// <summary>`__Host-` prefix requires Secure + Path=/ + no Domain; used whenever the origin is HTTPS.</summary>
    public string CookieName => SecureCookies ? "__Host-sid" : "sid";
}
