using AnnabaDigital.Application.Users;

namespace AnnabaDigital.Application.Roles;

public sealed record RoleListItem(Guid Id, string Key, string Name, string Description, bool IsSystem, bool IsProtected, bool Archived, int PermissionCount, int UserCount, int Version, string[] Permissions);
public sealed record RoleDetail(Guid Id, string Key, string Name, string Description, bool IsSystem, bool IsProtected, bool Archived, string[] Permissions, int UserCount, int Version, DateTimeOffset UpdatedAt);
public sealed record RoleUser(Guid Id, string Name, string Email, string Status);
public sealed record SaveRoleRequest(string? Name, string? Description, string[]? Permissions, int? Version);

public sealed record PermissionItem(string Key, string Module, string Description, bool Critical, string[] GrantedTo);
public sealed record PermissionDef(string Key, string Module, string Description, bool Critical);
public sealed record MatrixRole(Guid Id, string Key, string Name, bool IsSystem, bool IsProtected, string[] Permissions);
/// <param name="Version">Opaque fingerprint of every role's permission set; echo it back when saving so concurrent edits are detected.</param>
/// <param name="Grantable">Permissions the caller may add to a role (exactly the ones they hold themselves).</param>
public sealed record MatrixView(string Version, PermissionDef[] Permissions, MatrixRole[] Roles, string[] Grantable);
public sealed record MatrixChange(Guid RoleId, string[]? Add, string[]? Remove);
public sealed record SaveMatrixRequest(MatrixChange[]? Changes, string? ExpectedVersion);
public sealed record MatrixSaveResult(int RolesChanged, int Added, int Removed, string Version);
