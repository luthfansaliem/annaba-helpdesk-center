using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using AnnabaDigital.Application.Integrations;
using AnnabaDigital.Domain;
using AnnabaDigital.Infrastructure.Persistence;

namespace AnnabaDigital.Infrastructure.Integrations;

public static class WebhookHttp
{
    public const string ClientName = "webhooks";

    /// <summary>
    /// The HTTP client for webhooks: no redirects, 10 s timeout, and an SSRF guard at connect time — the host name is resolved here and every address must be public,
    /// so a name that points (or later re-points) at a private, local or cloud-metadata address is never reached. <c>Webhooks:AllowPrivateTargets</c> (tests, local use) lifts it.
    /// </summary>
    public static IServiceCollection AddWebhookHttp(this IServiceCollection services, IConfiguration config)
    {
        services.AddHttpClient(ClientName, c => { c.Timeout = TimeSpan.FromSeconds(10); c.DefaultRequestHeaders.UserAgent.ParseAdd("AnnabaHelpdesk-Webhooks/1.0"); })
            .ConfigurePrimaryHttpMessageHandler(sp =>
            {
                // read when the handler is built (after the host's configuration is complete), not at registration
                var allowPrivate = sp.GetRequiredService<IConfiguration>().GetValue<bool>("Webhooks:AllowPrivateTargets");
                return new SocketsHttpHandler
            {
                AllowAutoRedirect = false, UseCookies = false, UseProxy = false, PooledConnectionLifetime = TimeSpan.FromMinutes(2),
                ConnectCallback = async (ctx, ct) =>
                {
                    var addresses = await Dns.GetHostAddressesAsync(ctx.DnsEndPoint.Host, ct);
                    if (addresses.Length == 0) throw new HttpRequestException("The host has no address.");
                    if (!allowPrivate && addresses.Any(a => !IntegrationRules.IsPublic(a))) throw new HttpRequestException("Blocked: the host resolves to a private or local address.");
                    var ip = addresses[0];
                    var socket = new Socket(ip.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                    try { await socket.ConnectAsync(new IPEndPoint(ip, ctx.DnsEndPoint.Port), ct); return new NetworkStream(socket, ownsSocket: true); }
                    catch { socket.Dispose(); throw; }
                },
            };
            });
        return services;
    }
}

/// <summary>
/// INT-002. Sends the webhook outbox: claims due deliveries with FOR UPDATE SKIP LOCKED (safe with several instances), signs each with its endpoint's secret
/// (<c>X-Annaba-Signature: v1=HMAC-SHA256(secret, "{timestamp}.{body}")</c>), posts it, and on anything but 2xx retries with back-off up to 8 attempts.
/// </summary>
public sealed class WebhookDispatcher(IServiceScopeFactory scopes, IHttpClientFactory http, IConfiguration config, ILogger<WebhookDispatcher> log) : BackgroundService
{
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var poll = TimeSpan.FromSeconds(Math.Clamp(config.GetValue("Webhooks:PollSeconds", 5), 1, 60));
        var lastPurge = DateTimeOffset.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var worked = await DispatchBatchAsync(stoppingToken);
                if (DateTimeOffset.UtcNow - lastPurge > TimeSpan.FromHours(6)) { await PurgeAsync(stoppingToken); lastPurge = DateTimeOffset.UtcNow; }
                if (worked) continue;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception e) { log.LogError("Webhook dispatcher iteration failed ({Error}).", e.GetType().Name); }
            try { await Task.Delay(poll, stoppingToken); } catch (OperationCanceledException) { return; }
        }
    }

    public async Task<bool> DispatchBatchAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnnabaDigitalDbContext>();
        var integrations = scope.ServiceProvider.GetRequiredService<IntegrationService>();
        var now = DateTimeOffset.UtcNow; var leaseUntil = now + Lease;
        var batch = await db.WebhookDeliveries.FromSql($"""
            UPDATE webhook_deliveries SET attempts = attempts + 1, next_attempt_at = {leaseUntil}
            WHERE id IN (SELECT id FROM webhook_deliveries WHERE delivered_at IS NULL AND failed_at IS NULL AND next_attempt_at <= {now}
                         ORDER BY next_attempt_at LIMIT 20 FOR UPDATE SKIP LOCKED)
            RETURNING *
            """).IgnoreQueryFilters().AsNoTracking().ToListAsync(ct);
        if (batch.Count == 0) return false;
        var client = http.CreateClient(WebhookHttp.ClientName);
        foreach (var d in batch)
        {
            var endpoint = await db.WebhookEndpoints.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(w => w.Id == d.EndpointId, ct);
            if (endpoint is null || !endpoint.Enabled)
            {
                await db.WebhookDeliveries.IgnoreQueryFilters().Where(x => x.Id == d.Id).ExecuteUpdateAsync(u => u.SetProperty(x => x.FailedAt, DateTimeOffset.UtcNow).SetProperty(x => x.LastError, "The webhook is switched off or removed."), ct);
                continue;
            }
            int? status = null; string? error = null;
            try
            {
                var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                using var req = new HttpRequestMessage(HttpMethod.Post, endpoint.Url) { Content = new StringContent(d.PayloadJson, Encoding.UTF8, "application/json") };
                req.Headers.Add("X-Annaba-Event", d.Event);
                req.Headers.Add("X-Annaba-Delivery", d.EventId.ToString());
                req.Headers.Add("X-Annaba-Timestamp", ts.ToString());
                req.Headers.Add("X-Annaba-Signature", IntegrationRules.Signature(integrations.SecretOf(endpoint), ts, d.PayloadJson));
                using var res = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
                status = (int)res.StatusCode;
                if (res.IsSuccessStatusCode)
                {
                    await db.WebhookDeliveries.IgnoreQueryFilters().Where(x => x.Id == d.Id).ExecuteUpdateAsync(u => u.SetProperty(x => x.DeliveredAt, DateTimeOffset.UtcNow).SetProperty(x => x.LastStatus, status).SetProperty(x => x.LastError, (string?)null), ct);
                    continue;
                }
                error = $"HTTP {status}";
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception e) { error = e is HttpRequestException { Message: var m } && m.StartsWith("Blocked") ? m : e.GetType().Name; }
            if (d.Attempts >= IntegrationRules.MaxAttempts)
            {
                log.LogWarning("Giving up on webhook {Event} to endpoint {Endpoint} after {Attempts} attempts ({Error}).", d.Event, d.EndpointId, d.Attempts, error);
                await db.WebhookDeliveries.IgnoreQueryFilters().Where(x => x.Id == d.Id).ExecuteUpdateAsync(u => u.SetProperty(x => x.FailedAt, DateTimeOffset.UtcNow).SetProperty(x => x.LastStatus, status).SetProperty(x => x.LastError, error), ct);
            }
            else
            {
                var next = DateTimeOffset.UtcNow + IntegrationRules.Backoff(d.Attempts);
                await db.WebhookDeliveries.IgnoreQueryFilters().Where(x => x.Id == d.Id).ExecuteUpdateAsync(u => u.SetProperty(x => x.NextAttemptAt, next).SetProperty(x => x.LastStatus, status).SetProperty(x => x.LastError, error), ct);
            }
        }
        return true;
    }

    /// <summary>Delivered or given-up events are kept 30 days for the delivery log, then removed (with their payloads).</summary>
    private async Task PurgeAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnnabaDigitalDbContext>();
        var cutoff = DateTimeOffset.UtcNow.AddDays(-30);
        await db.WebhookDeliveries.IgnoreQueryFilters().Where(d => (d.DeliveredAt != null && d.DeliveredAt < cutoff) || (d.FailedAt != null && d.FailedAt < cutoff)).ExecuteDeleteAsync(ct);
    }
}
