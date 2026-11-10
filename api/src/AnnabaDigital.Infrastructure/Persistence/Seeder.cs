using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Infrastructure.Persistence;

/// <summary>Idempotent reference-data seed: permission catalog, system roles (+ their permissions), default settings. Never creates users.</summary>
public static class Seeder
{
    public static async Task SeedReferenceDataAsync(AnnabaDigitalDbContext db, DateTimeOffset now, CancellationToken ct = default)
    {
        foreach (var def in Permissions.Catalog)
        {
            var p = await db.Permissions.FindAsync([def.Key], ct);
            if (p is null) db.Permissions.Add(new Permission { Key = def.Key, Module = def.Module, Description = def.Description, IsCritical = def.Critical });
            else { p.Module = def.Module; p.Description = def.Description; p.IsCritical = def.Critical; }
        }
        await db.SaveChangesAsync(ct);

        if (!await db.Tenants.AnyAsync(t => t.Id == Tenant.DefaultId, ct))
            db.Tenants.Add(new Tenant { Id = Tenant.DefaultId, Slug = "default", Name = "Default organisation", IsPlatform = true, CreatedAt = now });
        await db.SaveChangesAsync(ct);

        foreach (var tenantId in await db.Tenants.Select(t => t.Id).ToListAsync(ct))
        {
            await SeedSystemRolesAsync(db, tenantId, now, ct);
            await AnnabaDigital.Application.Helpdesk.HelpdeskDefaults.SeedAsync(db, tenantId, now, ct);
        }

        foreach (var (key, value) in SecuritySettings.Defaults)
        {
            if (await db.Settings.AnyAsync(s => s.Key == key, ct)) continue;
            db.Settings.Add(new SystemSetting { Key = key, ValueJson = JsonSerializer.Serialize(value), UpdatedAt = now });
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Every tenant owns its own copy of the system roles, so tuning one organisation's roles never touches another's.</summary>
    public static async Task SeedSystemRolesAsync(AnnabaDigitalDbContext db, Guid tenantId, DateTimeOffset now, CancellationToken ct = default)
    {
        foreach (var def in SystemRoles.Seed)
        {
            var role = await db.Roles.IgnoreQueryFilters().Include(r => r.Permissions).FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Key == def.Key, ct);
            if (role is null)
            {
                role = new Role { TenantId = tenantId, Key = def.Key, Name = def.Name, Description = def.Description, IsSystem = true, IsProtected = def.Protected, CreatedAt = now, UpdatedAt = now };
                foreach (var key in def.Permissions) role.Permissions.Add(new RolePermission { PermissionKey = key, GrantedAt = now });
                db.Roles.Add(role);
            }
            else if (role.IsProtected)
            {
                // Protected roles always mirror the code definition; other system roles are left as the operator tuned them.
                var have = role.Permissions.Select(p => p.PermissionKey).ToHashSet();
                foreach (var key in def.Permissions.Where(k => !have.Contains(k))) role.Permissions.Add(new RolePermission { PermissionKey = key, GrantedAt = now });
                role.Permissions.RemoveAll(p => !def.Permissions.Contains(p.PermissionKey));
            }
        }
        await db.SaveChangesAsync(ct);
    }
}
