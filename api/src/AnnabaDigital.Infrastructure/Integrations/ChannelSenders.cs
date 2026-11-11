using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Infrastructure.Email;

namespace AnnabaDigital.Infrastructure.Integrations;

/// <summary>Shared HTTP handling: the SSRF-guarded client (the Teams address is typed by a person), 2xx is success, 4xx (but 408/429) will not get better, anything else is retried.</summary>
public abstract class HttpChannelSender(IHttpClientFactory http) : IChannelSender
{
    public abstract string Kind { get; }
    protected HttpClient Client => http.CreateClient(WebhookHttp.ClientName);
    public abstract Task SendAsync(ChannelSend m, CancellationToken ct = default);
    public virtual Task<string?> ResolveAsync(ChannelSend settings, string email, CancellationToken ct = default) => Task.FromResult<string?>(null);
    protected static string Need(ChannelSend m, string key) => m.Settings.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : throw new ChannelSendException($"The channel has no {key}.", true);
    protected static async Task EnsureAsync(HttpResponseMessage res, CancellationToken ct)
    {
        if (res.IsSuccessStatusCode) return;
        var code = (int)res.StatusCode;
        throw new ChannelSendException($"HTTP {code}", code is >= 400 and < 500 && code is not (408 or 429));
    }
}

/// <summary>WhatsApp Cloud API (Meta): a template message (outside the 24-hour window only templates may be sent). The notify template has three body
/// variables (title, text, link); the verify template one (the code).</summary>
public sealed class WhatsAppSender(IHttpClientFactory http) : HttpChannelSender(http)
{
    public override string Kind => ChannelRules.WhatsApp;
    public override async Task SendAsync(ChannelSend m, CancellationToken ct = default)
    {
        var verify = m.Purpose == "verify";
        var template = Need(m, verify ? "verifyTemplate" : "notifyTemplate");
        var lang = m.Settings.TryGetValue("language", out var l) && !string.IsNullOrWhiteSpace(l) ? l : (m.Language == "id" ? "id" : "en");
        object[] parameters = verify ? [new { type = "text", text = m.Title }]
            : [new { type = "text", text = m.Title }, new { type = "text", text = string.IsNullOrWhiteSpace(m.Body) ? "-" : m.Body }, new { type = "text", text = m.Url ?? "-" }];
        using var req = new HttpRequestMessage(HttpMethod.Post, $"https://graph.facebook.com/v20.0/{Uri.EscapeDataString(Need(m, "phoneNumberId"))}/messages")
        {
            Content = JsonContent.Create(new
            {
                messaging_product = "whatsapp", to = m.Address.TrimStart('+'), type = "template",
                template = new { name = template, language = new { code = lang }, components = new[] { new { type = "body", parameters } } },
            }),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Need(m, "accessToken"));
        using var res = await Client.SendAsync(req, ct);
        await EnsureAsync(res, ct);
    }
}

/// <summary>Slack: a direct message from the organisation's bot (chat.postMessage to the member id), the member found by e-mail (users.lookupByEmail; scopes chat:write, users:read.email).</summary>
public sealed class SlackSender(IHttpClientFactory http) : HttpChannelSender(http)
{
    public override string Kind => ChannelRules.Slack;
    private static readonly string[] Permanent = ["invalid_auth", "not_authed", "account_inactive", "channel_not_found", "user_not_found", "token_revoked", "missing_scope", "users_not_found"];
    private async Task<JsonElement> CallAsync(HttpRequestMessage req, ChannelSend m, CancellationToken ct)
    {
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Need(m, "botToken"));
        using var res = await Client.SendAsync(req, ct);
        await EnsureAsync(res, ct);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement.Clone();
        if (root.TryGetProperty("ok", out var ok) && ok.GetBoolean()) return root;
        var err = root.TryGetProperty("error", out var e) ? e.GetString() ?? "error" : "error";
        throw new ChannelSendException($"Slack: {err}", Permanent.Contains(err));
    }
    public override async Task SendAsync(ChannelSend m, CancellationToken ct = default)
    {
        var text = $"*{m.Title}*" + (string.IsNullOrWhiteSpace(m.Body) ? "" : "\n" + m.Body) + (m.Url is null ? "" : $"\n<{m.Url}|Open in the helpdesk>");
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://slack.com/api/chat.postMessage") { Content = JsonContent.Create(new { channel = m.Address, text, unfurl_links = false }) };
        await CallAsync(req, m, ct);
    }
    public override async Task<string?> ResolveAsync(ChannelSend settings, string email, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "https://slack.com/api/users.lookupByEmail?email=" + Uri.EscapeDataString(email));
        try { var r = await CallAsync(req, settings, ct); return r.GetProperty("user").GetProperty("id").GetString(); }
        catch (ChannelSendException e) when (e.Message == "Slack: users_not_found") { return null; }
    }
}

/// <summary>Microsoft Teams through the person's own Workflows flow ("When a Teams webhook request is received" → post in a chat): an Adaptive Card.</summary>
public sealed class TeamsSender(IHttpClientFactory http) : HttpChannelSender(http)
{
    public override string Kind => ChannelRules.Teams;
    public override async Task SendAsync(ChannelSend m, CancellationToken ct = default)
    {
        var body = new List<object> { new { type = "TextBlock", text = m.Title, weight = "Bolder", wrap = true } };
        if (!string.IsNullOrWhiteSpace(m.Body)) body.Add(new { type = "TextBlock", text = m.Body, wrap = true });
        var card = new Dictionary<string, object?>
        {
            ["$schema"] = "http://adaptivecards.io/schemas/adaptive-card.json", ["type"] = "AdaptiveCard", ["version"] = "1.4", ["body"] = body,
            ["actions"] = m.Url is null ? Array.Empty<object>() : new object[] { new { type = "Action.OpenUrl", title = "Open in the helpdesk", url = m.Url } },
        };
        using var req = new HttpRequestMessage(HttpMethod.Post, m.Address)
        {
            Content = JsonContent.Create(new { type = "message", attachments = new[] { new { contentType = "application/vnd.microsoft.card.adaptive", content = card } } }),
        };
        using var res = await Client.SendAsync(req, ct);
        await EnsureAsync(res, ct);
    }
}

/// <summary>NTF-003: sends the channel outbox every few seconds (same switches as the review scheduler, so tests run it by hand).</summary>
public sealed class ChannelWorker(IServiceScopeFactory scopes, IOptions<ReviewSchedulerOptions> options, ILogger<ChannelWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled) return;
        try { await Task.Delay(TimeSpan.FromSeconds(options.Value.StartupDelaySeconds + 15), stoppingToken); } catch (OperationCanceledException) { return; }
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var r = await scope.ServiceProvider.GetRequiredService<AnnabaDigital.Application.Helpdesk.ChannelService>().DispatchDueAsync(stoppingToken);
                if (r.Failed + r.FellBack > 0) log.LogWarning("Channels: {Delivered} delivered, {Retried} retried, {Failed} failed, {FellBack} sent by e-mail instead", r.Delivered, r.Retried, r.Failed, r.FellBack);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception e) { log.LogError(e, "Channel run failed"); }
            try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); } catch (OperationCanceledException) { return; }
        }
    }
}
