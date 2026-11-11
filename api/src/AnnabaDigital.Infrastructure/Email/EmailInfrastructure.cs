using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using AnnabaDigital.Application.Abstractions;

namespace AnnabaDigital.Infrastructure.Email;

public sealed class EmailOptions
{
    /// <summary>"console" (development), "resend" (Resend REST API) or "smtp" (any SMTP server).</summary>
    public string Provider { get; set; } = "console";
    public string From { get; set; } = "Annaba Digital <no-reply@annabadigital.io>";
    public string? ResendApiKey { get; set; }
    /// <summary>SMTP provider settings. <see cref="SmtpSecurity"/> is "starttls" (usually port 587), "ssl" (implicit TLS, usually 465) or "none" (development/local relay only; refused in Production).</summary>
    public string? SmtpHost { get; set; }
    public int SmtpPort { get; set; } = 587;
    public string SmtpSecurity { get; set; } = "starttls";
    public string? SmtpUsername { get; set; }
    public string? SmtpPassword { get; set; }
    /// <summary>Development convenience: print the full message (including one-time links) to the log. Never enable in production.</summary>
    public bool LogBodies { get; set; }
    public int MaxAttempts { get; set; } = 3;
}

/// <summary>Development adapter: logs recipient + subject (and the body only when explicitly enabled). Sends nothing.</summary>
public sealed class ConsoleEmailSender(ILogger<ConsoleEmailSender> log, IOptions<EmailOptions> options) : IEmailSender
{
    public Task SendAsync(EmailMessage m, CancellationToken ct = default)
    {
        if (options.Value.LogBodies) log.LogInformation("EMAIL to={To} subject={Subject}\n{Text}", m.To, m.Subject, m.Text);
        else log.LogInformation("EMAIL to={To} subject={Subject} (body hidden; set Email:LogBodies=true in development to print links)", m.To, m.Subject);
        if (m.Attachment is { } a) log.LogInformation("EMAIL attachment {File} ({Bytes} bytes)", a.FileName, a.Content.Length);
        return Task.CompletedTask;
    }
}

/// <summary>Production adapter (Resend REST API). The API key is read from configuration/secret store and never logged.</summary>
public sealed class ResendEmailSender(HttpClient http, IOptions<EmailOptions> options, IEmailCredentials? credentials = null) : IEmailSender
{
    public async Task SendAsync(EmailMessage m, CancellationToken ct = default)
    {
        var o = options.Value;
        var key = (credentials is null ? null : await credentials.GetResendApiKeyAsync(ct)) ?? o.ResendApiKey; // an administrator-stored (encrypted) key wins over configuration
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("No email API key is configured.");
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        req.Content = m.Attachment is { } a
            ? JsonContent.Create(new { from = o.From, to = new[] { m.To }, subject = m.Subject, text = m.Text, html = m.Html, attachments = new[] { new { filename = a.FileName, content = Convert.ToBase64String(a.Content), content_type = a.ContentType } } })
            : JsonContent.Create(new { from = o.From, to = new[] { m.To }, subject = m.Subject, text = m.Text, html = m.Html });
        using var res = await http.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode) throw new HttpRequestException($"Email provider returned {(int)res.StatusCode}."); // body intentionally not included
    }
}

/// <summary>
/// SMTP adapter (MailKit). Works with any server: a relay such as Gmail/Brevo/SES, or Postfix on the same host.
/// Credentials come from configuration only and are never logged; failures surface as the exception type (the outbox stores nothing else).
/// </summary>
public sealed class SmtpEmailSender(IOptions<EmailOptions> options) : IEmailSender
{
    public async Task SendAsync(EmailMessage m, CancellationToken ct = default)
    {
        var o = options.Value;
        if (string.IsNullOrWhiteSpace(o.SmtpHost)) throw new InvalidOperationException("No SMTP host is configured.");
        var msg = new MimeKit.MimeMessage();
        msg.From.Add(MimeKit.MailboxAddress.Parse(o.From));
        msg.To.Add(MimeKit.MailboxAddress.Parse(m.To));
        msg.Subject = m.Subject;
        var body = new MimeKit.BodyBuilder { TextBody = m.Text, HtmlBody = m.Html };
        if (m.Attachment is { } a) body.Attachments.Add(a.FileName, a.Content, MimeKit.ContentType.Parse(a.ContentType));
        msg.Body = body.ToMessageBody();

        var security = o.SmtpSecurity.Trim().ToLowerInvariant() switch
        {
            "ssl" => MailKit.Security.SecureSocketOptions.SslOnConnect,
            "none" => MailKit.Security.SecureSocketOptions.None,
            _ => MailKit.Security.SecureSocketOptions.StartTls, // default: refuse to continue unless the server upgrades to TLS
        };
        using var client = new MailKit.Net.Smtp.SmtpClient { Timeout = 20_000 };
        await client.ConnectAsync(o.SmtpHost, o.SmtpPort, security, ct);
        if (!string.IsNullOrEmpty(o.SmtpUsername)) await client.AuthenticateAsync(o.SmtpUsername, o.SmtpPassword ?? "", ct);
        await client.SendAsync(msg, ct);
        await client.DisconnectAsync(true, ct);
    }
}

