using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Application.Platform;

// SPIKE H00 (2026-11-24). Not a finished integration: it answers one question, "can this application be entered from Annaba Workspace without rewriting it?".
// What it shows and what it leaves out is written in docs/71-spike-h00-core-contract.md.

/// <summary>The principal Annaba Core returns from the launch exchange (Core: docs/workspace/03-app-contract.md §3).</summary>
public sealed record CoreAccount(string Ref, string Email, string Name, string? Language);
public sealed record CoreWorkspace(string Key, string Name, string? Timezone);
public sealed record CoreAccess(string Role, int MembershipRevision, int GrantRevision);
public sealed record CorePrincipal(CoreAccount Account, CoreWorkspace Workspace, CoreAccess Access, string? App);

public interface ICoreExchange
{
    /// <summary>Spends a launch code with Core. Null = Core refused it (replayed, expired, foreign, wrong state, access changed).</summary>
    Task<CorePrincipal?> ExchangeAsync(string code, string? state, CancellationToken ct);
}

public sealed class CoreOptions
{
    public string BaseUrl { get; set; } = ""; public string ServiceKey { get; set; } = ""; public string AdapterSecret { get; set; } = ""; public string AppKey { get; set; } = "helpdesk";
    public bool Configured => BaseUrl.Length > 0 && ServiceKey.Length > 0 && AdapterSecret.Length >= 32;
}

/// <summary>
/// The seam between Annaba Workspace and this application's own model. A Workspace is a tenant (found by its Workspace key); a member is a user of that
/// tenant (found by the account reference Core vouches for, never by e-mail); the role Workspace granted picks one of this application's system roles.
/// Everything behind the session is unchanged: the same tenant filter, the same permissions, the same screens.
/// </summary>
public sealed partial class WorkspaceBridge(IAppDb db, IClock clock, ITenantContext tenant, SessionService sessions, ICoreExchange core, Microsoft.Extensions.Options.IOptions<CoreOptions> options)
{
    /// <summary>Which of this application's roles a role granted in Workspace stands for. Unknown roles get the plain employee role: they can ask, nothing more.</summary>
    public static string RoleFor(string coreRole) => coreRole switch { "app_admin" => SystemRoles.SuperAdmin, "operator" => SystemRoles.GaAgent, "supervisor" => SystemRoles.GaSupervisor, "manager" => SystemRoles.GaManager, _ => SystemRoles.User };

    [GeneratedRegex("[^a-z0-9]+")] private static partial Regex NotSlug();

    /// <summary>Core delivered an entitlement: the Workspace gets its tenant here (once), with the same roles and starter set-up a tenant made by hand gets.</summary>
    public async Task<int> ApplyEntitlementAsync(string workspaceKey, int revision, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(workspaceKey) || workspaceKey.Length > 60 || revision <= 0) throw DomainException.Validation("Invalid entitlement.", new Dictionary<string, string>());
        var now = clock.UtcNow;
        var t = await db.Tenants.FirstOrDefaultAsync(x => x.WorkspaceKey == workspaceKey, ct);
        if (t is null)
        {
            var slug = NotSlug().Replace(workspaceKey.ToLowerInvariant(), "-").Trim('-'); if (slug.Length > 40) slug = slug[..40].Trim('-');
            t = new Tenant { Slug = slug, Name = "Workspace " + workspaceKey, WorkspaceKey = workspaceKey, CreatedAt = now };
            db.Tenants.Add(t); await db.SaveChangesAsync(ct);
            using (tenant.Switch(t.Id))
            {
                foreach (var def in SystemRoles.Seed)
                {
                    var role = new Role { Key = def.Key, Name = def.Name, Description = def.Description, IsSystem = true, IsProtected = def.Protected, CreatedAt = now, UpdatedAt = now };
                    foreach (var key in def.Permissions) role.Permissions.Add(new RolePermission { PermissionKey = key, GrantedAt = now });
                    db.Roles.Add(role);
                }
                await db.SaveChangesAsync(ct);
                await Helpdesk.HelpdeskDefaults.SeedAsync(db, t.Id, now, ct);
            }
        }
        if (revision > t.EntitlementRevision) { t.EntitlementRevision = revision; await db.SaveChangesAsync(ct); } // an older revision arriving late never lowers it
        return t.EntitlementRevision;
    }

    /// <summary>Spends a launch code and returns a session of this application for the member Core names. Null token = refused.</summary>
    public async Task<(string? Token, string Reason)> LaunchAsync(string? code, string? state, ClientInfo client, CancellationToken ct = default)
    {
        if (!options.Value.Configured) return (null, "not_configured");
        if (string.IsNullOrWhiteSpace(code) || code.Length > 200) return (null, "launch_invalid");
        var p = await core.ExchangeAsync(code, state, ct);
        if (p is null) return (null, "launch_refused");
        if (!string.Equals(p.App, options.Value.AppKey, StringComparison.Ordinal)) return (null, "launch_wrong_audience"); // second lock; Core already refuses another application's key
        var t = await db.Tenants.FirstOrDefaultAsync(x => x.WorkspaceKey == p.Workspace.Key, ct);
        if (t is null || t.EntitlementRevision == 0) return (null, "not_provisioned"); // a grant or a paid invoice alone creates nothing here
        if (t.Status != TenantStatus.Active) return (null, "closed");
        var now = clock.UtcNow;
        using (tenant.Switch(t.Id))
        {
            if (!string.IsNullOrWhiteSpace(p.Workspace.Name) && t.Name != p.Workspace.Name) t.Name = p.Workspace.Name.Length > 200 ? p.Workspace.Name[..200] : p.Workspace.Name;
            var email = (p.Account.Email ?? "").Trim().ToLowerInvariant(); var name = (p.Account.Name ?? "").Trim(); if (name.Length < 2) name = email;
            var user = await db.Users.Include(u => u.UserRoles).FirstOrDefaultAsync(u => u.AccountRef == p.Account.Ref, ct);
            if (user is null)
            {
                user = new User { Email = email, Name = name, AccountRef = p.Account.Ref, Status = UserStatus.Active, EmailVerifiedAt = now, CreatedAt = now, UpdatedAt = now };
                db.Users.Add(user);
            }
            else { user.Email = email; user.Name = name; user.Status = UserStatus.Active; user.UpdatedAt = now; } // the address is display data and may have changed in Workspace
            // The role is what Workspace grants today, not what was granted at an earlier launch.
            var wanted = await db.Roles.Where(r => r.Key == RoleFor(p.Access.Role)).Select(r => r.Id).SingleAsync(ct);
            foreach (var old in user.UserRoles.Where(r => r.RoleId != wanted).ToList()) user.UserRoles.Remove(old);
            if (user.UserRoles.All(r => r.RoleId != wanted)) user.UserRoles.Add(new UserRole { RoleId = wanted, AssignedAt = now });
            user.LastLoginAt = now;
            var created = await sessions.CreateAsync(user, remember: false, mfaRequired: false, client, ct); // the second factor is Workspace's business, not asked again here
            await db.SaveChangesAsync(ct);
            return (created.RawToken, "ok");
        }
    }
}
