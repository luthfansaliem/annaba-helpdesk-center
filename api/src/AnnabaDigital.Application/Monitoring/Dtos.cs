using System.Text.Json;
using System.Text.Json.Serialization;
using AnnabaDigital.Application.Users;

namespace AnnabaDigital.Application.Monitoring;

public sealed record MySession(Guid Id, bool Current, string Device, string Browser, string? Ip, DateTimeOffset CreatedAt, DateTimeOffset LastActiveAt, DateTimeOffset ExpiresAt, bool Locked, bool Remember);
public sealed record AllSessionItem(Guid Id, Guid UserId, string UserName, string UserEmail, string Device, string Browser, string? Ip, DateTimeOffset CreatedAt, DateTimeOffset LastActiveAt, DateTimeOffset ExpiresAt, bool Locked);
public sealed record SessionQuery(Guid? UserId, int Page = 1, int PageSize = 25);

public sealed record AuditQuery(string? Q, string? Outcome, string? Category, Guid? ActorId, DateTimeOffset? From, DateTimeOffset? To, int Page = 1, int PageSize = 25);
public sealed record AuditListItem(long Id, DateTimeOffset Ts, Guid? ActorId, string Actor, string Action, string TargetType, string? TargetId, string Outcome);
public sealed record AuditDetail(long Id, DateTimeOffset Ts, Guid? ActorId, string Actor, string Action, string TargetType, string? TargetId, string Outcome, string? Ip, string? UserAgent, string? RequestId, JsonElement? Meta, JsonElement? Diff);

public sealed record AuditExport(string Content, string ContentType, string FileName, int Count, bool Truncated);

public sealed record SecurityQuery(string? Severity, bool? Acknowledged, int Page = 1, int PageSize = 25);
public sealed record SecurityEventItem(Guid Id, DateTimeOffset Ts, string Type, string Severity, Guid? UserId, string? UserName, string Detail, bool Acknowledged, string? AcknowledgedBy, DateTimeOffset? AcknowledgedAt);

public sealed record RetentionResult(int Audit, int Security, int Tokens, int Sessions, int RateBuckets);

public sealed record DashKpis(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? TotalUsers, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? ActiveUsers,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? SuspendedUsers, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? PendingInvitations,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? TotalRoles, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? ActiveSessions);
public sealed record LoginActivity(string[] Days, int[] Success, int[] Failed);
public sealed record RecentLogin(DateTimeOffset Ts, string Actor, string Outcome);
public sealed record RecentChange(DateTimeOffset Ts, string Action, string Target, string Actor);
public sealed record PendingInvite(Guid Id, string Email, string[] Roles, DateTimeOffset ExpiresAt);
public sealed record AdminDashboard(
    DashKpis Kpis,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int[]? UserGrowth,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] StatusCount[]? UserStatus,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] StatusCount[]? RoleDistribution,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] LoginActivity? LoginActivity,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] UserListItem[]? RecentRegistrations,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] RecentLogin[]? RecentLogins,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] RecentChange[]? RecentRoleChanges,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] SecurityEventItem[]? RecentSecurityEvents,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PendingInvite[]? PendingInvitations,
    string[] QuickActions,
    string[] Visible);

public sealed record ProfileCompletion(int Percent, string[] Missing);
public sealed record MyActivity(DateTimeOffset Ts, string Action, string Outcome);
public sealed record UserDashboard(
    string Name, string Email, string Status, bool EmailVerified, DateTimeOffset? LastLoginAt, RoleRef[] Roles, int PermissionCount, string[] Permissions,
    int ActiveSessions, bool MfaEnabled, string MfaPolicy, DateTimeOffset? PasswordChangedAt, ProfileCompletion ProfileCompletion, MyActivity[] RecentActivity, string[] QuickLinks);
