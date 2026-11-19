using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using AnnabaDigital.Api.Http;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Infrastructure.Persistence;

namespace AnnabaDigital.Api;

/// <summary>
/// Development-only cast for a product demo (<c>Api:SeedDemoUsers=true</c>, together with <c>Api:SeedDevUsers</c>): one signed-in person per role
/// of the helpdesk, with departments and direct managers, so every flow can be shown from the right seat. Same password as the other dev accounts.
/// Never runs in Production (StartupChecks rejects the flag there). Idempotent: people that exist are left alone.
/// </summary>
public static class DemoPersonas
{
    public sealed record Person(string Email, string Name, string Role, string Department, string Title, string? Manager, string Phone);

    public static readonly Person[] Cast =
    [
        // the helpdesk team
        new("hendra.kusuma@annabadigital.io", "Hendra Kusuma", "ga_manager", "General Affairs", "GA Manager", "dimas.prakoso@annabadigital.io", "+62 812 1000 0001"),
        new("putri.anggraini@annabadigital.io", "Putri Anggraini", "ga_supervisor", "General Affairs", "GA Supervisor", "hendra.kusuma@annabadigital.io", "+62 812 1000 0002"),
        new("agus.setiawan@annabadigital.io", "Agus Setiawan", "ga_agent", "General Affairs", "Teknisi AC & listrik", "putri.anggraini@annabadigital.io", "+62 812 1000 0003"),
        new("bayu.nugroho@annabadigital.io", "Bayu Nugroho", "ga_agent", "General Affairs", "Teknisi umum & kebersihan", "putri.anggraini@annabadigital.io", "+62 812 1000 0004"),
        new("sinta.maharani@annabadigital.io", "Sinta Maharani", "receptionist", "General Affairs", "Resepsionis", "putri.anggraini@annabadigital.io", "+62 812 1000 0005"),
        // a vendor's technician (linked to PT Sejuk Abadi by the demo script)
        new("joko.susilo@sejukabadi.co.id", "Joko Susilo", "vendor", "PT Sejuk Abadi", "Teknisi lapangan", null, "+62 812 1000 0006"),
        // employees, with a direct manager so approvals and supply requests have someone to go to
        new("dimas.prakoso@annabadigital.io", "Dimas Prakoso", "user", "Operations", "Head of Operations", null, "+62 812 1000 0010"),
        new("anita.rahmawati@annabadigital.io", "Anita Rahmawati", "user", "Human Resources", "HR Specialist", "dimas.prakoso@annabadigital.io", "+62 812 1000 0011"),
        new("fajar.ramadhan@annabadigital.io", "Fajar Ramadhan", "user", "Finance", "Finance Analyst", "dimas.prakoso@annabadigital.io", "+62 812 1000 0012"),
        new("lina.marlina@annabadigital.io", "Lina Marlina", "user", "Marketing", "Marketing Executive", "dimas.prakoso@annabadigital.io", "+62 812 1000 0013"),
    ];

    public static async Task SeedAsync(IServiceProvider sp)
    {
        var db = sp.GetRequiredService<AnnabaDigitalDbContext>();
        var hasher = sp.GetRequiredService<IPasswordHasher>();
        var clock = sp.GetRequiredService<IClock>();
        var pw = sp.GetRequiredService<IOptions<ApiOptions>>().Value.DevPassword;
        var now = clock.UtcNow;

        foreach (var p in Cast)
        {
            if (await db.Users.AnyAsync(u => u.Email == p.Email)) continue;
            var role = await db.Roles.FirstAsync(r => r.TenantId == Tenant.DefaultId && r.Key == p.Role);
            var u = new User { Email = p.Email, Name = p.Name, Status = UserStatus.Active, EmailVerifiedAt = now, CreatedAt = now.AddDays(-90), UpdatedAt = now };
            u.Credential = new UserCredential { PasswordHash = hasher.Hash(pw), PasswordChangedAt = now };
            u.Profile = new UserProfile { Department = p.Department, JobTitle = p.Title, Phone = p.Phone, Language = "id" };
            u.UserRoles.Add(new UserRole { RoleId = role.Id, AssignedAt = now });
            db.Users.Add(u);
        }
        await db.SaveChangesAsync();

        // second pass: direct managers (needs everybody to exist), plus a manager for the existing demo requester
        var byEmail = await db.Users.Include(u => u.Profile).ToDictionaryAsync(u => u.Email);
        foreach (var p in Cast.Where(p => p.Manager is not null))
            if (byEmail.TryGetValue(p.Email, out var u) && byEmail.TryGetValue(p.Manager!, out var m) && u.Profile is { ManagerId: null } prof) prof.ManagerId = m.Id;
        if (byEmail.TryGetValue("rizky.pratama@annabadigital.io", out var rizky) && byEmail.TryGetValue("dimas.prakoso@annabadigital.io", out var dimas) && rizky.Profile is { } rp)
        {
            rp.ManagerId ??= dimas.Id; rp.Department ??= "Operations"; rp.JobTitle ??= "Operations Officer"; rp.Language = "id";
        }
        await db.SaveChangesAsync();
    }
}
