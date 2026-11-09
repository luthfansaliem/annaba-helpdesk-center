using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;

namespace AnnabaDigital.Application.Common;

public static class Guards
{
    public static readonly TimeSpan ReauthWindow = TimeSpan.FromMinutes(5);

    /// <summary>Critical operations need a password re-entry in the last 5 minutes (setting <c>requireReauthForCritical</c>).</summary>
    public static void RequireRecentReauthForCritical(SessionPrincipal actor, SecuritySettings settings, IClock clock)
    {
        if (!settings.RequireReauthForCritical) return;
        if (actor.ReauthAt is not { } t || clock.UtcNow - t > ReauthWindow) throw DomainException.ReauthRequired();
    }

    /// <summary>Privilege-escalation rule: you may only hand out what you hold — every permission of the role must be in your own set.</summary>
    public static bool CanGrant(IReadOnlySet<string> actorPermissions, IEnumerable<string> rolePermissions) => rolePermissions.All(actorPermissions.Contains);

    /// <summary>Hierarchy rule: you may not act on someone whose effective permissions exceed yours (a User Admin cannot suspend a Super Admin).</summary>
    public static bool CanManage(Guid actorId, IReadOnlySet<string> actorPermissions, Guid targetId, IEnumerable<string> targetPermissions)
        => actorId == targetId || targetPermissions.All(actorPermissions.Contains);

    public static bool IsActive(User u) => u.Status == UserStatus.Active;
}
