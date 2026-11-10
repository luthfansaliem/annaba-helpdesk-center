using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Tests.Infra;
using Xunit;

namespace AnnabaDigital.Tests;

/// <summary>
/// The asset register (AST-001/003/004/006): who may do what, asset types and their questions, tags, the lifecycle trail, filters, retiring, linking requests to an asset with
/// the queue's visibility, and CSV export and import. Each test works with its own asset type so counts are exact in the shared database.
/// </summary>
[Collection("db")]
public sealed class AssetTests(PostgresFixture fx)
{
    private readonly Seed _seed = new(fx.Factory, fx.Clock);
    private static string Uniq() => Guid.NewGuid().ToString("N")[..8];
    private const HttpStatusCode Invalid = (HttpStatusCode)422;
    private static async Task<JsonElement> Ok(HttpResponseMessage r) { Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync()); return await r.Body(); }
    private static Guid IdOf(JsonElement e) => e.GetProperty("id").GetGuid();
    private static string Str(JsonElement e, string p) => e.GetProperty(p).GetString()!;
    private static async Task<JsonElement> Fields(HttpResponseMessage r) { Assert.Equal(Invalid, r.StatusCode); return (await r.Body()).GetProperty("error").GetProperty("fields"); }

    private async Task<(User U, TestBrowser B)> Person(string role)
    {
        var u = await _seed.UserAsync(role: role); var b = TestBrowser.Create(fx.Factory); await b.LoginAsync(u.Email, Seed.Password); return (u, b);
    }

    private sealed record World(TestBrowser Manager, TestBrowser Agent, User AgentUser, TestBrowser Supervisor, TestBrowser Requester, User RequesterUser, Guid TypeId, string TypeName, Guid TeamId, Guid ServiceId);

    /// <summary>A GA Manager, an agent in a team, a supervisor and a requester; an asset type with three questions; a published service of that team.</summary>
    private async Task<World> Build()
    {
        var (_, mgr) = await Person("ga_manager"); var (au, agent) = await Person("ga_agent"); var (_, sup) = await Person("ga_supervisor"); var (ru, req) = await Person("user");
        var (_, admin) = await Person("super_admin");
        var team = IdOf(await Ok(await admin.Post("/api/teams", new { name = $"Team {Uniq()}" })));
        await Ok(await admin.SendAsync(HttpMethod.Put, $"/api/teams/{team}/members", new { members = new[] { new { userId = au.Id, isLead = false } } }));
        var cat = IdOf(await Ok(await admin.Post("/api/catalog/categories", new { name = $"Cat {Uniq()}" })));
        var svc = IdOf(await Ok(await admin.Post("/api/catalog/services", new { name = $"Svc {Uniq()}", categoryId = cat, owningTeamId = team })));
        await Ok(await admin.Post($"/api/catalog/services/{svc}/publish"));
        var name = $"Air conditioner {Uniq()}";
        var type = await Ok(await mgr.Post("/api/asset-types", new
        {
            name, description = "Split and central units",
            fields = new object[]
            {
                new { key = "btu", type = "number", label = "Capacity (BTU)", required = true, min = 1000 },
                new { key = "refrigerant", type = "select", label = "Refrigerant", options = new[] { new { value = "r32", label = "R-32" }, new { value = "r410a", label = "R-410A" } } },
                new { key = "inverter", type = "yes_no", label = "Inverter" },
            },
        }));
        return new World(mgr, agent, au, sup, req, ru, IdOf(type), name, team, svc);
    }

    private static Task<HttpResponseMessage> NewAsset(TestBrowser b, World w, string name, object? extra = null)
    {
        var body = new Dictionary<string, object?> { ["name"] = name, ["typeId"] = w.TypeId, ["status"] = "in_use", ["fields"] = new { btu = 12000 } };
        if (extra is not null) foreach (var p in extra.GetType().GetProperties()) body[p.Name] = p.GetValue(extra);
        return b.Post("/api/assets", body);
    }

    // ============================================================ who
    [Fact]
    public async Task Requesters_see_nothing_agents_and_supervisors_read_and_only_managers_change_or_import()
    {
        var w = await Build();
        var a = IdOf(await Ok(await NewAsset(w.Manager, w, "Lobby AC")));
        foreach (var url in new[] { "/api/assets", $"/api/assets/{a}", "/api/asset-types", "/api/assets/export" })
            Assert.Equal(HttpStatusCode.Forbidden, (await w.Requester.Get(url)).StatusCode);
        foreach (var b in new[] { w.Agent, w.Supervisor })
        {
            Assert.Equal(HttpStatusCode.OK, (await b.Get($"/api/assets?typeId={w.TypeId}")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await b.Get($"/api/assets/{a}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await NewAsset(b, w, "Not allowed")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await b.Post($"/api/assets/{a}/retire", new { reason = "nope" })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await b.Post("/api/asset-types", new { name = "X type" })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await b.Post("/api/assets/import", new { csv = "name,type\nA,B", dryRun = true })).StatusCode);
        }
        Assert.False((await Ok(await w.Agent.Get($"/api/assets/{a}"))).GetProperty("canManage").GetBoolean());
        Assert.True((await Ok(await w.Manager.Get($"/api/assets/{a}"))).GetProperty("canManage").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await w.Agent.Get("/api/assets/pick?q=Lobby")).StatusCode);                 // whoever edits tickets may pick an asset
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Requester.Get("/api/assets/pick?q=Lobby")).StatusCode);
    }

    // ============================================================ types
    [Fact]
    public async Task Asset_types_ask_simple_questions_have_unique_names_and_an_archived_type_takes_no_new_assets()
    {
        var w = await Build();
        Assert.Contains(await w.Manager.Get("/api/asset-types").ContinueWith(t => t.Result.Body()).Unwrap() is var list ? list.EnumerateArray().Select(t => Str(t, "name")) : [], n => n == w.TypeName);
        Assert.True((await Fields(await w.Manager.Post("/api/asset-types", new { name = w.TypeName.ToUpperInvariant() }))).TryGetProperty("name", out _));   // names are unique, ignoring case
        var withFile = await Fields(await w.Manager.Post("/api/asset-types", new { name = $"T {Uniq()}", fields = new object[] { new { key = "photo", type = "file", label = "Photo" } } }));
        Assert.True(withFile.TryGetProperty("fields[0].type", out _));
        Assert.True((await Fields(await w.Manager.Post("/api/asset-types", new { name = "x" }))).TryGetProperty("name", out _));

        var t = await Ok(await w.Manager.Post("/api/asset-types", new { name = $"Generator {Uniq()}" }));
        await Ok(await w.Manager.Post($"/api/asset-types/{IdOf(t)}/archive"));
        Assert.DoesNotContain((await Ok(await w.Manager.Get("/api/asset-types"))).EnumerateArray(), x => IdOf(x) == IdOf(t));
        Assert.Contains((await Ok(await w.Manager.Get("/api/asset-types?includeArchived=true"))).EnumerateArray(), x => IdOf(x) == IdOf(t) && x.GetProperty("archived").GetBoolean());
        Assert.True((await Fields(await w.Manager.Post("/api/assets", new { name = "Gen 1", typeId = IdOf(t), status = "in_use" }))).TryGetProperty("typeId", out _));
        await Ok(await w.Manager.Post($"/api/asset-types/{IdOf(t)}/restore"));
        Assert.Equal(HttpStatusCode.Created, (await w.Manager.Post("/api/assets", new { name = "Gen 1", typeId = IdOf(t), status = "in_use" })).StatusCode);
    }

    // ============================================================ creating and the trail
    [Fact]
    public async Task An_asset_gets_a_tag_its_answers_are_checked_and_every_change_is_on_its_trail()
    {
        var w = await Build();
        var site = IdOf(await Ok(await w.Manager.Post("/api/locations", new { type = "site", code = "AS-" + Uniq(), name = "Asset HQ" })));
        var auto = await Ok(await NewAsset(w.Manager, w, "Meeting room AC"));
        Assert.Matches("^AST-[0-9]{6}$", Str(auto, "tag"));                                                      // no tag given: the next automatic one
        var own = await Ok(await NewAsset(w.Manager, w, "Server room AC", new { tag = $"  ac-{Uniq()}  ", serial = "SN-1", locationId = site, purchaseDate = "2024-01-15", purchaseCost = 7500000.50m, warrantyEnd = "2027-01-15", vendorName = "CoolCo", fields = new { btu = 18000, refrigerant = "r32", inverter = true } }));
        Assert.StartsWith("AC-", Str(own, "tag")); Assert.Equal("Asset HQ", Str(own, "locationPath"));                // stored in capitals
        Assert.Equal(7500000.50m, own.GetProperty("purchaseCost").GetDecimal()); Assert.Equal("r32", own.GetProperty("values").GetProperty("refrigerant").GetString());
        Assert.Equal("created", Str(own.GetProperty("events")[0], "kind"));

        var dup = await Fields(await NewAsset(w.Manager, w, "Copy", new { tag = Str(own, "tag").ToLowerInvariant() })); Assert.True(dup.TryGetProperty("tag", out _));
        var bad = await Fields(await w.Manager.Post("/api/assets", new { name = "A", typeId = w.TypeId, status = "retired", purchaseDate = "2025-01-01", warrantyEnd = "2024-01-01", purchaseCost = 1.234m, fields = new { btu = 10, refrigerant = "freon" } }));
        foreach (var k in new[] { "name", "status", "warrantyEnd", "purchaseCost", "fields.btu", "fields.refrigerant" }) Assert.True(bad.TryGetProperty(k, out _), k);
        Assert.True((await Fields(await w.Manager.Post("/api/assets", new { name = "No answers", typeId = w.TypeId, status = "in_use" }))).TryGetProperty("fields.btu", out _));   // a required question
        Assert.True((await Fields(await NewAsset(w.Manager, w, "Bad tag", new { tag = "has space" }))).TryGetProperty("tag", out _));

        // move it, hand it to someone, put it in repair, and change a detail: four lines on its trail
        var id = IdOf(own); var other = IdOf(await Ok(await w.Manager.Post("/api/locations", new { type = "site", code = "AS2-" + Uniq(), name = "Warehouse" })));
        var cur = await Ok(await w.Manager.Get($"/api/assets/{id}"));
        var upd = await Ok(await w.Manager.SendAsync(HttpMethod.Put, $"/api/assets/{id}", new
        {
            tag = Str(cur, "tag"), name = "Server room AC", typeId = w.TypeId, serial = "SN-1", status = "in_repair", locationId = other, assignedToId = w.AgentUser.Id, purchaseDate = "2024-01-15", purchaseCost = 7500000.50m,
            warrantyEnd = "2027-01-15", vendorName = "CoolCo Service", fields = new { btu = 18000, refrigerant = "r32", inverter = true }, version = cur.GetProperty("version").GetInt32(),
        }));
        var kinds = upd.GetProperty("events").EnumerateArray().Select(e => Str(e, "kind")).ToList();
        foreach (var k in new[] { "moved", "assigned", "status", "updated" }) Assert.Contains(k, kinds);
        var bodies = upd.GetProperty("events").EnumerateArray().Select(e => e.GetProperty("body").GetString()).ToList();
        Assert.Contains("Moved from Asset HQ to Warehouse.", bodies); Assert.Contains("Status changed from in use to in repair.", bodies); Assert.Contains("Changed: vendor.", bodies);
        Assert.Contains(bodies, b => b!.StartsWith("Handed to "));
        var stale = await w.Manager.SendAsync(HttpMethod.Put, $"/api/assets/{id}", new { tag = Str(cur, "tag"), name = "x", typeId = w.TypeId, status = "in_use", fields = new { btu = 18000 }, version = cur.GetProperty("version").GetInt32() });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);                                                    // someone else changed it in between
        var actions = await _seed.Db(db => db.AuditEvents.Where(a => a.TargetId == id.ToString()).Select(a => a.Action).ToListAsync());
        Assert.Contains("asset.create", actions); Assert.Contains("asset.update", actions);
    }

    [Fact]
    public async Task Retiring_needs_a_reason_takes_it_out_of_the_usual_list_and_restoring_brings_it_back_as_stored()
    {
        var w = await Build();
        var id = IdOf(await Ok(await NewAsset(w.Manager, w, "Old AC")));
        Assert.True((await Fields(await w.Manager.Post($"/api/assets/{id}/retire", new { reason = "x" }))).TryGetProperty("reason", out _));
        var r = await Ok(await w.Manager.Post($"/api/assets/{id}/retire", new { reason = "Compressor dead, replaced" }));
        Assert.Equal("retired", Str(r, "status")); Assert.Equal("Compressor dead, replaced", Str(r, "retireReason"));
        Assert.Equal(HttpStatusCode.Conflict, (await w.Manager.Post($"/api/assets/{id}/retire", new { reason = "again" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await w.Manager.SendAsync(HttpMethod.Put, $"/api/assets/{id}", new { tag = Str(r, "tag"), name = "x", typeId = w.TypeId, status = "in_use", fields = new { btu = 12000 } })).StatusCode);
        Assert.Equal(0, (await Ok(await w.Manager.Get($"/api/assets?typeId={w.TypeId}"))).GetProperty("total").GetInt32());   // in service only, by default
        Assert.Equal(1, (await Ok(await w.Manager.Get($"/api/assets?typeId={w.TypeId}&status=retired"))).GetProperty("total").GetInt32());
        Assert.Equal(1, (await Ok(await w.Manager.Get($"/api/assets?typeId={w.TypeId}&status=all"))).GetProperty("total").GetInt32());
        var back = await Ok(await w.Manager.Post($"/api/assets/{id}/restore"));
        Assert.Equal("stored", Str(back, "status")); Assert.Equal(new[] { "restored", "retired", "created" }, back.GetProperty("events").EnumerateArray().Select(e => Str(e, "kind")).ToArray());
        Assert.Equal(Invalid, (await w.Manager.Get($"/api/assets?status=broken")).StatusCode);
    }

    [Fact]
    public async Task The_register_filters_by_text_type_place_and_its_children_person_and_warranty()
    {
        var w = await Build();
        var site = IdOf(await Ok(await w.Manager.Post("/api/locations", new { type = "site", code = "F-" + Uniq(), name = "Filter site" })));
        var floor = IdOf(await Ok(await w.Manager.Post("/api/locations", new { type = "floor", parentId = site, code = "FF-" + Uniq(), name = "Floor 2" })));
        var soon = DateOnly.FromDateTime(fx.Clock.UtcNow.UtcDateTime).AddDays(20).ToString("yyyy-MM-dd"); var later = DateOnly.FromDateTime(fx.Clock.UtcNow.UtcDateTime).AddDays(400).ToString("yyyy-MM-dd");
        await Ok(await NewAsset(w.Manager, w, "Pantry AC", new { serial = "PX_100%", locationId = floor, warrantyEnd = soon, assignedToId = w.AgentUser.Id }));
        await Ok(await NewAsset(w.Manager, w, "Hall AC", new { locationId = site, warrantyEnd = later }));
        await Ok(await NewAsset(w.Manager, w, "Spare AC"));
        async Task<string[]> Names(string query) => [.. (await Ok(await w.Manager.Get($"/api/assets?typeId={w.TypeId}&{query}"))).GetProperty("data").EnumerateArray().Select(a => Str(a, "name")).Order()];
        Assert.Equal(["Hall AC", "Pantry AC", "Spare AC"], await Names(""));
        Assert.Equal(["Pantry AC"], await Names("q=pantry"));
        Assert.Equal(["Pantry AC"], await Names("q=" + Uri.EscapeDataString("_100%")));                            // literal search, as in the queue
        Assert.Equal(["Hall AC", "Pantry AC"], await Names($"locationId={site}"));                               // the site includes its floors
        Assert.Equal(["Pantry AC"], await Names($"locationId={floor}"));
        Assert.Equal(["Pantry AC"], await Names($"assignedToId={w.AgentUser.Id}"));
        Assert.Equal(["Pantry AC"], await Names("warrantyWithinDays=30"));
        var row = (await Ok(await w.Manager.Get($"/api/assets?typeId={w.TypeId}&q=pantry"))).GetProperty("data")[0];
        Assert.Equal("Filter site › Floor 2", Str(row, "locationPath")); Assert.Equal(w.TypeName, Str(row, "typeName"));
    }

    // ============================================================ requests about an asset
    [Fact]
    public async Task Staff_link_a_request_to_an_asset_and_the_asset_shows_the_requests_its_reader_may_see()
    {
        var w = await Build();
        var asset = await Ok(await NewAsset(w.Manager, w, "Floor 3 AC")); var aid = IdOf(asset);
        var t = await Ok(await w.Requester.Post($"/api/portal/services/{w.ServiceId}/requests", new { subject = "AC leaking water", answers = new { } }));
        var tid = IdOf(t);
        var cur = await Ok(await w.Agent.Get($"/api/tickets/{tid}"));
        var upd = await Ok(await w.Agent.SendAsync(HttpMethod.Patch, $"/api/tickets/{tid}", new { subject = Str(cur, "subject"), priority = Str(cur, "priority"), locationId = (Guid?)null, assetId = aid, version = cur.GetProperty("version").GetInt32() }));
        Assert.Equal(Str(asset, "tag"), Str(upd.GetProperty("asset"), "tag"));
        Assert.Contains(upd.GetProperty("timeline").EnumerateArray(), e => e.GetProperty("body").GetString() is { } b && b.StartsWith("Asset set to " + Str(asset, "tag")));
        Assert.Equal(Str(asset, "tag"), Str((await Ok(await w.Requester.Get($"/api/tickets/{tid}"))).GetProperty("asset"), "tag"));   // the requester sees what it is about

        foreach (var reader in new[] { w.Agent, w.Supervisor })
        {
            var d = await Ok(await reader.Get($"/api/assets/{aid}"));
            Assert.Equal(1, d.GetProperty("ticketCount").GetInt32()); Assert.Equal("AC leaking water", Str(d.GetProperty("tickets")[0], "subject"));
        }
        var (_, outsider) = await Person("ga_agent");                                                             // an agent of no team: may read the register, not that team's ticket
        var seen = await Ok(await outsider.Get($"/api/assets/{aid}"));
        Assert.Equal(0, seen.GetProperty("ticketCount").GetInt32()); Assert.Empty(seen.GetProperty("tickets").EnumerateArray());

        // a retired asset can no longer be chosen; clearing works
        var spare = IdOf(await Ok(await NewAsset(w.Manager, w, "Retired AC"))); await Ok(await w.Manager.Post($"/api/assets/{spare}/retire", new { reason = "Gone for good" }));
        var v = (await Ok(await w.Agent.Get($"/api/tickets/{tid}"))).GetProperty("version").GetInt32();
        Assert.True((await Fields(await w.Agent.SendAsync(HttpMethod.Patch, $"/api/tickets/{tid}", new { subject = "AC leaking water", priority = Str(cur, "priority"), assetId = spare, version = v }))).TryGetProperty("assetId", out _));
        var cleared = await Ok(await w.Agent.SendAsync(HttpMethod.Patch, $"/api/tickets/{tid}", new { subject = "AC leaking water", priority = Str(cur, "priority"), assetId = (Guid?)null, version = v }));
        Assert.Equal(JsonValueKind.Null, cleared.GetProperty("asset").ValueKind);
        var picks = await Ok(await w.Agent.Get("/api/assets/pick?q=" + Uri.EscapeDataString("Retired AC")));
        Assert.Empty(picks.EnumerateArray());                                                                     // retired assets are not offered
    }

    // ============================================================ CSV
    [Fact]
    public async Task Export_writes_the_register_as_csv_with_the_types_own_questions_and_defuses_formulas()
    {
        var w = await Build();
        var site = await Ok(await w.Manager.Post("/api/locations", new { type = "site", code = "EX-" + Uniq(), name = "Export site" }));
        await Ok(await NewAsset(w.Manager, w, "=HYPERLINK(\"http://evil\")", new { locationId = IdOf(site), purchaseCost = 1250.5m, fields = new { btu = 9000, refrigerant = "r410a", inverter = false } }));
        var res = await w.Supervisor.Get($"/api/assets/export?typeId={w.TypeId}");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode); Assert.Equal("text/csv", res.Content.Headers.ContentType!.MediaType);
        var csv = (await res.Content.ReadAsStringAsync()).TrimStart('﻿');
        var lines = AssetRules.ParseCsv(csv);
        Assert.Equal(["tag", "name", "type", "serial", "status", "location_code", "assigned_to_email", "department", "purchase_date", "purchase_cost", "warranty_end", "vendor", "notes", "x_btu", "x_refrigerant", "x_inverter"], lines[0]);
        var row = lines[1];
        Assert.StartsWith("'=HYPERLINK", row[1]);                                                                // a spreadsheet would run it as a formula
        Assert.Equal(w.TypeName, row[2]); Assert.Equal("in_use", row[4]); Assert.Equal(Str(site, "code"), row[5]); Assert.Equal("1250.5", row[9]);
        Assert.Equal(["9000", "r410a", "no"], row[13..16]);
        Assert.Contains("asset.export", await _seed.Db(db => db.AuditEvents.OrderByDescending(a => a.Id).Take(5).Select(a => a.Action).ToListAsync()));
    }

    [Fact]
    public async Task Import_checks_every_line_reports_problems_without_writing_and_then_creates_and_updates_in_one_go()
    {
        var w = await Build();
        var site = await Ok(await w.Manager.Post("/api/locations", new { type = "site", code = "IM-" + Uniq(), name = "Import site" })); var code = Str(site, "code");
        var existing = await Ok(await NewAsset(w.Manager, w, "Existing AC", new { tag = $"IMP-{Uniq()}", serial = "KEEP-ME" })); var tag = Str(existing, "tag");
        var t = w.TypeName;

        var bad = $"tag,name,type,status,location_code,purchase_date,x_btu,x_refrigerant\n" +
                  $"DUP-{Uniq()},Line two,{t},in_use,{code},2024-02-30,9000,r32\n" +                     // impossible date
                  $",Line three,Nope type,in_use,,,9000,\n" +                                             // unknown type
                  $"SAME-1,Line four,{t},retired,NOPE-CODE,,9000,freon\n" +                               // retired status, unknown place, bad choice
                  $"SAME-1,Line five,{t},stored,,,,\n";                                                   // same tag twice; required question missing
        var r = await Ok(await w.Manager.Post("/api/assets/import", new { csv = bad, dryRun = false }));
        Assert.False(r.GetProperty("applied").GetBoolean()); Assert.Equal(4, r.GetProperty("rows").GetInt32());
        var problems = r.GetProperty("problems").EnumerateArray().ToDictionary(p => p.GetProperty("line").GetInt32(), p => p.GetProperty("errors"));
        Assert.True(problems[2].TryGetProperty("purchase_date", out _));
        Assert.True(problems[3].TryGetProperty("type", out _));
        foreach (var k in new[] { "status", "location_code", "x_refrigerant" }) Assert.True(problems[4].TryGetProperty(k, out _), k);
        Assert.True(problems[5].TryGetProperty("tag", out _)); Assert.True(problems[5].TryGetProperty("fields.btu", out _));
        Assert.Equal(1, (await Ok(await w.Manager.Get($"/api/assets?typeId={w.TypeId}"))).GetProperty("total").GetInt32());    // nothing was written

        var good = $"tag,name,type,status,location_code,purchase_cost,x_btu,x_refrigerant,x_inverter\r\n" +
                   $"{tag.ToLowerInvariant()},\"Existing AC, renamed\",,in_repair,{code},,24000,R-410A,yes\r\n" +   // update: matched by tag in any case; empty cells keep
                   $",New AC one,{t},,,\"1500000\",12000,r32,no\r\n" +
                   $"NEW-{Uniq()},New AC two,{t.ToUpperInvariant()},stored,,,9000,,\r\n";
        var dry = await Ok(await w.Manager.Post("/api/assets/import", new { csv = good, dryRun = true }));
        Assert.Empty(dry.GetProperty("problems").EnumerateArray()); Assert.Equal(2, dry.GetProperty("creates").GetInt32()); Assert.Equal(1, dry.GetProperty("updates").GetInt32()); Assert.False(dry.GetProperty("applied").GetBoolean());
        Assert.Equal(1, (await Ok(await w.Manager.Get($"/api/assets?typeId={w.TypeId}"))).GetProperty("total").GetInt32());    // a dry run changes nothing

        var done = await Ok(await w.Manager.Post("/api/assets/import", new { csv = good, dryRun = false }));
        Assert.True(done.GetProperty("applied").GetBoolean());
        Assert.Equal(3, (await Ok(await w.Manager.Get($"/api/assets?typeId={w.TypeId}&status=all"))).GetProperty("total").GetInt32());
        var upd = await Ok(await w.Manager.Get($"/api/assets/{IdOf(existing)}"));
        Assert.Equal("Existing AC, renamed", Str(upd, "name")); Assert.Equal("in_repair", Str(upd, "status")); Assert.Equal("KEEP-ME", Str(upd, "serial"));   // the empty serial cell kept the value
        Assert.Equal(24000, upd.GetProperty("values").GetProperty("btu").GetInt32()); Assert.Equal("r410a", upd.GetProperty("values").GetProperty("refrigerant").GetString()); Assert.True(upd.GetProperty("values").GetProperty("inverter").GetBoolean());
        Assert.Equal("Updated from a CSV file.", upd.GetProperty("events")[0].GetProperty("body").GetString());
        var created = (await Ok(await w.Manager.Get($"/api/assets?typeId={w.TypeId}&q=New AC one"))).GetProperty("data")[0];
        Assert.Matches("^AST-[0-9]{6}$", Str(created, "tag"));
        Assert.Single(await _seed.Db(db => db.AuditEvents.Where(a => a.Action == "asset.import" && a.ActorId != null).OrderByDescending(a => a.Id).Take(1).ToListAsync()));

        Assert.Equal(Invalid, (await w.Manager.Post("/api/assets/import", new { csv = "name,type,colour\nA,B,red", dryRun = true })).StatusCode);   // unknown column
        Assert.Equal(Invalid, (await w.Manager.Post("/api/assets/import", new { csv = "tag,type\nA,B", dryRun = true })).StatusCode);              // name column missing
        Assert.Equal(Invalid, (await w.Manager.Post("/api/assets/import", new { csv = "name,type\n\"open quote,B", dryRun = true })).StatusCode); // unreadable
    }

    [Fact]
    public void Csv_reading_follows_rfc_4180()
    {
        var rows = AssetRules.ParseCsv("﻿a,b,c\r\n\"x, y\",\"he said \"\"hi\"\"\",\"two\nlines\"\n\n1,,3");
        Assert.Equal(3, rows.Count);
        Assert.Equal(["a", "b", "c"], rows[0]); Assert.Equal(["x, y", "he said \"hi\"", "two\nlines"], rows[1]); Assert.Equal(["1", "", "3"], rows[2]);
        Assert.Throws<FormatException>(() => AssetRules.ParseCsv("a,\"b"));
    }
}
