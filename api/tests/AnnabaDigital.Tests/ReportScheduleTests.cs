using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using AnnabaDigital.Application.Helpdesk;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Tests.Infra;
using Xunit;

namespace AnnabaDigital.Tests;

/// <summary>Report exports and schedules (R3-8: RPT-007): each report as CSV or XLSX, permission-checked for the person it is built for, formula-safe and audited;
/// schedules e-mail the file to people who may see it, skip anyone who lost access and pause when the owner did.</summary>
[Collection("db")]
public sealed class ReportScheduleTests(PostgresFixture fx)
{
    private const HttpStatusCode Invalid = (HttpStatusCode)422;
    private static async Task<JsonElement> Ok(HttpResponseMessage r) { Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync()); return await r.Body(); }
    private static Guid IdOf(JsonElement e) => e.GetProperty("id").GetGuid();
    private static async Task<JsonElement> Fields(HttpResponseMessage r) { Assert.Equal(Invalid, r.StatusCode); return (await r.Body()).GetProperty("error").GetProperty("fields"); }
    private static string Sheet(byte[] xlsx)
    {
        using var zip = new ZipArchive(new MemoryStream(xlsx));
        using var r = new StreamReader(zip.GetEntry("xl/worksheets/sheet1.xml")!.Open());
        return r.ReadToEnd();
    }

    [Fact]
    public async Task Reports_export_as_csv_or_xlsx_only_for_people_who_may_see_them()
    {
        var iso = await fx.NewIsolatedAsync(); var f = iso.Factory; var clock = iso.Clock; var seed = new Seed(f, clock);
        async Task<(User, TestBrowser)> Person(string role) { var u = await seed.UserAsync(role: role); var b = TestBrowser.Create(f); await b.LoginAsync(u.Email, Seed.Password); return (u, b); }
        var (_, ga) = await Person("ga_manager"); var (_, req) = await Person("user"); var (_, admin) = await Person("super_admin");
        var store = IdOf(await Ok(await admin.Post("/api/locations", new { type = "site", code = "ST-" + Guid.NewGuid().ToString("N")[..4], name = "Store room" })));
        var item = IdOf(await Ok(await ga.Post("/api/inventory/items", new { sku = "ATK-EVIL", name = "=HYPERLINK(\"http://x\",\"pens\")", category = "atk", unit = "pcs", reorderLevel = 5, unitCost = 2500 })));
        await Ok(await ga.Post($"/api/inventory/items/{item}/moves", new { kind = "receipt", locationId = store, quantity = 4 }));

        // what each person may export
        Assert.Equal(0, (await Ok(await req.Get("/api/reports/catalog"))).GetArrayLength());
        var cat = (await Ok(await ga.Get("/api/reports/catalog"))).EnumerateArray().Select(k => k.GetProperty("key").GetString()).ToList();
        Assert.Contains("inventory", cat); Assert.Contains("tickets", cat); Assert.Contains("costs", cat);
        Assert.Equal(HttpStatusCode.Forbidden, (await req.Get("/api/reports/export?report=inventory")).StatusCode);
        Assert.True((await Fields(await ga.Get("/api/reports/export?report=nope"))).TryGetProperty("report", out _));
        Assert.True((await Fields(await ga.Get("/api/reports/export?report=inventory&format=pdf"))).TryGetProperty("format", out _));

        // CSV: UTF-8 with BOM, quoted, and a formula kept as text
        var csv = await ga.Get("/api/reports/export?report=inventory&format=csv");
        Assert.Equal(HttpStatusCode.OK, csv.StatusCode);
        Assert.StartsWith("text/csv", csv.Content.Headers.ContentType!.ToString());
        var bytes = await csv.Content.ReadAsByteArrayAsync();
        Assert.Equal(Encoding.UTF8.GetPreamble(), bytes[..3]);
        var text = Encoding.UTF8.GetString(bytes[3..]);
        Assert.StartsWith("SKU,Item,Category,Place,Quantity", text);
        Assert.Contains("\"'=HYPERLINK(\"\"http://x\"\",\"\"pens\"\")\"", text);
        Assert.Contains("Store room,4,pcs,5,yes,2500", text);
        Assert.Equal("1", csv.Headers.GetValues("X-Report-Rows").Single());

        // XLSX: a real workbook (a zip with one sheet) holding the same rows
        var xlsx = await ga.Get("/api/reports/export?report=inventory&format=xlsx");
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", xlsx.Content.Headers.ContentType!.MediaType);
        var sheet = Sheet(await xlsx.Content.ReadAsByteArrayAsync());
        Assert.Contains("ATK-EVIL", sheet); Assert.Contains("'=HYPERLINK", sheet); Assert.Contains("<v>4</v>", sheet);

        // other reports build too (empty organisation: headers and totals only)
        foreach (var k in new[] { "tickets", "sla", "csat", "costs", "compliance", "utilisation" })
            Assert.Equal(HttpStatusCode.OK, (await ga.Get($"/api/reports/export?report={k}&days=30&format=csv")).StatusCode);
        Assert.True(await seed.Db(db => db.AuditEvents.IgnoreQueryFilters().AnyAsync(a => a.Action == "report.export" && a.TargetId == "inventory")));
    }

    [Fact]
    public async Task Schedules_mail_the_file_to_people_who_may_see_it_and_pause_when_the_owner_cannot()
    {
        var iso = await fx.NewIsolatedAsync(); var f = iso.Factory; var clock = iso.Clock; var seed = new Seed(f, clock);
        async Task<(User, TestBrowser)> Person(string role) { var u = await seed.UserAsync(role: role); var b = TestBrowser.Create(f); await b.LoginAsync(u.Email, Seed.Password); return (u, b); }
        var (gu, ga) = await Person("ga_manager"); var (au, _) = await Person("ga_agent"); var (ru, req) = await Person("user"); var (_, other) = await Person("ga_manager");
        var mails = f.Services.GetRequiredService<InMemoryEmailSender>();

        // validation: a report the owner sees, people who exist and may see it
        var bad = await Fields(await ga.Post("/api/report-schedules", new { name = "", report = "inventory", format = "pdf", frequency = "hourly", hour = 25, recipients = Array.Empty<string>() }));
        foreach (var k in new[] { "name", "format", "frequency", "hour", "recipients" }) Assert.True(bad.TryGetProperty(k, out _), k);
        Assert.Contains(ru.Email, (await Fields(await ga.Post("/api/report-schedules", new { name = "Stock", report = "inventory", recipients = new[] { au.Email, ru.Email } })))
            .GetProperty("recipients").GetString());
        Assert.True((await Fields(await ga.Post("/api/report-schedules", new { name = "Stock", report = "inventory", recipients = new[] { "nobody@example.com" } }))).TryGetProperty("recipients", out _));
        Assert.True((await Fields(await req.Post("/api/report-schedules", new { name = "Mine", report = "inventory", recipients = new[] { ru.Email } }))).TryGetProperty("report", out _));

        // a weekly schedule: Monday 07:00 in the organisation's time zone (Asia/Jakarta, UTC+7)
        var s = await Ok(await ga.Post("/api/report-schedules", new { name = "Weekly stock", report = "inventory", format = "xlsx", frequency = "weekly", weekday = 1, hour = 7, recipients = new[] { gu.Email, au.Email } }));
        var id = IdOf(s);
        var next = s.GetProperty("nextRunAt").GetDateTimeOffset();
        Assert.True(next > clock.UtcNow && next <= clock.UtcNow.AddDays(7));
        Assert.Equal(DayOfWeek.Monday, next.ToOffset(TimeSpan.FromHours(7)).DayOfWeek); Assert.Equal(7, next.ToOffset(TimeSpan.FromHours(7)).Hour);
        Assert.Equal(2, s.GetProperty("recipients").GetArrayLength());
        // other people do not see or touch it
        Assert.Empty((await Ok(await other.Get("/api/report-schedules"))).EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await other.Post($"/api/report-schedules/{id}/send")).StatusCode);

        // send now: each recipient gets the workbook attached
        var sent = await Ok(await ga.Post($"/api/report-schedules/{id}/send"));
        Assert.StartsWith("Sent to 2 people", sent.GetProperty("lastResult").GetString());
        var m = await mails.WaitFor(au.Email, "Weekly stock");
        Assert.NotNull(m.Attachment); Assert.EndsWith(".xlsx", m.Attachment!.FileName);
        Assert.Contains("<sheetData>", Sheet(m.Attachment.Content));
        Assert.Equal(next, (await Ok(await ga.Get("/api/report-schedules")))[0].GetProperty("nextRunAt").GetDateTimeOffset());   // the regular time is unchanged

        // the regular run: due once, then not again until next week; an agent who left is skipped
        await seed.Db(db => db.Users.IgnoreQueryFilters().Where(u => u.Id == au.Id).ExecuteUpdateAsync(u => u.SetProperty(x => x.Status, UserStatus.Suspended)));
        async Task<ReportRunResult> Run() { using var scope = f.Services.CreateScope(); return await scope.ServiceProvider.GetRequiredService<ReportExportService>().RunDueAsync(); }
        Assert.Equal(0, (await Run()).Schedules);
        clock.Advance(next - clock.UtcNow + TimeSpan.FromMinutes(1));
        var r1 = await Run();
        Assert.Equal(1, r1.Schedules); Assert.Equal(1, r1.Sent); Assert.Equal(1, r1.Skipped);
        Assert.Equal(0, (await Run()).Schedules);
        var row = await seed.Db(db => db.ReportSchedules.IgnoreQueryFilters().AsNoTracking().FirstAsync(x => x.Id == id));
        Assert.Equal(next.AddDays(7), row.NextRunAt);
        Assert.Contains("1 skipped", row.LastResult);

        // the owner is suspended: the next run pauses the schedule and sends nothing
        await seed.Db(db => db.Users.IgnoreQueryFilters().Where(u => u.Id == gu.Id).ExecuteUpdateAsync(u => u.SetProperty(x => x.Status, UserStatus.Suspended)));
        clock.Advance(TimeSpan.FromDays(7));
        var r2 = await Run();
        Assert.Equal(1, r2.Schedules); Assert.Equal(0, r2.Sent);
        row = await seed.Db(db => db.ReportSchedules.IgnoreQueryFilters().AsNoTracking().FirstAsync(x => x.Id == id));
        Assert.False(row.Active); Assert.StartsWith("Paused", row.LastResult);
        Assert.True(await seed.Db(db => db.AuditEvents.IgnoreQueryFilters().AnyAsync(a => a.Action == "report.schedule_sent" && a.TargetId == id.ToString())));
    }
}
