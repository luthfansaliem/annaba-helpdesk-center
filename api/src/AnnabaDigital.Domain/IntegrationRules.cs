using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace AnnabaDigital.Domain;

/// <summary>INT-001/002. Scopes, events, the SSRF rules for webhook targets, retry timing and the signature.</summary>
public static class IntegrationRules
{
    public const string TokenPrefix = "adk_";
    public static readonly string[] Scopes = ["tickets.read", "tickets.write", "catalog.read"];
    public static readonly string[] Events = ["ticket.created", "ticket.status_changed", "ticket.assigned", "ticket.commented", "workorder.completed"];
    public const int MaxAttempts = 8, MaxKeys = 50, MaxEndpoints = 20, RequestsPerMinute = 120;

    /// <summary>1 min, 5 min, 15 min, 1 h, 3 h, 6 h, 12 h between attempts.</summary>
    public static TimeSpan Backoff(int attempt) => attempt switch { <= 1 => TimeSpan.FromMinutes(1), 2 => TimeSpan.FromMinutes(5), 3 => TimeSpan.FromMinutes(15), 4 => TimeSpan.FromHours(1), 5 => TimeSpan.FromHours(3), 6 => TimeSpan.FromHours(6), _ => TimeSpan.FromHours(12) };

    /// <summary>Addresses a webhook may reach: public unicast only. Loopback, private, link-local (cloud metadata), carrier-grade NAT, multicast, unspecified and IPv6 site/unique-local are refused.</summary>
    public static bool IsPublic(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any) || ip.Equals(IPAddress.Broadcast)) return false;
        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            return !(b[0] == 10 || b[0] == 0 || b[0] == 127 || (b[0] == 169 && b[1] == 254) || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168)
                || (b[0] == 100 && b[1] >= 64 && b[1] <= 127) || (b[0] == 192 && b[1] == 0 && b[2] == 0) || (b[0] == 198 && (b[1] == 18 || b[1] == 19)) || b[0] >= 224);
        }
        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
            return !(ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast || (ip.GetAddressBytes()[0] & 0xFE) == 0xFC);
        return false;
    }

    /// <summary>What is wrong with a webhook URL (null when fine). HTTPS on port 443 or 8443 only, no user info, no IP literal that is not public; host names are checked again when connecting.</summary>
    public static string? CheckUrl(string? url, bool allowPrivate)
    {
        if (string.IsNullOrWhiteSpace(url) || url.Length > 500 || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var u)) return "Use a full address such as https://example.com/hooks/helpdesk.";
        if (allowPrivate) return u.Scheme is "https" or "http" ? null : "Use an https:// address.";
        if (u.Scheme != "https") return "Use an https:// address.";
        if (!string.IsNullOrEmpty(u.UserInfo)) return "Don't put a user name or password in the address.";
        if (u.Port is not (443 or 8443)) return "Use port 443 (or 8443).";
        if (IPAddress.TryParse(u.Host.Trim('[', ']'), out var ip) && !IsPublic(ip)) return "The address points to a private or local network.";
        if (u.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || u.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) || u.Host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase) || !u.Host.Contains('.'))
            return "The address points to a private or local network.";
        return null;
    }

    /// <summary><c>v1=</c> hex HMAC-SHA256 of <c>"{timestamp}.{body}"</c> with the endpoint's secret.</summary>
    public static string Signature(string secret, long timestamp, string body)
        => "v1=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{timestamp}.{body}"))).ToLowerInvariant();

    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
}