/// <summary>
/// Durable queue: <see cref="Enqueue"/> writes a row to <c>outbox_emails</c> (own short-lived context, so it works after the caller's
/// transaction has committed) and wakes the dispatcher. A crash or restart loses nothing; unsent rows are picked up on the next start.
/// </summary>
/// <summary>The queue services use: tags each mail with the organisation of the current request (see <see cref="OutboxEmailQueue"/>).</summary>
public sealed class TenantEmailQueue(OutboxEmailQueue inner, ITenantContext tenant) : IEmailQueue
{
    public void Enqueue(EmailMessage message) => inner.Enqueue(message, tenant.Scoped ? tenant.TenantId : null);
}

public sealed class OutboxEmailQueue(IServiceScopeFactory scopes, ILogger<OutboxEmailQueue> log) : IEmailQueue
{
    private readonly SemaphoreSlim _signal = new(0, int.MaxValue);
    public Task WaitAsync(TimeSpan timeout, CancellationToken ct) => _signal.WaitAsync(timeout, ct);

    public void Enqueue(EmailMessage message) => Enqueue(message, null);

    /// <summary>
    /// <paramref name="tenantId"/> is the organisation the mail belongs to (what its Email Delivery page lists). Without one (anonymous flows such as
    /// registration, password reset or accepting an invitation) it is the organisation of the recipient: their account, else their pending invitation, else the default.
    /// </summary>
    public void Enqueue(EmailMessage message, Guid? tenantId)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AnnabaDigitalDbContext>();
            var now = DateTimeOffset.UtcNow;
            var to = message.To.Trim().ToLowerInvariant();
            // the recipient's own language if they have an account, else the sender's hint, else English
            var lang = db.Users.IgnoreQueryFilters().Where(u => u.Email == to).Select(u => u.Profile != null ? u.Profile.Language : null).FirstOrDefault() ?? message.Lang ?? "en";
            if (message.Localize is not null) message = message.Localize(lang) ?? message;
            var tid = tenantId
                ?? db.Users.IgnoreQueryFilters().Where(u => u.Email == to).Select(u => (Guid?)u.TenantId).FirstOrDefault()
                ?? db.Invitations.IgnoreQueryFilters().Where(i => i.Email == to).OrderByDescending(i => i.CreatedAt).Select(i => (Guid?)i.TenantId).FirstOrDefault()
                ?? Tenant.DefaultId;
            // the organisation's own wording (subject prefix, closing paragraph, sign-off name), in the recipient's language
            var wording = db.EmailWordings.IgnoreQueryFilters().AsNoTracking().FirstOrDefault(x => x.TenantId == tid);
            message = AnnabaDigital.Application.Email.EmailWordingService.Apply(message, wording, lang);
            db.OutboxEmails.Add(new OutboxEmail { TenantId = tid, ToAddress = message.To, Subject = message.Subject, Text = message.Text, Html = message.Html, CreatedAt = now, NextAttemptAt = now,
                AttachmentName = message.Attachment?.FileName, AttachmentType = message.Attachment?.ContentType, AttachmentContent = message.Attachment?.Content });
            db.SaveChanges();
            _signal.Release();
        }
        catch (Exception e) { log.LogError("Could not persist an email to the outbox ({Error}); it will not be sent.", e.GetType().Name); }
    }
}

