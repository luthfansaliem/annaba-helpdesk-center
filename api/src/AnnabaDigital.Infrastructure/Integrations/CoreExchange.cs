using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using AnnabaDigital.Application.Platform;
using AnnabaDigital.Domain;

namespace AnnabaDigital.Infrastructure.Integrations;

/// <summary>SPIKE H00. The launch exchange with Annaba Core, with this application's service key. A timeout or a 5xx is "cannot verify" and is never a sign-in.</summary>
public sealed class CoreExchange(HttpClient http, IOptions<CoreOptions> options, ILogger<CoreExchange> log) : ICoreExchange
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    public async Task<CorePrincipal?> ExchangeAsync(string code, string? state, CancellationToken ct)
    {
        var o = options.Value;
        using var req = new HttpRequestMessage(HttpMethod.Post, o.BaseUrl.TrimEnd('/') + "/api/integration/v1/launch/exchange") { Content = JsonContent.Create<object>(state is null ? new { code } : new { code, state }, options: Web) };
        req.Headers.Add("X-Service-Key", o.ServiceKey);
        try
        {
            using var res = await http.SendAsync(req, ct);
            if (res.IsSuccessStatusCode) return await res.Content.ReadFromJsonAsync<CorePrincipal>(Web, ct);
            if ((int)res.StatusCode >= 500) throw new DomainException("core_unavailable", "Access cannot be verified right now.", 503);
            return null;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or TimeoutException) { log.LogWarning("Launch exchange could not reach the platform: {Kind}", e.GetType().Name); throw new DomainException("core_unavailable", "Access cannot be verified right now.", 503); }
    }
}
