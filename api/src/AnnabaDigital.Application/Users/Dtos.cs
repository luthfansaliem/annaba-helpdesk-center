namespace AnnabaDigital.Application.Users;

public sealed record RoleRef(Guid Id, string Key, string Name);
/// <summary><c>Capped</c> = there are more than <c>Total</c> matches; counting stops at a ceiling so that a very broad search stays fast (ticket lists).</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Data, int Page, int PageSize, int Total, bool Capped = false);

public sealed record UserListItem(Guid Id, string Name, string Email, string Status, bool Locked, bool Verified, RoleRef[] Roles, DateTimeOffset? LastLoginAt, DateTimeOffset CreatedAt, bool MfaEnabled, long? AvatarVersion = null, bool Manageable = true);
public sealed record UserDetail(Guid Id, string Name, string Email, string Status, bool Locked, bool Verified, RoleRef[] Roles, DateTimeOffset? LastLoginAt, DateTimeOffset CreatedAt, bool MfaEnabled,
    string? Phone, string? Department, string? JobTitle, string[] EffectivePermissions, int ActiveSessions, int Version, long? AvatarVersion = null, Guid? ManagerId = null, string? ManagerName = null);
public sealed record SetManagerRequest(Guid? ManagerId);

public sealed record UserQuery(string? Q, string? Status, string? Role, bool? Verified, string? Sort, string? Dir, int Page = 1, int PageSize = 10);

public sealed record CreateUserRequest(string? Name, string? Email, string? Phone, string? Department, string? JobTitle, Guid[]? RoleIds, string? Mode);
public sealed record UpdateUserRequest(string? Name, string? Phone, string? Department, string? JobTitle, int? Version);

public sealed record StatusCount(string Key, int Count);
public sealed record UserStats(int Total, int Active, int Pending, int Suspended, int Deactivated, int Locked, int PendingVerification, int[] GrowthLast12Months, StatusCount[] ByStatus, StatusCount[] ByRole, UserListItem[] RecentRegistrations);
public sealed record SessionInfo(Guid Id, string Device, string Browser, string? Ip, DateTimeOffset CreatedAt, DateTimeOffset LastActiveAt, DateTimeOffset ExpiresAt);
public sealed record ActivityItem(long Id, DateTimeOffset Ts, string Action, string? Actor, string Target, string Outcome);
public sealed record BulkItemResult(Guid Id, bool Ok, string? Error);

public sealed record InvitationItem(Guid Id, string Email, string Status, RoleRef[] Roles, string? InvitedBy, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, DateTimeOffset? AcceptedAt);
public sealed record CreateInvitationRequest(string? Email, Guid[]? RoleIds, string? Message);

public sealed record ImportUsersRequest(string? Csv, Guid[]? RoleIds, bool DryRun);
public sealed record ImportRowResult(int Line, string Email, string Status, string? Message);
public sealed record ImportUsersResult(bool DryRun, int Total, int Created, int Failed, ImportRowResult[] Rows);
