/** Hand-written mirror of the .NET API DTOs (camelCase JSON). Keep in sync with api/src/AnnabaDigital.Application. */
export type Me = { id: string; email: string; name: string; roles: string[]; permissions: string[]; locked: boolean; mfaEnrolled: boolean; avatarVersion?: number | null; tenantName?: string; actingTenant?: string | null; isPlatform?: boolean; language?: "en" | "id"; vendorName?: string | null };
export type LoginResponse = { status: "ok" | "mfa_required" | "verify_email" | "suspended" | "deactivated" | "locked" | "mfa_enrollment_required" | "password_reset_required"; next: string | null; user: Me | null };
export type Paged<T> = { data: T[]; page: number; pageSize: number; total: number; capped?: boolean };
export type RoleRef = { id: string; key: string; name: string };
export type StatusCount = { key: string; count: number };

export type UserListItem = { id: string; name: string; email: string; status: string; locked: boolean; verified: boolean; roles: RoleRef[]; lastLoginAt: string | null; createdAt: string; mfaEnabled: boolean; avatarVersion?: number | null; manageable?: boolean };
export type UserDetail = UserListItem & { phone: string | null; department: string | null; jobTitle: string | null; effectivePermissions: string[]; activeSessions: number; version: number; managerId: string | null; managerName: string | null };
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

// ---- Helpdesk setup (locations, calendars, teams)
export type LocationType = "region" | "site" | "building" | "floor" | "room";
export type LocationItem = { id: string; parentId: string | null; type: LocationType; code: string; name: string; address: string | null; timeZone: string | null; calendarId: string | null; managerUserId: string | null; managerName: string | null; capacity: number | null; archived: boolean; children: number; version: number };
export type SaveLocation = { type: LocationType; parentId: string | null; code: string; name: string; address: string | null; timeZone: string | null; calendarId: string | null; managerUserId: string | null; capacity: number | null; version?: number };

export type CalendarPeriod = { day: number; startMinute: number; endMinute: number };
export type CalendarHoliday = { date: string; name: string };
export type CalendarItem = { id: string; name: string; timeZone: string; isAlwaysOpen: boolean; isDefault: boolean; archived: boolean; locations: number; version: number; periods: CalendarPeriod[]; holidays: CalendarHoliday[] };
export type SaveCalendar = { name: string; timeZone: string; isAlwaysOpen: boolean; periods: CalendarPeriod[]; holidays: CalendarHoliday[]; version?: number };

export type TeamItem = { id: string; name: string; description: string | null; archived: boolean; members: number; version: number };
export type TeamMember = { userId: string; name: string; email: string; isLead: boolean };
export type TeamDetail = { id: string; name: string; description: string | null; archived: boolean; version: number; members: TeamMember[] };

// ---- Service catalog and forms
export type Priority = "low" | "medium" | "high" | "urgent";
export type Audience = "everyone" | "staff";
export type CategoryItem = { id: string; parentId: string | null; name: string; description: string | null; icon: string | null; sortOrder: number; archived: boolean; services: number; children: number; version: number };
export type SaveCategory = { name: string; description: string | null; icon: string | null; parentId: string | null; sortOrder: number; version?: number };
export type ServiceListItem = { id: string; categoryId: string; categoryName: string; name: string; description: string | null; icon: string | null; owningTeamId: string | null; owningTeamName: string | null; defaultPriority: Priority; audience: Audience; isPublished: boolean; archived: boolean; currentFormVersion: number; version: number };
export type SaveService = { name: string; description: string | null; icon: string | null; categoryId: string | null; owningTeamId: string | null; defaultPriority: Priority; audience: Audience; version?: number };

