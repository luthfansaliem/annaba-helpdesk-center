using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Domain.Entities;

namespace AnnabaDigital.Application.Abstractions;

/// <summary>Persistence port. (Pragmatic trade-off: Application depends on EF Core abstractions, not on a concrete provider.)</summary>
public interface IAppDb
{
    /// <summary>Raised after a save that changed roles, role permissions, role assignments or users (what decides a person's permissions).</summary>
    event Action? PermissionDataChanged;
    DbSet<User> Users { get; }
    DbSet<UserCredential> Credentials { get; }
    DbSet<UserProfile> Profiles { get; }
    DbSet<Role> Roles { get; }
    DbSet<Permission> Permissions { get; }
    DbSet<RolePermission> RolePermissions { get; }
    DbSet<UserRole> UserRoles { get; }
    DbSet<Session> Sessions { get; }
    DbSet<Invitation> Invitations { get; }
    DbSet<MfaFactor> MfaFactors { get; }
    DbSet<MfaRecoveryCode> MfaRecoveryCodes { get; }
    DbSet<RateLimitBucket> RateLimitBuckets { get; }
    DbSet<Avatar> Avatars { get; }
    DbSet<OutboxEmail> OutboxEmails { get; }
    DbSet<AccessRequest> AccessRequests { get; }
    DbSet<AccessReview> AccessReviews { get; }
    DbSet<UserIdentity> UserIdentities { get; }
    DbSet<Tenant> Tenants { get; }
    DbSet<AccessReviewItem> AccessReviewItems { get; }
    DbSet<AccessReviewSchedule> AccessReviewSchedules { get; }
    DbSet<TenantSetting> TenantSettings { get; }
    DbSet<AccountClosureRequest> AccountClosureRequests { get; }
    DbSet<EmailVerificationToken> EmailVerificationTokens { get; }
    DbSet<PasswordResetToken> PasswordResetTokens { get; }
    DbSet<InvitationRole> InvitationRoles { get; }
    DbSet<AuditEvent> AuditEvents { get; }
    DbSet<SecurityEvent> SecurityEvents { get; }
    DbSet<SystemSetting> Settings { get; }
    DbSet<Location> Locations { get; }
    DbSet<BusinessCalendar> Calendars { get; }
    DbSet<CalendarPeriod> CalendarPeriods { get; }
    DbSet<CalendarHoliday> CalendarHolidays { get; }
    DbSet<Team> Teams { get; }
    DbSet<TeamMember> TeamMembers { get; }
    DbSet<ServiceCategory> ServiceCategories { get; }
    DbSet<ServiceItem> Services { get; }
    DbSet<ServiceFormVersion> ServiceFormVersions { get; }
    DbSet<Ticket> Tickets { get; }
    DbSet<TicketEvent> TicketEvents { get; }
    DbSet<TicketCounter> TicketCounters { get; }
    DbSet<SlaPolicy> SlaPolicies { get; }
    DbSet<AssignmentRule> AssignmentRules { get; }
    DbSet<ApprovalPolicy> ApprovalPolicies { get; }
    DbSet<TicketApproval> TicketApprovals { get; }
    DbSet<Notification> Notifications { get; }
    DbSet<Attachment> Attachments { get; }
    DbSet<TicketView> TicketViews { get; }
    DbSet<ApprovalDelegation> ApprovalDelegations { get; }
    DbSet<NotificationPreference> NotificationPreferences { get; }
    DbSet<RetentionPolicy> RetentionPolicies { get; }
    DbSet<TicketLegalHold> TicketLegalHolds { get; }
    DbSet<EmailWording> EmailWordings { get; }
    DbSet<AssetType> AssetTypes { get; }
    DbSet<Asset> Assets { get; }
    DbSet<AssetEvent> AssetEvents { get; }
    DbSet<WorkOrder> WorkOrders { get; }
    DbSet<WorkOrderTask> WorkOrderTasks { get; }
    DbSet<WorkOrderTime> WorkOrderTimes { get; }
    DbSet<WorkOrderCost> WorkOrderCosts { get; }
    DbSet<ChecklistTemplate> ChecklistTemplates { get; }
    DbSet<Vendor> Vendors { get; }
    DbSet<VendorContact> VendorContacts { get; }
    DbSet<VendorSite> VendorSites { get; }
    DbSet<VendorContract> VendorContracts { get; }
    DbSet<VendorDocument> VendorDocuments { get; }
    DbSet<MaintenancePlan> MaintenancePlans { get; }
    DbSet<MaintenanceVisit> MaintenanceVisits { get; }
    DbSet<TicketRating> TicketRatings { get; }
    DbSet<KbCategory> KbCategories { get; }
    DbSet<KbArticle> KbArticles { get; }
    DbSet<KbArticleVersion> KbArticleVersions { get; }
    DbSet<KbArticleService> KbArticleServices { get; }
    DbSet<KbFeedback> KbFeedback { get; }
    DbSet<KbFile> KbFiles { get; }
    DbSet<CostSettings> CostSettings { get; }
    DbSet<CostCenter> CostCenters { get; }
    DbSet<CostCenterPlace> CostCenterPlaces { get; }
    DbSet<Budget> Budgets { get; }
    DbSet<InboundMailbox> InboundMailboxes { get; }
    DbSet<InboundMessage> InboundMessages { get; }
    DbSet<ApiKey> ApiKeys { get; }
    DbSet<ApiIdempotency> ApiIdempotency { get; }
    DbSet<WebhookEndpoint> WebhookEndpoints { get; }
    DbSet<WebhookDelivery> WebhookDeliveries { get; }
    DbSet<VendorUser> VendorUsers { get; }
    DbSet<BookableResource> BookableResources { get; }
    DbSet<Booking> Bookings { get; }
    DbSet<BookingFeed> BookingFeeds { get; }
    DbSet<VehicleTrip> VehicleTrips { get; }
    DbSet<ResourceDocument> ResourceDocuments { get; }
    DbSet<DriverLicence> DriverLicences { get; }
    DbSet<Visit> Visits { get; }
    DbSet<VisitorBlock> VisitorBlocks { get; }
    DbSet<VisitorSettings> VisitorSettings { get; }
    DbSet<MailItem> MailItems { get; }
    DbSet<InventoryItem> InventoryItems { get; }
    DbSet<StockBalance> StockBalances { get; }
    DbSet<StockMovement> StockMovements { get; }
    DbSet<StockTake> StockTakes { get; }
    DbSet<StockTakeLine> StockTakeLines { get; }
    DbSet<SupplyRequest> SupplyRequests { get; }
    DbSet<SupplyRequestLine> SupplyRequestLines { get; }
    DbSet<SupplySettings> SupplySettings { get; }
    DbSet<ReportSchedule> ReportSchedules { get; }
    DbSet<DirectoryGroupMapping> DirectoryGroupMappings { get; }
    DbSet<ScimGroup> ScimGroups { get; }
    DbSet<ChannelSetting> ChannelSettings { get; }
    DbSet<UserChannel> UserChannels { get; }
    DbSet<ChannelDelivery> ChannelDeliveries { get; }
    DbSet<OfflineOp> OfflineOps { get; }
    /// <summary>Hands out the next ticket sequence for an organisation and year in one atomic upsert (no two tickets can share a number).</summary>
    Task<int> NextTicketSequenceAsync(Guid tenantId, int year, CancellationToken ct = default);
    /// <summary>Hands out the next automatic asset tag number for an organisation in one atomic upsert.</summary>
    Task<int> NextAssetNumberAsync(Guid tenantId, CancellationToken ct = default);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
    /// <summary>Discards tracked changes. Required after a rolled-back transaction so stale entity state can never be saved later.</summary>
    void ResetTracking();
    /// <summary>Raw SQL (used for row locks such as SELECT … FOR UPDATE inside a transaction).</summary>
    Task<int> ExecuteSqlAsync(FormattableString sql, CancellationToken ct = default);
    /// <summary>Runs <paramref name="work"/> in one DB transaction (commit on success, rollback on exception).</summary>
    Task<T> InTransactionAsync<T>(Func<Task<T>> work, CancellationToken ct = default);
}

