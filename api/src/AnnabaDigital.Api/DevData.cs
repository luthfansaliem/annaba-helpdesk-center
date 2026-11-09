using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using AnnabaDigital.Api.Http;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Infrastructure.Persistence;

namespace AnnabaDigital.Api;

/// <summary>Development-only demo accounts (never runs in Production; StartupChecks also rejects the flag there).</summary>
public static class DevData
{
    public static readonly (string Email, string Name, string Role)[] Users =
    [
        ("sarah.wijaya@annabadigital.io", "Sarah Wijaya", "super_admin"), ("daniel.tan@annabadigital.io", "Daniel Tan", "user_admin"),
        ("maya.putri@annabadigital.io", "Maya Putri", "security_auditor"), ("rizky.pratama@annabadigital.io", "Rizky Pratama", "user"),
    ];

    public static async Task SeedAsync(IServiceProvider sp)
    {
        var db = sp.GetRequiredService<AnnabaDigitalDbContext>();
        var hasher = sp.GetRequiredService<IPasswordHasher>();
        var clock = sp.GetRequiredService<IClock>();
        var pw = sp.GetRequiredService<IOptions<ApiOptions>>().Value.DevPassword;
        foreach (var (email, name, roleKey) in Users)
        {
            if (await db.Users.AnyAsync(u => u.Email == email)) continue;
            var role = await db.Roles.FirstAsync(r => r.TenantId == Tenant.DefaultId && r.Key == roleKey);
            var now = clock.UtcNow;
            var u = new User { Email = email, Name = name, Status = UserStatus.Active, EmailVerifiedAt = now, CreatedAt = now, UpdatedAt = now };
            u.Credential = new UserCredential { PasswordHash = hasher.Hash(pw), PasswordChangedAt = now };
            u.Profile = new UserProfile();
            u.UserRoles.Add(new UserRole { RoleId = role.Id, AssignedAt = now });
            db.Users.Add(u);
        }
        await db.SaveChangesAsync();
    }
}
