namespace AnnabaDigital.Domain.Entities;

/// <summary>
/// INT-003. "People in this directory group get this role." The group is a name: a value of the SSO token's groups claim, or the display name of a group an
/// identity provider pushed over SCIM. Roles that appear in any mapping are <em>managed by the directory</em>: they are added and removed to follow the person's
/// groups; every other role stays as an administrator set it. Super Admin can never be mapped.
/// </summary>
public class DirectoryGroupMapping : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    /// <summary>Compared without regard to case.</summary>
    public string Group { get; set; } = "";
    public Guid RoleId { get; set; }
    public Guid CreatedById { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>A group pushed by an identity provider over SCIM 2.0 (RFC 7643 Group). Its members' managed roles follow the mappings above.</summary>
public class ScimGroup : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string DisplayName { get; set; } = "";
    public string? ExternalId { get; set; }
    public List<Guid> MemberIds { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public static class DirectoryRules
{
    public const int MaxMappings = 200, MaxSyncRows = 5000, MaxSyncChars = 2_000_000, MaxGroupMembers = 10_000, ScimPageMax = 200;
    public const string Scope = "directory.write";
}