export type FieldType = "text" | "long_text" | "number" | "date" | "datetime" | "select" | "multi_select" | "yes_no" | "location" | "user" | "file";
export type ConditionOp = "equals" | "not_equals" | "in" | "filled" | "empty";
export type FieldOption = { value: string; label: string };
export type FieldCondition = { field: string; op: ConditionOp; values?: string[] };
export type FormField = { key: string; type: FieldType; label: string; help?: string | null; required?: boolean; options?: FieldOption[] | null; min?: number | null; max?: number | null; maxLength?: number | null; pattern?: string | null; visibleWhen?: FieldCondition | null };
export type FormView = { serviceId: string; serviceName: string; version: number; fields: FormField[]; note: string | null; savedAt: string | null; savedBy: string | null };
export type FormVersionItem = { number: number; fieldCount: number; note: string | null; createdAt: string; createdBy: string | null; current: boolean };

// ---- Requester portal and tickets
export type PortalCategory = { id: string; parentId: string | null; name: string; description: string | null; icon: string | null };
export type PortalServiceItem = { id: string; categoryId: string; name: string; description: string | null; icon: string | null };
export type PortalCatalog = { categories: PortalCategory[]; services: PortalServiceItem[] };
export type PortalServiceDetail = { service: PortalServiceItem; categoryName: string; defaultPriority: Priority; formVersion: number; fields: FormField[] };
export type PortalLocation = { id: string; name: string; type: LocationType; path: string };
export type PortalPerson = { id: string; name: string; department: string | null };

export type TicketStatus = "submitted" | "assigned" | "inprogress" | "onhold" | "resolved" | "closed" | "cancelled" | "reopened" | "pendingapproval" | "rejected";
export type TicketAction = "assign" | "start" | "hold" | "resume" | "resolve" | "cancel" | "confirm" | "reopen";
export type TicketListItem = { id: string; number: string; subject: string; status: TicketStatus; priority: Priority; serviceName: string; requesterName: string | null; assigneeName: string | null; teamName: string | null; locationName: string | null; createdAt: string; updatedAt: string; slaState: SlaState | null; resolutionDueAt: string | null };
export type AttachmentItem = { id: string; fileName: string; contentType: string; size: number; inline: boolean; uploadedById: string; uploadedBy: string | null; createdAt: string; canDelete: boolean };
export type UploadedFile = { id: string; fileName: string; contentType: string; size: number; inline: boolean };
export type TicketAnswer = { key: string; label: string; type: FieldType; value: unknown; display: string | null };
export type TicketEvent = { id: string; kind: "created" | "comment" | "status" | "assigned" | "updated" | "escalation" | "approval"; actor: string; internal: boolean; body: string | null; from: TicketStatus | null; to: TicketStatus | null; at: string };
export type TicketDetail = {
  id: string; number: string; subject: string; description: string | null; status: TicketStatus; priority: Priority; serviceId: string; serviceName: string; formVersion: number;
  requesterId: string; requesterName: string | null; requesterEmail: string | null; createdByName: string | null; assigneeId: string | null; assigneeName: string | null; teamId: string | null; teamName: string | null;
  locationId: string | null; locationName: string | null; holdReason: string | null; resolutionCode: string | null; resolutionNote: string | null; cancelReason: string | null;
  createdAt: string; updatedAt: string; resolvedAt: string | null; closedAt: string | null; reopenCount: number; version: number;
  allowed: TicketAction[]; canComment: boolean; canCommentInternal: boolean; canEdit: boolean; canAssign: boolean; answers: TicketAnswer[]; timeline: TicketEvent[]; sla: TicketSla | null; expectedBy: string | null; approvals: TicketApprovalItem[]; myApprovalId: string | null; attachments: AttachmentItem[]; canAttach: boolean;
  anonymisedAt: string | null; legalHold: LegalHoldInfo | null; canHold: boolean; asset: TicketAsset | null; rating: TicketRating | null;
};
export type TicketRating = { score: number | null; comment: string | null; at: string | null; canRate: boolean; rateUntil: string | null };
export type CsatRow = { key: string; label: string; count: number; average: number | null; satisfiedPercent: number | null; low: number };
export type CsatComment = { ticketId: string; number: string; subject: string; score: number; comment: string; agent: string | null; at: string };
export type CsatReport = { days: number; from: string; to: string; by: "agent" | "team" | "site" | "vendor"; ownOnly: boolean; count: number; average: number | null; satisfiedPercent: number | null; low: number; distribution: number[]; rows: CsatRow[]; comments: CsatComment[] };
export type LegalHoldInfo = { reason: string; placedAt: string; placedBy: string | null };
export type AssigneeOption = { id: string; name: string; email: string };

