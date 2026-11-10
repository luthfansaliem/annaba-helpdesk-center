using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using AnnabaDigital.Application.Helpdesk;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Tests.Infra;
using Xunit;

namespace AnnabaDigital.Tests;

/// <summary>
/// R2 slice 2 (AST-002/003/005): what a scanned label shows to anyone in the organisation, reporting a problem about an asset, the "fails often" hint, label sheets,
/// and warranty reminders.
/// </summary>
[Collection("db")]
public sealed class AssetScanTests(PostgresFixture fx)
{
    private const string StrongPw = "Cobalt-Meadow-Harvest-58";
    private static string Uniq() => Guid.NewGuid().ToString("N")[..8];
    private const HttpStatusCode Invalid = (HttpStatusCode)422;
    private static async Task<JsonElement> Ok(HttpResponseMessage r) { Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync()); return await r.Body(); }
    private static Guid IdOf(JsonElement e) => e.GetProperty("id").GetGuid();
    private static string Str(JsonElement e, string p) => e.GetProperty(p).GetString()!;

    private sealed record World(ApiFactory F, TestClock Clock, Seed Seed, TestBrowser Manager, User ManagerUser, TestBrowser Agent, TestBrowser Requester, Guid TypeId, Guid ServiceId);

    private async Task<World> Build(bool isolated = false)
    {
        ApiFactory f; TestClock clock;
        if (isolated) { var iso = await fx.NewIsolatedAsync(); f = iso.Factory; clock = iso.Clock; } else { f = fx.Factory; clock = fx.Clock; }
        var seed = new Seed(f, clock);
        async Task<(User, TestBrowser)> Person(string role) { var u = await seed.UserAsync(role: role); var b = TestBrowser.Create(f); await b.LoginAsync(u.Email, Seed.Password); return (u, b); }
        var (mu, mgr) = await Person("ga_manager"); var (au, agent) = await Person("ga_agent"); var (_, req) = await Person("user"); var (_, admin) = await Person("super_admin");
        var team = IdOf(await Ok(await admin.Post("/api/teams", new { name = $"Team {Uniq()}" })));
        await Ok(await admin.SendAsync(HttpMethod.Put, $"/api/teams/{team}/members", new { members = new[] { new { userId = au.Id, isLead = false } } }));
        var cat = IdOf(await Ok(await admin.Post("/api/catalog/categories", new { name = $"Cat {Uniq()}" })));
        var svc = IdOf(await Ok(await admin.Post("/api/catalog/services", new { name = $"Svc {Uniq()}", categoryId = cat, owningTeamId = team })));
        await Ok(await admin.Post($"/api/catalog/services/{svc}/publish"));
        var type = IdOf(await Ok(await mgr.Post("/api/asset-types", new { name = $"Printer {Uniq()}" })));
        return new World(f, clock, seed, mgr, mu, agent, req, type, svc);
    }

    private static async Task<JsonElement> NewAsset(World w, string name, object? extra = null)
    {
        var body = new Dictionary<string, object?> { ["name"] = name, ["typeId"] = w.TypeId, ["status"] = "in_use" };
        if (extra is not null) foreach (var p in extra.GetType().GetProperties()) body[p.Name] = p.GetValue(extra);
        return await Ok(await w.Manager.Post("/api/assets", body));
    }
    private static async Task<Guid> Report(World w, Guid? asset, string subject = "Paper jam")
        => IdOf(await Ok(await w.Requester.Post($"/api/portal/services/{w.ServiceId}/requests", new { subject, answers = new { }, assetId = asset })));

    [Fact]
    public async Task Anyone_signed_in_can_look_up_a_label_and_report_a_problem_about_the_asset_but_not_about_a_retired_one()
    {
        var w = await Build();
        var site = await Ok(await w.Manager.Post("/api/locations", new { type = "site", code = "SC-" + Uniq(), name = "Scan site" }));
        var a = await NewAsset(w, "Lobby printer", new { tag = $"PRN-{Uniq()}", locationId = IdOf(site) });
        var card = await Ok(await w.Requester.Get($"/api/assets/card?tag={Str(a, "tag").ToLowerInvariant()}"));       // the tag as typed or scanned, any case
        Assert.Equal("Lobby printer", Str(card, "name")); Assert.Equal("Scan site", Str(card, "locationPath")); Assert.False(card.GetProperty("canOpen").GetBoolean()); Assert.False(card.GetProperty("retired").GetBoolean());
        Assert.True((await Ok(await w.Agent.Get($"/api/assets/card?id={IdOf(a)}"))).GetProperty("canOpen").GetBoolean());
        Assert.Equal(HttpStatusCode.NotFound, (await w.Requester.Get("/api/assets/card?tag=NO-SUCH-TAG")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await TestBrowser.Create(w.F).Get($"/api/assets/card?tag={Str(a, "tag")}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Requester.Get($"/api/assets/{IdOf(a)}")).StatusCode);   // the card, not the register

        var t = await Report(w, IdOf(a));
        var ticket = await Ok(await w.Requester.Get($"/api/tickets/{t}"));
        Assert.Equal(Str(a, "tag"), Str(ticket.GetProperty("asset"), "tag"));
        Assert.Equal(1, (await Ok(await w.Agent.Get($"/api/assets/{IdOf(a)}"))).GetProperty("ticketCount").GetInt32());

        await Ok(await w.Manager.Post($"/api/assets/{IdOf(a)}/retire", new { reason = "Broken beyond repair" }));
        Assert.True((await Ok(await w.Requester.Get($"/api/assets/card?id={IdOf(a)}"))).GetProperty("retired").GetBoolean());
        var refused = await w.Requester.Post($"/api/portal/services/{w.ServiceId}/requests", new { subject = "Still broken", answers = new { }, assetId = IdOf(a) });
        Assert.Equal(Invalid, refused.StatusCode); Assert.True((await refused.Body()).GetProperty("error").GetProperty("fields").TryGetProperty("assetId", out _));
    }

    [Fact]
    public async Task Another_organisation_cannot_look_up_or_report_on_an_asset()
    {
        var w = await Build();
        var a = await NewAsset(w, "Our printer");
        var (_, other) = await OtherOrgAdmin(w);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Get($"/api/assets/card?tag={Str(a, "tag")}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Get($"/api/assets/card?id={IdOf(a)}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Get($"/api/assets/{IdOf(a)}")).StatusCode);
        Assert.DoesNotContain((await Ok(await other.Get("/api/assets/labels?ids=" + IdOf(a)))).EnumerateArray(), x => IdOf(x) == IdOf(a));
    }

    private async Task<(Guid, TestBrowser)> OtherOrgAdmin(World w)
    {
        var pu = await w.Seed.UserAsync(role: "super_admin"); var p = TestBrowser.Create(w.F); await p.LoginAsync(pu.Email, Seed.Password);
        await p.Post("/api/auth/reauth", new { password = Seed.Password });
        var slug = "s" + Uniq(); var email = $"{slug}.admin@test.local";
        var res = await p.Post("/api/tenants", new { name = $"Org {slug}", slug, adminEmail = email }); Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var token = InMemoryEmailSender.TokenIn(await w.Seed.Mail.WaitFor(email, "invited"));
        Assert.Equal(HttpStatusCode.OK, (await TestBrowser.Create(w.F).Post("/api/invitations/accept", new { token, name = "Org Admin", password = StrongPw }, csrf: false)).StatusCode);
        var b = TestBrowser.Create(w.F); await b.LoginAsync(email, StrongPw);
        return ((await res.Body()).GetProperty("id").GetGuid(), b);
    }

    [Fact]
    public async Task An_asset_with_three_requests_in_a_year_is_flagged_and_more_strongly_once_its_warranty_has_ended()
    {
        var w = await Build();
        var a = IdOf(await NewAsset(w, "Jammy printer", new { warrantyEnd = DateOnly.FromDateTime(w.Clock.UtcNow.UtcDateTime).AddDays(100).ToString("yyyy-MM-dd") }));
        var old = await Report(w, a, "Long ago");
        await w.Seed.Db(async db => await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE tickets SET created_at = {w.Clock.UtcNow.AddDays(-400)} WHERE id = {old}"));
        await Report(w, a); await Report(w, a);
        var d = await Ok(await w.Manager.Get($"/api/assets/{a}"));
        Assert.Equal(2, d.GetProperty("requestsLastYear").GetInt32()); Assert.Equal(JsonValueKind.Null, d.GetProperty("repairHint").ValueKind);   // the old one does not count
        await Report(w, a);
        d = await Ok(await w.Manager.Get($"/api/assets/{a}"));
        Assert.Equal(3, d.GetProperty("requestsLastYear").GetInt32()); Assert.Equal("frequent", Str(d, "repairHint"));
        Assert.Contains((await Ok(await w.Manager.Get($"/api/assets?typeId={w.TypeId}&frequent=true"))).GetProperty("data").EnumerateArray(), x => IdOf(x) == a);

        var cur = d;
        await Ok(await w.Manager.SendAsync(HttpMethod.Put, $"/api/assets/{a}", new { tag = Str(cur, "tag"), name = "Jammy printer", typeId = w.TypeId, status = "in_use", warrantyEnd = DateOnly.FromDateTime(w.Clock.UtcNow.UtcDateTime).AddDays(-1).ToString("yyyy-MM-dd"), fields = new { }, version = cur.GetProperty("version").GetInt32() }));
        Assert.Equal("frequent_out_of_warranty", Str(await Ok(await w.Manager.Get($"/api/assets/{a}")), "repairHint"));
        var quiet = IdOf(await NewAsset(w, "Quiet printer"));
        Assert.DoesNotContain((await Ok(await w.Manager.Get($"/api/assets?typeId={w.TypeId}&frequent=true"))).GetProperty("data").EnumerateArray(), x => IdOf(x) == quiet);
    }

    [Fact]
    public async Task Label_sheets_take_chosen_assets_or_a_filter_and_need_the_register()
    {
        var w = await Build();
        var b = await NewAsset(w, "Printer B", new { tag = $"LB-B-{Uniq()}" }); var a = await NewAsset(w, "Printer A", new { tag = $"LB-A-{Uniq()}" });
        var chosen = await Ok(await w.Agent.Get($"/api/assets/labels?ids={IdOf(b)},{IdOf(a)},not-a-guid"));
        Assert.Equal([Str(a, "tag"), Str(b, "tag")], chosen.EnumerateArray().Select(x => Str(x, "tag")).ToArray());        // in tag order
        var byType = await Ok(await w.Agent.Get($"/api/assets/labels?typeId={w.TypeId}"));
        Assert.Equal(2, byType.GetArrayLength());
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Requester.Get($"/api/assets/labels?ids={IdOf(a)}")).StatusCode);
    }

    [Fact]
    public async Task Warranty_reminders_go_to_asset_managers_once_per_date_and_many_at_once_become_one_summary()
    {
        var w = await Build(isolated: true);
        var today = DateOnly.FromDateTime(w.Clock.UtcNow.UtcDateTime);
        string D(int days) => today.AddDays(days).ToString("yyyy-MM-dd");
        var soon = await NewAsset(w, "Soon printer", new { warrantyEnd = D(10) });
        var ended = await NewAsset(w, "Ended printer", new { warrantyEnd = D(-1) });
        await NewAsset(w, "Far printer", new { warrantyEnd = D(100) });
        await NewAsset(w, "Ancient printer", new { warrantyEnd = D(-200) });          // ended long ago: noted, not announced
        await NewAsset(w, "No warranty printer");
        async Task<int> Run() { using var s = w.F.Services.CreateScope(); return await s.ServiceProvider.GetRequiredService<AssetService>().RunWarrantyRemindersAsync(); }
        async Task<List<string>> Titles() => await w.Seed.Db(db => db.Notifications.Where(n => n.UserId == w.ManagerUser.Id && n.Kind == "asset.warranty").OrderBy(n => n.CreatedAt).Select(n => n.Title).ToListAsync());

        Assert.Equal(2, await Run());
        var titles = await Titles();
        Assert.Equal(2, titles.Count);
        Assert.Contains($"Warranty of {Str(soon, "tag")} ends on {D(10)}", titles); Assert.Contains($"Warranty of {Str(ended, "tag")} has ended", titles);
        await w.Seed.Mail.WaitFor(w.ManagerUser.Email, $"Warranty of {Str(soon, "tag")}");                      // also by e-mail, as usual for this kind
        Assert.Equal(0, await Run());                                                                         // once per date

        // a new warranty date starts over
        var cur = await Ok(await w.Manager.Get($"/api/assets/{IdOf(soon)}"));
        await Ok(await w.Manager.SendAsync(HttpMethod.Put, $"/api/assets/{IdOf(soon)}", new { tag = Str(cur, "tag"), name = "Soon printer", typeId = w.TypeId, status = "in_use", warrantyEnd = D(20), fields = new { }, version = cur.GetProperty("version").GetInt32() }));
        Assert.Equal(1, await Run());
        Assert.Contains($"Warranty of {Str(soon, "tag")} ends on {D(20)}", await Titles());

        // many at once: one summary instead of a flood
        for (var i = 0; i < 6; i++) await NewAsset(w, $"Batch printer {i}", new { warrantyEnd = D(5) });
        Assert.Equal(6, await Run());
        Assert.Equal("6 asset warranties end soon or have ended", (await Titles()).Last());
        Assert.Equal(4, (await Titles()).Count);
    }
}
