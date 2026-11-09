using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Tests.Infra;
using Xunit;

namespace AnnabaDigital.Tests;

[Collection("db")]
public sealed class NewDeviceAlertTests(PostgresFixture fx)
{
    private readonly Seed _seed = new(fx.Factory, fx.Clock);
    private const string Mac = "Mozilla/5.0 (Macintosh; Intel Mac OS X) Chrome/124.0 Safari/537.36";
    private const string Phone = "Mozilla/5.0 (iPhone; CPU iPhone OS 17_4 like Mac OS X) AppleWebKit/605.1.15 Version/17.4 Mobile/15E148 Safari/604.1";

    private async Task<string> SignIn(string email, string userAgent) { var b = TestBrowser.Create(fx.Factory); b.UserAgent = userAgent; return (await (await b.LoginAsync(email, Seed.Password)).Body()).GetProperty("status").GetString()!; }
    private int Alerts(string email) => _seed.Mail.Sent.Count(m => m.To.Equals(email, StringComparison.OrdinalIgnoreCase) && m.Subject.Contains("New sign-in"));

    [Fact]
    public async Task A_sign_in_from_a_device_not_seen_lately_emails_the_owner_once_and_the_first_ever_sign_in_never_does()
    {
        var u = await _seed.UserAsync();
        Assert.Equal("ok", await SignIn(u.Email, Mac)); await _seed.Mail.AssertNone(u.Email, 200); // the very first sign-in has nothing to compare with
        Assert.Equal("ok", await SignIn(u.Email, Mac)); Assert.Equal(0, Alerts(u.Email)); // same device and browser again

        Assert.Equal("ok", await SignIn(u.Email, Phone));
        var mail = await _seed.Mail.WaitFor(u.Email, "New sign-in");
        Assert.Contains("iPhone", mail.Text); Assert.Contains("Safari", mail.Text); Assert.Contains("/profile?tab=security", mail.Text); Assert.Contains("Asia/Jakarta", mail.Text);
        Assert.Contains("10.", mail.Text); // the address it came from
        var logins = await _seed.Db(db => db.AuditEvents.Where(a => a.ActorId == u.Id && a.Action == "auth.login.success").ToListAsync());
        Assert.Single(logins, a => (a.MetaJson ?? "").Contains("newDevice")); // only the phone sign-in is flagged in the audit log

        Assert.Equal("ok", await SignIn(u.Email, Phone)); Assert.Equal("ok", await SignIn(u.Email, Mac));
        await Task.Delay(300); Assert.Equal(1, Alerts(u.Email)); // the phone is now known; nothing more
    }

    [Fact]
    public async Task People_who_turned_security_alerts_off_are_not_emailed_and_an_old_device_counts_as_new_again()
    {
        var quiet = await _seed.UserAsync(); await SignIn(quiet.Email, Mac);
        await _seed.Db(async db => { var p = await db.Profiles.FirstOrDefaultAsync(x => x.UserId == quiet.Id); if (p is null) db.Profiles.Add(new UserProfile { UserId = quiet.Id, NotifySecurity = false }); else p.NotifySecurity = false; await db.SaveChangesAsync(); return 0; });
        Assert.Equal("ok", await SignIn(quiet.Email, Phone)); await _seed.Mail.AssertNone(quiet.Email, 300);

        var u = await _seed.UserAsync(); await SignIn(u.Email, Mac);
        await _seed.Db(async db => await db.Sessions.Where(s => s.UserId == u.Id).ExecuteUpdateAsync(x => x.SetProperty(s => s.CreatedAt, DateTimeOffset.UtcNow.AddDays(-120))));
        Assert.Equal("ok", await SignIn(u.Email, Mac)); // same device, but not used for more than 90 days
        await _seed.Mail.WaitFor(u.Email, "New sign-in");
    }
}
