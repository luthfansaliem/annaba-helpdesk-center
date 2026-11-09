using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Infrastructure.Persistence;
using AnnabaDigital.Tests.Infra;
using Xunit;

namespace AnnabaDigital.Tests;

[Collection("db")]
public sealed class EmailDeliveryTests(PostgresFixture fx)
{
    private readonly Seed _seed = new(fx.Factory, fx.Clock);
    private static string U() => Guid.NewGuid().ToString("N")[..10];

    /// <summary>Rows are due far in the future so the running dispatcher never touches them.</summary>
    private async Task AddAsync(string subject, string state)
    {
        using var scope = fx.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnnabaDigitalDbContext>();
        var now = fx.Clock.UtcNow;
        db.OutboxEmails.Add(new OutboxEmail
        {
            ToAddress = $"{U()}@example.com", Subject = subject, Text = "", Html = "", CreatedAt = now, NextAttemptAt = now.AddDays(1),
            Attempts = state == "queued" ? 1 : 3,
            SentAt = state == "sent" ? now : null, FailedAt = state == "failed" ? now : null, LastError = state == "failed" ? "SmtpCommandException" : null,
        });
        await db.SaveChangesAsync();
    }

    private async Task<TestBrowser> LoginAsync(string role)
    {
        var u = await _seed.UserAsync(role: role); var b = TestBrowser.Create(fx.Factory); await b.LoginAsync(u.Email, Seed.Password); return b;
    }

    [Fact]
    public async Task Admin_sees_queued_sent_and_failed_mail_without_any_message_body()
    {
        var tag = U();
        await AddAsync($"queued-{tag}", "queued"); await AddAsync($"sent-{tag}", "sent"); await AddAsync($"failed-{tag}", "failed");
        var b = await LoginAsync("super_admin");

        var failed = await (await b.Get("/api/email-deliveries?status=failed&pageSize=100")).Body();
        var row = failed.GetProperty("data").EnumerateArray().Single(r => r.GetProperty("subject").GetString() == $"failed-{tag}");
        Assert.Equal("failed", row.GetProperty("status").GetString()); Assert.Equal("SmtpCommandException", row.GetProperty("lastError").GetString());
        Assert.Equal(3, row.GetProperty("attempts").GetInt32());
        Assert.True(failed.GetProperty("summary").GetProperty("failed7d").GetInt32() >= 1);

        var queued = await (await b.Get("/api/email-deliveries?status=queued&pageSize=100")).Body();
        Assert.Contains(queued.GetProperty("data").EnumerateArray(), r => r.GetProperty("subject").GetString() == $"queued-{tag}" && r.GetProperty("status").GetString() == "queued");
        var sent = await (await b.Get("/api/email-deliveries?status=sent&pageSize=100")).Body();
        Assert.Contains(sent.GetProperty("data").EnumerateArray(), r => r.GetProperty("subject").GetString() == $"sent-{tag}" && r.GetProperty("status").GetString() == "sent");
        Assert.DoesNotContain(failed.GetProperty("data").EnumerateArray(), r => r.GetProperty("subject").GetString() == $"sent-{tag}"); // the filter really filters

        var raw = await (await b.Get("/api/email-deliveries?pageSize=100")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("\"text\"", raw); Assert.DoesNotContain("\"html\"", raw); // bodies (one-time links) are never exposed
    }

    [Fact]
    public async Task Only_settings_managers_can_read_the_delivery_log()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await TestBrowser.Create(fx.Factory).Get("/api/email-deliveries")).StatusCode);
        foreach (var role in new[] { "user", "user_admin", "security_auditor" })
            Assert.Equal(HttpStatusCode.Forbidden, (await (await LoginAsync(role)).Get("/api/email-deliveries")).StatusCode);
    }

    [Fact]
    public async Task Public_settings_tell_the_ui_whether_e_mail_can_be_delivered()
    {
        var pub = await (await TestBrowser.Create(fx.Factory).Get("/api/settings/public")).Body();
        Assert.True(pub.GetProperty("emailEnabled").GetBoolean()); // the test host uses the console sender, which counts as configured
    }
}