// ---- SLA and assignment rules
export type SlaState = "none" | "ontrack" | "atrisk" | "breached" | "paused" | "met";
export type SlaPolicyItem = {
  id: string; name: string; position: number; serviceId: string | null; serviceName: string | null; categoryId: string | null; categoryName: string | null; locationId: string | null; locationName: string | null;
  priority: Priority | null; calendarId: string | null; calendarName: string | null; firstResponseMinutes: number; resolutionMinutes: number; escalate1Percent: number; escalate2Percent: number;
  archived: boolean; conditions: number; version: number;
};
export type SaveSlaPolicy = {
  name: string; serviceId: string | null; categoryId: string | null; locationId: string | null; priority: Priority | null; calendarId: string | null;
  firstResponseMinutes: number; resolutionMinutes: number; escalate1Percent: number; escalate2Percent: number; version?: number;
};
export type SlaPreview = { policyId: string | null; policyName: string | null; calendarName: string | null; firstResponseMinutes: number; resolutionMinutes: number };
export type Strategy = "none" | "roundrobin" | "leastloaded";
export type AssignmentRuleItem = {
  id: string; name: string; position: number; serviceId: string | null; serviceName: string | null; categoryId: string | null; categoryName: string | null; locationId: string | null; locationName: string | null;
  priority: Priority | null; teamId: string; teamName: string | null; strategy: Strategy; archived: boolean; conditions: number; version: number;
};
export type SaveAssignmentRule = { name: string; serviceId: string | null; categoryId: string | null; locationId: string | null; priority: Priority | null; teamId: string | null; strategy: Strategy; version?: number };
export type RouteCandidate = { id: string; name: string; openTickets: number; lastAssignedAt: string | null };
export type RoutePreview = { ruleId: string | null; ruleName: string | null; teamId: string | null; teamName: string | null; strategy: Strategy; assigneeId: string | null; assigneeName: string | null; candidates: RouteCandidate[] };
export type TicketSla = { state: SlaState; policyName: string | null; firstResponseMinutes: number; resolutionMinutes: number; firstResponseDueAt: string | null; firstRespondedAt: string | null; resolutionDueAt: string | null; pausedAt: string | null; escalationLevel: number; breachedAt: string | null };

// ---- Approvals and notifications
export type ApproverRef = { id: string; name: string };
export type ApprovalStepItem = { name: string; rule: "any" | "all"; users: ApproverRef[]; roles: ApproverRef[]; requesterManager?: boolean };
export type ApprovalPolicyItem = { serviceId: string; serviceName: string; categoryName: string; published: boolean; hasPolicy: boolean; steps: ApprovalStepItem[]; fallbackUserId: string | null; fallbackName: string | null; version: number };
export type ApprovalStepDef = { name: string; rule: "any" | "all"; userIds: string[]; roleIds: string[]; requesterManager?: boolean };
export type ApprovalStatus = "waiting" | "pending" | "approved" | "rejected" | "skipped" | "cancelled";
export type MyApprovalItem = { id: string; ticketId: string; ticketNumber: string; ticketSubject: string; serviceName: string; requesterName: string | null; stepNo: number; stepName: string; status: ApprovalStatus; comment: string | null; createdAt: string; decidedAt: string | null; onBehalfOfName?: string | null };
export type TicketApprovalItem = { id: string; stepNo: number; stepName: string; rule: "any" | "all"; approverId: string; approverName: string; status: ApprovalStatus; comment: string | null; decidedAt: string | null; decidedVia: string | null; onBehalfOfName?: string | null };
export type DelegationState = "upcoming" | "active" | "ended" | "revoked";
export type DelegationItem = { id: string; delegateId: string; delegateName: string; startsAt: string; endsAt: string; note: string | null; state: DelegationState; createdAt: string };
export type DelegationResult = { delegation: DelegationItem; handedOver: number; kept: number };
export type ApprovalLinkInfo = { ticketNumber: string; subject: string; description: string | null; serviceName: string; requesterName: string | null; stepName: string; approverName: string; expiresAt: string };
export type NotificationItem = { id: string; kind: string; title: string; body: string | null; link: string | null; ticketId: string | null; createdAt: string; read: boolean };

