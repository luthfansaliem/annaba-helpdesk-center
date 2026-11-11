using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using AnnabaDigital.Application.Helpdesk;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Tests.Infra;
using Xunit;

namespace AnnabaDigital.Tests;

/// <summary>Vehicles (R3-3: BKG-004, RPT-005): the trip of a vehicle booking (asking for and assigning a driver, licence checks, pick-up and return with the odometer,
/// costs, checklist and damage, returning early), trip costs in the cost report, vehicle documents and driver licences with expiry reminders, and the
/// utilisation report.</summary>
[Collection("db")]
public sealed class VehicleTests(PostgresFixture fx)
{
    private static string Uniq() => Guid.NewGuid().ToString("N")[..8];
    private const HttpStatusCode Invalid = (HttpStatusCode)422;
    private static async Task<JsonElement> Ok(HttpResponseMessage r) { Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync()); return await r.Body(); }
    private static Guid IdOf(JsonElement e) => e.GetProperty("id").GetGuid();
    private static async Task<JsonElement> Fields(HttpResponseMessage r) { Assert.Equal(Invalid, r.StatusCode); return (await r.Body()).GetProperty("error").GetProperty("fields"); }

    private sealed record World(ApiFactory F, TestClock Clock, Seed Seed, TestBrowser Manager, User ManagerUser, TestBrowser Supervisor, TestBrowser Requester, User RequesterUser, User Driver, User OldDriver);
    private async Task<World> Build()
    {
        var iso = await fx.NewIsolatedAsync(); var f = iso.Factory; var clock = iso.Clock;
        var seed = new Seed(f, clock);
        async Task<(User, TestBrowser)> Person(string role) { var u = await seed.UserAsync(role: role); var b = TestBrowser.Create(f); await b.LoginAsync(u.Email, Seed.Password); return (u, b); }
        var (mu, mgr) = await Person("ga_manager"); var (_, sup) = await Person("ga_supervisor"); var (ru, req) = await Person("user");
        var driver = await seed.UserAsync(role: "user"); var old = await seed.UserAsync(role: "user");
        return new World(f, clock, seed, mgr, mu, sup, req, ru, driver, old);
    }
    private static async Task<Guid> Car(World w, string name = "Innova") => IdOf(await Ok(await w.Manager.Post("/api/resources", new { kind = "vehicle", name = $"{name} {Uniq()}", code = "B " + Uniq(), capacity = 6, openDays = 127, bufferMinutes = 0 })));
    private static DateTimeOffset Step(DateTimeOffset t, int plusMinutes) { var x = t.AddMinutes(plusMinutes); return new DateTimeOffset(x.Year, x.Month, x.Day, x.Hour, x.Minute - x.Minute % 5, 0, TimeSpan.Zero).AddMinutes(5); }
    private static Dictionary<string, bool> AllOk() => VehicleRules.ReturnChecklist.ToDictionary(i => i, _ => true);

    [Fact]
    public async Task A_trip_is_driven_picked_up_returned_and_costed()
    {
        var w = await Build();
        var car = await Car(w);
        var s = Step(w.Clock.UtcNow, 5);
        var booking = IdOf(await Ok(await w.Requester.Post("/api/bookings", new { resourceId = car, startsAt = s, endsAt = s.AddHours(8), title = "Site visit Bandung", attendees = 3 })));

        // the requester asks General Affairs for a driver
        var trip = await Ok(await w.Requester.SendAsync(HttpMethod.Put, $"/api/bookings/{booking}/trip", new { destination = "Bandung office", purpose = "Inspect the new branch", needsDriver = true }));
        Assert.True(trip.GetProperty("needsDriver").GetBoolean()); Assert.True(trip.GetProperty("canPickUp").GetBoolean()); Assert.False(trip.GetProperty("canAssignDriver").GetBoolean());
        Assert.True((await Fields(await w.Requester.SendAsync(HttpMethod.Put, $"/api/bookings/{booking}/trip", new { destination = "Bandung office", driverId = w.Driver.Id }))).TryGetProperty("driverId", out _));
        // licences: recorded by e-mail; an expired one is refused for the trip
        await Ok(await w.Manager.Post("/api/drivers/licences", new { email = w.Driver.Email, number = "SIM-123", @class = "a", expiresOn = DateOnly.FromDateTime(w.Clock.UtcNow.UtcDateTime.AddYears(2)) }));
        await Ok(await w.Manager.Post("/api/drivers/licences", new { email = w.OldDriver.Email, number = "SIM-OLD", expiresOn = DateOnly.FromDateTime(w.Clock.UtcNow.UtcDateTime.AddDays(-3)) }));
        Assert.True((await Fields(await w.Manager.Post("/api/drivers/licences", new { email = "nobody@nowhere.test", number = "X", expiresOn = "2030-01-01" }))).TryGetProperty("email", out _));
        var drivers = await Ok(await w.Supervisor.Get("/api/drivers"));
        Assert.Equal(w.Driver.Id, drivers[0].GetProperty("userId").GetGuid());                                   // valid licences first
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Requester.Get("/api/drivers")).StatusCode);
        Assert.Contains("expired", (await Fields(await w.Supervisor.SendAsync(HttpMethod.Put, $"/api/bookings/{booking}/trip", new { destination = "Bandung office", driverId = w.OldDriver.Id }))).GetProperty("driverId").GetString());
        trip = await Ok(await w.Supervisor.SendAsync(HttpMethod.Put, $"/api/bookings/{booking}/trip", new { destination = "Bandung office", purpose = "Inspect the new branch", driverId = w.Driver.Id }));
        Assert.False(trip.GetProperty("needsDriver").GetBoolean()); Assert.Equal(w.Driver.Id, trip.GetProperty("driverId").GetGuid());
        Assert.Equal(1, await w.Seed.Db(db => db.Notifications.IgnoreQueryFilters().CountAsync(n => n.UserId == w.Driver.Id && n.Title.StartsWith("You drive"))));

        // pick-up and return with every rule
        Assert.True((await Fields(await w.Requester.Post($"/api/bookings/{booking}/trip/pick-up", new { }))).TryGetProperty("odometerOut", out _));
        Assert.Equal(HttpStatusCode.Conflict, (await w.Requester.Post($"/api/bookings/{booking}/trip/return", new { odometerIn = 10 })).StatusCode);   // not picked up yet
        trip = await Ok(await w.Requester.Post($"/api/bookings/{booking}/trip/pick-up", new { odometerOut = 15000 }));
        Assert.True(trip.GetProperty("canReturn").GetBoolean());
        Assert.NotEqual(JsonValueKind.Null, (await Ok(await w.Requester.Get($"/api/bookings/{booking}"))).GetProperty("checkedInAt").ValueKind);   // picking up checks in
        Assert.Equal(HttpStatusCode.Conflict, (await w.Requester.Post($"/api/bookings/{booking}/trip/pick-up", new { odometerOut = 15000 })).StatusCode);
        Assert.True((await Fields(await w.Requester.Post($"/api/bookings/{booking}/trip/return", new { odometerIn = 14000, checklist = AllOk() }))).TryGetProperty("odometerIn", out _));
        Assert.True((await Fields(await w.Requester.Post($"/api/bookings/{booking}/trip/return", new { odometerIn = 15300 }))).TryGetProperty("checklist", out _));
        var damaged = AllOk(); damaged["no_new_damage"] = false;
        Assert.True((await Fields(await w.Requester.Post($"/api/bookings/{booking}/trip/return", new { odometerIn = 15300, checklist = damaged }))).TryGetProperty("damageNotes", out _));
        Assert.True((await Fields(await w.Requester.Post($"/api/bookings/{booking}/trip/return", new { odometerIn = 15300, fuelCost = -5, checklist = AllOk() }))).TryGetProperty("fuelCost", out _));
        w.Clock.Advance(TimeSpan.FromHours(3));
        w = w with { Requester = TestBrowser.Create(w.F), Supervisor = TestBrowser.Create(w.F), Manager = TestBrowser.Create(w.F) };
        await w.Requester.LoginAsync(w.RequesterUser.Email, Seed.Password); await w.Manager.LoginAsync(w.ManagerUser.Email, Seed.Password);
        var supUser = await w.Seed.UserAsync(role: "ga_supervisor"); await w.Supervisor.LoginAsync(supUser.Email, Seed.Password);
        trip = await Ok(await w.Requester.Post($"/api/bookings/{booking}/trip/return", new { odometerIn = 15320, fuelCost = 250000, tollCost = 85000, parkingCost = 15000, damageNotes = "Scratch on the rear bumper", checklist = damaged }));
        Assert.Equal(320, trip.GetProperty("distance").GetInt32()); Assert.Equal(350000m, trip.GetProperty("cost").GetDecimal());
        Assert.False(trip.GetProperty("canReturn").GetBoolean());
        Assert.Contains(trip.GetProperty("checklist").EnumerateArray(), c => c.GetProperty("item").GetString() == "no_new_damage" && !c.GetProperty("ok").GetBoolean());
        // back early: the booking now ends at the next 5-minute step, so the car is free again
        var after = await Ok(await w.Requester.Get($"/api/bookings/{booking}"));
        Assert.True(after.GetProperty("endsAt").GetDateTimeOffset() <= w.Clock.UtcNow.AddMinutes(5));
        Assert.Equal(HttpStatusCode.Conflict, (await w.Requester.SendAsync(HttpMethod.Put, $"/api/bookings/{booking}/trip", new { destination = "Elsewhere" })).StatusCode);   // a returned trip is final

        // the next trip cannot start below the last reading
        var s2 = Step(w.Clock.UtcNow, 10);
        var next = IdOf(await Ok(await w.Requester.Post("/api/bookings", new { resourceId = car, startsAt = s2, endsAt = s2.AddHours(1), title = "Bank run" })));
        Assert.Contains("15,320", (await Fields(await w.Requester.Post($"/api/bookings/{next}/trip/pick-up", new { odometerOut = 15100 }))).GetProperty("odometerOut").GetString());
        Assert.Equal(15320, (await Ok(await w.Requester.Get($"/api/bookings/{next}/trip"))).GetProperty("lastOdometer").GetInt32());

        // the trip's costs appear in the cost report as their own kind; damage alerted General Affairs
        var today = DateOnly.FromDateTime(w.Clock.UtcNow.UtcDateTime).ToString("yyyy-MM-dd");
        var report = await Ok(await w.Supervisor.Get($"/api/costs/report?from={today}&to={today}&by=category"));
        var row = report.GetProperty("rows").EnumerateArray().Single(r => r.GetProperty("key").GetString() == "vehicle");
        Assert.Equal("Vehicle trips", row.GetProperty("label").GetString()); Assert.Equal(350000m, row.GetProperty("vehicle").GetDecimal());
        Assert.Equal(0, row.GetProperty("workOrders").GetInt32()); Assert.Equal(350000m, report.GetProperty("total").GetProperty("total").GetDecimal());
        Assert.Contains("vehicle trips", await (await w.Supervisor.Get($"/api/costs/report.csv?from={today}&to={today}")).Content.ReadAsStringAsync());
        Assert.True(await w.Seed.Db(db => db.Notifications.IgnoreQueryFilters().AnyAsync(n => n.Kind == "vehicle.alert" && n.Title.StartsWith("Damage reported"))));
        // a room has no trip
        var room = IdOf(await Ok(await w.Manager.Post("/api/resources", new { kind = "room", name = "Room", openDays = 127 })));
        var rb = IdOf(await Ok(await w.Requester.Post("/api/bookings", new { resourceId = room, startsAt = s2, endsAt = s2.AddHours(1), title = "Meeting" })));
        Assert.Equal(HttpStatusCode.Conflict, (await w.Requester.Get($"/api/bookings/{rb}/trip")).StatusCode);
    }

    [Fact]
    public async Task Saving_a_trip_and_picking_up_at_the_same_moment_never_fails_with_a_server_error()
    {
        var w = await Build();
        var car = await Car(w);
        for (var round = 0; round < 3; round++)
        {
            var requester = w.Requester;
            var s = Step(w.Clock.UtcNow, 5);   // the previous round's booking is cancelled, so the same slot is free again
            var booking = IdOf(await Ok(await requester.Post("/api/bookings", new { resourceId = car, startsAt = s, endsAt = s.AddHours(2), title = $"Race {round}" })));
            var results = await Task.WhenAll(
                requester.SendAsync(HttpMethod.Put, $"/api/bookings/{booking}/trip", new { destination = "Bogor" }),
                requester.Post($"/api/bookings/{booking}/trip/pick-up", new { odometerOut = 1000 + round * 100 }));
            Assert.All(results, r => Assert.True(r.IsSuccessStatusCode || r.StatusCode == HttpStatusCode.Conflict, $"{(int)r.StatusCode} {r.Content.ReadAsStringAsync().Result}"));
            Assert.Equal(1, await w.Seed.Db(db => db.VehicleTrips.IgnoreQueryFilters().CountAsync(t => t.BookingId == booking)));
            await requester.Post($"/api/bookings/{booking}/cancel", new { });
        }
    }

    [Fact]
    public async Task Vehicle_documents_and_licences_remind_before_and_when_they_expire()
    {
        var w = await Build();
        var car = await Car(w);
        var today = DateOnly.FromDateTime(w.Clock.UtcNow.UtcDateTime);
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Requester.Get($"/api/resources/{car}/documents")).StatusCode);
        Assert.True((await Fields(await w.Manager.Post($"/api/resources/{car}/documents", new { kind = "passport" }))).TryGetProperty("expiresOn", out _));
        var docs = await Ok(await w.Manager.Post($"/api/resources/{car}/documents", new { kind = "registration", number = "STNK 1234", expiresOn = today.AddDays(10).ToString("yyyy-MM-dd") }));
        docs = await Ok(await w.Manager.Post($"/api/resources/{car}/documents", new { kind = "insurance", number = "POL-77", expiresOn = today.AddDays(-1).ToString("yyyy-MM-dd") }));
        docs = await Ok(await w.Manager.Post($"/api/resources/{car}/documents", new { kind = "inspection", expiresOn = today.AddDays(200).ToString("yyyy-MM-dd") }));
        Assert.Equal(["expired", "soon", "ok"], docs.EnumerateArray().Select(d => d.GetProperty("state").GetString()).ToArray());
        await Ok(await w.Manager.Post("/api/drivers/licences", new { email = w.Driver.Email, number = "SIM-9", expiresOn = today.AddDays(20).ToString("yyyy-MM-dd") }));

        async Task<int> Run() { using var s = w.F.Services.CreateScope(); return await s.ServiceProvider.GetRequiredService<VehicleService>().RunExpiryRemindersAsync(); }
        Assert.Equal(3, await Run());                                              // registration soon, insurance expired, licence soon
        Assert.Equal(0, await Run());
        Assert.Equal(1, await w.Seed.Db(db => db.Notifications.IgnoreQueryFilters().CountAsync(n => n.UserId == w.Driver.Id && n.Kind == "resource.document")));   // the driver hears about their own licence
        Assert.True(await w.Seed.Db(db => db.Notifications.IgnoreQueryFilters().AnyAsync(n => n.UserId == w.ManagerUser.Id && n.Title.Contains("insurance has expired"))));

        // the day it expires the "soon" reminder becomes "expired" once; a new date starts over
        w.Clock.Advance(TimeSpan.FromDays(11));
        Assert.Equal(1, await Run());                                              // the registration expired
        var reg = (await w.Seed.Db(db => db.ResourceDocuments.IgnoreQueryFilters().FirstAsync(d => d.Kind == ResourceDocumentKind.Registration && d.ResourceId == car)));
        var m2 = TestBrowser.Create(w.F); await m2.LoginAsync(w.ManagerUser.Email, Seed.Password);
        await Ok(await m2.SendAsync(HttpMethod.Put, $"/api/resources/{car}/documents/{reg.Id}", new { kind = "registration", number = "STNK 1234", expiresOn = today.AddYears(5).ToString("yyyy-MM-dd") }));
        Assert.Null(await w.Seed.Db(db => db.ResourceDocuments.IgnoreQueryFilters().Where(d => d.Id == reg.Id).Select(d => d.Reminded).FirstAsync()));
        var left = await Ok(await m2.SendAsync(HttpMethod.Delete, $"/api/resources/{car}/documents/{reg.Id}"));
        Assert.Equal(2, left.GetArrayLength());
    }

    [Fact]
    public async Task The_utilisation_report_measures_booked_hours_against_opening_hours()
    {
        var w = await Build();
        var room = IdOf(await Ok(await w.Manager.Post("/api/resources", new { kind = "room", name = "Utilised", openDays = 127 })));
        var car = await Car(w);
        // a booking that already happened: made now, then the clock moves past it
        var s = Step(w.Clock.UtcNow, 30);
        await Ok(await w.Requester.Post("/api/bookings", new { resourceId = room, startsAt = s, endsAt = s.AddHours(2), title = "Workshop" }));
        var gone = IdOf(await Ok(await w.Requester.Post("/api/bookings", new { resourceId = room, startsAt = s.AddHours(3), endsAt = s.AddHours(4), title = "Cancelled one" })));
        await Ok(await w.Requester.Post($"/api/bookings/{gone}/cancel", new { }));
        w.Clock.Advance(TimeSpan.FromHours(6));
        var sup = TestBrowser.Create(w.F); var su = await w.Seed.UserAsync(role: "ga_supervisor"); await sup.LoginAsync(su.Email, Seed.Password);
        var req = TestBrowser.Create(w.F); await req.LoginAsync(w.RequesterUser.Email, Seed.Password);
        Assert.Equal(HttpStatusCode.Forbidden, (await req.Get("/api/reports/utilisation")).StatusCode);
        var r = await Ok(await sup.Get("/api/reports/utilisation?days=7"));
        Assert.Equal(7, r.GetProperty("days").GetInt32());
        var row = r.GetProperty("rows").EnumerateArray().Single(x => x.GetProperty("resourceId").GetGuid() == room);
        Assert.Equal(2.0, row.GetProperty("bookedHours").GetDouble());
        Assert.Equal(168.0, row.GetProperty("openHours").GetDouble());                        // around the clock for the 7 days
        Assert.Equal(1, row.GetProperty("bookings").GetInt32()); Assert.Equal(1, row.GetProperty("cancelled").GetInt32());
        Assert.Contains(r.GetProperty("rows").EnumerateArray(), x => x.GetProperty("resourceId").GetGuid() == car && x.GetProperty("kind").GetString() == "vehicle");
        var rooms = await Ok(await sup.Get("/api/reports/utilisation?kind=room"));
        Assert.All(rooms.GetProperty("rows").EnumerateArray(), x => Assert.Equal("room", x.GetProperty("kind").GetString()));
        Assert.Equal(2.0, rooms.GetProperty("total").GetProperty("bookedHours").GetDouble());
    }
}
