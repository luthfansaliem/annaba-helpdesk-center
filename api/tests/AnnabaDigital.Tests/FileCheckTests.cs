using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Infrastructure;
using AnnabaDigital.Tests.Infra;
using Xunit;

namespace AnnabaDigital.Tests;

/// <summary>Files versus database (restore drill support): what is missing from the volume, which bytes nothing refers to, and removing only those that are certainly unused.</summary>
[Collection("db")]
public sealed class FileCheckTests(PostgresFixture fx)
{
    private static string Uniq() => Guid.NewGuid().ToString("N")[..8];
    private static async Task<JsonElement> Ok(HttpResponseMessage r) { Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync()); return await r.Body(); }
    private static Guid IdOf(JsonElement e) => e.GetProperty("id").GetGuid();
    private static byte[] Png() { var b = new byte[64]; new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(b, 0); Random.Shared.NextBytes(b.AsSpan(8)); return b; }

    private sealed record World(ApiFactory F, TestClock Clock, Seed Seed, TestBrowser Admin, TestBrowser Requester, TestBrowser Supervisor, Guid ServiceId);

    private async Task<World> Build()
    {
        var iso = await fx.NewIsolatedAsync(); var seed = new Seed(iso.Factory, iso.Clock);
        async Task<TestBrowser> Person(string role) { var u = await seed.UserAsync(role: role); var b = TestBrowser.Create(iso.Factory); await b.LoginAsync(u.Email, Seed.Password); return b; }
        var admin = await Person("super_admin"); var requester = await Person("user"); var sup = await Person("ga_supervisor");
        await admin.Post("/api/auth/reauth", new { password = Seed.Password });
        var team = IdOf(await Ok(await admin.Post("/api/teams", new { name = $"Team {Uniq()}" })));
        var cat = IdOf(await Ok(await admin.Post("/api/catalog/categories", new { name = $"Cat {Uniq()}" })));
        var svc = IdOf(await Ok(await admin.Post("/api/catalog/services", new { name = $"Svc {Uniq()}", categoryId = cat, owningTeamId = team })));
        await Ok(await admin.Post($"/api/catalog/services/{svc}/publish"));
        return new World(iso.Factory, iso.Clock, seed, admin, requester, sup, svc);
    }

    private static async Task<(Guid Ticket, Guid File, string Key)> TicketWithFile(World w, string name)
    {
        var t = IdOf(await Ok(await w.Requester.Post($"/api/portal/services/{w.ServiceId}/requests", new { subject = "With file", answers = new { } })));
        var f = IdOf(await Ok(await w.Requester.UploadAsync($"/api/tickets/{t}/attachments", name, Png(), "image/png")));
        var key = await w.Seed.Db(db => db.Attachments.Where(a => a.Id == f).Select(a => a.StorageKey).SingleAsync());
        return (t, f, key);
    }
    private static string PathOf(World w, string key) => Path.Combine(Path.GetFullPath(w.F.Services.GetRequiredService<IOptions<StorageOptions>>().Value.Path), key.Replace('/', Path.DirectorySeparatorChar));
    /// <summary>Puts bytes on the volume under a key of this organisation (or another folder) as if left behind, with the given age.</summary>
    private static async Task<string> Stray(World w, string folder, int minutesOld, int size = 10)
    {
        var key = $"{folder}/{Guid.NewGuid():N}"; var path = PathOf(w, key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); await File.WriteAllBytesAsync(path, new byte[size]);
        File.SetLastWriteTimeUtc(path, w.Clock.UtcNow.UtcDateTime.AddMinutes(-minutesOld));
        return key;
    }
    private static string MyFolder(string key) => key.Split('/')[0];

    [Fact]
    public async Task A_consistent_volume_reports_nothing_and_only_retention_managers_may_look()
    {
        var w = await Build(); var (_, _, _) = await TicketWithFile(w, "a.png");
        var c = await Ok(await w.Admin.Get("/api/retention/files"));
        Assert.Equal(1, c.GetProperty("files").GetInt32()); Assert.Equal(0, c.GetProperty("missing").GetInt32()); Assert.Equal(0, c.GetProperty("orphans").GetInt32());
        foreach (var b in new[] { w.Supervisor, w.Requester })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await b.Get("/api/retention/files")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await b.Post("/api/retention/files/clean")).StatusCode);
        }
    }

    [Fact]
    public async Task Files_whose_bytes_are_gone_are_listed_with_their_ticket_as_after_restoring_a_database_without_its_volume()
    {
        var w = await Build(); var (ticket, _, key) = await TicketWithFile(w, "lost.png"); await TicketWithFile(w, "kept.png");
        File.Delete(PathOf(w, key));
        var c = await Ok(await w.Admin.Get("/api/retention/files"));
        Assert.Equal(2, c.GetProperty("files").GetInt32()); Assert.Equal(1, c.GetProperty("missing").GetInt32());
        var m = c.GetProperty("missingSample")[0]; Assert.Equal("lost.png", m.GetProperty("fileName").GetString()); Assert.Equal(ticket, m.GetProperty("ticketId").GetGuid()); Assert.StartsWith("GA-", m.GetProperty("number").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await w.Requester.Get($"/api/tickets/{ticket}/attachments/{m.GetProperty("attachmentId").GetGuid()}")).StatusCode);   // downloads answer "not found", not an error
    }

    [Fact]
    public async Task Unreferenced_bytes_are_found_and_removed_but_recent_files_and_other_organisations_are_left_alone()
    {
        var w = await Build(); var (_, file, key) = await TicketWithFile(w, "live.png"); var mine = MyFolder(key);
        var oldStray = await Stray(w, mine, 180, 100);                                  // left behind long ago
        var recent = await Stray(w, mine, 5, 100);                                      // could be an upload in flight
        var foreign = await Stray(w, Guid.NewGuid().ToString("N"), 180, 100);           // another organisation's folder
        var (_, delFile, delKey) = await TicketWithFile(w, "removed.png");
        File.SetLastWriteTimeUtc(PathOf(w, delKey), w.Clock.UtcNow.UtcDateTime.AddMinutes(-180));
        await w.Seed.Db(async db => await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE attachments SET deleted_at = {w.Clock.UtcNow} WHERE id = {delFile}"));   // row marked removed, bytes still there

        var c = await Ok(await w.Admin.Get("/api/retention/files"));
        Assert.Equal(1, c.GetProperty("files").GetInt32()); Assert.Equal(2, c.GetProperty("orphans").GetInt32()); Assert.Equal(100 + new FileInfo(PathOf(w, delKey)).Length, c.GetProperty("orphanBytes").GetInt64());

        var clean = await Ok(await w.Admin.Post("/api/retention/files/clean"));
        Assert.Equal(2, clean.GetProperty("removed").GetInt32()); Assert.Equal(0, clean.GetProperty("failed").GetInt32());
        Assert.False(File.Exists(PathOf(w, oldStray))); Assert.False(File.Exists(PathOf(w, delKey)));
        Assert.True(File.Exists(PathOf(w, recent))); Assert.True(File.Exists(PathOf(w, foreign))); Assert.True(File.Exists(PathOf(w, key)));      // live, recent and foreign files stay
        Assert.Equal(HttpStatusCode.OK, (await w.Requester.Get($"/api/tickets/{(await w.Seed.Db(db => db.Attachments.Where(a => a.Id == file).Select(a => a.TicketId).SingleAsync()))}/attachments/{file}")).StatusCode);
        var again = await Ok(await w.Admin.Get("/api/retention/files")); Assert.Equal(0, again.GetProperty("orphans").GetInt32());
        var rec = await w.Seed.Db(db => db.AuditEvents.Where(a => a.Action == "files.orphans.clean").ToListAsync()); Assert.Single(rec);
    }

    [Fact]
    public async Task Cleaning_needs_a_fresh_password_check()
    {
        var w = await Build(); var other = await w.Seed.UserAsync(role: "super_admin"); var b = TestBrowser.Create(w.F); await b.LoginAsync(other.Email, Seed.Password);
        var r = await b.Post("/api/retention/files/clean"); Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode); Assert.Equal("reauth_required", await r.ErrorCode());
    }
}
