using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Application.Users;

public sealed record CreateTenantRequest(string? Name, string? Slug, string? AdminEmail);
public sealed record TenantItem(Guid Id, string Slug, string Name, string Status, bool IsPlatform, int Users, DateTimeOffset CreatedAt);

/// <summary>
/// TEN-001. Only administrators of the platform organisation manage tenants. A new tenant gets its own copy of the system roles and a pending Super Admin
/// invitation for the address given; from then on that organisation administers itself and can never see another tenant's data.
/// </summary>
public sealed partial class TenantService(IAppDb db, IClock clock, ITenantContext tenant, SettingsProvider settings, InvitationAdminService invitations, AuditWriter audit)
{
    [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]{0,38}[a-z0-9])?$")] private static partial Regex SlugRule();

    private void RequirePlatform(SessionPrincipal actor)
    {
        if (!tenant.IsPlatform) throw DomainException.DeniedBecause("Only the platform organisation can manage tenants.");
    }

    public async Task<List<TenantItem>> ListAsync(SessionPrincipal actor, CancellationToken ct = default)
    {
        RequirePlatform(actor);
        var counts = await db.Users.IgnoreQueryFilters().AsNoTracking().GroupBy(u => u.TenantId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
        var rows = await db.Tenants.AsNoTracking().OrderByDescending(t => t.IsPlatform).ThenBy(t => t.Name).ToListAsync(ct);
        return [.. rows.Select(t => new TenantItem(t.Id, t.Slug, t.Name, t.Status.ToString().ToLowerInvariant(), t.IsPlatform, counts.GetValueOrDefault(t.Id), t.CreatedAt))];
    }

    public async Task<TenantItem> CreateAsync(CreateTenantRequest req, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        RequirePlatform(actor);
        Guards.RequireRecentReauthForCritical(actor, await settings.GetAsync(ct), clock);
        var name = (req.Name ?? "").Trim(); var slug = (req.Slug ?? "").Trim().ToLowerInvariant(); var admin = AuthService.NormalizeEmail(req.AdminEmail);
        var fields = new Dictionary<string, string>();
        if (name.Length < 2 || name.Length > 200) fields["name"] = "Enter the organisation's name.";
        if (!SlugRule().IsMatch(slug)) fields["slug"] = "Use 1–40 lowercase letters, digits or hyphens.";
        if (admin.Length == 0 || admin.Length > 254 || !Regex.IsMatch(admin, @"^[^\s@]+@[^\s@]+\.[^\s@]+$")) fields["adminEmail"] = "Enter a valid email address for the first administrator.";
        if (fields.Count > 0) throw DomainException.Validation("Please correct the highlighted fields.", fields);
        if (await db.Tenants.AnyAsync(t => t.Slug == slug, ct)) throw DomainException.Conflict("That identifier is already taken.");
        if (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == admin, ct)) throw DomainException.Conflict("A user with this email already exists.");

        var now = clock.UtcNow;
        var t = new Tenant { Slug = slug, Name = name, CreatedAt = now };
        db.Tenants.Add(t);
        audit.Record("tenant.create", "tenant", t.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["slug"] = slug, ["name"] = name, ["admin"] = admin });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { throw DomainException.Conflict("That identifier is already taken."); }

        using (tenant.Switch(t.Id)) // everything below belongs to the new tenant
        {
            Guid superAdmin = default;
            foreach (var def in SystemRoles.Seed)
            {
                var role = new Role { Key = def.Key, Name = def.Name, Description = def.Description, IsSystem = true, IsProtected = def.Protected, CreatedAt = now, UpdatedAt = now };
                foreach (var key in def.Permissions) role.Permissions.Add(new RolePermission { PermissionKey = key, GrantedAt = now });
                db.Roles.Add(role);
                if (def.Key == SystemRoles.SuperAdmin) superAdmin = role.Id;
            }
            await db.SaveChangesAsync(ct);
            await invitations.CreateAsync(new CreateInvitationRequest(admin, [superAdmin], $"You have been set up as the first administrator of {name}."), actor, client, ct);
        }
        return (await ListAsync(actor, ct)).First(x => x.Id == t.Id);
    }

    public async Task SetStatusAsync(Guid id, bool suspend, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        RequirePlatform(actor);
        Guards.RequireRecentReauthForCritical(actor, await settings.GetAsync(ct), clock);
        var t = await db.Tenants.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Tenant");
        if (t.IsPlatform) throw DomainException.Invariant("The platform organisation can't be suspended.");
        var to = suspend ? TenantStatus.Suspended : TenantStatus.Active;
        if (t.Status == to) throw DomainException.Conflict(suspend ? "This tenant is already suspended." : "This tenant is already active.");
        t.Status = to;
        if (suspend)
        {
            var now = clock.UtcNow;
            await db.Sessions.IgnoreQueryFilters().Where(s => s.TenantId == id && s.RevokedAt == null).ExecuteUpdateAsync(u => u.SetProperty(s => s.RevokedAt, now).SetProperty(s => s.RevokedReason, RevokeReason.Suspended), ct);
        }
        audit.Record(suspend ? "tenant.suspend" : "tenant.activate", "tenant", id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, new Dictionary<string, string> { ["slug"] = t.Slug });
        audit.Security(suspend ? "tenant.suspended" : "tenant.activated", Severity.Medium, $"Tenant {t.Name} {(suspend ? "suspended" : "re-activated")}", actor.UserId);
        await db.SaveChangesAsync(ct);
    }

    // ============================================================ support mode
    /// <summary>
    /// A platform administrator administers another organisation from their own session ("support mode"): the admin screens (users, roles, invitations, audit, reviews…)
    /// then show that organisation, everything they do is stamped into ITS audit trail as "(platform support)", and a security event tells its administrators.
    /// Needs a fresh re-authentication, ends by itself (<see cref="SessionService.SupportMax"/>), and never reaches platform-only settings from inside.
    /// </summary>
    public async Task EnterSupportAsync(Guid id, SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        RequirePlatform(actor);
        Guards.RequireRecentReauthForCritical(actor, await settings.GetAsync(ct), clock);
        var t = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound("Tenant");
        if (t.IsPlatform) throw DomainException.Invariant("This is your own organisation. Support mode is for other tenants.");
        if (t.Status != TenantStatus.Active) throw DomainException.Conflict("This tenant is suspended. Re-activate it first.");
        var now = clock.UtcNow;
        await db.Sessions.IgnoreQueryFilters().Where(x => x.Id == actor.SessionId).ExecuteUpdateAsync(u => u.SetProperty(x => x.ActingTenantId, id).SetProperty(x => x.ActingSince, now), ct);
        await RecordSupportAsync("tenant.support.enter", t, actor, client, $"Platform support entered {t.Name}", ct);
    }

    public async Task ExitSupportAsync(SessionPrincipal actor, ClientInfo client, CancellationToken ct = default)
    {
        RequirePlatform(actor);
        if (actor.ActingTenantId is not { } id) return; // not in support mode: nothing to do
        await db.Sessions.IgnoreQueryFilters().Where(x => x.Id == actor.SessionId).ExecuteUpdateAsync(u => u.SetProperty(x => x.ActingTenantId, (Guid?)null).SetProperty(x => x.ActingSince, (DateTimeOffset?)null), ct);
        var t = await db.Tenants.AsNoTracking().FirstAsync(x => x.Id == id, ct);
        await RecordSupportAsync("tenant.support.exit", t, actor, client, $"Platform support left {t.Name}", ct);
    }

    /// <summary>One audit row in the platform's own trail and one (plus a security event) in the organisation's, so both sides can see it.</summary>
    private async Task RecordSupportAsync(string action, Tenant target, SessionPrincipal actor, ClientInfo client, string detail, CancellationToken ct)
    {
        var meta = new Dictionary<string, string> { ["tenant"] = target.Name, ["slug"] = target.Slug };
        audit.Record(action, "tenant", target.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, meta);
        await db.SaveChangesAsync(ct);
        using (tenant.Switch(target.Id))
        {
            audit.Record(action, "tenant", target.Id.ToString(), Outcome.Success, client, actor.UserId, actor.Email, meta);
            audit.Security("support.access", Severity.Medium, detail + $" ({actor.Email})");
            await db.SaveChangesAsync(ct);
        }
    }
}
