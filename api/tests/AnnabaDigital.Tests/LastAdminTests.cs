using System.Net;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Domain.Security;
using AnnabaDigital.Infrastructure.Persistence;
using AnnabaDigital.Tests.Infra;
using Xunit;

namespace AnnabaDigital.Tests;

/// <summary>Runs in brand-new databases so the test fully controls how many Super Admins exist.</summary>
[Collection("db")]
public sealed class LastAdminTests(PostgresFixture fx)
{
    private sealed record Env(ApiFactory F, TestClock Clock, Seed Seed, string Cs)
    {
        public async Task<(TestBrowser B, User U)> Operator(string key)
        {
            var u = await Seed.UserAsync(role: key); var b = TestBrowser.Create(F); await b.LoginAsync(u.Email, Seed.Password);
            await b.Post("/api/auth/reauth", new { password = Seed.Password }); return (b, u);
        }
        public Task<int> ActiveSuperAdmins() => Seed.Db(db => db.UserRoles.CountAsync(ur => ur.Role!.Key == "super_admin" && ur.User!.Status == UserStatus.Active));
    }

    private async Task<Env> NewEnv()
    {
        var (f, clock, cs) = await fx.NewIsolatedAsync(); var seed = new Seed(f, clock);
        await seed.RoleAsync("ops_all", [.. Permissions.All]); // holds every permission but is NOT the protected super_admin role
        return new Env(f, clock, seed, cs);
    }

    [Fact]
    public async Task The_last_active_super_admin_cannot_be_suspended_deactivated_or_demoted_by_anyone()
    {
        var e = await NewEnv(); var (op, _) = await e.Operator("ops_all");
        var only = await e.Seed.UserAsync(role: "super_admin");
        foreach (var action in new[] { "suspend", "deactivate" })
        {
            var r = await op.Post($"/api/users/{only.Id}/{action}", new { });
            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode); Assert.Contains("last active Super Admin", (await r.Body()).GetProperty("error").GetProperty("message").GetString());
        }
        var userRole = await e.Seed.RoleId("user");
        var demote = await op.SendAsync(HttpMethod.Put, $"/api/users/{only.Id}/roles", new { roleIds = new[] { userRole } });
        Assert.Equal(HttpStatusCode.Conflict, demote.StatusCode);
        Assert.Equal(1, await e.ActiveSuperAdmins());
        Assert.True(await e.Seed.Db(db => db.AuditEvents.CountAsync(a => a.Outcome == Outcome.Failure && a.Action.StartsWith("user."))) >= 3);

        var second = await e.Seed.UserAsync(role: "super_admin"); // with a second one the first may go
        Assert.Equal(HttpStatusCode.OK, (await op.Post($"/api/users/{only.Id}/suspend", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await op.Post($"/api/users/{second.Id}/suspend", new { })).StatusCode); // and now the second is the last
        Assert.Equal(1, await e.ActiveSuperAdmins());
    }

    [Fact]
    public async Task A_super_admin_cannot_remove_their_own_super_admin_role_even_when_others_exist()
    {
        var e = await NewEnv(); var a = await e.Seed.UserAsync(role: "super_admin"); await e.Seed.UserAsync(role: "super_admin");
        var b = TestBrowser.Create(e.F); await b.LoginAsync(a.Email, Seed.Password); await b.Post("/api/auth/reauth", new { password = Seed.Password });
        var r = await b.SendAsync(HttpMethod.Put, $"/api/users/{a.Id}/roles", new { roleIds = new[] { await e.Seed.RoleId("user") } });
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode); Assert.Contains("own Super Admin role", (await r.Body()).GetProperty("error").GetProperty("message").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await b.Post($"/api/users/{a.Id}/suspend", new { })).StatusCode); // nor suspend themselves
    }

    [Fact]
    public async Task Two_operators_racing_to_remove_the_last_two_super_admins_cannot_leave_zero()
    {
        var e = await NewEnv(); var (op1, _) = await e.Operator("ops_all"); var (op2, _) = await e.Operator("ops_all");
        var s1 = await e.Seed.UserAsync(role: "super_admin"); var s2 = await e.Seed.UserAsync(role: "super_admin");
        for (var round = 0; round < 6; round++)
        {
            var results = await Task.WhenAll(op1.Post($"/api/users/{s1.Id}/suspend", new { }), op2.Post($"/api/users/{s2.Id}/suspend", new { }));
            Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.OK)); // row locks serialise the two transactions
            Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.Conflict));
            Assert.Equal(1, await e.ActiveSuperAdmins());
            await e.Seed.Db(async db => { foreach (var u in await db.Users.Where(x => x.Id == s1.Id || x.Id == s2.Id).ToListAsync()) u.Status = UserStatus.Active; return await db.SaveChangesAsync(); }); // reset
        }
    }

    // ---------- the database refuses it even if application code is bypassed
    [Fact]
    public async Task The_database_itself_rejects_removing_the_last_active_super_admin()
    {
        var e = await NewEnv(); var only = await e.Seed.UserAsync(role: "super_admin"); var plain = await e.Seed.UserAsync();

        await using var conn = new NpgsqlConnection(e.Cs); await conn.OpenAsync();
        async Task<Exception?> Try(string sql) { try { await using var c = conn.CreateCommand(); c.CommandText = sql; await c.ExecuteNonQueryAsync(); return null; } catch (PostgresException x) { return x; } }
        Assert.Null(await Try($"UPDATE users SET status = 'Suspended' WHERE id = '{plain.Id}'")); // someone who is not a super admin: fine
        Assert.Null(await Try($"UPDATE users SET status = 'Active' WHERE id = '{plain.Id}'"));

        var viaStatus = await Try($"UPDATE users SET status = 'Suspended' WHERE id = '{only.Id}'");
        Assert.Contains("at least one active super_admin must remain", viaStatus!.Message);
        var viaRole = await Try($"DELETE FROM user_roles WHERE user_id = '{only.Id}'");
        Assert.Contains("at least one active super_admin must remain", viaRole!.Message);
        var viaDelete = await Try($"DELETE FROM users WHERE id = '{only.Id}'");
        Assert.Contains("at least one active super_admin must remain", viaDelete!.Message);
        Assert.Equal(1, await e.ActiveSuperAdmins());

        // two admins: swapping inside ONE transaction is allowed because the check is deferred to commit
        var other = await e.Seed.UserAsync(role: "user"); var saRole = await e.Seed.RoleId("super_admin");
        await using var tx = await conn.BeginTransactionAsync();
        await using (var c = conn.CreateCommand()) { c.Transaction = tx; c.CommandText = $"INSERT INTO user_roles (user_id, role_id, assigned_at) VALUES ('{other.Id}', '{saRole}', now()); DELETE FROM user_roles WHERE user_id = '{only.Id}' AND role_id = '{saRole}'"; await c.ExecuteNonQueryAsync(); }
        await tx.CommitAsync();
        Assert.Equal(1, await e.ActiveSuperAdmins());
        Assert.NotNull(await Try($"DELETE FROM user_roles WHERE user_id = '{other.Id}' AND role_id = '{saRole}'")); // …but now it is the last one again
    }
}
