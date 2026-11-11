using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Helpdesk;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Tests.Infra;
using Xunit;

namespace AnnabaDigital.Tests;

/// <summary>A stand-in for WhatsApp, Slack and Teams: records what would have been sent, can be told to fail, and knows one Slack member.</summary>
public sealed class FakeChannels
{
    public ConcurrentQueue<ChannelSend> Sent { get; } = new();
    public ConcurrentDictionary<string, bool> Failing { get; } = new();
    public Dictionary<string, string> SlackMembers { get; } = [];
    public IChannelSender Sender(string kind) => new One(this, kind);
    private sealed class One(FakeChannels owner, string kind) : IChannelSender
    {
        public string Kind => kind;
        public Task SendAsync(ChannelSend m, CancellationToken ct = default)
        {
            if (owner.Failing.TryGetValue(kind, out var permanent)) throw new ChannelSendException("HTTP 503", permanent);
            owner.Sent.Enqueue(m); return Task.CompletedTask;
        }
        public Task<string?> ResolveAsync(ChannelSend settings, string email, CancellationToken ct = default)
            => Task.FromResult(kind == ChannelRules.Slack && settings.Settings.ContainsKey("botToken") && owner.SlackMembers.TryGetValue(email, out var id) ? id : null);
    }
}

/// <summary>Notification channels (R3-10: NTF-003): the organisation turns channels on with encrypted credentials; people opt in (WhatsApp confirmed with a code,
/// Slack found by e-mail, Teams with a Workflows URL); notifications that would be e-mailed go to the channels too, or instead, with retries and the e-mail as fallback.</summary>
[Collection("db")]
public sealed class ChannelTests(PostgresFixture fx)
{
    private const HttpStatusCode Invalid = (HttpStatusCode)422;
    private static async Task<JsonElement> Ok(HttpResponseMessage r) { Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync()); return r.StatusCode == HttpStatusCode.NoContent ? default : await r.Body(); }
    private static async Task<JsonElement> Fields(HttpResponseMessage r) { Assert.Equal(Invalid, r.StatusCode); return (await r.Body()).GetProperty("error").GetProperty("fields"); }

    [Fact]
    public async Task People_opt_in_per_channel_and_notifications_follow_with_retries_and_an_email_fallback()
    {
        var iso = await fx.NewIsolatedAsync(); var clock = iso.Clock; var f = iso.Factory; var fake = f.Services.GetRequiredService<FakeChannels>();
        var seed = new Seed(f, clock);
        async Task<(User, TestBrowser)> Person(string role) { var u = await seed.UserAsync(role: role); var b = TestBrowser.Create(f); await b.LoginAsync(u.Email, Seed.Password); return (u, b); }
        var (adm, admin) = await Person("super_admin"); var (_, ga) = await Person("ga_manager"); var (pu, person) = await Person("user");
        var mails = f.Services.GetRequiredService<InMemoryEmailSender>();
        async Task<ChannelRunResult> Run() { using var scope = f.Services.CreateScope(); return await scope.ServiceProvider.GetRequiredService<ChannelService>().DispatchDueAsync(); }

        // the organisation's set-up: integrations.manage; a channel needs its settings to be turned on; credentials are never shown again
        Assert.Equal(HttpStatusCode.Forbidden, (await ga.Get("/api/channels/settings")).StatusCode);
        var miss = await Fields(await admin.SendAsync(HttpMethod.Put, "/api/channels/settings/whatsapp", new { enabled = true }));
        Assert.True(miss.TryGetProperty("secrets.accessToken", out _)); Assert.True(miss.TryGetProperty("settings.phoneNumberId", out _));
        var wa = await Ok(await admin.SendAsync(HttpMethod.Put, "/api/channels/settings/whatsapp", new { enabled = true,
            settings = new { phoneNumberId = "1099", notifyTemplate = "helpdesk_update", verifyTemplate = "helpdesk_code" }, secrets = new { accessToken = "EAAG-secret-token" } }));
        Assert.True(wa.GetProperty("enabled").GetBoolean()); Assert.Equal("accessToken", wa.GetProperty("secretsSet")[0].GetString());
        Assert.DoesNotContain("EAAG-secret-token", (await (await admin.Get("/api/channels/settings")).Content.ReadAsStringAsync()));
        var stored = await seed.Db(db => db.ChannelSettings.IgnoreQueryFilters().FirstAsync(s => s.Kind == "whatsapp"));
        Assert.DoesNotContain("EAAG", System.Text.Encoding.UTF8.GetString(stored.SecretCiphertext!));
        await Ok(await admin.SendAsync(HttpMethod.Put, "/api/channels/settings/slack", new { enabled = true, secrets = new { botToken = "xoxb-1" } }));
        await Ok(await admin.SendAsync(HttpMethod.Put, "/api/channels/settings/teams", new { enabled = true }));
        Assert.Equal(3, (await Ok(await person.Get("/api/me/channels"))).GetProperty("available").GetArrayLength());

        // WhatsApp: a number in local form, a code sent there, a wrong code refused, the right one confirms
        Assert.True((await Fields(await person.Post("/api/me/channels/whatsapp", new { address = "12" }))).TryGetProperty("address", out _));
        var c1 = await Ok(await person.Post("/api/me/channels/whatsapp", new { address = "0812-3456-7890" }));
        Assert.Equal("+62 •••• 7890", c1.GetProperty("mine")[0].GetProperty("hint").GetString());
        Assert.True(c1.GetProperty("mine")[0].GetProperty("codePending").GetBoolean());
        await Run();
        var code = fake.Sent.Single(m => m.Purpose == "verify");
        Assert.Equal("+6281234567890", code.Address); Assert.Equal("helpdesk_code", code.Settings["verifyTemplate"]); Assert.Equal("EAAG-secret-token", code.Settings["accessToken"]);
        Assert.True((await Fields(await person.Post("/api/me/channels/whatsapp/verify", new { code = code.Title == "000000" ? "111111" : "000000" }))).TryGetProperty("code", out _));
        Assert.True((await Ok(await person.Post("/api/me/channels/whatsapp/verify", new { code = code.Title }))).GetProperty("mine")[0].GetProperty("verified").GetBoolean());

        // Slack: found by the person's e-mail (or not); Teams: only a Workflows HTTPS address
        Assert.Equal(HttpStatusCode.Conflict, (await person.Post("/api/me/channels/slack", new { })).StatusCode);
        fake.SlackMembers[pu.Email] = "U0PERSON";
        await Ok(await person.Post("/api/me/channels/slack", new { }));
        Assert.True((await Fields(await person.Post("/api/me/channels/teams", new { address = "https://evil.example.com/hook" }))).TryGetProperty("address", out _));
        var teams = await Ok(await person.Post("/api/me/channels/teams", new { address = "https://prod-01.westeurope.logic.azure.com/workflows/abc/triggers/manual/paths/invoke?sig=x" }));
        Assert.Equal(3, teams.GetProperty("mine").EnumerateArray().Count(c => c.GetProperty("verified").GetBoolean()));

        // a notification that is e-mailed (incoming mail) also goes to the three channels
        var store = (await Ok(await admin.Post("/api/locations", new { type = "site", code = "HQ-" + Guid.NewGuid().ToString("N")[..4], name = "HQ" }))).GetProperty("id").GetGuid();
        await Ok(await ga.Post("/api/mail", new { direction = "incoming", kind = "parcel", personEmail = pu.Email, otherParty = "Tokopedia", locationId = store }));
        var r1 = await Run();
        Assert.Equal(3, r1.Delivered);
        var notes = fake.Sent.Where(m => m.Purpose == "notify").ToList();
        Assert.Equal(["slack", "teams", "whatsapp"], notes.Select(n => n.Kind).Order());
        Assert.All(notes, n => Assert.EndsWith("/mail", n.Url));
        Assert.Equal("U0PERSON", notes.Single(n => n.Kind == "slack").Address);
        await mails.WaitFor(pu.Email, "A parcel for you");                                    // and still by e-mail (not "instead")

        // "instead of e-mail" on WhatsApp: the e-mail is held back; WhatsApp keeps failing, retries with back-off, gives up and the e-mail goes after all
        await Ok(await person.SendAsync(HttpMethod.Put, "/api/me/channels/whatsapp", new { replaceEmail = true }));
        await Ok(await person.SendAsync(HttpMethod.Put, "/api/me/channels/slack", new { enabled = false }));
        await Ok(await person.SendAsync(HttpMethod.Delete, "/api/me/channels/teams"));
        fake.Failing["whatsapp"] = false;                                              // temporary failure
        await Ok(await ga.Post("/api/mail", new { direction = "incoming", kind = "letter", personEmail = pu.Email, otherParty = "Bank BCA", locationId = store }));
        await Task.Delay(600);
        Assert.DoesNotContain(mails.Sent, m => m.To == pu.Email && m.Subject.Contains("A letter for you"));
        Assert.Equal(1, (await Run()).Retried);
        Assert.Equal(0, (await Run()).Retried);                                        // not due yet: back-off
        for (var i = 0; i < 5; i++) { clock.Advance(TimeSpan.FromHours(1)); await Run(); }
        var row = await seed.Db(db => db.ChannelDeliveries.IgnoreQueryFilters().AsNoTracking().Where(d => d.UserId == pu.Id && d.Purpose == "notify" && d.Channel == "whatsapp" && d.ReplacesEmail).SingleAsync());
        Assert.NotNull(row.FailedAt); Assert.NotNull(row.FellBackAt); Assert.Equal(5, row.Attempts); Assert.Equal("", row.Title);   // content cleared
        await mails.WaitFor(pu.Email, "A letter for you");
        await person.LoginAsync(pu.Email, Seed.Password); await admin.LoginAsync(adm.Email, Seed.Password);   // five hours later the sessions have idled out
        Assert.Contains("HTTP 503", (await Ok(await person.Get("/api/me/channels"))).GetProperty("mine").EnumerateArray().Single(c => c.GetProperty("kind").GetString() == "whatsapp").GetProperty("lastError").GetString());

        // the delivery log (without content) and the test message
        var log = await Ok(await admin.Get("/api/channels/deliveries?kind=whatsapp"));
        Assert.Contains(log.EnumerateArray(), d => d.GetProperty("fellBack").GetBoolean());
        fake.Failing.Clear();
        await Ok(await person.Post("/api/me/channels/whatsapp/test"));
        await Run();
        Assert.Contains(fake.Sent, m => m.Purpose == "test" && m.Kind == "whatsapp");

        // the channel switched off by the organisation: nothing queued, the person sees it is not available
        await Ok(await admin.SendAsync(HttpMethod.Put, "/api/channels/settings/whatsapp", new { enabled = false }));
        Assert.Equal(0, (await Ok(await person.Get("/api/me/channels"))).GetProperty("available").EnumerateArray().Count(k => k.GetString() == "whatsapp"));
        Assert.Equal(HttpStatusCode.Conflict, (await person.Post("/api/me/channels/whatsapp/test")).StatusCode);
        Assert.True(await seed.Db(db => db.AuditEvents.IgnoreQueryFilters().AnyAsync(a => a.Action == "channel.fallback")));
    }
}
