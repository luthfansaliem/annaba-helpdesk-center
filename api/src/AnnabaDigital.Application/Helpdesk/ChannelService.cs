using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Application.Email;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;

namespace AnnabaDigital.Application.Helpdesk;

public sealed record ChannelSettingDto(string Kind, bool Enabled, IReadOnlyDictionary<string, string> Settings, IReadOnlyList<string> SecretsSet, int People, int Failed7, DateTimeOffset? UpdatedAt);
public sealed record SaveChannelSettingRequest(bool? Enabled, Dictionary<string, string?>? Settings, Dictionary<string, string?>? Secrets);
public sealed record MyChannel(string Kind, string Hint, bool Verified, bool Enabled, bool ReplaceEmail, string? LastError, bool CodePending);
public sealed record MyChannels(IReadOnlyList<string> Available, IReadOnlyList<MyChannel> Mine);
public sealed record ConnectChannelRequest(string? Address);
public sealed record VerifyChannelRequest(string? Code);
public sealed record UpdateMyChannelRequest(bool? Enabled, bool? ReplaceEmail);
public sealed record ChannelDeliveryItem(Guid Id, string Channel, string Purpose, string? NotificationKind, string? Person, int Attempts, DateTimeOffset CreatedAt, DateTimeOffset? DeliveredAt, DateTimeOffset? FailedAt, string? LastError, bool FellBack);
public sealed record ChannelRunResult(int Delivered, int Retried, int Failed, int FellBack);

