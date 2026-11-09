namespace AnnabaDigital.Domain.Entities;

public class Permission
{
    public string Key { get; set; } = "";
    public string Module { get; set; } = "";
    public string Description { get; set; } = "";
    public bool IsCritical { get; set; }
}

public class Role : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public bool IsSystem { get; set; }
    public bool IsProtected { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    /// <summary>Bumped on every change to the role or its permission set (optimistic concurrency).</summary>
    public int Version { get; set; } = 1;
    public List<RolePermission> Permissions { get; set; } = [];
}

public class RolePermission
{
    public Guid RoleId { get; set; }
    public string PermissionKey { get; set; } = "";
    public Guid? GrantedBy { get; set; }
    public DateTimeOffset GrantedAt { get; set; }
}

public class UserRole
{
    public Guid UserId { get; set; }
    public Guid RoleId { get; set; }
    public Guid? AssignedBy { get; set; }
    public DateTimeOffset AssignedAt { get; set; }
    public User? User { get; set; }
    public Role? Role { get; set; }
}

public class Invitation : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Email { get; set; } = "";
    public string TokenHash { get; set; } = "";
    public Guid InvitedBy { get; set; }
    public string? Message { get; set; }
    public InvitationStatus Status { get; set; } = InvitationStatus.Pending;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? AcceptedAt { get; set; }
    public Guid? AcceptedUserId { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public List<InvitationRole> Roles { get; set; } = [];
}

public class InvitationRole
{
    public Guid InvitationId { get; set; }
    public Guid RoleId { get; set; }
}
