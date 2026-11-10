using System.Net;
using System.Net.Sockets;
using System.Text;
using AnnabaDigital.Api;
using AnnabaDigital.Api.Http;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Email;
using AnnabaDigital.Infrastructure.Email;
using AnnabaDigital.Infrastructure.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace AnnabaDigital.Tests;

public sealed class SmtpEmailTests
{
    /// <summary>Minimal single-connection SMTP server (no TLS, no auth): enough to check what the adapter puts on the wire.</summary>
    private sealed class FakeSmtp : IDisposable
    {
        private readonly TcpListener _l = new(IPAddress.Loopback, 0);
        public int Port => ((IPEndPoint)_l.LocalEndpoint).Port;
        public string? MailFrom, RcptTo, Data;
        public Task Done { get; private set; } = Task.CompletedTask;
        public FakeSmtp() { _l.Start(); Done = Task.Run(Serve); }
        private async Task Serve()
        {
            using var c = await _l.AcceptTcpClientAsync();
            using var s = c.GetStream();
            using var r = new StreamReader(s, Encoding.ASCII); using var w = new StreamWriter(s, new UTF8Encoding(false)) { NewLine = "\r\n", AutoFlush = true };
            await w.WriteLineAsync("220 fake ESMTP");
            while (await r.ReadLineAsync() is { } line)
            {
                if (line.StartsWith("EHLO", StringComparison.OrdinalIgnoreCase)) { await w.WriteLineAsync("250-fake"); await w.WriteLineAsync("250 8BITMIME"); }
                else if (line.StartsWith("MAIL FROM", StringComparison.OrdinalIgnoreCase)) { MailFrom = line; await w.WriteLineAsync("250 ok"); }
                else if (line.StartsWith("RCPT TO", StringComparison.OrdinalIgnoreCase)) { RcptTo = line; await w.WriteLineAsync("250 ok"); }
                else if (line.Equals("DATA", StringComparison.OrdinalIgnoreCase))
                {
                    await w.WriteLineAsync("354 go"); var sb = new StringBuilder();
                    while (await r.ReadLineAsync() is { } d && d != ".") sb.AppendLine(d);
                    Data = sb.ToString(); await w.WriteLineAsync("250 queued");
                }
                else if (line.StartsWith("QUIT", StringComparison.OrdinalIgnoreCase)) { await w.WriteLineAsync("221 bye"); return; }
                else await w.WriteLineAsync("250 ok");
            }
        }
        public void Dispose() => _l.Stop();
    }

    private static SmtpEmailSender Sender(int port, string host = "127.0.0.1", string security = "none")
        => new(Options.Create(new EmailOptions { Provider = "smtp", SmtpHost = host, SmtpPort = port, SmtpSecurity = security, From = "Annaba Digital <no-reply@annabadigital.io>" }));

    [Fact]
    public async Task Smtp_adapter_sends_a_multipart_message_with_the_configured_sender()
    {
        using var srv = new FakeSmtp();
        await Sender(srv.Port).SendAsync(new EmailMessage("to@example.com", "Welcome aboard", "plain-text-link", "<p>html-link</p>"));
        await srv.Done.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Contains("no-reply@annabadigital.io", srv.MailFrom);
        Assert.Contains("to@example.com", srv.RcptTo);
        Assert.Contains("Subject: Welcome aboard", srv.Data);
        Assert.Contains("multipart/alternative", srv.Data);
        Assert.Contains("text/plain", srv.Data); Assert.Contains("text/html", srv.Data);
        Assert.Contains("plain-text-link", srv.Data); Assert.Contains("html-link", srv.Data);
    }

    [Fact]
    public async Task Smtp_adapter_fails_clearly_when_unconfigured_or_unreachable()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => new SmtpEmailSender(Options.Create(new EmailOptions { Provider = "smtp" })).SendAsync(new EmailMessage("a@b.co", "s", "t", "h")));
        var free = new TcpListener(IPAddress.Loopback, 0); free.Start(); var port = ((IPEndPoint)free.LocalEndpoint).Port; free.Stop(); // nothing listens here any more
        await Assert.ThrowsAnyAsync<Exception>(() => Sender(port).SendAsync(new EmailMessage("a@b.co", "s", "t", "h")));
    }

    private static void Validate(EmailOptions mail, AnnabaDigital.Infrastructure.StorageOptions? storage = null)
    {
        var sp = new ServiceCollection()
            .AddSingleton(Options.Create(new ApiOptions { Origin = "https://app.example.com", ProxyKey = new string('p', 40) }))
            .AddSingleton(Options.Create(new SecurityOptions { CsrfKey = new string('c', 40), PasswordPepper = new string('x', 40), EncryptionKeys = new() { ["1"] = Convert.ToBase64String(new byte[32].Select((_, i) => (byte)(i + 7)).ToArray()) } }))
            .AddSingleton(Options.Create(storage ?? new AnnabaDigital.Infrastructure.StorageOptions { Path = Path.GetFullPath("files"), ClamAvHost = "clamav" }))
            .AddSingleton(Options.Create(mail)).BuildServiceProvider();
        StartupChecks.Validate(sp, new TestEnv());
    }
    private sealed class TestEnv : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production; public string ApplicationName { get; set; } = "t";
        public string ContentRootPath { get; set; } = "."; public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    [Fact]
    public void Production_accepts_smtp_with_tls_or_resend_with_a_key()
    {
        Validate(new EmailOptions { Provider = "smtp", SmtpHost = "smtp.example.com", SmtpPort = 587, SmtpSecurity = "starttls", SmtpUsername = "u", SmtpPassword = "p" });
        Validate(new EmailOptions { Provider = "smtp", SmtpHost = "smtp.example.com", SmtpPort = 465, SmtpSecurity = "ssl" });
        Validate(new EmailOptions { Provider = "resend", ResendApiKey = "re_x" });
    }

    [Theory]
    [InlineData("console", null, "starttls", null)]
    [InlineData("smtp", null, "starttls", null)]
    [InlineData("smtp", "smtp.example.com", "none", null)]
    [InlineData("smtp", "smtp.example.com", "starttls", "user-without-password")]
    [InlineData("resend", null, "starttls", null)]
    public void Production_rejects_unsafe_or_incomplete_email_configuration(string provider, string? host, string security, string? user)
        => Assert.Throws<InvalidOperationException>(() => Validate(new EmailOptions { Provider = provider, SmtpHost = host, SmtpSecurity = security, SmtpUsername = user }));

    private static readonly EmailOptions GoodMail = new() { Provider = "resend", ResendApiKey = "re_x" };

    [Fact]
    public void Production_accepts_files_on_an_absolute_path_with_a_scanner_or_an_explicit_no_scan_decision()
    {
        var abs = Path.GetFullPath("files");
        Validate(GoodMail, new AnnabaDigital.Infrastructure.StorageOptions { Path = abs, ClamAvHost = "clamav" });
        Validate(GoodMail, new AnnabaDigital.Infrastructure.StorageOptions { Path = abs, AllowUnscanned = true });
    }

    [Theory]
    [InlineData("data/files", "clamav", false)]       // relative path: lost with the container
    [InlineData("ABS", null, false)]                   // no scanner and no decision to go without
    [InlineData("ABS", " ", false)]
    public void Production_rejects_uploads_without_a_persistent_path_or_a_scanning_decision(string path, string? clam, bool allow)
        => Assert.Throws<InvalidOperationException>(() => Validate(GoodMail, new AnnabaDigital.Infrastructure.StorageOptions { Path = path == "ABS" ? Path.GetFullPath("files") : path, ClamAvHost = clam, AllowUnscanned = allow }));
}
