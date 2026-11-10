using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Tests.Infra;
using Xunit;

namespace AnnabaDigital.Tests;

/// <summary>API keys and webhooks (INT-001/002, R2 slice 10): keys only on the public API and only within their scopes, idempotent creates, revocation, rate limits; signed, retried webhooks; the SSRF rules.</summary>
[Collection("db")]
public sealed class IntegrationTests(PostgresFixture fx)
{
    private static string Uniq() => Guid.NewGuid().ToString("N")[..8];
    private static async Task<JsonElement> Ok(HttpResponseMessage r) { Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync()); return r.StatusCode == HttpStatusCode.NoContent ? default : await r.Body(); }
    private static Guid IdOf(JsonElement e) => e.GetProperty("id").GetGuid();
    private static string Str(JsonElement e, string p) => e.GetProperty(p).GetString()!;

    /// <summary>Like another system: the proxy key the web front adds, a bearer token, no Origin, no cookie.</summary>
    private static Task<HttpResponseMessage> Api(ApiFactory f, string? token, HttpMethod method, string path, object? body = null, string? idempotency = null)
    {
        var req = new HttpRequestMessage(method, path);
        req.Headers.Add("X-Internal-Proxy-Key", ApiFactory.ProxyKey);
        if (token is not null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (idempotency is not null) req.Headers.Add("Idempotency-Key", idempotency);
        if (body is not null) req.Content = JsonContent.Create(body);
        return f.CreateClient().SendAsync(req);
    }

    private sealed record World(ApiFactory F, Seed Seed, TestBrowser Admin, User AdminUser, TestBrowser Manager, TestBrowser Requester, User RequesterUser, Guid ServiceId);

    private async Task<World> Build()
    {
        var iso = await fx.NewIsolatedAsync(); var f = iso.Factory; var seed = new Seed(f, iso.Clock);
        async Task<(User, TestBrowser)> Person(string role) { var u = await seed.UserAsync(role: role); var b = TestBrowser.Create(f); await b.LoginAsync(u.Email, Seed.Password); return (u, b); }
        var (au, admin) = await Person("super_admin"); var (_, mgr) = await Person("ga_manager"); var (ru, req) = await Person("user");
        var cat = IdOf(await Ok(await admin.Post("/api/catalog/categories", new { name = $"Cat {Uniq()}" })));
        var svc = IdOf(await Ok(await admin.Post("/api/catalog/services", new { name = $"Facilities {Uniq()}", categoryId = cat })));
        await Ok(await admin.Post($"/api/catalog/services/{svc}/publish"));
        await admin.Post("/api/auth/reauth", new { password = Seed.Password });
        return new World(f, seed, admin, au, mgr, req, ru, svc);
    }

    [Fact]
    public async Task Keys_work_only_on_the_public_api_within_their_scopes_and_stop_when_revoked_or_expired()
    {
        var w = await Build();
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Manager.Post("/api/integrations/keys", new { name = "Mine", scopes = new[] { "tickets.read" } })).StatusCode);   // integrations.manage only
        Assert.Equal((HttpStatusCode)422, (await w.Admin.Post("/api/integrations/keys", new { name = "x", scopes = new[] { "everything" } })).StatusCode);
        var reader = await Ok(await w.Admin.Post("/api/integrations/keys", new { name = "BI reader", scopes = new[] { "tickets.read" } }));
        var writer = await Ok(await w.Admin.Post("/api/integrations/keys", new { name = "HR system", scopes = new[] { "tickets.write", "tickets.read" } }));
        string rt = Str(reader, "token"), wt = Str(writer, "token");
        Assert.StartsWith("adk_", rt);
        Assert.DoesNotContain(rt, await (await w.Admin.Get("/api/integrations/keys")).Content.ReadAsStringAsync());          // the token is never shown again
        var listed = (await Ok(await w.Admin.Get("/api/integrations/keys"))).EnumerateArray().ToList();
        Assert.Equal(rt.Substring(4, 8), Str(listed.Single(k => Str(k, "name") == "BI reader"), "prefix"));

        // a create with Idempotency-Key, on behalf of a person; repeating it returns the same ticket
        var body = new { serviceId = w.ServiceId, subject = "Desk chair broken (from HR)", description = "Reported by phone", requesterEmail = w.RequesterUser.Email.ToUpperInvariant() };
        var created = await Api(w.F, wt, HttpMethod.Post, "/api/v1/tickets", body, "hr-4711");
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var t = await created.Body();
        var again = await Ok(await Api(w.F, wt, HttpMethod.Post, "/api/v1/tickets", body, "hr-4711"));
        Assert.Equal(IdOf(t), IdOf(again));
        var row = await w.Seed.Db(db => db.Tickets.FirstAsync(x => x.Id == IdOf(t)));
        Assert.Equal(w.RequesterUser.Id, row.RequesterId); Assert.Equal(w.AdminUser.Id, row.CreatedById);           // acts as the person who made the key
        await Ok(await Api(w.F, wt, HttpMethod.Post, $"/api/v1/tickets/{IdOf(t)}/comments", new { body = "Chair model: Ergo 2" }));
        Assert.Contains(Str(t, "number"), (await Ok(await Api(w.F, rt, HttpMethod.Get, "/api/v1/tickets?q=chair"))).GetProperty("data").EnumerateArray().Select(x => Str(x, "number")));
        Assert.Equal(Str(t, "subject"), Str(await Ok(await Api(w.F, rt, HttpMethod.Get, $"/api/v1/tickets/{IdOf(t)}")), "subject"));

        // scopes, the public API only, no browser sessions there, and no Origin needed
        Assert.Equal(HttpStatusCode.Forbidden, (await Api(w.F, rt, HttpMethod.Post, "/api/v1/tickets", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Api(w.F, rt, HttpMethod.Get, "/api/v1/services")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Api(w.F, rt, HttpMethod.Get, "/api/tickets")).StatusCode);                  // a key is not a session elsewhere
        Assert.Equal(HttpStatusCode.Unauthorized, (await w.Admin.Get("/api/v1/tickets")).StatusCode);                              // the public API wants a key
        Assert.Equal(HttpStatusCode.Unauthorized, (await Api(w.F, "adk_" + Uniq() + Uniq() + Uniq(), HttpMethod.Get, "/api/v1/tickets")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Api(w.F, null, HttpMethod.Get, "/api/v1/tickets")).StatusCode);

        // revoked and expired keys stop at once
        Assert.Equal(HttpStatusCode.NoContent, (await w.Admin.Post($"/api/integrations/keys/{IdOf(reader.GetProperty("key"))}/revoke")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Api(w.F, rt, HttpMethod.Get, "/api/v1/tickets")).StatusCode);
        await w.Seed.Db(async db => { await db.ApiKeys.Where(k => k.Name == "HR system").ExecuteUpdateAsync(u => u.SetProperty(k => k.ExpiresAt, DateTimeOffset.UtcNow.AddDays(-1))); return 0; });
        Assert.Equal(HttpStatusCode.Unauthorized, (await Api(w.F, wt, HttpMethod.Get, "/api/v1/tickets")).StatusCode);
        Assert.True(await w.Seed.Db(db => db.AuditEvents.AnyAsync(a => a.Action == "integration.key.revoke")));
    }

    [Fact]
    public async Task A_key_is_rate_limited_per_minute()
    {
        var w = await Build();
        var token = Str(await Ok(await w.Admin.Post("/api/integrations/keys", new { name = "Busy", scopes = new[] { "catalog.read" } })), "token");
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < IntegrationRules.RequestsPerMinute + 5 && !statuses.Contains((HttpStatusCode)429); i++) statuses.Add((await Api(w.F, token, HttpMethod.Get, "/api/v1/services")).StatusCode);
        Assert.Contains((HttpStatusCode)429, statuses);
        Assert.True(statuses.Count(s => s == HttpStatusCode.OK) >= IntegrationRules.RequestsPerMinute - 60);   // a minute boundary may fall inside the loop
    }

    [Fact]
    public async Task Webhooks_are_signed_sent_for_subscribed_events_only_and_retried_on_failure()
    {
        var w = await Build();
        var got = new ConcurrentQueue<(string Path, IHeaderDictionary Headers, string Body)>();
        var receiver = WebApplication.CreateSlimBuilder();
        receiver.WebHost.UseUrls("http://127.0.0.1:0");
        var app = receiver.Build();
        app.MapPost("/{kind}", async (string kind, HttpContext ctx) =>
        {
            using var reader = new StreamReader(ctx.Request.Body); var b = await reader.ReadToEndAsync();
            got.Enqueue((kind, new HeaderDictionary(ctx.Request.Headers.ToDictionary(h => h.Key, h => h.Value)), b));
            return kind == "fail" ? Results.StatusCode(500) : Results.Ok();
        });
        await app.StartAsync();
        try
        {
            var baseUrl = app.Urls.First();
            Assert.Equal((HttpStatusCode)422, (await w.Admin.Post("/api/integrations/webhooks", new { url = baseUrl + "/ok", events = new[] { "ticket.deleted" } })).StatusCode);
            var ok = await Ok(await w.Admin.Post("/api/integrations/webhooks", new { url = baseUrl + "/ok", description = "Ops chat", events = new[] { "ticket.created", "ticket.commented" } }));
            var secret = Str(ok, "secret"); Assert.StartsWith("whsec_", secret);
            var failing = await Ok(await w.Admin.Post("/api/integrations/webhooks", new { url = baseUrl + "/fail", events = new[] { "ticket.created" } }));
            await Ok(await w.Admin.Post("/api/integrations/webhooks", new { url = baseUrl + "/off", events = new[] { "ticket.created" }, enabled = false }));

            var id = IdOf(await Ok(await w.Requester.Post($"/api/portal/services/{w.ServiceId}/requests", new { subject = "Light flickers in corridor", answers = new { } })));
            await Ok(await w.Admin.Post($"/api/tickets/{id}/comments", new { body = "Internal: check the ballast", @internal = true }));
            await Ok(await w.Admin.Post($"/api/tickets/{id}/comments", new { body = "We'll come today." }));

            for (var i = 0; i < 120 && got.Count(g => g.Path == "ok") < 2; i++) await Task.Delay(250);         // up to 30 s: the whole suite shares one database server
            var oks = got.Where(g => g.Path == "ok").OrderBy(g => g.Headers["X-Annaba-Event"].ToString() == "ticket.created" ? 0 : 1).ToList();
            Assert.True(oks.Count == 2, $"ok endpoint got: {string.Join(", ", got.Select(g => g.Path + ":" + g.Headers["X-Annaba-Event"]))}");
            Assert.Equal(["ticket.created", "ticket.commented"], oks.Select(g => g.Headers["X-Annaba-Event"].ToString()));   // the internal note never left
            foreach (var g in oks)
            {
                var ts = long.Parse(g.Headers["X-Annaba-Timestamp"].ToString());
                var expected = "v1=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"{ts}.{g.Body}"))).ToLowerInvariant();
                Assert.Equal(expected, g.Headers["X-Annaba-Signature"].ToString());
                using var doc = JsonDocument.Parse(g.Body);
                Assert.Equal(g.Headers["X-Annaba-Delivery"].ToString(), doc.RootElement.GetProperty("id").GetString());
                Assert.Equal(id, doc.RootElement.GetProperty("data").GetProperty("ticket").GetProperty("id").GetGuid());
            }
            Assert.Equal("We'll come today.", JsonDocument.Parse(oks[1].Body).RootElement.GetProperty("data").GetProperty("extra").GetProperty("body").GetString());
            Assert.DoesNotContain(got, g => g.Path == "off");

            // the failing endpoint got it once and waits about a minute before trying again
            for (var i = 0; i < 120 && !got.Any(g => g.Path == "fail"); i++) await Task.Delay(250);
            var fid = failing.GetProperty("webhook").GetProperty("id").GetGuid();
            WebhookDelivery? d = null;
            for (var i = 0; i < 80 && d?.LastStatus is null; i++) { d = await w.Seed.Db(db => db.WebhookDeliveries.FirstAsync(x => x.EndpointId == fid)); if (d.LastStatus is null) await Task.Delay(250); }
            Assert.Equal(1, d!.Attempts); Assert.Equal(500, d.LastStatus); Assert.Null(d.DeliveredAt); Assert.Null(d.FailedAt);
            Assert.True(d.NextAttemptAt > DateTimeOffset.UtcNow.AddSeconds(30));
            var log = await Ok(await w.Admin.Get($"/api/integrations/webhooks/{fid}/deliveries"));
            Assert.Equal("HTTP 500", Str(log[0], "lastError"));
            var list = (await Ok(await w.Admin.Get("/api/integrations/webhooks"))).EnumerateArray().ToList();
            Assert.Equal(1, list.Single(x => IdOf(x) == fid).GetProperty("pending").GetInt32());
            Assert.DoesNotContain(secret, (await (await w.Admin.Get("/api/integrations/webhooks")).Content.ReadAsStringAsync()));
        }
        finally { await app.StopAsync(); await app.DisposeAsync(); }
    }

    [Fact]
    public void Webhook_targets_must_be_public_https_and_backoff_grows()
    {
        foreach (var bad in new[] { "http://hooks.example.com/x", "https://127.0.0.1/x", "https://169.254.169.254/latest", "https://10.0.0.5/x", "https://192.168.1.10/x", "https://localhost/x",
                     "https://intranet/x", "https://user:pw@hooks.example.com/x", "https://hooks.example.com:22/x", "https://[::1]/x", "https://[fd00::1]/x", "https://metadata.internal/x", "ftp://hooks.example.com" })
            Assert.NotNull(IntegrationRules.CheckUrl(bad, allowPrivate: false));
        Assert.Null(IntegrationRules.CheckUrl("https://hooks.example.com/helpdesk", allowPrivate: false));
        Assert.Null(IntegrationRules.CheckUrl("https://hooks.example.com:8443/helpdesk", allowPrivate: false));
        Assert.False(IntegrationRules.IsPublic(IPAddress.Parse("100.64.1.1")));
        Assert.False(IntegrationRules.IsPublic(IPAddress.Parse("::ffff:10.1.2.3")));
        Assert.True(IntegrationRules.IsPublic(IPAddress.Parse("8.8.8.8")));
        Assert.True(IntegrationRules.IsPublic(IPAddress.Parse("2606:4700:4700::1111")));
        Assert.Equal([TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), TimeSpan.FromHours(1)], Enumerable.Range(1, 4).Select(IntegrationRules.Backoff));
    }
}
