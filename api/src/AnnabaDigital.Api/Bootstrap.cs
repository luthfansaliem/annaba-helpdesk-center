using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Email;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Infrastructure.Persistence;

namespace AnnabaDigital.Api;

/// <summary>
/// First-run bootstrap for environments without demo users: when <c>Api:BootstrapAdminEmail</c> is set and the database has no users,
/// issue one normal super-admin invitation to that address. No password ever appears in configuration; the person sets their own
/// through the usual invitation page. Does nothing once any user exists, so leaving the setting in place is harmless.
/// </summary>
public static class Bootstrap
{
    public static readonly TimeSpan Validity = TimeSpan.FromDays(7);

    public static async Task<bool> RunAsync(IServiceProvider sp, string? adminEmail, CancellationToken ct = default)
    {
        var addr = adminEmail?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(addr)) return false;
        var db = sp.GetRequiredService<AnnabaDigitalDbContext>();
        var log = sp.GetRequiredService<ILoggerFactory>().CreateLogger("Bootstrap");
        if (!addr.Contains('@') || addr.Length > 320) { log.LogError("Api:BootstrapAdminEmail is not a valid address; no invitation issued."); return false; }
        if (await db.Users.AnyAsync(ct)) return false;

        var clock = sp.GetRequiredService<IClock>(); var tokens = sp.GetRequiredService<ITokenService>();
        var now = clock.UtcNow;
        // Re-issue on every start until accepted: retire any earlier bootstrap invitation so a lost e-mail can be recovered by restarting.
        foreach (var old in await db.Invitations.Where(i => i.Email == addr && i.Status == InvitationStatus.Pending).ToListAsync(ct)) { old.Status = InvitationStatus.Revoked; old.RevokedAt = now; }
        await db.SaveChangesAsync(ct);

        var role = await db.Roles.SingleAsync(r => r.TenantId == Tenant.DefaultId && r.Key == "super_admin", ct);
        var raw = tokens.NewToken();
        var inv = new Invitation { Email = addr, TokenHash = tokens.Hash(raw), InvitedBy = Guid.Empty, Message = "First administrator", Status = InvitationStatus.Pending, CreatedAt = now, ExpiresAt = now + Validity };
        inv.Roles.Add(new InvitationRole { RoleId = role.Id });
        db.Invitations.Add(inv);
        await db.SaveChangesAsync(ct);

        var link = $"{sp.GetRequiredService<IAppLinks>().Origin.TrimEnd('/')}/invite?token={Uri.EscapeDataString(raw)}";
        sp.GetRequiredService<IEmailQueue>().Enqueue(EmailTemplates.Invitation(addr, "Annaba Digital setup", link, (int)Validity.TotalDays));
        log.LogWarning("No users exist: a super-admin invitation was e-mailed to the configured bootstrap address (valid {Days} days).", (int)Validity.TotalDays);
        return true;
    }
}