/// <summary>Claims due rows with FOR UPDATE SKIP LOCKED (safe with several instances), sends them, and backs off between attempts.</summary>
public sealed class EmailDispatcherService(OutboxEmailQueue queue, IServiceScopeFactory scopes, ILogger<EmailDispatcherService> log, IOptions<EmailOptions> options) : BackgroundService
{
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan Retention = TimeSpan.FromDays(7);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var lastPurge = DateTimeOffset.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var worked = await DispatchBatchAsync(stoppingToken);
                if (DateTimeOffset.UtcNow - lastPurge > TimeSpan.FromHours(1)) { await PurgeAsync(stoppingToken); lastPurge = DateTimeOffset.UtcNow; }
                if (worked) continue;
                try { await queue.WaitAsync(TimeSpan.FromSeconds(5), stoppingToken); } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception e)
            {
                log.LogError("Email dispatcher iteration failed ({Error}).", e.GetType().Name);
                try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); } catch (OperationCanceledException) { return; }
            }
        }
    }

    /// <summary>Returns true when at least one message was handled.</summary>
    public async Task<bool> DispatchBatchAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnnabaDigitalDbContext>();
        var now = DateTimeOffset.UtcNow;
        var leaseUntil = now + Lease;
        // Claim: bump attempts and push next_attempt_at out by the lease, so a crash mid-send retries after the lease and still counts.
        var batch = await db.OutboxEmails.FromSql($"""
            UPDATE outbox_emails SET attempts = attempts + 1, next_attempt_at = {leaseUntil}
            WHERE id IN (SELECT id FROM outbox_emails WHERE sent_at IS NULL AND failed_at IS NULL AND next_attempt_at <= {now}
                         ORDER BY next_attempt_at LIMIT 10 FOR UPDATE SKIP LOCKED)
            RETURNING *
            """).IgnoreQueryFilters().AsNoTracking().ToListAsync(ct);
        if (batch.Count == 0) return false;

        var max = Math.Max(1, options.Value.MaxAttempts);
        foreach (var row in batch)
        {
            try
            {
                var sender = scope.ServiceProvider.GetRequiredService<IEmailSender>();
                await sender.SendAsync(new EmailMessage(row.ToAddress, row.Subject, row.Text, row.Html)
                {
                    Attachment = row is { AttachmentName: { } an, AttachmentContent: { } ac } ? new EmailAttachment(an, row.AttachmentType ?? "application/octet-stream", ac) : null,
                }, ct);
                await db.OutboxEmails.Where(x => x.Id == row.Id).ExecuteUpdateAsync(u => u
                    .SetProperty(x => x.SentAt, DateTimeOffset.UtcNow).SetProperty(x => x.Text, "").SetProperty(x => x.Html, "").SetProperty(x => x.AttachmentContent, (byte[]?)null).SetProperty(x => x.LastError, (string?)null), ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception e)
            {
                var error = e.GetType().Name;
                if (row.Attempts >= max)
                {
                    log.LogError("Giving up on email to {To} after {Attempts} attempts ({Error}).", row.ToAddress, row.Attempts, error);
                    await db.OutboxEmails.Where(x => x.Id == row.Id).ExecuteUpdateAsync(u => u
                        .SetProperty(x => x.FailedAt, DateTimeOffset.UtcNow).SetProperty(x => x.Text, "").SetProperty(x => x.Html, "").SetProperty(x => x.AttachmentContent, (byte[]?)null).SetProperty(x => x.LastError, error), ct);
                }
                else
                {
                    var next = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(Math.Min(300, 2 * Math.Pow(3, row.Attempts)));
                    log.LogWarning("Email to {To} failed (attempt {Attempt}): {Error}", row.ToAddress, row.Attempts, error);
                    await db.OutboxEmails.Where(x => x.Id == row.Id).ExecuteUpdateAsync(u => u
                        .SetProperty(x => x.NextAttemptAt, next).SetProperty(x => x.LastError, error), ct);
                }
            }
        }
        return true;
    }

    private async Task PurgeAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnnabaDigitalDbContext>();
        var cutoff = DateTimeOffset.UtcNow - Retention;
        await db.OutboxEmails.Where(x => (x.SentAt != null && x.SentAt < cutoff) || (x.FailedAt != null && x.FailedAt < cutoff)).ExecuteDeleteAsync(ct);
    }
}

