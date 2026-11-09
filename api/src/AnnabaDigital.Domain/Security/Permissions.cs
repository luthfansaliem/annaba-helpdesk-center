namespace AnnabaDigital.Domain.Security;

/// <summary>Permission catalog — single source of truth (seeded into the DB, never edited at runtime).</summary>
public static class Permissions
{
    public const string UsersRead = "users.read";
    public const string UsersCreate = "users.create";
    public const string UsersUpdate = "users.update";
    public const string UsersSuspend = "users.suspend";
    public const string UsersAssignRole = "users.assign_role";
    public const string InvitationsRead = "invitations.read";
    public const string InvitationsManage = "invitations.manage";
    public const string RolesRead = "roles.read";
    public const string RolesCreate = "roles.create";
    public const string RolesUpdate = "roles.update";
    public const string PermissionsRead = "permissions.read";
    public const string PermissionsAssign = "permissions.assign";
    public const string SessionsReadAll = "sessions.read_all";
    public const string SessionsRevoke = "sessions.revoke";
    public const string AuditRead = "audit.read";
    public const string SecurityRead = "security.read";
    public const string SettingsManage = "settings.manage";
    public const string TenantsManage = "tenants.manage";
    public const string UsersResetCredentials = "users.reset_credentials";

    public sealed record Def(string Key, string Module, string Description, bool Critical = false);

    public static readonly IReadOnlyList<Def> Catalog =
    [
        new(UsersRead, "Users", "View users and their details"),
        new(UsersCreate, "Users", "Create users"),
        new(UsersUpdate, "Users", "Edit user profiles"),
        new(UsersSuspend, "Users", "Suspend, deactivate and unlock accounts"),
        new(UsersAssignRole, "Users", "Assign and remove roles on users", true),
        new(UsersResetCredentials, "Users", "Reset someone's two-step verification and require a new password", true),
        new(InvitationsRead, "Invitations", "View invitations"),
        new(InvitationsManage, "Invitations", "Create, resend and revoke invitations"),
        new(RolesRead, "Roles", "View roles"),
        new(RolesCreate, "Roles", "Create and duplicate roles"),
        new(RolesUpdate, "Roles", "Edit and archive roles"),
        new(PermissionsRead, "Permissions", "View permission catalog and access matrix"),
        new(PermissionsAssign, "Permissions", "Change role permission assignments", true),
        new(SessionsReadAll, "Sessions", "View sessions of other users"),
        new(SessionsRevoke, "Sessions", "Revoke sessions of other users"),
        new(AuditRead, "Monitoring", "View audit logs"),
        new(SecurityRead, "Monitoring", "View and acknowledge security events"),
        new(SettingsManage, "Administration", "Manage system settings", true),
        new(TenantsManage, "Administration", "Create and suspend tenants (platform organisation only)", true),
    ];

    public static IEnumerable<string> All => Catalog.Select(c => c.Key);
}

/// <summary>Seed roles (matches docs/09-rbac-matrix.md).</summary>
public static class SystemRoles
{
    public const string SuperAdmin = "super_admin";
    public const string UserAdmin = "user_admin";
    public const string SecurityAuditor = "security_auditor";
    public const string User = "user";

    public sealed record Def(string Key, string Name, string Description, bool Protected, string[] Permissions);

    public static readonly IReadOnlyList<Def> Seed =
    [
        new(SuperAdmin, "Super Admin", "Unrestricted access. Permission set is immutable.", true, [.. Permissions.All]),
        new(UserAdmin, "User Admin", "Manages users and invitations.", false,
        [
            Permissions.UsersRead, Permissions.UsersCreate, Permissions.UsersUpdate, Permissions.UsersSuspend, Permissions.UsersAssignRole,
            Permissions.InvitationsRead, Permissions.InvitationsManage, Permissions.RolesRead, Permissions.SessionsReadAll, Permissions.SessionsRevoke,
        ]),
        new(SecurityAuditor, "Security Auditor", "Read-only access to monitoring and sessions.", false,
        [
            Permissions.UsersRead, Permissions.RolesRead, Permissions.PermissionsRead, Permissions.SessionsReadAll, Permissions.AuditRead, Permissions.SecurityRead,
        ]),
        new(User, "User", "Self-service only. No administrative permissions.", false, []),
    ];
}