// ---- Reports
export type OpsKpis = { open: number; overdue: number; atRisk: number; pendingApproval: number; unassigned: number; created: number; resolved: number; avgFirstResponseMinutes: number | null; avgResolutionMinutes: number | null; slaAttainmentPercent: number | null };
export type CountItem = { key: string; label: string; count: number };
export type AttentionItem = { id: string; number: string; subject: string; status: TicketStatus; priority: Priority; assignee: string | null; team: string | null; slaState: SlaState; dueAt: string | null };
export type OperationsReport = { days: number; from: string; to: string; kpis: OpsKpis; daily: { day: string; created: number; resolved: number }[]; byStatus: CountItem[]; byPriority: CountItem[]; backlogByTeam: CountItem[]; aging: CountItem[]; attention: AttentionItem[] };
export type SlaRow = { key: string; label: string; met: number; breached: number; attainmentPercent: number | null; avgResolutionMinutes: number | null };
export type BreachItem = { id: string; number: string; subject: string; status: TicketStatus; priority: Priority; serviceName: string; team: string | null; assignee: string | null; breachedAt: string; reason: "first_response" | "resolution" };
export type SlaReport = { days: number; from: string; to: string; met: number; breached: number; attainmentPercent: number | null; openBreached: number; openAtRisk: number; byPriority: SlaRow[]; byTeam: SlaRow[]; byService: SlaRow[]; breaches: BreachItem[] };

// ---- Setup checklist and quick start
export type SetupStep = { key: string; done: boolean; required: boolean; count: number; link: string };
export type SetupStatus = { steps: SetupStep[]; ready: boolean; done: number; total: number };
export type QuickStartResult = { created: string[]; skipped: string[]; status: SetupStatus };

// ---- Queue productivity: bulk actions and saved views
export type TicketBulkItem = { id: string; number: string | null; ok: boolean; error: string | null };
export type TicketBulkResult = { action: string; requested: number; succeeded: number; items: TicketBulkItem[] };
export type TicketViewItem = { id: string; name: string; filter: Record<string, string>; shared: boolean; mine: boolean; ownerName: string | null; canEdit: boolean };

// ---- Notification preferences (NTF-002)
export type NotificationPrefItem = { kind: string; group: string; locked: boolean; inApp: boolean; email: boolean; defaultEmail: boolean; custom: boolean };
export type NotificationPrefs = { emailMaster: boolean; items: NotificationPrefItem[] };

// ---- data retention (ADM-006)
export type RetentionAction = "anonymise" | "delete";
export type RetentionView = {
  enabled: boolean; finishedTicketDays: number | null; ticketAction: RetentionAction; attachmentDays: number | null; notificationDays: number | null; auditDays: number; holds: number; updatedAt: string | null;
  lastRun: { at: string; tickets: number; files: number; notifications: number; filesFailed: number } | null;
};
export type RetentionDraft = { enabled: boolean; finishedTicketDays: number | null; ticketAction: RetentionAction; attachmentDays: number | null; notificationDays: number | null };
export type RetentionPreview = { tickets: number; heldTickets: number; files: number; fileBytes: number; notifications: number };
export type RetentionRunResult = { tickets: number; files: number; notifications: number; more: boolean; filesFailed: number };
export type RetentionNotice = { enabled: boolean; finishedTicketDays: number | null; ticketAction: RetentionAction; attachmentDays: number | null; notificationDays: number | null; auditDays: number };
export type LegalHoldItem = { id: string; ticketId: string; number: string; subject: string; status: string; reason: string; placedBy: string | null; placedAt: string };
export type MissingFile = { attachmentId: string; fileName: string; ticketId: string | null; number: string | null };
export type FileCheck = { files: number; missing: number; missingSample: MissingFile[]; orphans: number; orphanBytes: number; checkedAt: string };
export type FileClean = { removed: number; bytes: number; failed: number };

