using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using AnnabaDigital.Application.Helpdesk;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Tests.Infra;
using Xunit;

namespace AnnabaDigital.Tests;

/// <summary>Mail and courier (R3-5: VIS-004/005): reception logs incoming mail (the recipient is told, reminded after two days, and who collected it is recorded),
/// outgoing mail with courier and waybill, voiding a mistake, what each person sees, the summary, and blanking details after the retention period.</summary>
[Collection("db")]
public sealed class MailTests(PostgresFixture fx)
{
    private const HttpStatusCode Invalid = (HttpStatusCode)422;
    private static async Task<JsonElement> Ok(HttpResponseMessage r) { Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync()); return await r.Body(); }
    private static Guid IdOf(JsonElement e) => e.GetProperty("id").GetGuid();
    private static string Status(JsonElement e) => e.GetProperty("status").GetString()!;
    private static async Task<JsonElement> Fields(HttpResponseMessage r) { Assert.Equal(Invalid, r.StatusCode); return (await r.Body()).GetProperty("error").GetProperty("fields"); }

    [Fact]
    public async Task Reception_logs_mail_the_recipient_is_told_and_reminded_and_details_are_blanked_later()
    {
        var iso = await fx.NewIsolatedAsync(); var f = iso.Factory; var clock = iso.Clock; var seed = new Seed(f, clock);
        async Task<(User, TestBrowser)> Person(string role) { var u = await seed.UserAsync(role: role); var b = TestBrowser.Create(f); await b.LoginAsync(u.Email, Seed.Password); return (u, b); }
        var (du, desk) = await Person("receptionist"); var (ru, req) = await Person("user"); var (_, other) = await Person("user"); var (_, mgr) = await Person("ga_manager");

        Assert.Equal(HttpStatusCode.Forbidden, (await req.Post("/api/mail", new { direction = "incoming", personEmail = ru.Email })).StatusCode);
        var f1 = await Fields(await desk.Post("/api/mail", new { direction = "sideways", kind = "box", personEmail = "nobody@nowhere.test" }));
        foreach (var k in new[] { "direction", "kind", "personEmail" }) Assert.True(f1.TryGetProperty(k, out _), k);
        Assert.True((await Fields(await desk.Post("/api/mail", new { direction = "outgoing", personEmail = ru.Email }))).TryGetProperty("otherParty", out _));

        // incoming: logged, the recipient is told at once
        var parcel = await Ok(await desk.Post("/api/mail", new { direction = "incoming", kind = "parcel", personEmail = ru.Email.ToUpperInvariant(), otherParty = "Tokopedia", carrier = "JNE", waybill = "jne123", description = "Small box" }));
        Assert.Equal("waiting", Status(parcel)); Assert.Equal("JNE123", parcel.GetProperty("waybill").GetString()); Assert.True(parcel.GetProperty("canHandle").GetBoolean());
        Assert.Equal(1, await seed.Db(db => db.Notifications.IgnoreQueryFilters().CountAsync(n => n.UserId == ru.Id && n.Kind == "mail.arrived" && n.Title == "A parcel for you is at reception")));
        // each person sees only their own; the desk sees everything and can search by waybill
        var mine = await Ok(await req.Get("/api/mail"));
        Assert.Equal(IdOf(parcel), IdOf(Assert.Single(mine.GetProperty("data").EnumerateArray())));
        Assert.False(mine.GetProperty("data")[0].GetProperty("canHandle").GetBoolean());
        Assert.Empty((await Ok(await other.Get("/api/mail"))).GetProperty("data").EnumerateArray());
        Assert.Equal(HttpStatusCode.Forbidden, (await req.Get("/api/mail?scope=desk")).StatusCode);
        Assert.Single((await Ok(await desk.Get("/api/mail?scope=desk&q=jne1"))).GetProperty("data").EnumerateArray());

        // outgoing: registered, then handed to the courier with a waybill
        var letter = await Ok(await desk.Post("/api/mail", new { direction = "outgoing", kind = "document", personEmail = ru.Email, otherParty = "PT Klien", address = "Jl. Sudirman 1, Jakarta" }));
        Assert.Equal("registered", Status(letter));
        var send = await Fields(await desk.Post($"/api/mail/{IdOf(letter)}/send", new { }));
        Assert.True(send.TryGetProperty("carrier", out _)); Assert.True(send.TryGetProperty("waybill", out _));
        Assert.Equal("sent", Status(await Ok(await desk.Post($"/api/mail/{IdOf(letter)}/send", new { carrier = "SiCepat", waybill = "sc-9" }))));
        Assert.Equal(HttpStatusCode.Conflict, (await desk.Post($"/api/mail/{IdOf(letter)}/collect", new { })).StatusCode);

        // a mistake is voided with a reason and disappears from the person's list
        var wrong = IdOf(await Ok(await desk.Post("/api/mail", new { direction = "incoming", personEmail = ru.Email, description = "Logged twice" })));
        Assert.True((await Fields(await desk.Post($"/api/mail/{wrong}/void", new { }))).TryGetProperty("note", out _));
        Assert.Equal("voided", Status(await Ok(await desk.Post($"/api/mail/{wrong}/void", new { note = "Duplicate" }))));
        Assert.Equal(2, (await Ok(await req.Get("/api/mail"))).GetProperty("total").GetInt32());

        var sum = await Ok(await desk.Get("/api/mail/summary"));
        Assert.Equal(1, sum.GetProperty("waiting").GetInt32()); Assert.Equal(1, sum.GetProperty("receivedToday").GetInt32()); Assert.Equal(1, sum.GetProperty("sentThisWeek").GetInt32());
        Assert.Equal(HttpStatusCode.Forbidden, (await req.Get("/api/mail/summary")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await mgr.Get("/api/mail/summary")).StatusCode);

        // two days later: one reminder; then collected by a colleague
        async Task<MailService.RunResult> Run() { using var s = f.Services.CreateScope(); return await s.ServiceProvider.GetRequiredService<MailService>().RunAsync(); }
        Assert.Equal(0, (await Run()).Reminded);
        clock.Advance(TimeSpan.FromDays(2).Add(TimeSpan.FromMinutes(1)));
        Assert.Equal(1, (await Run()).Reminded); Assert.Equal(0, (await Run()).Reminded);
        desk = TestBrowser.Create(f); await desk.LoginAsync(du.Email, Seed.Password);
        var sum2 = await Ok(await desk.Get("/api/mail/summary"));
        Assert.Equal(1, sum2.GetProperty("waitingOver2Days").GetInt32()); Assert.Equal(2, sum2.GetProperty("oldestWaitingDays").GetInt32());
        var got = await Ok(await desk.Post($"/api/mail/{IdOf(parcel)}/collect", new { collectedBy = "Andi (same team)" }));
        Assert.Equal("collected", Status(got)); Assert.Equal("Andi (same team)", got.GetProperty("collectedBy").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await desk.Post($"/api/mail/{IdOf(parcel)}/collect", new { })).StatusCode);

        // after the visitor retention period the names, addresses, waybills and descriptions are gone
        clock.Advance(TimeSpan.FromDays(181));
        var r = await Run(); Assert.Equal(3, r.Anonymised);
        desk = TestBrowser.Create(f); await desk.LoginAsync(du.Email, Seed.Password);
        var all = (await Ok(await desk.Get("/api/mail?scope=desk&pageSize=50"))).GetProperty("data").EnumerateArray().ToList();
        Assert.All(all, m => { Assert.True(m.GetProperty("anonymised").GetBoolean()); foreach (var k in new[] { "otherParty", "address", "waybill", "description", "collectedBy" }) Assert.Equal(JsonValueKind.Null, m.GetProperty(k).ValueKind); });
        Assert.Contains(all, m => Status(m) == "collected");
        Assert.Equal(0, (await Run()).Anonymised);
    }
}