/// <summary>
/// NTF-003. WhatsApp, Slack and Microsoft Teams as extra notification channels behind the notification port.
/// <list type="bullet">
/// <item>The organisation turns a channel on and supplies its credentials (<c>integrations.manage</c>); they are encrypted and never returned.</item>
/// <item>People opt in on their own notification settings: WhatsApp with a number they confirm with a code, Slack by their e-mail, Teams with a Workflows URL they made.</item>
/// <item>A channel receives what the person would get by e-mail; with "instead of e-mail" the e-mail is held back and sent only when every such channel failed.</item>
/// <item>Messages go through an outbox, retried with back-off; delivery is logged without its content.</item>
/// </list>
/// </summary>
public sealed class ChannelService(IAppDb db, IClock clock, AuditWriter audit, ISecretProtector protector, ITenantContext tenant, IEmailQueue email, IAppLinks links,
    IEnumerable<IChannelSender> senders)
{
    private static readonly ClientInfo Background = new(null, "background", null);
    private static DomainException Invalid(string field, string message) => DomainException.Validation("Please correct the highlighted fields.", new Dictionary<string, string> { [field] = message });
    private static readonly Dictionary<string, string[]> PublicKeys = new() { [ChannelRules.WhatsApp] = ["phoneNumberId", "notifyTemplate", "verifyTemplate", "language"], [ChannelRules.Slack] = [], [ChannelRules.Teams] = [] };
    private static readonly Dictionary<string, string[]> SecretKeys = new() { [ChannelRules.WhatsApp] = ["accessToken"], [ChannelRules.Slack] = ["botToken"], [ChannelRules.Teams] = [] };
    private static byte[] Aad(string what, Guid id) => Encoding.UTF8.GetBytes($"channel:{what}:{id}");
    private IChannelSender Sender(string kind) => senders.FirstOrDefault(s => s.Kind == kind) ?? throw new InvalidOperationException($"No sender for {kind}.");

    private Dictionary<string, string> SecretsOf(ChannelSetting s)
        => s.SecretCiphertext is null || s.SecretNonce is null ? []
            : JsonSerializer.Deserialize<Dictionary<string, string>>(protector.Unprotect(new ProtectedSecret(s.SecretCiphertext, s.SecretNonce, s.SecretKeyVersion), Aad("setting", s.Id))) ?? [];
    private static Dictionary<string, string> PublicOf(ChannelSetting s) => JsonSerializer.Deserialize<Dictionary<string, string>>(s.SettingsJson) ?? [];
    private string AddressOf(UserChannel c) => Encoding.UTF8.GetString(protector.Unprotect(new ProtectedSecret(c.AddressCiphertext, c.AddressNonce, c.AddressKeyVersion), Aad("address", c.Id)));
    private void SetAddress(UserChannel c, string address)
    {
        var p = protector.Protect(Encoding.UTF8.GetBytes(address), Aad("address", c.Id));
        c.AddressCiphertext = p.Ciphertext; c.AddressNonce = p.Nonce; c.AddressKeyVersion = p.KeyVersion;
    }
    private static string Kind(string? kind) => ChannelRules.Kinds.Contains((kind ?? "").Trim().ToLowerInvariant()) ? kind!.Trim().ToLowerInvariant() : throw DomainException.NotFound("Channel");

    // ================================================================ the organisation's set-up
    public async Task<IReadOnlyList<ChannelSettingDto>> SettingsAsync(CancellationToken ct = default)
    {
        var rows = await db.ChannelSettings.AsNoTracking().ToDictionaryAsync(s => s.Kind, ct);
        var people = await db.UserChannels.AsNoTracking().Where(c => c.VerifiedAt != null && c.Enabled).GroupBy(c => c.Kind).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
        var since = clock.UtcNow.AddDays(-7);
        var failed = await db.ChannelDeliveries.AsNoTracking().Where(d => d.FailedAt >= since).GroupBy(d => d.Channel).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
        return [.. ChannelRules.Kinds.Select(k => rows.TryGetValue(k, out var s)
            ? new ChannelSettingDto(k, s.Enabled, PublicOf(s), [.. SecretsOf(s).Keys.Order()], people.GetValueOrDefault(k), failed.GetValueOrDefault(k), s.UpdatedAt)
            : new ChannelSettingDto(k, false, new Dictionary<string, string>(), [], 0, failed.GetValueOrDefault(k), null))];
    }

    public async Task<ChannelSettingDto> SaveSettingAsync(string kind, SaveChannelSettingRequest req, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        kind = Kind(kind);
        var s = await db.ChannelSettings.FirstOrDefaultAsync(x => x.Kind == kind, ct);
        if (s is null) { s = new ChannelSetting { Kind = kind }; db.ChannelSettings.Add(s); }
        var pub = PublicOf(s); var sec = SecretsOf(s);
        foreach (var (k, v) in req.Settings ?? [])
        {
            if (!PublicKeys[kind].Contains(k)) throw Invalid($"settings.{k}", "Unknown setting.");
            var t = (v ?? "").Trim(); if (t.Length > 200) throw Invalid($"settings.{k}", "Use at most 200 characters.");
            if (t.Length == 0) pub.Remove(k); else pub[k] = t;
        }
        foreach (var (k, v) in req.Secrets ?? [])
        {
            if (!SecretKeys[kind].Contains(k)) throw Invalid($"secrets.{k}", "Unknown credential.");
            var t = (v ?? "").Trim(); if (t.Length > 2000) throw Invalid($"secrets.{k}", "That is too long for a token.");
            if (t.Length > 0) sec[k] = t;       // empty keeps what is stored; credentials are replaced, never shown
        }
        var enabled = req.Enabled ?? s.Enabled;
        if (enabled)
        {
            var missing = PublicKeys[kind].Where(k => k != "language" && !pub.ContainsKey(k)).Select(k => $"settings.{k}").Concat(SecretKeys[kind].Where(k => !sec.ContainsKey(k)).Select(k => $"secrets.{k}")).ToList();
            if (missing.Count > 0) throw DomainException.Validation("Fill in what the channel needs before turning it on.", missing.ToDictionary(m => m, _ => "Required to turn the channel on."));
        }
        s.Enabled = enabled; s.SettingsJson = JsonSerializer.Serialize(pub);
        if (sec.Count > 0) { var p = protector.Protect(JsonSerializer.SerializeToUtf8Bytes(sec), Aad("setting", s.Id)); s.SecretCiphertext = p.Ciphertext; s.SecretNonce = p.Nonce; s.SecretKeyVersion = p.KeyVersion; }
        s.UpdatedById = actor.UserId; s.UpdatedAt = clock.UtcNow;
        audit.Record("channel.settings", "channel", kind, Outcome.Success, client, actor.UserId, actor.Email,
            new Dictionary<string, string> { ["enabled"] = enabled ? "yes" : "no", ["credentialsChanged"] = (req.Secrets ?? []).Any(x => !string.IsNullOrWhiteSpace(x.Value)) ? "yes" : "no" });
        await db.SaveChangesAsync(ct);
        return (await SettingsAsync(ct)).First(x => x.Kind == kind);
    }

    public async Task<IReadOnlyList<ChannelDeliveryItem>> DeliveriesAsync(string? kind, CancellationToken ct = default)
    {
        var q = db.ChannelDeliveries.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(kind)) { var k = Kind(kind); q = q.Where(d => d.Channel == k); }
        var rows = await q.OrderByDescending(d => d.CreatedAt).Take(100).ToListAsync(ct);
        var ids = rows.Select(r => r.UserId).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Name, ct);
        return [.. rows.Select(d => new ChannelDeliveryItem(d.Id, d.Channel, d.Purpose, d.NotificationKind, names.GetValueOrDefault(d.UserId), d.Attempts, d.CreatedAt, d.DeliveredAt, d.FailedAt, d.LastError, d.FellBackAt != null))];
    }

    // ================================================================ a person's channels
    private async Task<HashSet<string>> AvailableAsync(CancellationToken ct) => (await db.ChannelSettings.AsNoTracking().Where(s => s.Enabled).Select(s => s.Kind).ToListAsync(ct)).ToHashSet();

    public async Task<MyChannels> MineAsync(Guid userId, CancellationToken ct = default)
    {
        var available = await AvailableAsync(ct);
        var mine = await db.UserChannels.AsNoTracking().Where(c => c.UserId == userId).OrderBy(c => c.Kind).ToListAsync(ct);
        return new MyChannels([.. ChannelRules.Kinds.Where(available.Contains)],
            [.. mine.Select(c => new MyChannel(c.Kind, c.Hint, c.VerifiedAt != null, c.Enabled, c.ReplaceEmail, c.LastError, c.VerifiedAt == null && c.CodeExpiresAt > clock.UtcNow))]);
    }

    private async Task<ChannelSetting> LiveSettingAsync(string kind, CancellationToken ct)
        => await db.ChannelSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Kind == kind && s.Enabled, ct) ?? throw DomainException.Conflict("Your organisation has not turned this channel on.");

    private async Task<UserChannel> MineOrNewAsync(Guid userId, string kind, CancellationToken ct)
    {
        var c = await db.UserChannels.FirstOrDefaultAsync(x => x.UserId == userId && x.Kind == kind, ct);
        if (c is null) { c = new UserChannel { UserId = userId, Kind = kind, CreatedAt = clock.UtcNow }; db.UserChannels.Add(c); }
        return c;
    }
    private string CodeHash(Guid channelId, string code) => protector.KeyedHash("channel-code", $"{channelId}:{code}");

    /// <summary>WhatsApp: the number to use, then a code is sent there; Slack: found by the person's e-mail; Teams: the person's Workflows URL.</summary>
    public async Task<MyChannels> ConnectAsync(string kind, ConnectChannelRequest req, SessionPrincipal p, ClientInfo client, CancellationToken ct = default)
    {
        kind = Kind(kind);
        var setting = await LiveSettingAsync(kind, ct);
        var now = clock.UtcNow;
        switch (kind)
        {
            case ChannelRules.WhatsApp:
            {
                var phone = ChannelRules.Phone(req.Address) ?? throw Invalid("address", "Give a mobile number, for example 0812 3456 7890 or +62 812 3456 7890.");
                var c = await MineOrNewAsync(p.UserId, kind, ct);
                if (c.CodeExpiresAt > now.AddMinutes(ChannelRules.CodeMinutes - 1)) throw DomainException.Conflict("A code was just sent. Wait a minute before asking for another.");
                SetAddress(c, phone); c.Hint = ChannelRules.PhoneHint(phone); c.VerifiedAt = null; c.LastError = null; c.UpdatedAt = now;
                var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("000000");
                c.CodeHash = CodeHash(c.Id, code); c.CodeExpiresAt = now.AddMinutes(ChannelRules.CodeMinutes); c.CodeAttempts = 0;
                db.ChannelDeliveries.Add(new ChannelDelivery { UserId = p.UserId, Channel = kind, Purpose = "verify", GroupId = Guid.CreateVersion7(), Title = code, NextAttemptAt = now, CreatedAt = now });
                audit.Record("channel.code_sent", "user", p.UserId.ToString(), Outcome.Success, client, p.UserId, p.Email, new Dictionary<string, string> { ["channel"] = kind });
                break;
            }
            case ChannelRules.Slack:
            {
                string? member;
                try { member = await Sender(kind).ResolveAsync(new ChannelSend(kind, "", "resolve", "", null, null, Merge(setting, SecretsOf(setting)), "en"), p.Email, ct); }
                catch (ChannelSendException e) { throw DomainException.Conflict($"Slack did not answer: {e.Message}"); }
                if (member is null) throw DomainException.Conflict($"Slack has nobody with the e-mail {p.Email} in your workspace.");
                var c = await MineOrNewAsync(p.UserId, kind, ct);
                SetAddress(c, member); c.Hint = p.Email; c.VerifiedAt = now; c.LastError = null; c.UpdatedAt = now; c.Enabled = true;
                audit.Record("channel.connect", "user", p.UserId.ToString(), Outcome.Success, client, p.UserId, p.Email, new Dictionary<string, string> { ["channel"] = kind });
                break;
            }
            default:
            {
                var url = (req.Address ?? "").Trim();
                if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps || !ChannelRules.TeamsHosts.Any(h => u.Host.EndsWith(h, StringComparison.OrdinalIgnoreCase)) || url.Length > 2000)
                    throw Invalid("address", "Paste the HTTPS address of a Teams Workflows flow (\"When a Teams webhook request is received\").");
                var c = await MineOrNewAsync(p.UserId, kind, ct);
                SetAddress(c, url); c.Hint = u.Host; c.VerifiedAt = now; c.LastError = null; c.UpdatedAt = now; c.Enabled = true;
                audit.Record("channel.connect", "user", p.UserId.ToString(), Outcome.Success, client, p.UserId, p.Email, new Dictionary<string, string> { ["channel"] = kind });
                break;
            }
        }
        await db.SaveChangesAsync(ct);
        return await MineAsync(p.UserId, ct);
    }

    public async Task<MyChannels> VerifyAsync(VerifyChannelRequest req, SessionPrincipal p, ClientInfo client, CancellationToken ct = default)
    {
        var c = await db.UserChannels.FirstOrDefaultAsync(x => x.UserId == p.UserId && x.Kind == ChannelRules.WhatsApp, ct) ?? throw DomainException.NotFound("Channel");
        if (c.VerifiedAt is not null) return await MineAsync(p.UserId, ct);
        if (c.CodeHash is null || c.CodeExpiresAt <= clock.UtcNow) throw Invalid("code", "The code has expired. Ask for a new one.");
        if (c.CodeAttempts >= ChannelRules.MaxCodeAttempts) throw Invalid("code", "Too many wrong codes. Ask for a new one.");
        var code = new string((req.Code ?? "").Where(char.IsDigit).ToArray());
        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(CodeHash(c.Id, code)), Encoding.ASCII.GetBytes(c.CodeHash)))
        {
            c.CodeAttempts++; await db.SaveChangesAsync(ct);
            throw Invalid("code", "That code is not right.");
        }
        c.VerifiedAt = clock.UtcNow; c.CodeHash = null; c.CodeExpiresAt = null; c.Enabled = true; c.UpdatedAt = clock.UtcNow;
        audit.Record("channel.connect", "user", p.UserId.ToString(), Outcome.Success, client, p.UserId, p.Email, new Dictionary<string, string> { ["channel"] = ChannelRules.WhatsApp });
        await db.SaveChangesAsync(ct);
        return await MineAsync(p.UserId, ct);
    }

    public async Task<MyChannels> UpdateAsync(string kind, UpdateMyChannelRequest req, SessionPrincipal p, CancellationToken ct = default)
    {
        kind = Kind(kind);
        var c = await db.UserChannels.FirstOrDefaultAsync(x => x.UserId == p.UserId && x.Kind == kind, ct) ?? throw DomainException.NotFound("Channel");
        if (c.VerifiedAt is null) throw DomainException.Conflict("Confirm this channel first.");
        if (req.Enabled is { } e) c.Enabled = e;
        if (req.ReplaceEmail is { } r) c.ReplaceEmail = r;
        c.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        return await MineAsync(p.UserId, ct);
    }

    public async Task<MyChannels> RemoveAsync(string kind, SessionPrincipal p, ClientInfo client, CancellationToken ct = default)
    {
        kind = Kind(kind);
        var c = await db.UserChannels.FirstOrDefaultAsync(x => x.UserId == p.UserId && x.Kind == kind, ct) ?? throw DomainException.NotFound("Channel");
        db.UserChannels.Remove(c);
        audit.Record("channel.disconnect", "user", p.UserId.ToString(), Outcome.Success, client, p.UserId, p.Email, new Dictionary<string, string> { ["channel"] = kind });
        await db.SaveChangesAsync(ct);
        return await MineAsync(p.UserId, ct);
    }

    public async Task TestAsync(string kind, SessionPrincipal p, CancellationToken ct = default)
    {
        kind = Kind(kind);
        await LiveSettingAsync(kind, ct);
        if (!await db.UserChannels.AnyAsync(x => x.UserId == p.UserId && x.Kind == kind && x.VerifiedAt != null, ct)) throw DomainException.Conflict("Connect and confirm this channel first.");
        var now = clock.UtcNow;
        db.ChannelDeliveries.Add(new ChannelDelivery { UserId = p.UserId, Channel = kind, Purpose = "test", GroupId = Guid.CreateVersion7(), Title = "Test message from the helpdesk", Body = "If you can read this, notifications will reach you here.", Link = "/notifications/settings", NextAttemptAt = now, CreatedAt = now });
        await db.SaveChangesAsync(ct);
    }

    // ================================================================ the notification port
    /// <summary>Queues the notification on each of the person's live channels (the caller saves). True when one of them replaces the e-mail.</summary>
    public async Task<bool> QueueAsync(Guid userId, string kind, string title, string? body, string? link, CancellationToken ct)
    {
        var live = await (from c in db.UserChannels.AsNoTracking()
                          join s in db.ChannelSettings.AsNoTracking() on c.Kind equals s.Kind
                          where c.UserId == userId && c.Enabled && c.VerifiedAt != null && s.Enabled
                          select new { c.Kind, c.ReplaceEmail }).ToListAsync(ct);
        if (live.Count == 0) return false;
        var now = clock.UtcNow; var group = Guid.CreateVersion7();
        foreach (var c in live)
            db.ChannelDeliveries.Add(new ChannelDelivery { UserId = userId, Channel = c.Kind, Purpose = "notify", NotificationKind = kind, GroupId = group, Title = Clip(title, 200), Body = body is null ? null : Clip(body, 1000),
                Link = link, ReplacesEmail = c.ReplaceEmail, NextAttemptAt = now, CreatedAt = now });
        return live.Any(c => c.ReplaceEmail);
    }
    private static string Clip(string s, int n) => s.Length <= n ? s : s[..(n - 1)] + "…";
    private static Dictionary<string, string> Merge(ChannelSetting s, Dictionary<string, string>? secrets = null) => PublicOf(s).Concat(secrets ?? []).ToDictionary(x => x.Key, x => x.Value);

    /// <summary>Background run (every few seconds): sends due deliveries in every organisation, retries with back-off and, when every channel that replaced an
    /// e-mail has given up, sends that e-mail after all. Kept 30 days (without content) for the delivery log.</summary>
    public async Task<ChannelRunResult> DispatchDueAsync(CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var orgs = await db.ChannelDeliveries.IgnoreQueryFilters().AsNoTracking().Where(d => d.DeliveredAt == null && d.FailedAt == null && d.NextAttemptAt <= now).Select(d => d.TenantId).Distinct().ToListAsync(ct);
        int delivered = 0, retried = 0, failed = 0, fellBack = 0;
        foreach (var org in orgs)
        {
            using (tenant.Switch(org))
            {
                db.ResetTracking();
                var due = await db.ChannelDeliveries.AsNoTracking().Where(d => d.DeliveredAt == null && d.FailedAt == null && d.NextAttemptAt <= now).OrderBy(d => d.NextAttemptAt).Take(50).ToListAsync(ct);
                var settings = await db.ChannelSettings.AsNoTracking().ToDictionaryAsync(s => s.Kind, ct);
                foreach (var d in due)
                {
                    // claim: bump the attempt and push the next try out, so two instances never send the same message twice
                    var lease = now.AddMinutes(2);
                    if (await db.ChannelDeliveries.Where(x => x.Id == d.Id && x.Attempts == d.Attempts && x.DeliveredAt == null && x.FailedAt == null)
                            .ExecuteUpdateAsync(u => u.SetProperty(x => x.Attempts, x => x.Attempts + 1).SetProperty(x => x.NextAttemptAt, lease), ct) == 0) continue;
                    var attempt = d.Attempts + 1;
                    string? error = null; var permanent = false;
                    var channel = await db.UserChannels.AsNoTracking().FirstOrDefaultAsync(c => c.UserId == d.UserId && c.Kind == d.Channel, ct);
                    var lang = await db.Profiles.AsNoTracking().Where(x => x.UserId == d.UserId).Select(x => x.Language).FirstOrDefaultAsync(ct) ?? "en";
                    if (!settings.TryGetValue(d.Channel, out var setting) || !setting.Enabled) { error = "The channel is switched off."; permanent = true; }
                    else if (channel is null || (d.Purpose != "verify" && (channel.VerifiedAt is null || !channel.Enabled))) { error = "The person is no longer connected."; permanent = true; }
                    else
                    {
                        try
                        {
                            var url = d.Link is null ? null : links.Origin.TrimEnd('/') + d.Link;
                            await Sender(d.Channel).SendAsync(new ChannelSend(d.Channel, AddressOf(channel), d.Purpose, d.Title, d.Body, url, Merge(setting, SecretsOf(setting)), lang), ct);
                        }
                        catch (ChannelSendException e) { error = e.Message; permanent = e.Permanent; }
                        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                        catch (Exception e) { error = e.GetType().Name; }
                    }
                    if (error is null)
                    {
                        await db.ChannelDeliveries.Where(x => x.Id == d.Id).ExecuteUpdateAsync(u => u.SetProperty(x => x.DeliveredAt, clock.UtcNow).SetProperty(x => x.LastError, (string?)null)
                            .SetProperty(x => x.Title, "").SetProperty(x => x.Body, (string?)null), ct);
                        delivered++;
                        continue;
                    }
                    if (permanent || attempt >= ChannelRules.MaxAttempts)
                    {
                        await db.ChannelDeliveries.Where(x => x.Id == d.Id).ExecuteUpdateAsync(u => u.SetProperty(x => x.FailedAt, clock.UtcNow).SetProperty(x => x.LastError, Clip(error, 300)), ct);
                        failed++;
                        if (channel is not null) await db.UserChannels.Where(c => c.Id == channel.Id).ExecuteUpdateAsync(u => u.SetProperty(c => c.LastError, Clip(error, 300)), ct);
                        if (d.ReplacesEmail && await FallBackAsync(d, ct)) fellBack++;
                        await db.ChannelDeliveries.Where(x => x.Id == d.Id).ExecuteUpdateAsync(u => u.SetProperty(x => x.Title, "").SetProperty(x => x.Body, (string?)null), ct);
                    }
                    else
                    {
                        await db.ChannelDeliveries.Where(x => x.Id == d.Id).ExecuteUpdateAsync(u => u.SetProperty(x => x.NextAttemptAt, clock.UtcNow + ChannelRules.Backoff(attempt)).SetProperty(x => x.LastError, Clip(error, 300)), ct);
                        retried++;
                    }
                }
                var cutoff = now.AddDays(-ChannelRules.KeepDays);
                await db.ChannelDeliveries.Where(d => (d.DeliveredAt != null && d.DeliveredAt < cutoff) || (d.FailedAt != null && d.FailedAt < cutoff)).ExecuteDeleteAsync(ct);
            }
        }
        return new ChannelRunResult(delivered, retried, failed, fellBack);
    }

    /// <summary>The e-mail goes after all when no channel that replaced it delivered or is still trying (once per notification).</summary>
    private async Task<bool> FallBackAsync(ChannelDelivery d, CancellationToken ct)
    {
        if (await db.ChannelDeliveries.AnyAsync(x => x.GroupId == d.GroupId && x.ReplacesEmail && x.Id != d.Id && (x.DeliveredAt != null || x.FailedAt == null), ct)) return false;
        if (await db.ChannelDeliveries.Where(x => x.GroupId == d.GroupId && x.FellBackAt == null).ExecuteUpdateAsync(u => u.SetProperty(x => x.FellBackAt, clock.UtcNow), ct) == 0) return false;
        var u = await db.Users.AsNoTracking().Where(x => x.Id == d.UserId && x.Status == UserStatus.Active).Select(x => new { x.Email, x.Name }).FirstOrDefaultAsync(ct);
        if (u is null) return false;
        email.Enqueue(EmailTemplates.Notification(u.Email, u.Name, d.Title, d.Body, links.Origin.TrimEnd('/') + (d.Link ?? "/notifications")));
        audit.Record("channel.fallback", "user", d.UserId.ToString(), Outcome.Success, Background, null, "system (channels)", new Dictionary<string, string> { ["channel"] = d.Channel, ["kind"] = d.NotificationKind ?? "" });
        await db.SaveChangesAsync(ct);
        return true;
    }
}
