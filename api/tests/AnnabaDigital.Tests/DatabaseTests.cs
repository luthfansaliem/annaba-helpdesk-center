using Microsoft.EntityFrameworkCore;
using Npgsql;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Domain.Security;
using AnnabaDigital.Infrastructure.Persistence;
using AnnabaDigital.Tests.Infra;
using Xunit;

namespace AnnabaDigital.Tests;

[Collection("db")]
public sealed class DatabaseTests(PostgresFixture fx)
{
    private readonly Seed _seed = new(fx.Factory, fx.Clock);

    [Fact]
    public async Task Seed_is_idempotent_and_matches_the_rbac_matrix()
    {
        await _seed.Db(async db => { await Seeder.SeedReferenceDataAsync(db, fx.Clock.UtcNow); await Seeder.SeedReferenceDataAsync(db, fx.Clock.UtcNow); return 0; });
        await _seed.Db(async db =>
        {
            Assert.Equal(Permissions.Catalog.Count, await db.Permissions.CountAsync());
            Assert.Equal(1, await db.Roles.CountAsync(r => r.TenantId == Tenant.DefaultId && r.Key == "super_admin"));
            var superPerms = await (from r in db.Roles where r.TenantId == Tenant.DefaultId && r.Key == "super_admin" join rp in db.RolePermissions on r.Id equals rp.RoleId select rp.PermissionKey).ToListAsync();
            Assert.Equal(Permissions.All.Order(), superPerms.Order());
            Assert.Empty(await (from r in db.Roles where r.TenantId == Tenant.DefaultId && r.Key == "user" join rp in db.RolePermissions on r.Id equals rp.RoleId select rp).ToListAsync());
            Assert.True(await db.Settings.AnyAsync(s => s.Key == "lockoutAttempts"));
            return 0;
        });
    }

    [Fact]
    public async Task Email_must_be_lower_case_and_unique()
    {
        var email = $"case{Guid.NewGuid():N}@test.local";
        await _seed.UserAsync(email);
        await Assert.ThrowsAnyAsync<DbUpdateException>(() => _seed.UserAsync(email)); // unique
        await Assert.ThrowsAnyAsync<DbUpdateException>(() => _seed.Db(async db =>
        {
            db.Users.Add(new User { Email = "UPPER@test.local", Name = "Upper Case", CreatedAt = fx.Clock.UtcNow, UpdatedAt = fx.Clock.UtcNow });
            return await db.SaveChangesAsync();
        })); // check constraint ck_users_email_lower
    }

    [Fact]
    public async Task Only_one_pending_invitation_per_email_is_allowed()
    {
        var u = await _seed.UserAsync(role: "user_admin");
        var email = $"inv{Guid.NewGuid():N}@test.local";
        Invitation Make(string hash, InvitationStatus st) => new() { Email = email, TokenHash = hash, InvitedBy = u.Id, Status = st, CreatedAt = fx.Clock.UtcNow, ExpiresAt = fx.Clock.UtcNow.AddDays(7) };
        await _seed.Db(async db => { db.Invitations.Add(Make("h1" + Guid.NewGuid(), InvitationStatus.Pending)); return await db.SaveChangesAsync(); });
        await Assert.ThrowsAnyAsync<DbUpdateException>(() => _seed.Db(async db => { db.Invitations.Add(Make("h2" + Guid.NewGuid(), InvitationStatus.Pending)); return await db.SaveChangesAsync(); }));
        await _seed.Db(async db => { db.Invitations.Add(Make("h3" + Guid.NewGuid(), InvitationStatus.Revoked)); return await db.SaveChangesAsync(); }); // history is fine
    }

    [Fact]
    public async Task Audit_log_is_append_only_in_the_database_itself()
    {
        var u = await _seed.UserAsync();
        long id = await _seed.Db(async db =>
        {
            var e = new AuditEvent { Ts = fx.Clock.UtcNow, Action = "test.append", TargetType = "test", Outcome = Outcome.Success, ActorId = u.Id };
            db.AuditEvents.Add(e); await db.SaveChangesAsync(); return e.Id;
        });
        var upd = await Assert.ThrowsAnyAsync<Exception>(() => _seed.Db(db => db.Database.ExecuteSqlAsync($"UPDATE audit_events SET action = 'tampered' WHERE id = {id}")));
        Assert.Contains("append-only", upd.ToString());
        var del = await Assert.ThrowsAnyAsync<Exception>(() => _seed.Db(db => db.Database.ExecuteSqlAsync($"DELETE FROM audit_events WHERE id = {id}")));
        Assert.Contains("append-only", del.ToString());
        var trunc = await Assert.ThrowsAnyAsync<Exception>(() => _seed.Db(db => db.Database.ExecuteSqlRawAsync("TRUNCATE audit_events")));
        Assert.Contains("append-only", trunc.ToString());

        // retention purge: allowed only inside the explicit maintenance context
        await _seed.Db(async db =>
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlRawAsync("SET LOCAL annabadigital.allow_audit_purge = 'on'");
            var n = await db.Database.ExecuteSqlAsync($"DELETE FROM audit_events WHERE id = {id}");
            await tx.CommitAsync(); Assert.Equal(1, n); return n;
        });
    }

    [Fact]
    public async Task Session_token_hashes_are_unique()
    {
        var u = await _seed.UserAsync();
        await _seed.Db(async db => { db.Sessions.Add(new Session { TokenHash = "dup-hash-" + u.Id, UserId = u.Id, CreatedAt = fx.Clock.UtcNow, LastActiveAt = fx.Clock.UtcNow, IdleExpiresAt = fx.Clock.UtcNow, AbsoluteExpiresAt = fx.Clock.UtcNow }); return await db.SaveChangesAsync(); });
        await Assert.ThrowsAnyAsync<DbUpdateException>(() => _seed.Db(async db => { db.Sessions.Add(new Session { TokenHash = "dup-hash-" + u.Id, UserId = u.Id, CreatedAt = fx.Clock.UtcNow, LastActiveAt = fx.Clock.UtcNow, IdleExpiresAt = fx.Clock.UtcNow, AbsoluteExpiresAt = fx.Clock.UtcNow }); return await db.SaveChangesAsync(); }));
    }
}
