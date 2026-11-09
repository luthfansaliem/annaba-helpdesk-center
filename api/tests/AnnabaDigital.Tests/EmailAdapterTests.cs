using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Email;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Infrastructure.Email;
using AnnabaDigital.Infrastructure.Persistence;
using AnnabaDigital.Tests.Infra;
using Xunit;

namespace AnnabaDigital.Tests;

public sealed class EmailAdapterTests
{
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> f) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Req, string Body)> Calls { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        { Calls.Add((r, r.Content is null ? "" : await r.Content.ReadAsStringAsync(ct))); return f(r); }
    }

    [Fact]
    public async Task Templates_html_encode_user_controlled_values()
    {
        var m = EmailTemplates.VerifyEmail("a@b.co", "<script>alert(1)</script>", "http://x/verify?token=abc&x=\"><img>");
        Assert.DoesNotContain("<script>", m.Html); Assert.Contains("&lt;script&gt;", m.Html); Assert.DoesNotContain("\"><img>", m.Html);
        Assert.DoesNotContain("password", m.Text, StringComparison.OrdinalIgnoreCase); // emails never carry passwords
    }

    [Fact]
    public async Task Resend_adapter_sends_bearer_json_and_hides_provider_error_bodies()
    {
        var h = new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var sender = new ResendEmailSender(new HttpClient(h), Options.Create(new EmailOptions { Provider = "resend", ResendApiKey = "re_test_key", From = "Annaba Digital <x@annabadigital.io>" }));
        await sender.SendAsync(new EmailMessage("to@x.co", "Subj", "text", "<p>html</p>"));
        var (req, body) = Assert.Single(h.Calls);
        Assert.Equal("Bearer", req.Headers.Authorization!.Scheme); Assert.Equal("re_test_key", req.Headers.Authorization.Parameter);
        Assert.Equal("https://api.resend.com/emails", req.RequestUri!.ToString());
        Assert.Equal("to@x.co", JsonDocument.Parse(body).RootElement.GetProperty("to")[0].GetString());

        var failing = new ResendEmailSender(new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("secret-details re_test_key") })),
            Options.Create(new EmailOptions { ResendApiKey = "re_test_key" }));
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => failing.SendAsync(new EmailMessage("a@b.co", "s", "t", "h")));
        Assert.DoesNotContain("re_test_key", ex.Message); Assert.DoesNotContain("secret-details", ex.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new ResendEmailSender(new HttpClient(), Options.Create(new EmailOptions())).SendAsync(new EmailMessage("a@b.co", "s", "t", "h")));
    }

    [Fact]
    public async Task Hibp_checker_sends_only_a_5_char_hash_prefix_detects_matches_and_fails_open()
    {
        var sha1 = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData("hunter2-breached"u8.ToArray()));
        var h = new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($"0000000000000000000000000000000000A:0\n{sha1[5..]}:42\nFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF:3") });
        var chk = new HibpBreachedPasswordChecker(new HttpClient(h), NullLogger<HibpBreachedPasswordChecker>.Instance);
        Assert.True(await chk.IsBreachedAsync("hunter2-breached"));
        Assert.Equal($"https://api.pwnedpasswords.com/range/{sha1[..5]}", h.Calls[0].Req.RequestUri!.ToString());
        Assert.DoesNotContain("hunter2", h.Calls[0].Req.RequestUri!.ToString());
        Assert.False(await chk.IsBreachedAsync("some-other-password-xyz"));

        var down = new HibpBreachedPasswordChecker(new HttpClient(new Handler(_ => throw new HttpRequestException("offline"))), NullLogger<HibpBreachedPasswordChecker>.Instance);
        Assert.False(await down.IsBreachedAsync("anything-at-all")); // fail open
        var bad = new HibpBreachedPasswordChecker(new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError))), NullLogger<HibpBreachedPasswordChecker>.Instance);
        Assert.False(await bad.IsBreachedAsync("anything-at-all"));
    }

    private sealed class FlakySender(Func<int, EmailMessage, Exception?> fail) : IEmailSender
    {
        public int Calls; public List<EmailMessage> Delivered { get; } = [];
        public Task SendAsync(EmailMessage m, CancellationToken ct = default)
        {
            var ex = fail(++Calls, m); if (ex is not null) throw ex;
            Delivered.Add(m); return Task.CompletedTask;
        }
    }

    /// <summary>Own provider over an isolated, migrated database; the factory that created it is disposed so its dispatcher cannot steal rows.</summary>
    private static async Task<(ServiceProvider Sp, OutboxEmailQueue Queue, EmailDispatcherService Svc)> HarnessAsync(PostgresFixture pg, FlakySender sender, int maxAttempts = 3)
    {
        var (f, _, cs) = await pg.NewIsolatedAsync();
        await f.DisposeAsync();
        var sp = new ServiceCollection().AddLogging()
            .AddDbContext<AnnabaDigitalDbContext>(o => o.UseNpgsql(cs).UseSnakeCaseNamingConvention())
            .AddSingleton<IEmailSender>(sender).BuildServiceProvider();
        var scopes = sp.GetRequiredService<IServiceScopeFactory>();
        var queue = new OutboxEmailQueue(scopes, NullLogger<OutboxEmailQueue>.Instance);
        var svc = new EmailDispatcherService(queue, scopes, NullLogger<EmailDispatcherService>.Instance, Options.Create(new EmailOptions { MaxAttempts = maxAttempts }));
        return (sp, queue, svc);
    }

    private static async Task<OutboxEmail> RowAsync(ServiceProvider sp)
    {
        using var scope = sp.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AnnabaDigitalDbContext>().OutboxEmails.AsNoTracking().SingleAsync();
    }

    private static async Task MakeDueAsync(ServiceProvider sp)
    {
        using var scope = sp.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AnnabaDigitalDbContext>().OutboxEmails.ExecuteUpdateAsync(u => u.SetProperty(x => x.NextAttemptAt, DateTimeOffset.UtcNow.AddSeconds(-1)));
    }

    [Collection("db")]
    public sealed class OutboxTests(PostgresFixture pg)
    {
        [Fact]
        public async Task Enqueue_persists_then_dispatch_sends_and_wipes_the_body()
        {
            var sender = new FlakySender((_, _) => null);
            var (sp, queue, svc) = await HarnessAsync(pg, sender);
            queue.Enqueue(new EmailMessage("a@b.co", "Welcome", "secret-link-text", "<p>secret-link-html</p>"));
            var queued = await RowAsync(sp);
            Assert.Equal("secret-link-text", queued.Text); Assert.Null(queued.SentAt);

            Assert.True(await svc.DispatchBatchAsync(CancellationToken.None));
            Assert.Equal("a@b.co", Assert.Single(sender.Delivered).To);
            var sent = await RowAsync(sp);
            Assert.NotNull(sent.SentAt); Assert.Equal("", sent.Text); Assert.Equal("", sent.Html); // one-time links do not linger in the database
            Assert.False(await svc.DispatchBatchAsync(CancellationToken.None)); // nothing left to do
        }

        [Fact]
        public async Task Transient_failures_back_off_and_retry_until_sent()
        {
            var sender = new FlakySender((n, _) => n < 3 ? new HttpRequestException("transient") : null);
            var (sp, queue, svc) = await HarnessAsync(pg, sender);
            queue.Enqueue(new EmailMessage("a@b.co", "s", "t", "h"));

            await svc.DispatchBatchAsync(CancellationToken.None);
            var r1 = await RowAsync(sp);
            Assert.Equal(1, r1.Attempts); Assert.Null(r1.SentAt); Assert.Equal("HttpRequestException", r1.LastError); Assert.True(r1.NextAttemptAt > DateTimeOffset.UtcNow);
            Assert.False(await svc.DispatchBatchAsync(CancellationToken.None)); // backing off: not due yet

            await MakeDueAsync(sp); await svc.DispatchBatchAsync(CancellationToken.None);
            await MakeDueAsync(sp); await svc.DispatchBatchAsync(CancellationToken.None);
            var done = await RowAsync(sp);
            Assert.Equal(3, done.Attempts); Assert.NotNull(done.SentAt); Assert.Single(sender.Delivered);
        }

        [Fact]
        public async Task Gives_up_after_max_attempts_and_wipes_the_body_without_storing_provider_details()
        {
            var sender = new FlakySender((_, _) => new HttpRequestException("provider said: key re_live_123 is bad"));
            var (sp, queue, svc) = await HarnessAsync(pg, sender, maxAttempts: 2);
            queue.Enqueue(new EmailMessage("a@b.co", "s", "token-in-body", "h"));
            await svc.DispatchBatchAsync(CancellationToken.None);
            await MakeDueAsync(sp); await svc.DispatchBatchAsync(CancellationToken.None);
            var row = await RowAsync(sp);
            Assert.NotNull(row.FailedAt); Assert.Null(row.SentAt); Assert.Equal("", row.Text);
            Assert.Equal("HttpRequestException", row.LastError); Assert.DoesNotContain("re_live", row.LastError);
            await MakeDueAsync(sp);
            Assert.False(await svc.DispatchBatchAsync(CancellationToken.None)); // abandoned rows are never retried
            Assert.Empty(sender.Delivered);
        }

        [Fact]
        public async Task A_crashed_claim_is_retried_after_the_lease_and_a_fresh_dispatcher_picks_up_unsent_mail()
        {
            var sender = new FlakySender((_, _) => null);
            var (sp, queue, svc) = await HarnessAsync(pg, sender);
            queue.Enqueue(new EmailMessage("a@b.co", "s", "t", "h"));
            // Simulate a worker that claimed the row and died before sending: attempts bumped, lease in the future.
            using (var scope = sp.CreateScope())
                await scope.ServiceProvider.GetRequiredService<AnnabaDigitalDbContext>().OutboxEmails.ExecuteUpdateAsync(u => u.SetProperty(x => x.Attempts, 1).SetProperty(x => x.NextAttemptAt, DateTimeOffset.UtcNow.AddMinutes(2)));
            Assert.False(await svc.DispatchBatchAsync(CancellationToken.None)); // still leased
            await MakeDueAsync(sp); // lease expired
            var restarted = new EmailDispatcherService(queue, sp.GetRequiredService<IServiceScopeFactory>(), NullLogger<EmailDispatcherService>.Instance, Options.Create(new EmailOptions()));
            Assert.True(await restarted.DispatchBatchAsync(CancellationToken.None));
            Assert.Single(sender.Delivered); Assert.Equal(2, (await RowAsync(sp)).Attempts);
        }
    }
}
