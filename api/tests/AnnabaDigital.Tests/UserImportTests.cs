using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Users;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Tests.Infra;
using Xunit;

namespace AnnabaDigital.Tests;

[Collection("db")]
public sealed class UserImportTests(PostgresFixture fx)
{
    private readonly Seed _seed = new(fx.Factory, fx.Clock);
    private static string Mail() => $"imp{Guid.NewGuid():N}@test.local";

    private async Task<TestBrowser> Admin(string role = "user_admin", bool reauth = true)
    {
        var u = await _seed.UserAsync(role: role); var b = TestBrowser.Create(fx.Factory); await b.LoginAsync(u.Email, Seed.Password);
        if (reauth) await b.Post("/api/auth/reauth", new { password = Seed.Password });
        return b;
    }

    [Fact]
    public void Parser_handles_quotes_embedded_commas_line_breaks_bom_and_blank_endings()
    {
        var rows = UserImportService.ParseCsv("﻿name,email\r\n\"Doe, Jane\",jane@x.io\r\n\"Say \"\"hi\"\"\",a@x.io\n\"two\nlines\",b@x.io\n");
        Assert.Equal(4, rows.Count);
        Assert.Equal(["name", "email"], rows[0]);
        Assert.Equal("Doe, Jane", rows[1][0]); Assert.Equal("Say \"hi\"", rows[2][0]); Assert.Equal("two\nlines", rows[3][0]);
    }

    [Fact]
    public async Task Dry_run_reports_each_row_and_writes_nothing()
    {
        var b = await Admin(); var ok = Mail(); var taken = (await _seed.UserAsync()).Email; var role = await _seed.RoleId("user");
        var csv = $"Name,Email,Department\nGood Person,{ok},Ops\nBad Email,not-an-email,Ops\nTaken Person,{taken},Ops\nGood Again,{ok.ToUpperInvariant()},Ops\n";
        var res = await b.Post("/api/users/import", new { csv, roleIds = new[] { role }, dryRun = true });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var d = await res.Body();
        Assert.True(d.GetProperty("dryRun").GetBoolean()); Assert.Equal(4, d.GetProperty("total").GetInt32()); Assert.Equal(0, d.GetProperty("created").GetInt32()); Assert.Equal(3, d.GetProperty("failed").GetInt32());
        var st = d.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("status").GetString()).ToArray();
        Assert.Equal(["ok", "error", "error", "error"], st);
        Assert.Null(await _seed.Db(db => db.Users.Where(u => u.Email == ok).FirstOrDefaultAsync()));
    }

    [Fact]
    public async Task Import_creates_pending_users_with_invitations_the_chosen_role_and_one_audit_summary_and_skips_bad_rows()
    {
        var b = await Admin(); var a = Mail(); var c = Mail(); var role = await _seed.RoleId("user");
        var csv = $"name,email,phone,job title\nAnna Import,{a},+62 811,Analyst\nX,bad,,\nCarl Import,{c},,\n";
        var res = await b.Post("/api/users/import", new { csv, roleIds = new[] { role }, dryRun = false });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var d = await res.Body();
        Assert.Equal(2, d.GetProperty("created").GetInt32()); Assert.Equal(1, d.GetProperty("failed").GetInt32());
        foreach (var email in new[] { a, c })
        {
            var u = await _seed.Db(db => db.Users.Include(x => x.UserRoles).Include(x => x.Profile).SingleAsync(x => x.Email == email));
            Assert.Equal(UserStatus.Pending, u.Status); Assert.Equal(role, Assert.Single(u.UserRoles).RoleId);
            Assert.True(await _seed.Db(db => db.Invitations.AnyAsync(i => i.Email == email && i.Status == InvitationStatus.Pending)));
        }
        Assert.Equal("Analyst", (await _seed.Db(db => db.Users.Include(x => x.Profile).SingleAsync(x => x.Email == a))).Profile!.JobTitle);
        var summaries = (await _seed.Db(db => db.AuditEvents.Where(x => x.Action == "user.import").ToListAsync())).Select(x => JsonDocument.Parse(x.MetaJson!).RootElement).ToList();
        Assert.Contains(summaries, m => m.GetProperty("rows").GetString() == "3" && m.GetProperty("created").GetString() == "2" && m.GetProperty("failed").GetString() == "1");
    }

    [Fact]
    public async Task Import_is_validated_authorised_and_needs_recent_reauthentication()
    {
        var role = await _seed.RoleId("user"); var csv = $"name,email\nSome One,{Mail()}\n";
        Assert.Equal(HttpStatusCode.Forbidden, (await (await Admin("user", reauth: false)).Post("/api/users/import", new { csv, roleIds = new[] { role } })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await TestBrowser.Create(fx.Factory).Post("/api/users/import", new { csv, roleIds = new[] { role } })).StatusCode);

        var noReauth = await Admin(reauth: false);
        var need = await noReauth.Post("/api/users/import", new { csv, roleIds = new[] { role }, dryRun = false });
        Assert.Equal(HttpStatusCode.Forbidden, need.StatusCode); Assert.Equal("reauth_required", await need.ErrorCode());

        var b = await Admin();
        Assert.Equal((HttpStatusCode)422, (await b.Post("/api/users/import", new { csv = "", roleIds = new[] { role } })).StatusCode);
        Assert.Equal((HttpStatusCode)422, (await b.Post("/api/users/import", new { csv = "foo,bar\n1,2\n", roleIds = new[] { role } })).StatusCode);
        Assert.Equal((HttpStatusCode)422, (await b.Post("/api/users/import", new { csv = "name,email\n" + string.Concat(Enumerable.Range(0, 201).Select(i => $"P{i} Name,{Mail()}\n")), roleIds = new[] { role } })).StatusCode);
        Assert.Equal((HttpStatusCode)422, (await b.Post("/api/users/import", new { csv, roleIds = new[] { Guid.NewGuid() } })).StatusCode); // unknown role
    }
}