/// <summary>HIBP "Pwned Passwords" range API with k-anonymity: only the first 5 hex chars of the SHA-1 leave the server. Fails open.</summary>
public sealed class HibpBreachedPasswordChecker(HttpClient http, ILogger<HibpBreachedPasswordChecker> log) : IBreachedPasswordChecker
{
    public async Task<bool> IsBreachedAsync(string password, CancellationToken ct = default)
    {
        try
        {
            var sha1 = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(password)));
            var prefix = sha1[..5]; var suffix = sha1[5..];
            using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.pwnedpasswords.com/range/{prefix}");
            req.Headers.Add("Add-Padding", "true"); // hides the response size
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct); cts.CancelAfter(TimeSpan.FromSeconds(2));
            using var res = await http.SendAsync(req, cts.Token);
            if (!res.IsSuccessStatusCode) return false;
            foreach (var line in (await res.Content.ReadAsStringAsync(cts.Token)).Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Trim().Split(':');
                if (parts.Length == 2 && parts[0].Equals(suffix, StringComparison.OrdinalIgnoreCase) && int.TryParse(parts[1], out var count) && count > 0) return true;
            }
            return false;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            log.LogWarning("Breached-password check unavailable ({Error}); failing open.", e.GetType().Name);
            return false;
        }
    }
}

public sealed class EmailInfo(IOptions<EmailOptions> options) : IEmailInfo
{
    public string Provider => options.Value.Provider;
    public bool ConfigurationKeyPresent => !string.IsNullOrWhiteSpace(options.Value.ResendApiKey);
    public bool Configured => options.Value.Provider.Trim().ToLowerInvariant() switch
    {
        "smtp" => !string.IsNullOrWhiteSpace(options.Value.SmtpHost),
        "resend" => ConfigurationKeyPresent,
        _ => true, // console (development)
    };
}

public sealed class DbEmailCredentials(AnnabaDigital.Application.Settings.SettingsService settings) : IEmailCredentials
{
    public Task<string?> GetResendApiKeyAsync(CancellationToken ct = default) => settings.ReadEmailApiKeyAsync(ct);
}

public sealed class RetentionOptions
{
    public bool Enabled { get; set; } = true;
    public int IntervalHours { get; set; } = 24;
    public int StartupDelayMinutes { get; set; } = 2;
}

/// <summary>Daily housekeeping: purge evidence past retention and expired credentials. Idempotent, so concurrent instances are harmless.</summary>
public sealed class RetentionWorker(IServiceScopeFactory scopes, IOptions<RetentionOptions> options, ILogger<RetentionWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled) return;
        try { await Task.Delay(TimeSpan.FromMinutes(options.Value.StartupDelayMinutes), stoppingToken); } catch (OperationCanceledException) { return; }
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var r = await scope.ServiceProvider.GetRequiredService<AnnabaDigital.Application.Monitoring.MonitoringService>().PurgeAsync(stoppingToken);
                log.LogInformation("Retention purge done: audit={Audit} security={Security} tokens={Tokens} sessions={Sessions} buckets={Buckets}", r.Audit, r.Security, r.Tokens, r.Sessions, r.RateBuckets);
                var h = await scope.ServiceProvider.GetRequiredService<AnnabaDigital.Application.Helpdesk.RetentionService>().RunDueAsync(stoppingToken);
                if (h.Tickets + h.Files + h.Notifications > 0) log.LogInformation("Helpdesk retention done: tickets={Tickets} files={Files} notifications={Notifications} more={More}", h.Tickets, h.Files, h.Notifications, h.More);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception e) { log.LogError(e, "Retention purge failed"); }
            try { await Task.Delay(TimeSpan.FromHours(Math.Max(1, options.Value.IntervalHours)), stoppingToken); } catch (OperationCanceledException) { return; }
        }
    }
}

public sealed class ReviewSchedulerOptions
{
    public bool Enabled { get; set; } = true;
    public int IntervalMinutes { get; set; } = 60;
    public int StartupDelaySeconds { get; set; } = 90;
}