public interface IClock { DateTimeOffset UtcNow { get; } }

/// <summary>Where attachment bytes live (TKT-005). Keys are generated by the application (<c>{tenant}/{id}</c>), never taken from a user. Local disk today; an object store is another adapter.</summary>
public interface IFileStorage
{
    Task SaveAsync(string key, Stream content, CancellationToken ct = default);
    /// <summary>The stored bytes, or <c>null</c> if there are none. The caller disposes the stream.</summary>
    Task<Stream?> OpenAsync(string key, CancellationToken ct = default);
    Task DeleteAsync(string key, CancellationToken ct = default);
    /// <summary>Every stored object whose key starts with <paramref name="prefix"/> (a tenant's folder), with size and last change. Used to compare the volume with the database after a restore.</summary>
    IAsyncEnumerable<StoredFile> ListAsync(string prefix, CancellationToken ct = default);
}

public sealed record StoredFile(string Key, long Size, DateTimeOffset ModifiedAt);

public sealed record ScanResult(bool Clean, string? Reason = null);

/// <summary>The antivirus hook (TKT-005): every upload is scanned before it is stored. The default accepts everything; plug a real scanner (ClamAV, a cloud service) in here.</summary>
public interface IAttachmentScanner
{
    /// <summary>False for a scanner that checks nothing; the setup checklist and the Production start-up check use it.</summary>
    bool Active => true;
    Task<ScanResult> ScanAsync(Stream content, string fileName, CancellationToken ct = default);
}

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);
    /// <summary>True when the stored hash uses weaker parameters than the current policy.</summary>
    bool NeedsRehash(string hash);
    /// <summary>Burns the same CPU as a real verification (used for unknown accounts to keep timing uniform).</summary>
    void VerifyDummy(string password);
}

