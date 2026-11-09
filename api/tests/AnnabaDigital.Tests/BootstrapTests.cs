using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using AnnabaDigital.Api;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Infrastructure.Persistence;
using AnnabaDigital.Tests.Infra;
using Xunit;

namespace AnnabaDigital.Tests;

[Collection("db")]
public sealed class BootstrapTests(PostgresFixture fx)
{
    private const string Pw = "Zq9!Lantern-Orbit-42";

    [Fact]
    public async Task An_empty_system_e_mails_one_super_admin_invitation_that_works_and_never_repeats_once_a_user_exists()
    {
        var (f, clock, _) = await fx.NewIsolatedAsync(); var seed = new Seed(f, clock);
        const string addr = "first.admin@example.com";
        using (var scope = f.Services.CreateScope()) Assert.True(await Bootstrap.RunAsync(scope.ServiceProvider, "  First.Admin@Example.com "));

        var mail = await seed.Mail.WaitFor(addr, "invit");
        Assert.DoesNotContain("password", mail.Text, StringComparison.OrdinalIgnoreCase);
        var token = Uri.UnescapeDataString(Seed_TokenIn(mail));

        var b = TestBrowser.Create(f);
        var accepted = await b.Post("/api/invitations/accept", new { token, name = "First Admin", password = Pw }, csrf: false);
        Assert.True(accepted.IsSuccessStatusCode, await accepted.Content.ReadAsStringAsync());
        var login = await b.LoginAsync(addr, Pw);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var roles = await seed.Db(db => db.UserRoles.Where(ur => ur.User!.Email == addr).Select(ur => ur.Role!.Key).ToListAsync());
        Assert.Equal(["super_admin"], roles);

        // a user now exists: restarting with the setting still present does nothing
        using var again = f.Services.CreateScope();
        Assert.False(await Bootstrap.RunAsync(again.ServiceProvider, addr));
        Assert.False(await Bootstrap.RunAsync(again.ServiceProvider, null));
    }

    [Fact]
    public async Task A_lost_invitation_is_replaced_on_restart_and_the_old_link_stops_working()
    {
        var (f, clock, _) = await fx.NewIsolatedAsync(); var seed = new Seed(f, clock);
        const string addr = "second.admin@example.com";
        using (var s1 = f.Services.CreateScope()) await Bootstrap.RunAsync(s1.ServiceProvider, addr);
        var first = Seed_TokenIn(await seed.Mail.WaitFor(addr, "invit"));
        using (var s2 = f.Services.CreateScope()) await Bootstrap.RunAsync(s2.ServiceProvider, addr);
        await Task.Delay(300);
        Assert.Equal(1, await seed.Db(db => db.Invitations.CountAsync(i => i.Email == addr && i.Status == InvitationStatus.Pending)));
        var res = await TestBrowser.Create(f).Post("/api/invitations/accept", new { token = Uri.UnescapeDataString(first), name = "Late", password = Pw }, csrf: false);
        Assert.False(res.IsSuccessStatusCode);
    }

    [Fact]
    public async Task Invalid_address_issues_nothing()
    {
        var (f, clock, _) = await fx.NewIsolatedAsync(); var seed = new Seed(f, clock);
        using var scope = f.Services.CreateScope();
        Assert.False(await Bootstrap.RunAsync(scope.ServiceProvider, "not-an-address"));
        Assert.Equal(0, await seed.Db(db => db.Invitations.CountAsync()));
    }

    private static string Seed_TokenIn(AnnabaDigital.Application.Abstractions.EmailMessage m) => InMemoryEmailSender.TokenIn(m);
}