// ---- e-mail wording (NTF-002)
export type EmailWording = { senderName: string | null; subjectPrefix: string | null; footerEn: string | null; footerId: string | null; updatedAt: string | null };
export type EmailWordingPreview = { versions: { lang: string; subject: string; text: string }[] };

// ---- asset register (R2-1)
export type AssetStatus = "in_use" | "in_repair" | "stored" | "retired";
export type AssetTypeItem = { id: string; name: string; description: string | null; fields: FormField[]; archived: boolean; assets: number; version: number };
export type AssetListItem = { id: string; tag: string; name: string; typeId: string; typeName: string; status: AssetStatus; serial: string | null; locationPath: string | null; assignedToName: string | null; warrantyEnd: string | null; updatedAt: string };
export type AssetEventItem = { id: string; kind: "created" | "updated" | "moved" | "assigned" | "status" | "retired" | "restored"; actor: string; body: string | null; at: string };
export type AssetTicketItem = { id: string; number: string; subject: string; status: TicketStatus; createdAt: string };
export type AssetDetail = {
  id: string; tag: string; name: string; typeId: string; typeName: string; serial: string | null; status: AssetStatus; locationId: string | null; locationPath: string | null; assignedToId: string | null; assignedToName: string | null;
  department: string | null; purchaseDate: string | null; purchaseCost: number | null; warrantyEnd: string | null; vendorName: string | null; notes: string | null; fields: FormField[]; values: Record<string, unknown>;
  events: AssetEventItem[]; tickets: AssetTicketItem[]; ticketCount: number; retiredAt: string | null; retireReason: string | null; createdAt: string; updatedAt: string; version: number; canManage: boolean;
  requestsLastYear: number; repairHint: "frequent" | "frequent_out_of_warranty" | null;
};
export type AssetPick = { id: string; tag: string; name: string; typeName: string; locationPath: string | null };
export type ImportProblem = { line: number; tag: string | null; errors: Record<string, string> };
export type ImportReport = { rows: number; creates: number; updates: number; problems: ImportProblem[]; applied: boolean };
export type TicketAsset = { id: string; tag: string; name: string; retired: boolean };
export type AssetCard = { id: string; tag: string; name: string; typeName: string; locationId: string | null; locationPath: string | null; status: AssetStatus; retired: boolean; canOpen: boolean };
export type AssetLabel = { id: string; tag: string; name: string; typeName: string; locationPath: string | null };

// ---- work orders (R2-3)
export type WorkOrderStatus = "open" | "in_progress" | "on_hold" | "done" | "cancelled";
export type WorkOrderListItem = { id: string; number: string; title: string; status: WorkOrderStatus; required: boolean; assigneeId: string | null; assigneeName: string | null; teamName: string | null; ticketId: string; ticketNumber: string; ticketSubject: string; dueAt: string | null; tasksDone: number; tasksTotal: number; updatedAt: string; vendorName: string | null };
export type TicketWorkOrders = { resolveTicket: boolean; canManage: boolean; items: WorkOrderListItem[] };
export type WoTask = { id: string; position: number; text: string; required: boolean; doneAt: string | null; doneBy: string | null; note: string | null };
export type WoTime = { id: string; userId: string; person: string; minutes: number; date: string; note: string | null; canDelete: boolean };
export type WoCost = { id: string; kind: "part" | "labour" | "service" | "other"; description: string; quantity: number; unitCost: number; total: number; by: string; canDelete: boolean };
export type WoFile = { id: string; fileName: string; contentType: string; stage: "before" | "after" | null; inline: boolean; createdAt: string };
export type WorkOrderDetail = {
  id: string; number: string; title: string; instructions: string | null; status: WorkOrderStatus; required: boolean; requireAfterPhoto: boolean; assigneeId: string | null; assigneeName: string | null; teamId: string | null; teamName: string | null;
  ticketId: string; ticketNumber: string; ticketSubject: string; ticketStatus: TicketStatus; assetId: string | null; assetTag: string | null; assetName: string | null; dueAt: string | null; holdReason: string | null;
  startedAt: string | null; completedAt: string | null; completionNote: string | null; cancelReason: string | null; tasks: WoTask[]; times: WoTime[]; totalMinutes: number; costs: WoCost[] | null; costTotal: number | null; files: WoFile[];
  allowed: ("start" | "hold" | "resume" | "complete" | "cancel")[]; canEdit: boolean; canWork: boolean; canCosts: boolean; missing: string[]; createdAt: string; updatedAt: string; version: number;
  vendorId: string | null; vendorName: string | null; vendorPhone: string | null; purchaseOrder: string | null; labourCost: number | null;
};
export type ChecklistTemplateItem = { id: string; name: string; items: { text: string; required: boolean }[]; archived: boolean; version: number };

