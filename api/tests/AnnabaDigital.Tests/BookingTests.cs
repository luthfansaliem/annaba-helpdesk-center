using System.Net;
using System.Text.Json;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Tests.Infra;
using Xunit;

namespace AnnabaDigital.Tests;

/// <summary>Bookings (R3-1: BKG-001/002/003): bookable resources and who maintains them, booking rules (opening hours and days, buffer, longest booking, capacity,
/// 5-minute steps, the past), double-booking refused by the application and by the database under a race, privacy of others' bookings, who may change or cancel,
/// role-restricted resources, archiving, and the summary.</summary>
[Collection("db")]
public sealed class BookingTests(PostgresFixture fx)
{
    private static string Uniq() => Guid.NewGuid().ToString("N")[..8];
    private const HttpStatusCode Invalid = (HttpStatusCode)422;
    private static async Task<JsonElement> Ok(HttpResponseMessage r) { Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync()); return await r.Body(); }
    private static Guid IdOf(JsonElement e) => e.GetProperty("id").GetGuid();
    private static async Task<JsonElement> Fields(HttpResponseMessage r) { Assert.Equal(Invalid, r.StatusCode); return (await r.Body()).GetProperty("error").GetProperty("fields"); }

    private sealed record World(ApiFactory F, TestClock Clock, Seed Seed, TestBrowser Manager, TestBrowser Supervisor, TestBrowser Agent, User AgentUser, TestBrowser Requester, User RequesterUser, TestBrowser Other, User OtherUser);
    private async Task<World> Build(bool isolated = false)
    {
        ApiFactory f; TestClock clock;
        if (isolated) { var iso = await fx.NewIsolatedAsync(); f = iso.Factory; clock = iso.Clock; } else { f = fx.Factory; clock = fx.Clock; }
        var seed = new Seed(f, clock);
        async Task<(User, TestBrowser)> Person(string role) { var u = await seed.UserAsync(role: role); var b = TestBrowser.Create(f); await b.LoginAsync(u.Email, Seed.Password); return (u, b); }
        var (_, mgr) = await Person("ga_manager"); var (_, sup) = await Person("ga_supervisor"); var (au, agent) = await Person("ga_agent"); var (ru, req) = await Person("user"); var (ou, other) = await Person("user");
        return new World(f, clock, seed, mgr, sup, agent, au, req, ru, other, ou);
    }

    /// <summary>The next <paramref name="day"/> after today (in the resource's time zone) at the given local time.</summary>
    private static DateTimeOffset Next(TestClock clock, string zoneId, DayOfWeek day, int hour, int minute = 0)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
        var local = TimeZoneInfo.ConvertTime(clock.UtcNow, zone).Date.AddDays(1);
        while (local.DayOfWeek != day) local = local.AddDays(1);
        var at = local.AddHours(hour).AddMinutes(minute);
        return new DateTimeOffset(at, zone.GetUtcOffset(at));
    }
    private static object Booking(Guid resource, DateTimeOffset s, DateTimeOffset e, string title = "Team meeting", int? attendees = null) => new { resourceId = resource, startsAt = s, endsAt = e, title, attendees };

    private static async Task<(Guid Id, string Zone)> Room(World w, string name, object? extra = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["kind"] = "room", ["name"] = name, ["code"] = "R-" + Uniq(), ["capacity"] = 8, ["features"] = new[] { "projector", "Video Call" },
            ["openFrom"] = "08:00", ["openUntil"] = "18:00", ["openDays"] = 31, ["bufferMinutes"] = 15, ["maxMinutes"] = 240,
        };
        if (extra is not null) foreach (var p in extra.GetType().GetProperties()) body[p.Name] = p.GetValue(extra);
        var r = await Ok(await w.Manager.Post("/api/resources", body));
        return (IdOf(r), r.GetProperty("timeZone").GetString()!);
    }

    [Fact]
    public async Task Resources_are_kept_by_people_who_manage_them_and_validated()
    {
        var w = await Build();
        // requesters may book but not maintain resources
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Requester.Post("/api/resources", new { kind = "room", name = "Nope" })).StatusCode);
        var f = await Fields(await w.Manager.Post("/api/resources", new { kind = "boat", name = "", openFrom = "18:00", openUntil = "08:00", openDays = 0, bufferMinutes = 7, capacity = 0, features = new[] { "a b!" } }));
        foreach (var k in new[] { "kind", "name", "openUntil", "openDays", "bufferMinutes", "capacity", "features" }) Assert.True(f.TryGetProperty(k, out _), k);
        Assert.True((await Fields(await w.Manager.Post("/api/resources", new { kind = "room", name = "Half", openFrom = "08:00" }))).TryGetProperty("openUntil", out _));

        var name = $"Garuda {Uniq()}";
        var (id, _) = await Room(w, name);
        var item = await Ok(await w.Requester.Get($"/api/resources/{id}"));
        Assert.Equal("room", item.GetProperty("kind").GetString());
        Assert.Equal(new[] { "projector", "video_call" }, item.GetProperty("features").EnumerateArray().Select(x => x.GetString()).ToArray());   // normalised
        Assert.True(item.GetProperty("canBook").GetBoolean());
        // the code is unique per organisation
        var code = item.GetProperty("code").GetString();
        Assert.True((await Fields(await w.Manager.Post("/api/resources", new { kind = "room", name = "Copy", code }))).TryGetProperty("code", out _));
        // found by capacity and feature
        var found = await Ok(await w.Requester.Get($"/api/resources?kind=room&capacity=6&feature=projector&q={Uri.EscapeDataString(name)}"));
        Assert.Single(found.EnumerateArray());
        Assert.Empty((await Ok(await w.Requester.Get($"/api/resources?capacity=9&q={Uri.EscapeDataString(name)}"))).EnumerateArray());
        // edits use the version
        Assert.Equal(HttpStatusCode.Conflict, (await w.Manager.SendAsync(HttpMethod.Put, $"/api/resources/{id}", new { kind = "room", name, version = 99 })).StatusCode);

        // booking needs no permission (like a request), but a vendor's person never reaches it
        var vendorUser = await w.Seed.UserAsync(role: "vendor");
        var vendor = IdOf(await Ok(await w.Manager.Post("/api/vendors", new { name = $"Vendor {Uniq()}" })));
        await Ok(await w.Manager.Post($"/api/vendors/{vendor}/portal-users", new { email = vendorUser.Email }));
        var vb = TestBrowser.Create(w.F); await vb.LoginAsync(vendorUser.Email, Seed.Password);
        Assert.Equal(HttpStatusCode.Forbidden, (await vb.Get("/api/resources")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await vb.Post("/api/bookings", new { resourceId = id, title = "Nope" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await TestBrowser.Create(w.F).Get("/api/bookings")).StatusCode);
    }

    [Fact]
    public async Task Booking_rules_hours_days_buffer_length_capacity_and_overlap()
    {
        var w = await Build();
        var (room, zone) = await Room(w, $"Rules {Uniq()}");
        var mon10 = Next(w.Clock, zone, DayOfWeek.Monday, 10);
        var b1 = await Ok(await w.Requester.Post("/api/bookings", Booking(room, mon10, mon10.AddHours(1), attendees: 6)));
        Assert.Equal("confirmed", b1.GetProperty("status").GetString());
        Assert.Equal(mon10.AddHours(1).AddMinutes(15), b1.GetProperty("blockedUntil").GetDateTimeOffset());   // the buffer

        // overlap and the buffer after it are refused with a clear message
        var clash = await w.Other.Post("/api/bookings", Booking(room, mon10.AddMinutes(30), mon10.AddMinutes(90)));
        Assert.Equal(HttpStatusCode.Conflict, clash.StatusCode);
        Assert.Contains("already taken", (await clash.Body()).GetProperty("error").GetProperty("message").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await w.Other.Post("/api/bookings", Booking(room, mon10.AddHours(1), mon10.AddHours(2)))).StatusCode);
        await Ok(await w.Other.Post("/api/bookings", Booking(room, mon10.AddMinutes(75), mon10.AddHours(2))));   // after the buffer

        // opening hours, days, steps, length, capacity, the past
        Assert.True((await Fields(await w.Requester.Post("/api/bookings", Booking(room, mon10.AddHours(-3), mon10.AddHours(-2))))).TryGetProperty("startsAt", out _));   // 07:00
        Assert.True((await Fields(await w.Requester.Post("/api/bookings", Booking(room, mon10.AddHours(7), mon10.AddHours(9))))).TryGetProperty("startsAt", out _));    // until 19:00
        var sun = Next(w.Clock, zone, DayOfWeek.Sunday, 10);
        Assert.True((await Fields(await w.Requester.Post("/api/bookings", Booking(room, sun, sun.AddHours(1))))).TryGetProperty("startsAt", out _));
        Assert.True((await Fields(await w.Requester.Post("/api/bookings", Booking(room, mon10.AddHours(3).AddMinutes(2), mon10.AddHours(4))))).TryGetProperty("startsAt", out _));
        Assert.True((await Fields(await w.Requester.Post("/api/bookings", Booking(room, mon10.AddHours(3), mon10.AddHours(7.5))))).TryGetProperty("endsAt", out _));   // longer than 4 h
        Assert.True((await Fields(await w.Requester.Post("/api/bookings", Booking(room, mon10.AddHours(3), mon10.AddHours(4), attendees: 9)))).TryGetProperty("attendees", out _));
        var past = w.Clock.UtcNow.AddDays(-2);
        Assert.True((await Fields(await w.Requester.Post("/api/bookings", Booking(room, past, past.AddHours(1))))).EnumerateObject().Any());
        Assert.True((await Fields(await w.Requester.Post("/api/bookings", new { resourceId = room, startsAt = mon10, endsAt = mon10.AddHours(1), title = " " }))).TryGetProperty("title", out _));

        // moving a booking checks the same rules but not against itself
        var moved = await Ok(await w.Requester.SendAsync(HttpMethod.Put, $"/api/bookings/{IdOf(b1)}", new { resourceId = room, startsAt = mon10.AddMinutes(-30), endsAt = mon10.AddMinutes(30), title = "Earlier", version = b1.GetProperty("version").GetInt32() }));
        Assert.Equal("Earlier", moved.GetProperty("title").GetString());
    }

    [Fact]
    public async Task The_database_refuses_a_double_booking_made_at_the_same_moment()
    {
        var w = await Build();
        var (room, zone) = await Room(w, $"Race {Uniq()}", new { bufferMinutes = 0 });
        var tue = Next(w.Clock, zone, DayOfWeek.Tuesday, 9);
        for (var round = 0; round < 3; round++)
        {
            var at = tue.AddHours(round * 2);
            var results = await Task.WhenAll(w.Requester.Post("/api/bookings", Booking(room, at, at.AddHours(1), "A")), w.Other.Post("/api/bookings", Booking(room, at.AddMinutes(30), at.AddMinutes(90), "B")));
            Assert.True(results.Count(r => r.StatusCode == HttpStatusCode.Created) == 1, string.Join(" | ", await Task.WhenAll(results.Select(async r => $"{(int)r.StatusCode} {await r.Content.ReadAsStringAsync()}"))));
            Assert.Single(results, r => r.StatusCode == HttpStatusCode.Conflict);
        }
        var count = await w.Seed.Db(db => Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.CountAsync(db.Bookings, b => b.ResourceId == room && b.Status == BookingStatus.Confirmed));
        Assert.Equal(3, count);
    }

    [Fact]
    public async Task Others_see_only_that_a_slot_is_taken_and_only_the_people_involved_or_managers_change_it()
    {
        var w = await Build();
        var (room, zone) = await Room(w, $"Private {Uniq()}");
        var wed = Next(w.Clock, zone, DayOfWeek.Wednesday, 13);
        var mine = await Ok(await w.Requester.Post("/api/bookings", new { resourceId = room, startsAt = wed, endsAt = wed.AddHours(1), title = "Salary talk", note = "Confidential" }));
        var id = IdOf(mine);
        var day = $"/api/bookings/availability?resourceId={room}&from={Uri.EscapeDataString(wed.AddHours(-13).ToString("O"))}&to={Uri.EscapeDataString(wed.AddHours(11).ToString("O"))}";
        var seen = (await Ok(await w.Other.Get(day))).GetProperty("rows")[0].GetProperty("bookings")[0];
        Assert.Equal(JsonValueKind.Null, seen.GetProperty("title").ValueKind); Assert.Equal(JsonValueKind.Null, seen.GetProperty("bookedFor").ValueKind); Assert.Equal(JsonValueKind.Null, seen.GetProperty("note").ValueKind);
        Assert.False(seen.GetProperty("canChange").GetBoolean());
        Assert.Equal("Salary talk", (await Ok(await w.Supervisor.Get(day))).GetProperty("rows")[0].GetProperty("bookings")[0].GetProperty("title").GetString());   // bookings.read_all

        // another requester can neither read, change nor cancel it
        Assert.Equal(HttpStatusCode.NotFound, (await w.Other.Get($"/api/bookings/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await w.Other.Post($"/api/bookings/{id}/cancel", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await w.Other.SendAsync(HttpMethod.Put, $"/api/bookings/{id}", new { resourceId = room, startsAt = wed, endsAt = wed.AddHours(2), title = "Mine now" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Other.Get("/api/bookings?scope=all")).StatusCode);
        // only managers book for someone else
        Assert.True((await Fields(await w.Other.Post("/api/bookings", new { resourceId = room, startsAt = wed.AddHours(2), endsAt = wed.AddHours(3), title = "For you", bookedForId = w.RequesterUser.Id }))).TryGetProperty("bookedForId", out _));
        var forYou = await Ok(await w.Supervisor.Post("/api/bookings", new { resourceId = room, startsAt = wed.AddHours(2), endsAt = wed.AddHours(3), title = "Interview", bookedForId = w.RequesterUser.Id }));
        Assert.True(forYou.GetProperty("canChange").GetBoolean());
        var list = await Ok(await w.Requester.Get("/api/bookings?scope=mine"));
        Assert.Contains(list.GetProperty("data").EnumerateArray(), b => IdOf(b) == IdOf(forYou) && b.GetProperty("mine").GetBoolean());

        // a manager cancels someone's booking only with a reason, and the person is told
        Assert.True((await Fields(await w.Supervisor.Post($"/api/bookings/{id}/cancel", new { }))).TryGetProperty("reason", out _));
        var cancelled = await Ok(await w.Supervisor.Post($"/api/bookings/{id}/cancel", new { reason = "The board needs the room" }));
        Assert.Equal("cancelled", cancelled.GetProperty("status").GetString());
        var note = await w.Seed.Db(db => Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(db.Notifications, n => n.UserId == w.RequesterUser.Id && n.Kind == "booking.changed" && n.Title.Contains("cancelled")));
        Assert.NotNull(note);
        Assert.Equal(HttpStatusCode.Conflict, (await w.Requester.Post($"/api/bookings/{id}/cancel", new { })).StatusCode);   // already cancelled
        // the slot is free again
        await Ok(await w.Other.Post("/api/bookings", Booking(room, wed, wed.AddHours(1))));
    }

    [Fact]
    public async Task Restricted_resources_and_archiving()
    {
        var w = await Build();
        var agentRole = await w.Seed.RoleId("ga_agent");
        var (car, zone) = await Room(w, $"Pool car {Uniq()}", new { kind = "vehicle", openFrom = (string?)null, openUntil = (string?)null, openDays = 127, allowedRoleIds = new[] { agentRole }, maxMinutes = (int?)null, capacity = 6 });
        var thu = Next(w.Clock, zone, DayOfWeek.Thursday, 7);
        Assert.False((await Ok(await w.Requester.Get($"/api/resources/{car}"))).GetProperty("canBook").GetBoolean());
        Assert.True((await Fields(await w.Requester.Post("/api/bookings", Booking(car, thu, thu.AddHours(3))))).TryGetProperty("resourceId", out _));
        // around the clock and across midnight for vehicles
        var trip = await Ok(await w.Agent.Post("/api/bookings", Booking(car, thu, thu.AddHours(26), "Site visit Bandung", 3)));
        Assert.Equal("vehicle", trip.GetProperty("resourceKind").GetString());

        // a resource with upcoming bookings cannot be archived; once they are gone it can, and nobody books it any more
        Assert.Equal(HttpStatusCode.Conflict, (await w.Manager.Post($"/api/resources/{car}/archive")).StatusCode);
        await Ok(await w.Agent.Post($"/api/bookings/{IdOf(trip)}/cancel", new { }));
        await Ok(await w.Manager.Post($"/api/resources/{car}/archive"));
        Assert.Equal(HttpStatusCode.NotFound, (await w.Agent.Get($"/api/resources/{car}")).StatusCode);
        Assert.True((await Fields(await w.Agent.Post("/api/bookings", Booking(car, thu, thu.AddHours(1))))).TryGetProperty("resourceId", out _));
        Assert.Contains((await Ok(await w.Manager.Get("/api/resources?archived=true&kind=vehicle"))).EnumerateArray(), r => IdOf(r) == car);
        await Ok(await w.Manager.Post($"/api/resources/{car}/restore"));
    }

    [Fact]
    public async Task The_summary_counts_free_and_used_resources_today_and_the_next_week()
    {
        var w = await Build(isolated: true);
        Assert.Equal(0, (await Ok(await w.Requester.Get("/api/bookings/summary"))).GetProperty("resources").GetInt32());
        var (room, _) = await Room(w, "Summary room", new { openDays = 127, openFrom = (string?)null, openUntil = (string?)null, bufferMinutes = 0, maxMinutes = (int?)null });
        await Room(w, "Second room", new { openDays = 127 });
        // a meeting that started a moment ago (within the 15-minute grace) on a 5-minute step: in use now
        var now = w.Clock.UtcNow;
        var s = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, now.Minute - now.Minute % 5, 0, TimeSpan.Zero);
        await Ok(await w.Requester.Post("/api/bookings", Booking(room, s, s.AddHours(1))));
        await Ok(await w.Requester.Post("/api/bookings", Booking(room, s.AddDays(2), s.AddDays(2).AddHours(1))));
        var sum = await Ok(await w.Requester.Get("/api/bookings/summary"));
        Assert.Equal(2, sum.GetProperty("resources").GetInt32());
        Assert.Equal(1, sum.GetProperty("inUseNow").GetInt32()); Assert.Equal(1, sum.GetProperty("freeNow").GetInt32());
        Assert.Equal(2, sum.GetProperty("myUpcoming").GetInt32());
        Assert.Equal(7, sum.GetProperty("next7Days").GetArrayLength());
        Assert.Equal(2, sum.GetProperty("next7Days").EnumerateArray().Sum(d => d.GetProperty("bookings").GetInt32()));
        Assert.Equal(2, sum.GetProperty("byKind").EnumerateArray().First(k => k.GetProperty("key").GetString() == "room").GetProperty("count").GetInt32());
        Assert.Equal(0, (await Ok(await w.Other.Get("/api/bookings/summary"))).GetProperty("myUpcoming").GetInt32());
    }
}
