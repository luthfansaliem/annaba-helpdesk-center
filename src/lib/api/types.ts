/** Hand-written mirror of the .NET API DTOs (camelCase JSON). Keep in sync with api/src/AnnabaDigital.Application. */
export type Me = { id: string; email: string; name: string; roles: string[]; permissions: string[]; locked: boolean; mfaEnrolled: boolean; avatarVersion?: number | null; tenantName?: string; actingTenant?: string | null; isPlatform?: boolean; language?: "en" | "id" };
export type LoginResponse = { status: "ok" | "mfa_required" | "verify_email" | "suspended" | "deactivated" | "locked" | "mfa_enrollment_required" | "password_reset_required"; next: string | null; user: Me | null };
export type Paged<T> = { data: T[]; page: number; pageSize: number; total: number };
export type RoleRef = { id: string; key: string; name: string };
export type StatusCount = { key: string; count: number };

export type UserListItem = { id: string; name: string; email: string; status: string; locked: boolean; verified: boolean; roles: RoleRef[]; lastLoginAt: string | null; createdAt: string; mfaEnabled: boolean; avatarVersion?: number | null; manageable?: boolean };
export type UserDetail = UserListItem & { phone: string | null; department: string | null; jobTitle: string | null; effectivePermissions: string[]; activeSessions: number; version: number };
export type UserStats = { total: number; active: number; pending: number; suspended: number; deactivated: number; locked: number; pendingVerification: number; growthLast12Months: number[]; byStatus: StatusCount[]; byRole: StatusCount[]; recentRegistrations: UserListItem[] };
export type SessionInfo = { id: string; device: string; browser: string; ip: string | null; createdAt: string; lastActiveAt: string; expiresAt: string };
export type ActivityItem = { id: number; ts: string; action: string; actor: string | null; target: string; outcome: string };
export type BulkResult = { results: { id: string; ok: boolean; error: string | null }[] };

export type InvitationItem = { id: string; email: string; status: "pending" | "accepted" | "expired" | "revoked"; roles: RoleRef[]; invitedBy: string | null; createdAt: string; expiresAt: string; acceptedAt: string | null };
export type InvitationLookup = { state: "valid" | "expired" | "used" | "revoked" | "invalid"; email: string | null; roles: string[] | null };

export type RoleListItem = { id: string; key: string; name: string; description: string; isSystem: boolean; isProtected: boolean; archived: boolean; permissionCount: number; userCount: number; version: number; permissions: string[] };
export type RoleDetail = { id: string; key: string; name: string; description: string; isSystem: boolean; isProtected: boolean; archived: boolean; permissions: string[]; userCount: number; version: number; updatedAt: string };
export type RoleUser = { id: string; name: string; email: string; status: string };
export type PermissionItem = { key: string; module: string; description: string; critical: boolean; grantedTo: string[] };
export type MatrixView = { version: string; permissions: { key: string; module: string; description: string; critical: boolean }[]; roles: { id: string; key: string; name: string; isSystem: boolean; isProtected: boolean; permissions: string[] }[]; grantable: string[] };

export type MySession = { id: string; current: boolean; device: string; browser: string; ip: string | null; createdAt: string; lastActiveAt: string; expiresAt: string; locked: boolean; remember: boolean };
export type AllSession = { id: string; userId: string; userName: string; userEmail: string; device: string; browser: string; ip: string | null; createdAt: string; lastActiveAt: string; expiresAt: string; locked: boolean };
export type AuditItem = { id: number; ts: string; actorId: string | null; actor: string; action: string; targetType: string; targetId: string | null; outcome: string };
export type AuditDetail = AuditItem & { ip: string | null; userAgent: string | null; requestId: string | null; meta: Record<string, string> | null; diff: { Field: string; Before: string; After: string }[] | null };
export type SecurityEvent = { id: string; ts: string; type: string; severity: "low" | "medium" | "high"; userId: string | null; userName: string | null; detail: string; acknowledged: boolean; acknowledgedBy: string | null; acknowledgedAt: string | null };

