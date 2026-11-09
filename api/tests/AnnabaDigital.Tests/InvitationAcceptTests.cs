using System.Net;
using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Tests.Infra;
using Xunit;

namespace AnnabaDigital.Tests;

[Collection("db")]
public sealed class InvitationAcceptTests(PostgresFixture fx)
{
    private readonly Seed _seed = new(fx.Factory, fx.Clock);
    private const string StrongPw = "Maple-Orbit-Granite-77";
    private TestBrowser B() => TestBrowser.Create(fx.Factory);

    private async Task<(string raw, string email, Guid id)> Invite(string[]? roles = null, InvitationStatus status = InvitationStatus.Pending, TimeSpan? ttl = null, string? email = null)
    {
        var admin = await _seed.UserAsync(role: "user_admin");
        var raw = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var mail = email ?? $"inv{Guid.NewGuid():N}@test.local";
        var id = await _seed.Db(async db =>
        {
            var roleIds = await db.Roles.Where(r => r.TenantId == Tenant.DefaultId && (roles ?? new[] { "user" }).Contains(r.Key)).Select(r => r.Id).ToListAsync();
            var inv = new Invitation { Email = mail, TokenHash = new AnnabaDigital.Infrastructure.Security.TokenService(Microsoft.Extensions.Options.Options.Create(new AnnabaDigital.Infrastructure.Security.SecurityOptions())).Hash(raw),
                InvitedBy = admin.Id, Status = status, CreatedAt = fx.Clock.UtcNow, ExpiresAt = fx.Clock.UtcNow + (ttl ?? TimeSpan.FromDays(7)) };
            foreach (var r in roleIds) inv.Roles.Add(new InvitationRole { RoleId = r });
            db.Invitations.Add(inv); await db.SaveChangesAsync(); return inv.Id;
        });
        return (raw, mail, id);
    }

    [Fact]
    public async Task Lookup_reports_state_and_reveals_email_and_roles_only_for_valid_invitations()
    {
        var (raw, email, _) = await Invite(["security_auditor"]);
        var ok = await (await B().Get($"/api/invitations/lookup?token={raw}")).Body();
        Assert.Equal("valid", ok.GetProperty("state").GetString()); Assert.Equal(email, ok.GetProperty("email").GetString());
        Assert.Equal("Security Auditor", ok.GetProperty("roles")[0].GetString());

        var (expired, _, _) = await Invite(ttl: TimeSpan.FromMinutes(-1));
        var (revoked, _, _) = await Invite(status: InvitationStatus.Revoked);
        var (used, _, _) = await Invite(status: InvitationStatus.Accepted);
        foreach (var (token, state) in new[] { (expired, "expired"), (revoked, "revoked"), (used, "used"), ("no-such-token", "invalid") })
        {
            var r = await (await B().Get($"/api/invitations/lookup?token={token}")).Body();
            Assert.Equal(state, r.GetProperty("state").GetString());
            Assert.Equal(System.Text.Json.JsonValueKind.Null, r.GetProperty("email").ValueKind);
        }
    }

    [Fact]
    public async Task Accepting_creates_an_active_verified_user_with_exactly_the_invited_roles()
    {
        var (raw, email, invId) = await Invite(["security_auditor"]);
        var res = await B().Post("/api/invitations/accept", new { token = raw, name = "Invited Person", password = StrongPw }, csrf: false);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var u = await _seed.Db(db => db.Users.Include(x => x.UserRoles).ThenInclude(r => r.Role).SingleAsync(x => x.Email == email));
        Assert.Equal(UserStatus.Active, u.Status); Assert.NotNull(u.EmailVerifiedAt); Assert.Equal("Invited Person", u.Name);
        Assert.Equal(["security_auditor"], u.UserRoles.Select(r => r.Role!.Key).ToArray());
        var inv = await _seed.Db(db => db.Invitations.SingleAsync(i => i.Id == invId));
        Assert.Equal(InvitationStatus.Accepted, inv.Status); Assert.Equal(u.Id, inv.AcceptedUserId);
        Assert.Equal("ok", (await (await B().LoginAsync(email, StrongPw)).Body()).GetProperty("status").GetString());
        Assert.NotEmpty(await _seed.Db(db => db.AuditEvents.Where(a => a.Action == "invitation.accept" && a.ActorId == u.Id).ToListAsync()));
    }

