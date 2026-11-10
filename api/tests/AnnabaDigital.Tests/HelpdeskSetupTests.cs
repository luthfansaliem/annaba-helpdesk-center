using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Tests.Infra;
using Xunit;

namespace AnnabaDigital.Tests;

/// <summary>Locations, business calendars and teams: structure rules, permissions, tenant isolation and audit.</summary>
[Collection("db")]
public sealed class HelpdeskSetupTests(PostgresFixture fx)
{
    private readonly Seed _seed = new(fx.Factory, fx.Clock);
    private const string StrongPw = "Cobalt-Meadow-Harvest-58";
    private static string Uniq() => Guid.NewGuid().ToString("N")[..8];
    private static readonly HttpMethod Patch = HttpMethod.Patch;
    private static readonly HttpMethod Put = HttpMethod.Put;

    private async Task<TestBrowser> Admin()
    {
        var u = await _seed.UserAsync(role: "super_admin"); var b = TestBrowser.Create(fx.Factory); await b.LoginAsync(u.Email, Seed.Password); return b;
    }

    /// <summary>A brand-new organisation with its first administrator signed in (so tests can't disturb each other's defaults).</summary>
    private async Task<(Guid TenantId, TestBrowser Admin)> NewTenant()
    {
        var pu = await _seed.UserAsync(role: "super_admin"); var p = TestBrowser.Create(fx.Factory); await p.LoginAsync(pu.Email, Seed.Password);
        await p.Post("/api/auth/reauth", new { password = Seed.Password });
        var slug = "h" + Uniq(); var email = $"{slug}.admin@test.local";
        var res = await p.Post("/api/tenants", new { name = $"Org {slug}", slug, adminEmail = email });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var id = (await res.Body()).GetProperty("id").GetGuid();
        var token = InMemoryEmailSender.TokenIn(await _seed.Mail.WaitFor(email, "invited"));
        Assert.Equal(HttpStatusCode.OK, (await TestBrowser.Create(fx.Factory).Post("/api/invitations/accept", new { token, name = "Org Admin", password = StrongPw }, csrf: false)).StatusCode);
        var b = TestBrowser.Create(fx.Factory); Assert.Equal(HttpStatusCode.OK, (await b.LoginAsync(email, StrongPw)).StatusCode);
        return (id, b);
    }

    private static async Task<JsonElement> Created(HttpResponseMessage r) { Assert.True(r.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK, await r.Content.ReadAsStringAsync()); return await r.Body(); }
    private static object Loc(string type, Guid? parent, string code, string name, object? extra = null) => new { type, parentId = parent, code, name, address = (string?)null, timeZone = (string?)null, calendarId = (Guid?)null, managerUserId = (Guid?)null, capacity = (int?)null };