public interface ITokenService
{
    /// <summary>256-bit CSPRNG token, URL-safe.</summary>
    string NewToken();
    /// <summary>SHA-256 hex. Only the hash is ever stored.</summary>
    string Hash(string token);
    string CsrfToken(Guid sessionId);
    bool ConstantTimeEquals(string a, string b);
}

public readonly record struct RateLimitResult(bool Allowed, TimeSpan RetryAfter);

public interface IRateLimiter
{
    /// <summary>Counts one hit for <paramref name="key"/> in a fixed window and reports whether it is still within <paramref name="limit"/>.</summary>
    Task<RateLimitResult> HitAsync(string key, int limit, TimeSpan window, CancellationToken ct = default);
    Task ResetAsync(string key, CancellationToken ct = default);
}

/// <summary>Request metadata captured at the edge and stored with audit/session rows.</summary>
public sealed record ClientInfo(string? Ip, string? UserAgent, string? RequestId);

public sealed record EmailMessage(string To, string Subject, string Text, string Html)
{
    /// <summary>Re-renders the message in another language ("id"); null means "no such version, keep this one". Set by the templates, used when the mail is queued.</summary>
    public Func<string, EmailMessage?>? Localize { get; init; }
    /// <summary>Language to use when the recipient has no profile yet (an invitation: the inviter's language).</summary>
    public string? Lang { get; init; }
    /// <summary>One file sent with the mail (a scheduled report, RPT-007). Kept in the outbox only until the mail is sent or given up.</summary>
    public EmailAttachment? Attachment { get; init; }
}

public sealed record EmailAttachment(string FileName, string ContentType, byte[] Content);

/// <summary>Delivers one email through the configured provider (console in dev, Resend in production).</summary>
public interface IEmailSender { Task SendAsync(EmailMessage message, CancellationToken ct = default); }

/// <summary>Fire-and-forget hand-off so request latency does not depend on the email provider (prevents timing-based enumeration).</summary>
public interface IEmailQueue { void Enqueue(EmailMessage message); }

/// <summary>Reports whether a password appears in known breach corpora. Must fail open (return false) if the service is unavailable.</summary>
public interface IBreachedPasswordChecker { Task<bool> IsBreachedAsync(string password, CancellationToken ct = default); }

/// <summary>Browser-visible origin used to build links in emails (never taken from request headers).</summary>
public interface IAppLinks { string Origin { get; } }

