using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Monitoring;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Infrastructure.Persistence;
using AnnabaDigital.Tests.Infra;
using Xunit;

namespace AnnabaDigital.Tests;

[Collection("db")]
public sealed class AuditExportTests(PostgresFixture fx)
{
    private readonly Seed _seed = new(fx.Factory, fx.Clock);

    private async Task<TestBrowser> LoginAsync(string role)
    {
        var u = await _seed.UserAsync(role: role); var b = TestBrowser.Create(fx.Factory); await b.LoginAsync(u.Email, Seed.Password); return b;
    }

    [Theory]
    [InlineData("=SUM(A1)", "'=SUM(A1)")]
    [InlineData("+1", "'+1")]
    [InlineData("-1", "'-1")]
    [InlineData("@cmd", "'@cmd")]
    [InlineData("plain", "plain")]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
    [InlineData("two\nlines", "\"two\nlines\"")]
    [InlineData(null, "")]
    public void Csv_cells_are_quoted_and_cannot_run_as_spreadsheet_formulas(string? input, string expected)
        => Assert.Equal(expected, MonitoringService.Csv(input));

    [Fact]
    public async Task Export_returns_filtered_csv_with_a_download_name_and_audits_itself()
    {
        var u = await _seed.UserAsync();
        await TestBrowser.Create(fx.Factory).LoginAsync(u.Email, "wrong-password");
        var b = await LoginAsync("security_auditor");
        var res = await b.Get($"/api/audit/export?q={u.Email}&outcome=failure");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.StartsWith("text/csv", res.Content.Headers.ContentType!.ToString());
        Assert.Matches(@"^audit-\d{8}-\d{6}\.csv$", res.Content.Headers.ContentDisposition!.FileName!.Trim('"'));
        var lines = (await res.Content.ReadAsStringAsync()).TrimEnd().Split("\r\n");
        Assert.StartsWith("id,timestamp,actor_id,actor,action", lines[0]);
        Assert.Equal(2, lines.Length); Assert.Contains("auth.login.failure", lines[1]);
        Assert.Equal("false", res.Headers.GetValues("X-Export-Truncated").Single());
        var logged = await _seed.Db(db => db.AuditEvents.Where(a => a.Action == "audit.export").OrderByDescending(a => a.Id).FirstOrDefaultAsync());
        Assert.NotNull(logged); Assert.Equal("csv", JsonDocument.Parse(logged!.MetaJson!).RootElement.GetProperty("format").GetString());
    }

    [Fact]
    public async Task Export_json_is_valid_and_unknown_formats_are_rejected()
    {
        var b = await LoginAsync("security_auditor");
        var ok = await b.Get("/api/audit/export?format=json&category=auth");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode); Assert.StartsWith("application/json", ok.Content.Headers.ContentType!.ToString());
        Assert.Equal(JsonValueKind.Array, JsonDocument.Parse(await ok.Content.ReadAsStringAsync()).RootElement.ValueKind);
        Assert.Equal((HttpStatusCode)422, (await b.Get("/api/audit/export?format=xml")).StatusCode);
    }

    [Fact]
    public async Task Export_needs_audit_read()
    {
        var plain = await LoginAsync("user");
        Assert.Equal(HttpStatusCode.Forbidden, (await plain.Get("/api/audit/export")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await TestBrowser.Create(fx.Factory).Get("/api/audit/export")).StatusCode);
    }
}