export type AdminDashboard = {
  kpis: { totalUsers?: number; activeUsers?: number; suspendedUsers?: number; pendingInvitations?: number; totalRoles?: number; activeSessions?: number };
  userGrowth?: number[]; userStatus?: StatusCount[]; roleDistribution?: StatusCount[]; loginActivity?: { days: string[]; success: number[]; failed: number[] };
  recentRegistrations?: UserListItem[]; recentLogins?: { ts: string; actor: string; outcome: string }[]; recentRoleChanges?: { ts: string; action: string; target: string; actor: string }[];
  recentSecurityEvents?: SecurityEvent[]; pendingInvitations?: { id: string; email: string; roles: string[]; expiresAt: string }[]; quickActions: string[]; visible: string[];
};
export type UserDashboard = {
  name: string; email: string; status: string; emailVerified: boolean; lastLoginAt: string | null; roles: RoleRef[]; permissionCount: number; permissions: string[]; activeSessions: number;
  mfaEnabled: boolean; mfaPolicy: string; passwordChangedAt: string | null; profileCompletion: { percent: number; missing: string[] }; recentActivity: { ts: string; action: string; outcome: string }[]; quickLinks: string[];
};

export type Profile = { name: string; email: string; emailVerified: boolean; phone: string | null; department: string | null; jobTitle: string | null; hasAvatar: boolean; avatarVersion: number | null; mfaEnabled: boolean; completion: { percent: number; missing: string[] }; version: number };
export type Preferences = { language: "en" | "id"; timezone: string; notifyEmail: boolean; notifySecurity: boolean; notifyProduct: boolean; autoLockMinutes: number };
export type MfaStatus = { enrolled: boolean; confirmedAt: string | null; recoveryCodesRemaining: number; required: boolean };

export type SystemSettings = {
  registrationMode: "open" | "invitation-only"; passwordMinLength: number; requireBreachCheck: boolean; sessionIdleMinutes: number; sessionAbsoluteHours: number; rememberIdleDays: number; rememberAbsoluteDays: number;
  lockoutAttempts: number; lockoutMinutes: number; mfaPolicy: "off" | "optional" | "required"; invitationExpiryDays: number; auditRetentionDays: number; emailFrom: string; requireReauthForCritical: boolean;
  emailProvider: string; emailApiKeySet: boolean; emailApiKeySource: "database" | "configuration" | "none";
};
export type PublicSettings = { registrationOpen: boolean; passwordMinLength: number; mfaPolicy: string; emailEnabled: boolean; ssoEnabled: boolean; ssoName: string | null };
export type EmailDelivery = { id: string; to: string; subject: string; status: "queued" | "sent" | "failed"; attempts: number; createdAt: string; sentAt: string | null; failedAt: string | null; nextAttemptAt: string | null; lastError: string | null };
export type EmailDeliveryPage = { summary: { provider: string; configured: boolean; queued: number; sent24h: number; failed7d: number }; data: EmailDelivery[]; page: number; pageSize: number; total: number };

export type ImportRowResult = { line: number; email: string; status: "ok" | "error"; message: string | null };
export type ImportUsersResult = { dryRun: boolean; total: number; created: number; failed: number; rows: ImportRowResult[] };

export type RequestableRole = { id: string; key: string; name: string; description: string; permissionCount: number };
export type AccessRequestItem = {
  id: string; requesterId: string; requesterName: string; requesterEmail: string; role: RoleRef; reason: string;
  status: "pending" | "approved" | "rejected" | "cancelled"; createdAt: string; decidedByName: string | null; decidedAt: string | null; decisionNote: string | null;
};

export type AccessReviewSummary = {
  id: string; name: string; status: "open" | "closed"; createdAt: string; dueAt: string; closedAt: string | null; createdByName: string | null;
  total: number; kept: number; revoked: number; pending: number; overdue: boolean;
};
export type ReviewSchedule = { enabled: boolean; everyDays: number; dueInDays: number; nextRunAt: string | null; lastRunAt: string | null };
export type AccessReviewItem = {
  id: string; userId: string; userName: string; userEmail: string; role: RoleRef; decision: "pending" | "keep" | "revoke";
  reviewerName: string | null; decidedAt: string | null; note: string | null;
};

export type TenantItem = { id: string; slug: string; name: string; status: "active" | "suspended"; isPlatform: boolean; users: number; createdAt: string };

export type TenantPolicyItem = { key: string; kind: "int" | "bool" | "enum"; strict: string; min: number; max: number; options: string[] | null; platform: number | boolean | string; own: number | boolean | string | null; effective: number | boolean | string };
export type TenantPolicy = { tenantName: string; items: TenantPolicyItem[] };

export type ClosureView = { id: string; status: "pending" | "approved" | "rejected" | "cancelled" | "completed"; reason: string; requestedAt: string; executeAfter: string | null; decisionNote: string | null };
export type ClosureItem = { id: string; userId: string; userName: string; userEmail: string; status: ClosureView["status"]; reason: string; requestedAt: string; decidedByName: string | null; decidedAt: string | null; decisionNote: string | null; executeAfter: string | null };