    [Fact]
    public async Task An_invitation_can_be_accepted_only_once_even_concurrently()
    {
        var (raw, email, _) = await Invite();
        var all = await Task.WhenAll(Enumerable.Range(0, 5).Select(i => B().Post("/api/invitations/accept", new { token = raw, name = "Racer " + i, password = StrongPw + i }, csrf: false)));
        Assert.Equal(1, all.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(4, all.Count(r => r.StatusCode == HttpStatusCode.Gone));
        Assert.Equal(1, await _seed.Db(db => db.Users.CountAsync(u => u.Email == email)));
    }

    [Theory]
    [InlineData("expired")] [InlineData("revoked")] [InlineData("used")]
    public async Task Expired_revoked_and_used_invitations_cannot_be_accepted(string kind)
    {
        var (raw, email, _) = kind switch
        {
            "expired" => await Invite(ttl: TimeSpan.FromMinutes(-5)), "revoked" => await Invite(status: InvitationStatus.Revoked), _ => await Invite(status: InvitationStatus.Accepted),
        };
        var res = await B().Post("/api/invitations/accept", new { token = raw, name = "Late Person", password = StrongPw }, csrf: false);
        Assert.Equal(HttpStatusCode.Gone, res.StatusCode);
        Assert.False(await _seed.Db(db => db.Users.AnyAsync(u => u.Email == email)));
    }

    [Fact]
    public async Task A_weak_password_or_missing_name_does_not_burn_the_invitation()
    {
        var (raw, email, _) = await Invite();
        Assert.Equal((HttpStatusCode)422, (await B().Post("/api/invitations/accept", new { token = raw, name = "Ok Name", password = "short" }, csrf: false)).StatusCode);
        Assert.Equal((HttpStatusCode)422, (await B().Post("/api/invitations/accept", new { token = raw, name = "", password = StrongPw }, csrf: false)).StatusCode);
        Assert.Equal("valid", (await (await B().Get($"/api/invitations/lookup?token={raw}")).Body()).GetProperty("state").GetString());
        Assert.Equal(HttpStatusCode.OK, (await B().Post("/api/invitations/accept", new { token = raw, name = "Ok Name", password = StrongPw }, csrf: false)).StatusCode);
    }

    [Fact]
    public async Task An_invitation_activates_a_pending_placeholder_but_never_takes_over_an_existing_active_account()
    {
        var pending = await _seed.UserAsync(status: UserStatus.Pending, role: "user");
        var (raw, _, _) = await Invite(["user_admin"], email: pending.Email);
        Assert.Equal(HttpStatusCode.OK, (await B().Post("/api/invitations/accept", new { token = raw, name = "Placeholder Now Real", password = StrongPw }, csrf: false)).StatusCode);
        var u = await _seed.Db(db => db.Users.Include(x => x.UserRoles).ThenInclude(r => r.Role).SingleAsync(x => x.Id == pending.Id));
        Assert.Equal(UserStatus.Active, u.Status); Assert.Equal(["user_admin"], u.UserRoles.Select(r => r.Role!.Key).ToArray());

        var active = await _seed.UserAsync();
        var (raw2, _, id2) = await Invite(["super_admin"], email: active.Email);
        var res = await B().Post("/api/invitations/accept", new { token = raw2, name = "Takeover Attempt", password = StrongPw }, csrf: false);
        Assert.Equal(HttpStatusCode.Gone, res.StatusCode);
        Assert.Equal(Seed.Password, Seed.Password);
        Assert.Equal(["user"], (await _seed.Db(db => db.Users.Include(x => x.UserRoles).ThenInclude(r => r.Role).SingleAsync(x => x.Id == active.Id))).UserRoles.Select(r => r.Role!.Key).ToArray());
        Assert.Equal(InvitationStatus.Pending, (await _seed.Db(db => db.Invitations.SingleAsync(i => i.Id == id2))).Status); // rolled back with the transaction
        Assert.Equal(HttpStatusCode.OK, (await B().LoginAsync(active.Email, Seed.Password)).StatusCode); // original credentials untouched
    }

    [Fact]
    public async Task Invitation_endpoints_are_rate_limited_per_ip()
    {
        var b = B(); HttpResponseMessage? last = null;
        for (var i = 0; i < 31; i++) last = await b.Get($"/api/invitations/lookup?token=guess{i}");
        Assert.Equal((HttpStatusCode)429, last!.StatusCode);
    }
}