/// <summary>Opens the access reviews whose schedule has come due. Idempotent and safe with several instances (a schedule is claimed in one UPDATE).</summary>
public sealed class AccessReviewScheduler(IServiceScopeFactory scopes, IOptions<ReviewSchedulerOptions> options, ILogger<AccessReviewScheduler> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled) return;
        try { await Task.Delay(TimeSpan.FromSeconds(options.Value.StartupDelaySeconds), stoppingToken); } catch (OperationCanceledException) { return; }
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var n = await scope.ServiceProvider.GetRequiredService<AnnabaDigital.Application.Users.AccessReviewService>().RunDueAsync(stoppingToken);
                if (n > 0) log.LogInformation("Scheduled access reviews opened: {Count}", n);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception e) { log.LogError(e, "Scheduled access review run failed"); }
            try { await Task.Delay(TimeSpan.FromMinutes(Math.Max(1, options.Value.IntervalMinutes)), stoppingToken); } catch (OperationCanceledException) { return; }
        }
    }
}

public sealed class SlaWorkerOptions
{
    public bool Enabled { get; set; } = true;
    public int IntervalMinutes { get; set; } = 5;
    public int StartupDelaySeconds { get; set; } = 60;
}

/// <summary>Re-reads every running ticket's SLA clock so states and escalations stay current even when nobody touches the ticket. Idempotent; safe with several instances.</summary>
public sealed class SlaWorker(IServiceScopeFactory scopes, IOptions<SlaWorkerOptions> options, ILogger<SlaWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled) return;
        try { await Task.Delay(TimeSpan.FromSeconds(options.Value.StartupDelaySeconds), stoppingToken); } catch (OperationCanceledException) { return; }
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var n = await scope.ServiceProvider.GetRequiredService<AnnabaDigital.Application.Helpdesk.SlaService>().RunDueAsync(stoppingToken);
                if (n > 0) log.LogInformation("SLA states updated: {Count}", n);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception e) { log.LogError(e, "SLA check failed"); }
            try { await Task.Delay(TimeSpan.FromMinutes(Math.Max(1, options.Value.IntervalMinutes)), stoppingToken); } catch (OperationCanceledException) { return; }
        }
    }
}

/// <summary>Closes tickets whose resolution was neither confirmed nor reopened within the waiting period. Uses the same switches as the review scheduler (<c>ReviewScheduler__*</c>).</summary>
public sealed class TicketAutoCloseWorker(IServiceScopeFactory scopes, IOptions<ReviewSchedulerOptions> options, ILogger<TicketAutoCloseWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled) return;
        try { await Task.Delay(TimeSpan.FromSeconds(options.Value.StartupDelaySeconds + 45), stoppingToken); } catch (OperationCanceledException) { return; }
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var n = await scope.ServiceProvider.GetRequiredService<AnnabaDigital.Application.Helpdesk.TicketService>().AutoCloseDueAsync(stoppingToken);
                if (n > 0) log.LogInformation("Tickets closed automatically: {Count}", n);
                var purged = await scope.ServiceProvider.GetRequiredService<AnnabaDigital.Application.Helpdesk.AttachmentService>().PurgeOrphansAsync(stoppingToken);
                if (purged > 0) log.LogInformation("Unclaimed uploads removed: {Count}", purged);
                var warranties = await scope.ServiceProvider.GetRequiredService<AnnabaDigital.Application.Helpdesk.AssetService>().RunWarrantyRemindersAsync(stoppingToken);
                if (warranties > 0) log.LogInformation("Asset warranty reminders sent: {Count}", warranties);
                var contracts = await scope.ServiceProvider.GetRequiredService<AnnabaDigital.Application.Helpdesk.VendorService>().RunContractRemindersAsync(stoppingToken);
                if (contracts > 0) log.LogInformation("Vendor contract reminders sent: {Count}", contracts);
                var visits = await scope.ServiceProvider.GetRequiredService<AnnabaDigital.Application.Helpdesk.MaintenanceService>().RunDueAsync(stoppingToken);
                if (visits > 0) log.LogInformation("Planned maintenance visits made: {Count}", visits);
                var budgets = await scope.ServiceProvider.GetRequiredService<AnnabaDigital.Application.Helpdesk.CostService>().RunBudgetAlertsAsync(stoppingToken);
                if (budgets > 0) log.LogInformation("Budget alerts sent: {Count}", budgets);
                var papers = await scope.ServiceProvider.GetRequiredService<AnnabaDigital.Application.Helpdesk.VehicleService>().RunExpiryRemindersAsync(stoppingToken);
                if (papers > 0) log.LogInformation("Vehicle document and licence reminders sent: {Count}", papers);
                var guests = await scope.ServiceProvider.GetRequiredService<AnnabaDigital.Application.Helpdesk.VisitService>().RunAsync(stoppingToken);
                if (guests.NoShows + guests.Anonymised > 0) log.LogInformation("Visitors: {NoShows} no-shows, {Anonymised} anonymised", guests.NoShows, guests.Anonymised);
                var post = await scope.ServiceProvider.GetRequiredService<AnnabaDigital.Application.Helpdesk.MailService>().RunAsync(stoppingToken);
                if (post.Reminded + post.Anonymised > 0) log.LogInformation("Mail: {Reminded} reminders, {Anonymised} anonymised", post.Reminded, post.Anonymised);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception e) { log.LogError(e, "Ticket auto-close run failed"); }
            try { await Task.Delay(TimeSpan.FromMinutes(Math.Max(1, options.Value.IntervalMinutes)), stoppingToken); } catch (OperationCanceledException) { return; }
        }
    }
}