public readonly record struct ProtectedSecret(byte[] Ciphertext, byte[] Nonce, int KeyVersion);

/// <summary>Encrypts small secrets (MFA seeds) at rest with a key that never lives in the database.</summary>
public interface ISecretProtector
{
    /// <param name="aad">Binds the ciphertext to its row (e.g. user id) so it cannot be swapped between rows.</param>
    ProtectedSecret Protect(byte[] plaintext, byte[] aad);
    byte[] Unprotect(ProtectedSecret secret, byte[] aad);
    /// <summary>Keyed hash (HMAC-SHA256) for high-entropy values such as recovery codes. Domain-separated by <paramref name="purpose"/>.</summary>
    string KeyedHash(string purpose, string value);
}

/// <summary>Read-only facts about the email infrastructure that the settings screen may show (never the key itself).</summary>
public interface IEmailInfo
{
    string Provider { get; }
    bool ConfigurationKeyPresent { get; }
    /// <summary>The selected provider has what it needs to deliver (SMTP host, or Resend key in configuration). "console" counts as configured (development).</summary>
    bool Configured { get; }
}

/// <summary>Resolves the outbound email API key: an encrypted database secret wins over configuration.</summary>
public interface IEmailCredentials { Task<string?> GetResendApiKeyAsync(CancellationToken ct = default); }

/// <summary>Who an OpenID Connect provider says signed in (already validated: signature, issuer, audience, expiry, nonce).</summary>
/// <param name="Groups">The token's groups claim (R3-9), or null when the token carries none: then roles are left as they are.</param>
public sealed record OidcIdentity(string Issuer, string Subject, string? Email, bool EmailVerified, string? Name, IReadOnlyList<string>? Groups = null);
public sealed class OidcException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Single sign-on with any OpenID Connect provider (authorization-code flow with PKCE). Disabled unless configured.</summary>
public interface IOidcClient
{
    bool Enabled { get; }
    string DisplayName { get; }
    Task<string> BuildAuthorizeUrlAsync(string redirectUri, string state, string nonce, string codeChallenge, CancellationToken ct = default);
    Task<OidcIdentity> ExchangeAsync(string redirectUri, string code, string codeVerifier, string nonce, CancellationToken ct = default);
}

/// <summary>
/// Which tenant the current request acts in. Once <see cref="Use"/> is called (right after a session authenticates) every query is filtered to that tenant and every
/// new row is stamped with it. Until then — anonymous sign-in, registration, workers — the context is unscoped, and those few flows locate rows by unique values.
/// </summary>
public interface ITenantContext
{
    bool Scoped { get; }
    Guid TenantId { get; }
    bool IsPlatform { get; }
    /// <summary>True while a platform administrator is acting inside another organisation (support mode); audit labels then say so.</summary>
    bool Support { get; }
    void Use(Guid tenantId, bool isPlatform, bool support = false);
    /// <summary>Temporarily acts in another tenant (e.g. while the platform creates a tenant's first administrator); the previous scope returns on dispose.</summary>
    IDisposable Switch(Guid tenantId, bool isPlatform = false);
}

/// <summary>NTF-003. One message for one person on one outside channel. <see cref="Settings"/> holds the organisation's settings and decrypted credentials.</summary>
public sealed record ChannelSend(string Kind, string Address, string Purpose, string Title, string? Body, string? Url, IReadOnlyDictionary<string, string> Settings, string Language);

/// <summary>A channel could not take the message. <paramref name="permanent"/>: retrying will not help (bad credentials, unknown recipient).</summary>
public sealed class ChannelSendException(string message, bool permanent) : Exception(message) { public bool Permanent { get; } = permanent; }

/// <summary>NTF-003 port: WhatsApp, Slack and Teams adapters (Infrastructure); tests replace them.</summary>
public interface IChannelSender
{
    string Kind { get; }
    Task SendAsync(ChannelSend message, CancellationToken ct = default);
    /// <summary>Finds the person's address on the channel from their e-mail (Slack); null when unknown or not supported.</summary>
    Task<string?> ResolveAsync(ChannelSend settings, string email, CancellationToken ct = default);
}