    // ============================================================ permissions
    [Fact]
    public async Task Setup_endpoints_need_their_own_permissions()
    {
        var anon = TestBrowser.Create(fx.Factory);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.Get("/api/locations")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.Get("/api/teams")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.Get("/api/calendars")).StatusCode);

        var u = await _seed.UserAsync(role: "user"); var plain = TestBrowser.Create(fx.Factory); await plain.LoginAsync(u.Email, Seed.Password);
        foreach (var path in new[] { "/api/locations", "/api/teams", "/api/calendars" }) Assert.Equal(HttpStatusCode.Forbidden, (await plain.Get(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await plain.Post("/api/locations", Loc("site", null, "X1", "Nope"))).StatusCode);

        // read-only role: can list, cannot change
        await _seed.RoleAsync("setup_reader", "locations.read", "teams.read", "calendars.read");
        var ro = await _seed.UserAsync(role: "setup_reader"); var rb = TestBrowser.Create(fx.Factory); await rb.LoginAsync(ro.Email, Seed.Password);
        Assert.Equal(HttpStatusCode.OK, (await rb.Get("/api/locations")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await rb.Get("/api/calendars")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await rb.Post("/api/locations", Loc("site", null, "RO1", "Nope"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await rb.Post("/api/teams", new { name = "Nope" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await rb.Post("/api/calendars", new { name = "Nope" })).StatusCode);
    }

    // ============================================================ locations
    [Fact]
    public async Task Locations_form_a_tree_and_only_deeper_types_fit_inside_their_parent()
    {
        var b = await Admin(); var u = Uniq();
        var region = await Created(await b.Post("/api/locations", Loc("region", null, $"R-{u}", "Java")));
        var site = await Created(await b.Post("/api/locations", Loc("site", region.GetProperty("id").GetGuid(), $"S-{u}", "Jakarta HQ")));
        var building = await Created(await b.Post("/api/locations", Loc("building", site.GetProperty("id").GetGuid(), $"B-{u}", "Tower A")));
        var floor = await Created(await b.Post("/api/locations", Loc("floor", building.GetProperty("id").GetGuid(), $"F-{u}", "Level 3")));
        await Created(await b.Post("/api/locations", Loc("room", floor.GetProperty("id").GetGuid(), $"M-{u}", "Meeting 3.1")));

        Assert.Equal((HttpStatusCode)422, (await b.Post("/api/locations", Loc("building", null, $"X-{u}", "Loose building"))).StatusCode);            // only region/site at the top
        Assert.Equal((HttpStatusCode)422, (await b.Post("/api/locations", Loc("site", floor.GetProperty("id").GetGuid(), $"Y-{u}", "Site in floor"))).StatusCode); // shallower than parent
        Assert.Equal((HttpStatusCode)422, (await b.Post("/api/locations", Loc("floor", floor.GetProperty("id").GetGuid(), $"Z-{u}", "Floor in floor"))).StatusCode);
        Assert.Equal((HttpStatusCode)422, (await b.Post("/api/locations", Loc("planet", null, $"P-{u}", "Mars"))).StatusCode);

        var list = (await (await b.Get("/api/locations")).Body()).EnumerateArray().ToList();
        var up = u.ToUpperInvariant(); // codes are stored upper-case
        Assert.Contains(list, l => l.GetProperty("code").GetString() == $"M-{up}" && l.GetProperty("type").GetString() == "room");
        Assert.Equal(1, list.Single(l => l.GetProperty("code").GetString() == $"B-{up}").GetProperty("children").GetInt32());
    }

    [Fact]
    public async Task Location_codes_are_normalised_unique_among_live_places_and_validated()
    {
        var b = await Admin(); var u = Uniq();
        var first = await Created(await b.Post("/api/locations", Loc("site", null, $"dup-{u}", "First")));
        Assert.Equal($"DUP-{u}".ToUpperInvariant(), first.GetProperty("code").GetString());
        Assert.Equal((HttpStatusCode)422, (await b.Post("/api/locations", Loc("site", null, $"DUP-{u}", "Second"))).StatusCode);
        Assert.Equal((HttpStatusCode)422, (await b.Post("/api/locations", Loc("site", null, "bad code!", "Bad"))).StatusCode);
        Assert.Equal((HttpStatusCode)422, (await b.Post("/api/locations", Loc("site", null, $"N-{u}", "A"))).StatusCode); // name too short
        var tz = await b.Post("/api/locations", new { type = "site", code = $"TZ-{u}", name = "Odd zone", timeZone = "Mars/Olympus" });
        Assert.Equal((HttpStatusCode)422, tz.StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await b.Post("/api/locations", new { type = "site", code = $"OK-{u}", name = "Good zone", timeZone = "Asia/Jakarta", capacity = 120 })).StatusCode);

        // once archived, the code is free again
        var id = first.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await b.Post($"/api/locations/{id}/archive")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await b.Post("/api/locations", Loc("site", null, $"DUP-{u}", "Reuse"))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await b.Post($"/api/locations/{id}/restore")).StatusCode); // its code is taken now
    }

    [Fact]
    public async Task Updating_a_location_audits_the_change_and_detects_concurrent_edits()
    {
        var b = await Admin(); var u = Uniq();
        var site = await Created(await b.Post("/api/locations", Loc("site", null, $"U-{u}", "Before")));
        var id = site.GetProperty("id").GetGuid(); var version = site.GetProperty("version").GetInt32();

        var ok = await b.SendAsync(Patch, $"/api/locations/{id}", new { type = "site", code = $"U-{u}", name = "After", version });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var updated = await ok.Body(); Assert.Equal("After", updated.GetProperty("name").GetString()); Assert.Equal(version + 1, updated.GetProperty("version").GetInt32());

        var stale = await b.SendAsync(Patch, $"/api/locations/{id}", new { type = "site", code = $"U-{u}", name = "Stale", version });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        var audit = await _seed.Db(db => db.AuditEvents.Where(a => a.TargetId == id.ToString() && a.Action.StartsWith("location.")).OrderBy(a => a.Id).Select(a => new { a.Action, a.DiffJson }).ToListAsync());
        Assert.Equal(["location.create", "location.update"], audit.Select(a => a.Action).ToArray());
        Assert.Contains("Before", audit[1].DiffJson); Assert.Contains("After", audit[1].DiffJson);
    }

    [Fact]
    public async Task A_place_with_live_children_cannot_be_archived_and_a_child_cannot_outlive_its_parent_restore()
    {
        var b = await Admin(); var u = Uniq();
        var site = await Created(await b.Post("/api/locations", Loc("site", null, $"PS-{u}", "Parent site")));
        var siteId = site.GetProperty("id").GetGuid();
        var building = await Created(await b.Post("/api/locations", Loc("building", siteId, $"PB-{u}", "Child building")));
        var buildingId = building.GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.Conflict, (await b.Post($"/api/locations/{siteId}/archive")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await b.Post($"/api/locations/{buildingId}/archive")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await b.Post($"/api/locations/{siteId}/archive")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await b.Post($"/api/locations/{buildingId}/restore")).StatusCode); // parent is archived
        Assert.Equal(HttpStatusCode.OK, (await b.Post($"/api/locations/{siteId}/restore")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await b.Post($"/api/locations/{buildingId}/restore")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await b.Post($"/api/locations/{buildingId}/restore")).StatusCode); // already live
    }

    [Fact]
    public async Task Changing_a_type_or_parent_must_keep_the_children_valid_and_never_creates_a_cycle()
    {
        var b = await Admin(); var u = Uniq();
        var site = await Created(await b.Post("/api/locations", Loc("site", null, $"CS-{u}", "Site")));
        var building = await Created(await b.Post("/api/locations", Loc("building", site.GetProperty("id").GetGuid(), $"CB-{u}", "Building")));
        var siteId = site.GetProperty("id").GetGuid(); var buildingId = building.GetProperty("id").GetGuid();

        // a location can't be moved inside its own child (depth would go the wrong way) nor inside itself
        Assert.Equal((HttpStatusCode)422, (await b.SendAsync(Patch, $"/api/locations/{siteId}", new { type = "site", parentId = buildingId, code = $"CS-{u}", name = "Site" })).StatusCode);
        Assert.Equal((HttpStatusCode)422, (await b.SendAsync(Patch, $"/api/locations/{buildingId}", new { type = "building", parentId = buildingId, code = $"CB-{u}", name = "Building" })).StatusCode);
        // turning the site into a room would orphan the building's rules
        Assert.Equal((HttpStatusCode)422, (await b.SendAsync(Patch, $"/api/locations/{siteId}", new { type = "room", code = $"CS-{u}", name = "Site" })).StatusCode);
    }

    // ============================================================ calendars
    [Fact]
    public async Task Every_new_organisation_starts_with_a_default_office_hours_calendar()
    {
        var (_, admin) = await NewTenant();
        var cals = (await (await admin.Get("/api/calendars")).Body()).EnumerateArray().ToList();
        var c = Assert.Single(cals);
        Assert.Equal("Office hours", c.GetProperty("name").GetString()); Assert.True(c.GetProperty("isDefault").GetBoolean());
        Assert.Equal("Asia/Jakarta", c.GetProperty("timeZone").GetString());
        Assert.Equal(5, c.GetProperty("periods").GetArrayLength());
    }

    [Fact]
    public async Task Calendars_validate_windows_and_holidays_and_only_one_is_the_default()
    {
        var (_, admin) = await NewTenant(); var n = Uniq();
        var week = Enumerable.Range(1, 5).Select(d => new { day = d, startMinute = 480, endMinute = 1020 }).ToArray();

        Assert.Equal((HttpStatusCode)422, (await admin.Post("/api/calendars", new { name = $"Empty {n}", timeZone = "Asia/Jakarta", isAlwaysOpen = false, periods = Array.Empty<object>(), holidays = Array.Empty<object>() })).StatusCode);
        Assert.Equal((HttpStatusCode)422, (await admin.Post("/api/calendars", new { name = $"Backwards {n}", timeZone = "Asia/Jakarta", periods = new[] { new { day = 1, startMinute = 600, endMinute = 540 } }, holidays = Array.Empty<object>() })).StatusCode);
        Assert.Equal((HttpStatusCode)422, (await admin.Post("/api/calendars", new { name = $"Overlap {n}", timeZone = "Asia/Jakarta", periods = new[] { new { day = 1, startMinute = 480, endMinute = 720 }, new { day = 1, startMinute = 700, endMinute = 900 } }, holidays = Array.Empty<object>() })).StatusCode);
        Assert.Equal((HttpStatusCode)422, (await admin.Post("/api/calendars", new { name = $"BadZone {n}", timeZone = "Nowhere/Land", periods = week, holidays = Array.Empty<object>() })).StatusCode);
        Assert.Equal((HttpStatusCode)422, (await admin.Post("/api/calendars", new { name = $"BadHoliday {n}", timeZone = "Asia/Jakarta", periods = week, holidays = new[] { new { date = "17-08-2026", name = "x" } } })).StatusCode);
        Assert.Equal((HttpStatusCode)422, (await admin.Post("/api/calendars", new { name = $"Twice {n}", timeZone = "Asia/Jakarta", periods = week, holidays = new[] { new { date = "2026-08-17", name = "a" }, new { date = "2026-08-17", name = "b" } } })).StatusCode);

        var shift = await Created(await admin.Post("/api/calendars", new { name = $"Security {n}", timeZone = "Asia/Jakarta", isAlwaysOpen = true, periods = Array.Empty<object>(), holidays = new[] { new { date = "2026-08-17", name = "Independence Day" } } }));
        Assert.False(shift.GetProperty("isDefault").GetBoolean()); Assert.True(shift.GetProperty("isAlwaysOpen").GetBoolean());
        Assert.Equal((HttpStatusCode)422, (await admin.Post("/api/calendars", new { name = $"Security {n}", timeZone = "Asia/Jakarta", isAlwaysOpen = true })).StatusCode); // duplicate name

        var id = shift.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await admin.Post($"/api/calendars/{id}/default")).StatusCode);
        var list = (await (await admin.Get("/api/calendars")).Body()).EnumerateArray().ToList();
        Assert.Equal(1, list.Count(c => c.GetProperty("isDefault").GetBoolean()));
        Assert.True(list.Single(c => c.GetProperty("id").GetGuid() == id).GetProperty("isDefault").GetBoolean());
        Assert.Equal(HttpStatusCode.Conflict, (await admin.Post($"/api/calendars/{id}/archive")).StatusCode); // the default can't be archived
    }

    [Fact]
    public async Task Updating_a_calendar_replaces_its_windows_and_holidays_and_detects_stale_versions()
    {
        var (_, admin) = await NewTenant();
        var cal = (await (await admin.Get("/api/calendars")).Body()).EnumerateArray().Single();
        var id = cal.GetProperty("id").GetGuid(); var version = cal.GetProperty("version").GetInt32();
        var body = new { name = "Office hours", timeZone = "Asia/Jakarta", isAlwaysOpen = false, version, periods = new[] { new { day = 1, startMinute = 540, endMinute = 720 }, new { day = 1, startMinute = 780, endMinute = 1020 } }, holidays = new[] { new { date = "2026-12-25", name = "Christmas" } } };
        var res = await admin.SendAsync(Put, $"/api/calendars/{id}", body);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var updated = await res.Body();
        Assert.Equal(2, updated.GetProperty("periods").GetArrayLength()); Assert.Equal(1, updated.GetProperty("holidays").GetArrayLength());
        Assert.Equal(HttpStatusCode.Conflict, (await admin.SendAsync(Put, $"/api/calendars/{id}", body)).StatusCode); // same (now stale) version
        Assert.Contains("calendar.update", await _seed.Db(db => db.AuditEvents.Where(a => a.TargetId == id.ToString()).Select(a => a.Action).ToListAsync()));
    }

    [Fact]
    public async Task A_calendar_used_by_a_location_cannot_be_archived_until_it_is_released()
    {
        var (_, admin) = await NewTenant(); var n = Uniq();
        var cal = await Created(await admin.Post("/api/calendars", new { name = $"Night {n}", timeZone = "Asia/Jakarta", isAlwaysOpen = true }));
        var calId = cal.GetProperty("id").GetGuid();
        var site = await Created(await admin.Post("/api/locations", new { type = "site", code = $"NS-{n}", name = "Night site", calendarId = calId }));
        Assert.Equal(HttpStatusCode.Conflict, (await admin.Post($"/api/calendars/{calId}/archive")).StatusCode);
        // an unknown calendar can't be chosen
        Assert.Equal((HttpStatusCode)422, (await admin.Post("/api/locations", new { type = "site", code = $"NX-{n}", name = "Ghost calendar", calendarId = Guid.NewGuid() })).StatusCode);

        var siteId = site.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await admin.Post($"/api/locations/{siteId}/archive")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.Post($"/api/calendars/{calId}/archive")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.Post($"/api/calendars/{calId}/restore")).StatusCode);
    }

    // ============================================================ teams
    [Fact]
    public async Task Teams_have_unique_names_and_replace_their_members_as_a_whole()
    {
        var b = await Admin(); var n = Uniq();
        var team = await Created(await b.Post("/api/teams", new { name = $"Facilities {n}", description = "AC, plumbing, lights" }));
        var id = team.GetProperty("id").GetGuid();
        Assert.Equal((HttpStatusCode)422, (await b.Post("/api/teams", new { name = $"Facilities {n}" })).StatusCode);
        Assert.Equal((HttpStatusCode)422, (await b.Post("/api/teams", new { name = "x" })).StatusCode);

        var a = await _seed.UserAsync(role: "user"); var c = await _seed.UserAsync(role: "user"); var suspended = await _seed.UserAsync(role: "user", status: UserStatus.Suspended);
        var set = await b.SendAsync(Put, $"/api/teams/{id}/members", new { members = new[] { new { userId = a.Id, isLead = true }, new { userId = c.Id, isLead = false } } });
        Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        var detail = await (await b.Get($"/api/teams/{id}")).Body();
        Assert.Equal(2, detail.GetProperty("members").GetArrayLength());
        Assert.True(detail.GetProperty("members").EnumerateArray().Single(m => m.GetProperty("userId").GetGuid() == a.Id).GetProperty("isLead").GetBoolean());

        // suspended people and strangers can't join; the failed save changes nothing
        Assert.Equal((HttpStatusCode)422, (await b.SendAsync(Put, $"/api/teams/{id}/members", new { members = new[] { new { userId = suspended.Id, isLead = false } } })).StatusCode);
        Assert.Equal((HttpStatusCode)422, (await b.SendAsync(Put, $"/api/teams/{id}/members", new { members = new[] { new { userId = Guid.NewGuid(), isLead = false } } })).StatusCode);
        Assert.Equal(2, (await (await b.Get($"/api/teams/{id}")).Body()).GetProperty("members").GetArrayLength());

        // replacing: remove one, keep one
        Assert.Equal(HttpStatusCode.OK, (await b.SendAsync(Put, $"/api/teams/{id}/members", new { members = new[] { new { userId = c.Id, isLead = false } } })).StatusCode);
        Assert.Equal(1, (await (await b.Get($"/api/teams/{id}")).Body()).GetProperty("members").GetArrayLength());

        Assert.Equal(HttpStatusCode.OK, (await b.Post($"/api/teams/{id}/archive")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await b.SendAsync(Put, $"/api/teams/{id}/members", new { members = Array.Empty<object>() })).StatusCode); // archived teams are frozen
        Assert.Equal(HttpStatusCode.OK, (await b.Post($"/api/teams/{id}/restore")).StatusCode);
        Assert.Contains("team.members", await _seed.Db(db => db.AuditEvents.Where(x => x.TargetId == id.ToString()).Select(x => x.Action).ToListAsync()));
    }

    // ============================================================ tenant isolation
    [Fact]
    public async Task Another_organisation_never_sees_or_changes_locations_calendars_or_teams()
    {
        var mine = await Admin(); var n = Uniq();
        var loc = await Created(await mine.Post("/api/locations", Loc("site", null, $"ISO-{n}", "Private site")));
        var team = await Created(await mine.Post("/api/teams", new { name = $"Private team {n}" }));
        var cal = (await (await mine.Get("/api/calendars")).Body()).EnumerateArray().First();
        var locId = loc.GetProperty("id").GetGuid(); var teamId = team.GetProperty("id").GetGuid(); var calId = cal.GetProperty("id").GetGuid();

        var (_, other) = await NewTenant();
        Assert.DoesNotContain((await (await other.Get("/api/locations?includeArchived=true")).Body()).EnumerateArray(), l => l.GetProperty("id").GetGuid() == locId);
        Assert.DoesNotContain((await (await other.Get("/api/teams?includeArchived=true")).Body()).EnumerateArray(), t => t.GetProperty("id").GetGuid() == teamId);
        Assert.DoesNotContain((await (await other.Get("/api/calendars?includeArchived=true")).Body()).EnumerateArray(), c => c.GetProperty("id").GetGuid() == calId);

        Assert.Equal(HttpStatusCode.NotFound, (await other.Get($"/api/locations/{locId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Get($"/api/teams/{teamId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Get($"/api/calendars/{calId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.SendAsync(Patch, $"/api/locations/{locId}", new { type = "site", code = "HACK", name = "Hijacked" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Post($"/api/locations/{locId}/archive")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Post($"/api/teams/{teamId}/archive")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Post($"/api/calendars/{calId}/default")).StatusCode);
        // a place from another organisation can't be used as a parent, nor its calendar attached
        Assert.Equal((HttpStatusCode)422, (await other.Post("/api/locations", Loc("building", locId, "CROSS", "Cross-tenant child"))).StatusCode);
        Assert.Equal((HttpStatusCode)422, (await other.Post("/api/locations", new { type = "site", code = "CROSS2", name = "Cross calendar", calendarId = calId })).StatusCode);

        // …and the same code can be used by both organisations
        Assert.Equal(HttpStatusCode.Created, (await other.Post("/api/locations", Loc("site", null, $"ISO-{n}", "Same code elsewhere"))).StatusCode);
        Assert.Equal("Private site", (await _seed.Db(db => db.Locations.SingleAsync(l => l.Id == locId))).Name);
    }
}
