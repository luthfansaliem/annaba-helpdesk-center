using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using AnnabaDigital.Application.Users;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Tests.Infra;
using Xunit;

namespace AnnabaDigital.Tests;

[Collection("db")]
public sealed class AccountClosureTests(PostgresFixture fx)
{
    private readonly Seed _seed = new(fx.Factory, fx.Clock);

    private async Task<(TestBrowser b, User u)> Actor(string role = "user", bool reauth = true)
    {
        var u = await _seed.UserAsync(role: role); var b = TestBrowser.Create(fx.Factory); await b.LoginAsync(u.Email, Seed.Password);
        if (reauth) await b.Post("/api/auth/reauth", new { password = Seed.Password });
        return (b, u);
    }
    private async Task<int> RunDue() { using var scope = fx.Factory.Services.CreateScope(); return await scope.ServiceProvider.GetRequiredService<AccountClosureService>().RunDueAsync(); }
    private async Task<Guid> Request(TestBrowser b, string reason = "Leaving the company")
    {
        var res = await b.Post("/api/me/closure", new { reason }); Assert.Equal(HttpStatusCode.Created, res.StatusCode); return (await res.Body()).GetProperty("id").GetGuid();
    }
    private async Task MakeDue(Guid requestId) => await _seed.Db(async db => await db.AccountClosureRequests.IgnoreQueryFilters().Where(x => x.Id == requestId).ExecuteUpdateAsync(u => u.SetProperty(x => x.ExecuteAfter, fx.Clock.UtcNow.AddMinutes(-1))));

