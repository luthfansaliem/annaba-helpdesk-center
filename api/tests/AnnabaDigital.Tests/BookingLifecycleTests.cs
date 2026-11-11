using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using AnnabaDigital.Application.Helpdesk;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Tests.Infra;
using Xunit;

namespace AnnabaDigital.Tests;

/// <summary>Bookings R3-2 (BKG-003/005): approval for resources that need it, check-in with no-shows released, reminders, approvals that come too late,
/// repeating bookings with skipped clashes and "this and following" cancelling, and the private calendar feed.</summary>
[Collection("db")]
public sealed class BookingLifecycleTests(PostgresFixture fx)
{
    private static string Uniq() => Guid.NewGuid().ToString("N")[..8];
    private const HttpStatusCode Invalid = (HttpStatusCode)422;
    private static async Task<JsonElement> Ok(HttpResponseMessage r) { Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync()); return await r.Body(); }
    private static Guid IdOf(JsonElement e) => e.GetProperty("id").GetGuid();
    private static string Status(JsonElement e) => e.GetProperty("status").GetString()!;
    private static async Task<JsonElement> Fields(HttpResponseMessage r) { Assert.Equal(Invalid, r.StatusCode); return (await r.Body()).GetProperty("error").GetProperty("fields"); }

    private sealed record World(ApiFactory F, TestClock Clock, Seed Seed, TestBrowser Manager, User ManagerUser, TestBrowser Supervisor, User SupervisorUser, TestBrowser Requester, User RequesterUser, TestBrowser Other, User OtherUser);
    private async Task<World> Build(bool isolated = false)
    {
        ApiFactory f; TestClock clock;
        if (isolated) { var iso = await fx.NewIsolatedAsync(); f = iso.Factory; clock = iso.Clock; } else { f = fx.Factory; clock = fx.Clock; }
        var seed = new Seed(f, clock);
        async Task<(User, TestBrowser)> Person(string role) { var u = await seed.UserAsync(role: role); var b = TestBrowser.Create(f); await b.LoginAsync(u.Email, Seed.Password); return (u, b); }
        var (mu, mgr) = await Person("ga_manager"); var (su, sup) = await Person("ga_supervisor"); var (ru, req) = await Person("user"); var (ou, other) = await Person("user");
        return new World(f, clock, seed, mgr, mu, sup, su, req, ru, other, ou);
    }
    private static async Task<World> Relogin(World w)
    {
        async Task<TestBrowser> In(User u) { var b = TestBrowser.Create(w.F); await b.LoginAsync(u.Email, Seed.Password); return b; }
        return w with { Manager = await In(w.ManagerUser), Supervisor = await In(w.SupervisorUser), Requester = await In(w.RequesterUser), Other = await In(w.OtherUser) };
    }
    private static async Task<Guid> Resource(World w, object extra)
    {
        var body = new Dictionary<string, object?> { ["kind"] = "room", ["name"] = $"Room {Uniq()}", ["code"] = "L-" + Uniq(), ["capacity"] = 10, ["openDays"] = 127, ["bufferMinutes"] = 0 };
        foreach (var p in extra.GetType().GetProperties()) body[p.Name] = p.GetValue(extra);
        return IdOf(await Ok(await w.Manager.Post("/api/resources", body)));
    }
    /// <summary>A 5-minute step at least <paramref name="minutes"/> from now.</summary>
    private static DateTimeOffset At(TestClock c, int minutes)
    {
        var t = c.UtcNow.AddMinutes(minutes);
        return new DateTimeOffset(t.Year, t.Month, t.Day, t.Hour, t.Minute - t.Minute % 5, 0, TimeSpan.Zero).AddMinutes(5);
    }
    private static object Booking(Guid r, DateTimeOffset s, DateTimeOffset e, string title = "Meeting") => new { resourceId = r, startsAt = s, endsAt = e, title };
    private static async Task<BookingService.RunResult> Run(World w)
    {
        using var s = w.F.Services.CreateScope();
        return await s.ServiceProvider.GetRequiredService<BookingService>().RunAsync();
    }
    private static Task<int> Notes(World w, Guid user, string kind) => w.Seed.Db(db => db.Notifications.IgnoreQueryFilters().CountAsync(n => n.UserId == user && n.Kind == kind));

    [Fact]
    public async Task A_resource_that_needs_approval_holds_the_slot_until_someone_decides()
    {
        var w = await Build(isolated: true);   // approvers are counted on their own organisation
        var room = await Resource(w, new { needsApproval = true });
        var s = At(w.Clock, 60 * 24 * 3);
        var b = await Ok(await w.Requester.Post("/api/bookings", Booking(room, s, s.AddHours(1), "Board meeting")));
        Assert.Equal("pending", Status(b));
        Assert.True(await Notes(w, w.SupervisorUser.Id, "booking.approval") >= 1);                                   // approvers are told
        Assert.Equal(HttpStatusCode.Conflict, (await w.Other.Post("/api/bookings", Booking(room, s.AddMinutes(30), s.AddHours(2)))).StatusCode);   // the slot is held

        // only approvers see and decide; a rejection needs a reason
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Requester.Get("/api/bookings?scope=pending")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Requester.Post($"/api/bookings/{IdOf(b)}/approve", new { })).StatusCode);
        var pending = await Ok(await w.Supervisor.Get("/api/bookings?scope=pending&pageSize=100"));
        var row = pending.GetProperty("data").EnumerateArray().First(x => IdOf(x) == IdOf(b));
        Assert.True(row.GetProperty("canDecide").GetBoolean());
        Assert.True((await Fields(await w.Supervisor.Post($"/api/bookings/{IdOf(b)}/reject", new { }))).TryGetProperty("note", out _));
        Assert.Equal("confirmed", Status(await Ok(await w.Supervisor.Post($"/api/bookings/{IdOf(b)}/approve", new { note = "Enjoy" }))));
        Assert.Equal(1, await Notes(w, w.RequesterUser.Id, "booking.decided"));
        Assert.Equal(HttpStatusCode.Conflict, (await w.Supervisor.Post($"/api/bookings/{IdOf(b)}/approve", new { })).StatusCode);   // decided once

        // a rejected booking frees the slot; an approver's own booking needs no approval
        var b2 = await Ok(await w.Requester.Post("/api/bookings", Booking(room, s.AddHours(2), s.AddHours(3))));
        Assert.Equal("rejected", Status(await Ok(await w.Supervisor.Post($"/api/bookings/{IdOf(b2)}/reject", new { note = "Maintenance that afternoon" }))));
        Assert.Equal("confirmed", Status(await Ok(await w.Supervisor.Post("/api/bookings", Booking(room, s.AddHours(2), s.AddHours(3))))));
        // moving an approved booking by its owner sends it back for approval
        var moved = await Ok(await w.Requester.SendAsync(HttpMethod.Put, $"/api/bookings/{IdOf(b)}", new { resourceId = room, startsAt = s.AddMinutes(-30), endsAt = s.AddMinutes(30), title = "Board meeting" }));
        Assert.Equal("pending", Status(moved));
    }

    [Fact]
    public async Task Reminders_check_in_released_no_shows_and_approvals_that_come_too_late()
    {
        var w = await Build(isolated: true);
        var room = await Resource(w, new { checkInMinutes = 15 });
        var plain = await Resource(w, new { });
        var gated = await Resource(w, new { needsApproval = true });
        var s = At(w.Clock, 60);
        var a = IdOf(await Ok(await w.Requester.Post("/api/bookings", Booking(room, s, s.AddHours(1), "Checked in"))));
        var b = IdOf(await Ok(await w.Requester.Post("/api/bookings", Booking(room, s.AddHours(2), s.AddHours(3), "No-show"))));
        var c = IdOf(await Ok(await w.Requester.Post("/api/bookings", Booking(plain, s, s.AddHours(1), "No check-in needed"))));
        var d = IdOf(await Ok(await w.Requester.Post("/api/bookings", Booking(gated, s, s.AddHours(1), "Never approved"))));
        Assert.Equal(HttpStatusCode.Conflict, (await w.Requester.Post($"/api/bookings/{a}/check-in")).StatusCode);   // opens 15 minutes before

        // 30 minutes before: the reminder (confirmed bookings only, once)
        w.Clock.Advance(s - w.Clock.UtcNow - TimeSpan.FromMinutes(25)); w = await Relogin(w);
        var r1 = await Run(w); Assert.Equal(2, r1.Reminded);   // a and c; b starts later, d waits for approval
        Assert.Equal(0, (await Run(w)).Reminded);
        Assert.Equal(2, await Notes(w, w.RequesterUser.Id, "booking.reminder"));

        // 10 minutes before: check in to a
        w.Clock.Advance(TimeSpan.FromMinutes(15)); w = await Relogin(w);
        var checkedIn = await Ok(await w.Requester.Post($"/api/bookings/{a}/check-in"));
        Assert.NotEqual(JsonValueKind.Null, checkedIn.GetProperty("checkedInAt").ValueKind);
        Assert.Equal(HttpStatusCode.Conflict, (await w.Requester.Post($"/api/bookings/{a}/check-in")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await w.Requester.Post($"/api/bookings/{c}/check-in")).StatusCode);   // no check-in on that resource

        // 20 minutes after the start: d was never approved, a stays; then b's start passes without a check-in and it is released
        w.Clock.Advance(TimeSpan.FromMinutes(30)); w = await Relogin(w);
        var r2 = await Run(w); Assert.Equal(1, r2.Expired); Assert.Equal(0, r2.Released);
        Assert.Equal("rejected", Status(await Ok(await w.Requester.Get($"/api/bookings/{d}"))));
        Assert.Equal("confirmed", Status(await Ok(await w.Requester.Get($"/api/bookings/{a}"))));
        w.Clock.Advance(TimeSpan.FromMinutes(120)); w = await Relogin(w);   // b started 20 minutes ago
        var r3 = await Run(w); Assert.Equal(1, r3.Released);
        Assert.Equal("released", Status(await Ok(await w.Requester.Get($"/api/bookings/{b}"))));
        Assert.Equal(0, (await Run(w)).Released);
        // the released time can be booked again, and the summary counts the no-show
        var now = At(w.Clock, 0).AddMinutes(-5);
        await Ok(await w.Other.Post("/api/bookings", Booking(room, now, s.AddHours(3), "Walk-in")));
        Assert.Equal(1, (await Ok(await w.Supervisor.Get("/api/bookings/summary"))).GetProperty("noShows7").GetInt32());
    }

    [Fact]
    public async Task Repeating_bookings_skip_clashes_and_cancel_from_one_onwards()
    {
        var w = await Build();
        var room = await Resource(w, new { });
        var s = At(w.Clock, 60 * 24 * 2);
        // someone already has the third week
        await Ok(await w.Other.Post("/api/bookings", Booking(room, s.AddDays(14).AddMinutes(30), s.AddDays(14).AddMinutes(90), "Taken")));
        var series = await Ok(await w.Requester.Post("/api/bookings/series", new { resourceId = room, startsAt = s, endsAt = s.AddHours(1), title = "Weekly sync", every = "week", count = 4 }));
        Assert.Equal(3, series.GetProperty("made").GetArrayLength());
        var skipped = Assert.Single(series.GetProperty("skipped").EnumerateArray());
        Assert.Equal(s.AddDays(14), skipped.GetProperty("startsAt").GetDateTimeOffset());
        Assert.Contains("taken", skipped.GetProperty("reason").GetString(), StringComparison.OrdinalIgnoreCase);
        var made = series.GetProperty("made").EnumerateArray().ToList();
        Assert.All(made, m => Assert.Equal("week", m.GetProperty("repeat").GetString()));

        // cancel from the second one onwards: the first stays
        await Ok(await w.Requester.Post($"/api/bookings/{IdOf(made[1])}/cancel", new { scope = "following" }));
        Assert.Equal("confirmed", Status(await Ok(await w.Requester.Get($"/api/bookings/{IdOf(made[0])}"))));
        Assert.Equal("cancelled", Status(await Ok(await w.Requester.Get($"/api/bookings/{IdOf(made[1])}"))));
        Assert.Equal("cancelled", Status(await Ok(await w.Requester.Get($"/api/bookings/{IdOf(made[2])}"))));

        // weekdays until a date; limits
        var weekdays = await Ok(await w.Requester.Post("/api/bookings/series", new { resourceId = room, startsAt = s.AddHours(3), endsAt = s.AddHours(4), title = "Daily stand-up", every = "weekday", until = DateOnly.FromDateTime(s.AddDays(13).UtcDateTime).ToString("yyyy-MM-dd") }));
        Assert.All(weekdays.GetProperty("made").EnumerateArray(), m => Assert.DoesNotContain(m.GetProperty("startsAt").GetDateTimeOffset().ToOffset(TimeSpan.FromHours(7)).DayOfWeek, new[] { DayOfWeek.Saturday, DayOfWeek.Sunday }));
        Assert.InRange(weekdays.GetProperty("made").GetArrayLength(), 9, 10);
        Assert.True((await Fields(await w.Requester.Post("/api/bookings/series", new { resourceId = room, startsAt = s.AddHours(5), endsAt = s.AddHours(6), title = "Too many", every = "day", count = 60 }))).TryGetProperty("count", out _));
        Assert.True((await Fields(await w.Requester.Post("/api/bookings/series", new { resourceId = room, startsAt = s.AddHours(5), endsAt = s.AddHours(6), title = "No end", every = "day" }))).TryGetProperty("until", out _));
        Assert.True((await Fields(await w.Requester.Post("/api/bookings/series", new { resourceId = room, startsAt = s.AddHours(5), endsAt = s.AddHours(6), title = "Odd", every = "month", count = 3 }))).TryGetProperty("every", out _));
    }

    [Fact]
    public async Task The_calendar_feed_is_a_private_revocable_link()
    {
        var w = await Build();
        var room = await Resource(w, new { name = "Feed room, east; wing" });
        var s = At(w.Clock, 60 * 24 * 4);
        await Ok(await w.Requester.Post("/api/bookings", new { resourceId = room, startsAt = s, endsAt = s.AddHours(1), title = "Planning, Q1", note = "Bring the numbers" }));
        Assert.False((await Ok(await w.Requester.Get("/api/bookings/feed"))).GetProperty("active").GetBoolean());
        var url = (await Ok(await w.Requester.Post("/api/bookings/feed"))).GetProperty("url").GetString()!;
        var path = new Uri(url).AbsolutePath;
        Assert.StartsWith("/api/calendar/", path); Assert.EndsWith(".ics", path);

        var anon = TestBrowser.Create(w.F);
        var r = await anon.Get(path);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("text/calendar", r.Content.Headers.ContentType!.MediaType);
        var ics = await r.Content.ReadAsStringAsync();
        Assert.StartsWith("BEGIN:VCALENDAR\r\n", ics);
        Assert.Contains("SUMMARY:Planning\\, Q1 · Feed room\\, east\\; wing", ics.Replace("\r\n ", ""));   // escaped, unfolded
        Assert.Contains($"DTSTART:{s.UtcDateTime:yyyyMMdd'T'HHmmss'Z'}", ics);
        Assert.All(ics.Split("\r\n"), line => Assert.True(System.Text.Encoding.UTF8.GetByteCount(line) <= 75, line));
        Assert.True((await Ok(await w.Requester.Get("/api/bookings/feed"))).GetProperty("active").GetBoolean());

        // a new link retires the old one; revoking ends it; nonsense is a 404
        var url2 = (await Ok(await w.Requester.Post("/api/bookings/feed"))).GetProperty("url").GetString()!;
        Assert.Equal(HttpStatusCode.NotFound, (await anon.Get(path)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anon.Get(new Uri(url2).AbsolutePath)).StatusCode);
        await Ok(await w.Requester.SendAsync(HttpMethod.Delete, "/api/bookings/feed"));
        Assert.Equal(HttpStatusCode.NotFound, (await anon.Get(new Uri(url2).AbsolutePath)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anon.Get("/api/calendar/short.ics")).StatusCode);
        // a person's feed holds only their own bookings
        var otherUrl = (await Ok(await w.Other.Post("/api/bookings/feed"))).GetProperty("url").GetString()!;
        Assert.DoesNotContain("Planning", await (await anon.Get(new Uri(otherUrl).AbsolutePath)).Content.ReadAsStringAsync());
    }
}