// ---- vendors (R2-4)
export type VendorListItem = { id: string; name: string; services: string | null; phone: string | null; email: string | null; blocked: boolean; sites: number; openWorkOrders: number; nextContractEnd: string | null };
export type VendorContact = { id: string; name: string; role: string | null; email: string | null; phone: string | null };
export type VendorContract = { id: string; title: string; reference: string | null; startsOn: string; endsOn: string | null; value: number | null; notes: string | null; state: "active" | "ended" | "upcoming" };
export type VendorDocument = { id: string; fileName: string; contentType: string; size: number; contractId: string | null; createdAt: string; uploadedBy: string | null };
export type VendorDetail = {
  id: string; name: string; services: string | null; email: string | null; phone: string | null; website: string | null; address: string | null; taxId: string | null; notes: string | null; blocked: boolean; blockReason: string | null; blockedAt: string | null;
  contacts: VendorContact[]; sites: { locationId: string; path: string }[]; contracts: VendorContract[]; documents: VendorDocument[];
  score: { workOrders: number; done: number; open: number; onTime: number; late: number; cost: number | null; rating: number | null; ratings: number }; workOrders: WorkOrderListItem[]; canManage: boolean; version: number; portalUsers: VendorPortalUser[] | null;
};
export type VendorPick = { id: string; name: string; services: string | null; phone: string | null };

// ---- preventive maintenance (R2-5)
export type PmUnit = "day" | "week" | "month" | "year";
export type VisitState = "open" | "overdue" | "on_time" | "late" | "cancelled" | "missed" | "removed";
export type PlanListItem = { id: string; name: string; target: string; unit: PmUnit; every: number; nextDueOn: string | null; leadDays: number; teamName: string | null; assigneeName: string | null; vendorName: string | null; paused: boolean; openVisits: number; overdueVisits: number };
export type PlanVisit = { id: string; dueOn: string; assetId: string | null; assetTag: string | null; assetName: string | null; locationName: string | null; ticketId: string | null; ticketNumber: string | null; workOrderId: string | null; workOrderNumber: string | null; state: VisitState; completedAt: string | null };
export type ComplianceRow = { key: string; label: string; planned: number; onTime: number; late: number; open: number; overdue: number; missed: number; cancelled: number; percent: number | null };
export type PlanDetail = {
  id: string; name: string; instructions: string | null; serviceId: string; serviceName: string; assetId: string | null; assetLabel: string | null; assetTypeId: string | null; assetTypeName: string | null; locationId: string | null; locationPath: string | null;
  target: string; unit: PmUnit; every: number; startsOn: string; endsOn: string | null; leadDays: number; nextDueOn: string | null; upcoming: string[]; targets: number;
  checklistTemplateId: string | null; checklistName: string | null; teamId: string | null; teamName: string | null; assigneeId: string | null; assigneeName: string | null; vendorId: string | null; vendorName: string | null; requireAfterPhoto: boolean;
  paused: boolean; ownerName: string | null; visits: PlanVisit[]; lastYear: ComplianceRow; canManage: boolean; version: number;
};
export type ComplianceReport = { from: string; to: string; by: "plan" | "team" | "vendor" | "site"; rows: ComplianceRow[]; total: ComplianceRow };