    [Fact]
    public async Task Downloading_my_data_needs_a_fresh_password_check_is_audited_and_holds_no_secrets_or_other_peoples_data()
    {
        var other = await _seed.UserAsync(email: $"stranger{Guid.NewGuid():N}@test.local");
        var (stale, _) = await Actor(reauth: false);
        Assert.Equal(HttpStatusCode.Forbidden, (await stale.Get("/api/me/export")).StatusCode);

        var (b, me) = await Actor();
        var res = await b.Get("/api/me/export");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode); Assert.Contains("attachment", res.Content.Headers.ContentDisposition?.ToString() ?? ""); Assert.Equal("no-store", res.Headers.CacheControl?.ToString());
        var text = await res.Content.ReadAsStringAsync(); var json = JsonDocument.Parse(text).RootElement;
        Assert.Equal(me.Email, json.GetProperty("account").GetProperty("email").GetString());
        Assert.True(json.GetProperty("sessions").GetArrayLength() >= 1); Assert.True(json.GetProperty("activity").GetArrayLength() >= 1);
        foreach (var secret in new[] { "passwordHash", "PasswordHash", "secretCiphertext", "codeHash", "tokenHash" }) Assert.DoesNotContain(secret, text);
        Assert.DoesNotContain(other.Email, text);
        Assert.NotEmpty(await _seed.Db(db => db.AuditEvents.Where(a => a.ActorId == me.Id && a.Action == "account.export").ToListAsync()));
    }

    [Fact]
    public async Task A_closure_request_is_approved_waits_and_then_erases_the_account_while_cancelling_stops_it()
    {
        var (b, me) = await Actor();
        Assert.Equal(HttpStatusCode.NoContent, (await b.Get("/api/me/closure")).StatusCode); // nothing yet
        var (stale, _) = await Actor(reauth: false);
        Assert.Equal(HttpStatusCode.Forbidden, (await stale.Post("/api/me/closure", new { reason = "x" })).StatusCode); // needs a fresh password check
        var id = await Request(b);
        Assert.Equal(HttpStatusCode.Conflict, (await b.Post("/api/me/closure", new { reason = "again" })).StatusCode); // one at a time
        Assert.Equal("pending", (await (await b.Get("/api/me/closure")).Body()).GetProperty("status").GetString());

        // who decides: not the person, not someone without the permission, not someone below the person's own level
        Assert.Equal(HttpStatusCode.Forbidden, (await b.Post($"/api/account-closures/{id}/approve", new { })).StatusCode); // plain user lacks users.suspend
        var (ua, _) = await Actor("user_admin"); var (plain, _) = await Actor();
        Assert.Equal(HttpStatusCode.Forbidden, (await plain.Get("/api/account-closures")).StatusCode);
        await _seed.UserAsync(role: "super_admin"); // so the next one is not the only active Super Admin (those can't ask to close)
        var boss = await _seed.UserAsync(role: "super_admin"); var bb = TestBrowser.Create(fx.Factory); await bb.LoginAsync(boss.Email, Seed.Password); await bb.Post("/api/auth/reauth", new { password = Seed.Password });
        var bossRequest = await Request(bb);
        var denied = await ua.Post($"/api/account-closures/{bossRequest}/approve", new { });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode); Assert.Equal("denied", await denied.ErrorCode()); // hierarchy: a User Admin can't decide a Super Admin's

        Assert.Equal(HttpStatusCode.NoContent, (await ua.Post($"/api/account-closures/{id}/approve", new { note = "OK" })).StatusCode);
        var mine = await (await b.Get("/api/me/closure")).Body();
        Assert.Equal("approved", mine.GetProperty("status").GetString());
        var when = mine.GetProperty("executeAfter").GetDateTimeOffset(); Assert.True(when > fx.Clock.UtcNow.AddDays(13) && when < fx.Clock.UtcNow.AddDays(15));
        var mail = await _seed.Mail.WaitFor(me.Email, "closure was approved"); Assert.Contains("cancel", mail.Text, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(HttpStatusCode.OK, (await b.Get("/api/auth/me")).StatusCode); // still usable during the waiting period
        await RunDue(); Assert.Equal(UserStatus.Active, await _seed.Db(db => db.Users.Where(u => u.Id == me.Id).Select(u => u.Status).SingleAsync())); // not due yet

        var inviteId = await _seed.Db(async db => { var i = new Invitation { Email = me.Email, TokenHash = "h" + Guid.NewGuid().ToString("N"), InvitedBy = Guid.Empty, Message = "Welcome, Test Person!", Status = InvitationStatus.Accepted, AcceptedUserId = me.Id, CreatedAt = fx.Clock.UtcNow, ExpiresAt = fx.Clock.UtcNow.AddDays(7) }; db.Invitations.Add(i); await db.SaveChangesAsync(); return i.Id; });
        await MakeDue(id); await RunDue();
        var invite = await _seed.Db(db => db.Invitations.IgnoreQueryFilters().SingleAsync(i => i.Id == inviteId));
        Assert.EndsWith("@closed.invalid", invite.Email); Assert.Null(invite.Message); // the invitation no longer carries the address or the message
        var closed = await _seed.Db(db => db.Users.Include(u => u.Profile).SingleAsync(u => u.Id == me.Id));
        Assert.Equal(UserStatus.Deactivated, closed.Status); Assert.Equal("Closed account", closed.Name); Assert.EndsWith("@closed.invalid", closed.Email); Assert.Null(closed.Profile?.Phone);
        Assert.False(await _seed.Db(db => db.Credentials.AnyAsync(c => c.UserId == me.Id)));
        Assert.False(await _seed.Db(db => db.Sessions.AnyAsync(s => s.UserId == me.Id)));
        Assert.False(await _seed.Db(db => db.UserRoles.AnyAsync(r => r.UserId == me.Id)));
        Assert.Equal(ClosureStatus.Completed, await _seed.Db(db => db.AccountClosureRequests.Where(r => r.Id == id).Select(r => r.Status).SingleAsync()));
        await _seed.Mail.WaitFor(me.Email, "Your account was closed"); // to the address it had
        Assert.NotEmpty(await _seed.Db(db => db.AuditEvents.Where(a => a.Action == "account.close" && a.TargetId == me.Id.ToString()).ToListAsync()));
        var relogin = await TestBrowser.Create(fx.Factory).LoginAsync(me.Email, Seed.Password);
        Assert.False(relogin.IsSuccessStatusCode, "a closed account can no longer sign in");

        // cancelling during the wait stops it
        var (cb, cu) = await Actor(); var cid = await Request(cb);
        await ua.Post($"/api/account-closures/{cid}/approve", new { });
        Assert.Equal(HttpStatusCode.NoContent, (await cb.Post("/api/me/closure/cancel", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await cb.Post("/api/me/closure/cancel", new { })).StatusCode);
        await _seed.Db(async db => await db.AccountClosureRequests.IgnoreQueryFilters().Where(x => x.Id == cid).ExecuteUpdateAsync(u => u.SetProperty(x => x.ExecuteAfter, fx.Clock.UtcNow.AddMinutes(-1)))); await RunDue();
        Assert.Equal(UserStatus.Active, await _seed.Db(db => db.Users.Where(u => u.Id == cu.Id).Select(u => u.Status).SingleAsync()));
    }

    [Fact]
    public async Task A_declined_request_leaves_the_account_alone_and_the_person_may_ask_again()
    {
        var (b, me) = await Actor(); var id = await Request(b);
        var (ua, _) = await Actor("user_admin");
        Assert.Equal(HttpStatusCode.NoContent, (await ua.Post($"/api/account-closures/{id}/reject", new { note = "Please talk to HR first." })).StatusCode);
        var mail = await _seed.Mail.WaitFor(me.Email, "was declined"); Assert.Contains("talk to HR", mail.Text);
        Assert.Equal(HttpStatusCode.Conflict, (await ua.Post($"/api/account-closures/{id}/approve", new { })).StatusCode); // already decided
        Assert.Equal(UserStatus.Active, await _seed.Db(db => db.Users.Where(u => u.Id == me.Id).Select(u => u.Status).SingleAsync()));
        await Request(b, "I still want out");
    }
}