/// <summary>R3-2. Booking reminders, rooms released when nobody checked in, and approvals that came too late, every minute (those are minutes-sensitive, so
/// this does not wait for the hourly scheduler). Uses the review scheduler's switches (<c>ReviewScheduler__*</c>); every item is claimed in one UPDATE.</summary>
public sealed class BookingWorker(IServiceScopeFactory scopes, IOptions<ReviewSchedulerOptions> options, ILogger<BookingWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled) return;
        try { await Task.Delay(TimeSpan.FromSeconds(options.Value.StartupDelaySeconds + 60), stoppingToken); } catch (OperationCanceledException) { return; }
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var r = await scope.ServiceProvider.GetRequiredService<AnnabaDigital.Application.Helpdesk.BookingService>().RunAsync(stoppingToken);
                if (r.Reminded + r.Released + r.Expired > 0) log.LogInformation("Bookings: {Reminded} reminded, {Released} released (no check-in), {Expired} not approved in time", r.Reminded, r.Released, r.Expired);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception e) { log.LogError(e, "Booking run failed"); }
            try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); } catch (OperationCanceledException) { return; }
        }
    }
}

/// <summary>RPT-007: sends the scheduled reports that are due, every five minutes (same switches as the review scheduler).</summary>
public sealed class ReportScheduleWorker(IServiceScopeFactory scopes, IOptions<ReviewSchedulerOptions> options, ILogger<ReportScheduleWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled) return;
        try { await Task.Delay(TimeSpan.FromSeconds(options.Value.StartupDelaySeconds + 90), stoppingToken); } catch (OperationCanceledException) { return; }
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var r = await scope.ServiceProvider.GetRequiredService<AnnabaDigital.Application.Helpdesk.ReportExportService>().RunDueAsync(stoppingToken);
                if (r.Schedules > 0) log.LogInformation("Scheduled reports: {Schedules} run, {Sent} sent, {Skipped} skipped", r.Schedules, r.Sent, r.Skipped);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception e) { log.LogError(e, "Scheduled report run failed"); }
            try { await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken); } catch (OperationCanceledException) { return; }
        }
    }
}

/// <summary>Closes the accounts whose approved closure request has finished its waiting period. Uses the same switches as the review scheduler (<c>ReviewScheduler__*</c>).</summary>
public sealed class AccountClosureWorker(IServiceScopeFactory scopes, IOptions<ReviewSchedulerOptions> options, ILogger<AccountClosureWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled) return;
        try { await Task.Delay(TimeSpan.FromSeconds(options.Value.StartupDelaySeconds + 30), stoppingToken); } catch (OperationCanceledException) { return; }
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var n = await scope.ServiceProvider.GetRequiredService<AnnabaDigital.Application.Users.AccountClosureService>().RunDueAsync(stoppingToken);
                if (n > 0) log.LogInformation("Accounts closed: {Count}", n);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception e) { log.LogError(e, "Account closure run failed"); }
            try { await Task.Delay(TimeSpan.FromMinutes(Math.Max(1, options.Value.IntervalMinutes)), stoppingToken); } catch (OperationCanceledException) { return; }
        }
    }
}