// ---- knowledge base (R2-7)
export type KbCategoryItem = { id: string; name: string; articles: number; archived: boolean };
export type KbListItem = { id: string; title: string; summary: string | null; categoryName: string | null; visibility: "everyone" | "staff"; status: "draft" | "published" | "archived"; hasDraft: boolean; reviewBy: string | null; reviewDue: boolean; updatedAt: string; publishedAt: string | null; views: number; helpful: number; notHelpful: number; deflections: number };
export type KbServiceRef = { id: string; name: string };
export type KbFileItem = { id: string; fileName: string; contentType: string; size: number; createdAt: string };
export type KbVersionItem = { number: number; title: string; note: string | null; by: string | null; at: string; published: boolean };
export type KbArticleDetail = {
  id: string; title: string; summary: string | null; body: string; categoryId: string | null; categoryName: string | null; visibility: "everyone" | "staff"; status: "draft" | "published" | "archived";
  publishedVersion: number; latestVersion: number; shownVersion: number; hasDraft: boolean; reviewBy: string | null; reviewDue: boolean; services: KbServiceRef[]; files: KbFileItem[];
  views: number; helpful: number; notHelpful: number; deflections: number; myFeedback: boolean | null; versions: KbVersionItem[] | null; canWrite: boolean; canPublish: boolean; updatedAt: string; publishedAt: string | null; version: number;
};
export type KbSuggestion = { id: string; title: string; summary: string | null; forService: boolean };

// ---- cost and budget (R2-8)
export type CostSettings = { currency: string; labourRatePerHour: number | null };
export type CostGroup = "month" | "site" | "cost_center" | "category" | "service" | "department" | "vendor" | "asset";
export type CostRow = { key: string; label: string; part: number; labour: number; service: number; invoice: number; other: number; time: number; total: number; workOrders: number };
export type CostReport = { from: string; to: string; by: CostGroup; currency: string; labourRatePerHour: number | null; rows: CostRow[]; total: CostRow };
export type CostCenterItem = { id: string; code: string; name: string; archived: boolean; places: { locationId: string; path: string }[]; version: number };
export type BudgetItem = { id: string; scope: "cost_center" | "site" | "category"; scopeId: string; scopeName: string; year: number; amount: number; spent: number; percent: number; state: "ok" | "near" | "over"; note: string | null; version: number };
export type BudgetList = { year: number; currency: string; items: BudgetItem[]; canManage: boolean };

// ---- e-mail to ticket (R2-9)
export type InboundMessageItem = { id: string; receivedAt: string; fromAddress: string; subject: string; outcome: "created" | "replied" | "ignored" | "rejected"; reason: string | null; ticketId: string | null; ticketNumber: string | null };
export type MailboxDetail = { configured: boolean; enabled: boolean; address: string | null; defaultServiceId: string | null; defaultServiceName: string | null; tokenHint: string | null; recent: InboundMessageItem[]; version: number };

// ---- API keys and webhooks (R2-10)
export type ApiKeyItem = { id: string; name: string; prefix: string; scopes: string[]; owner: string | null; createdAt: string; expiresAt: string | null; lastUsedAt: string | null; active: boolean };
export type NewApiKey = { key: ApiKeyItem; token: string };
export type WebhookItem = { id: string; url: string; description: string | null; events: string[]; enabled: boolean; createdAt: string; pending: number; failed: number; lastDeliveredAt: string | null; version: number };
export type NewWebhook = { webhook: WebhookItem; secret: string };
export type DeliveryItem = { id: string; eventId: string; event: string; attempts: number; createdAt: string; deliveredAt: string | null; failedAt: string | null; nextAttemptAt: string; lastStatus: number | null; lastError: string | null };

// ---- vendor portal (R2-11)
export type VendorPortalUser = { userId: string; name: string; email: string; linkedAt: string };
