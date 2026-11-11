using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Domain.Entities;

namespace AnnabaDigital.Infrastructure.Persistence;

public class AnnabaDigitalDbContext(DbContextOptions<AnnabaDigitalDbContext> options) : DbContext(options), IAppDb
{
    public DbSet<User> Users => Set<User>();
    public DbSet<UserCredential> Credentials => Set<UserCredential>();
    public DbSet<UserProfile> Profiles => Set<UserProfile>();
    public DbSet<Avatar> Avatars => Set<Avatar>();
    public DbSet<ExternalIdentity> ExternalIdentities => Set<ExternalIdentity>();
    public DbSet<EmailVerificationToken> EmailVerificationTokens => Set<EmailVerificationToken>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<MfaFactor> MfaFactors => Set<MfaFactor>();
    public DbSet<MfaRecoveryCode> MfaRecoveryCodes => Set<MfaRecoveryCode>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<Invitation> Invitations => Set<Invitation>();
    public DbSet<InvitationRole> InvitationRoles => Set<InvitationRole>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<SecurityEvent> SecurityEvents => Set<SecurityEvent>();
    public DbSet<SystemSetting> Settings => Set<SystemSetting>();
    public DbSet<RateLimitBucket> RateLimitBuckets => Set<RateLimitBucket>();
    public DbSet<OutboxEmail> OutboxEmails => Set<OutboxEmail>();
    public DbSet<AccessRequest> AccessRequests => Set<AccessRequest>();
    public DbSet<AccessReview> AccessReviews => Set<AccessReview>();
    public DbSet<UserIdentity> UserIdentities => Set<UserIdentity>();
    public DbSet<AccessReviewItem> AccessReviewItems => Set<AccessReviewItem>();
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<AccessReviewSchedule> AccessReviewSchedules => Set<AccessReviewSchedule>();
    public DbSet<TenantSetting> TenantSettings => Set<TenantSetting>();
    public DbSet<AccountClosureRequest> AccountClosureRequests => Set<AccountClosureRequest>();
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<BusinessCalendar> Calendars => Set<BusinessCalendar>();
    public DbSet<CalendarPeriod> CalendarPeriods => Set<CalendarPeriod>();
    public DbSet<CalendarHoliday> CalendarHolidays => Set<CalendarHoliday>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<TeamMember> TeamMembers => Set<TeamMember>();
    public DbSet<ServiceCategory> ServiceCategories => Set<ServiceCategory>();
    public DbSet<ServiceItem> Services => Set<ServiceItem>();
    public DbSet<ServiceFormVersion> ServiceFormVersions => Set<ServiceFormVersion>();
    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<TicketEvent> TicketEvents => Set<TicketEvent>();
    public DbSet<TicketCounter> TicketCounters => Set<TicketCounter>();
    public DbSet<SlaPolicy> SlaPolicies => Set<SlaPolicy>();
    public DbSet<AssignmentRule> AssignmentRules => Set<AssignmentRule>();
    public DbSet<ApprovalPolicy> ApprovalPolicies => Set<ApprovalPolicy>();
    public DbSet<TicketApproval> TicketApprovals => Set<TicketApproval>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<Attachment> Attachments => Set<Attachment>();
    public DbSet<TicketView> TicketViews => Set<TicketView>();
    public DbSet<ApprovalDelegation> ApprovalDelegations => Set<ApprovalDelegation>();
    public DbSet<NotificationPreference> NotificationPreferences => Set<NotificationPreference>();
    public DbSet<RetentionPolicy> RetentionPolicies => Set<RetentionPolicy>();
    public DbSet<TicketLegalHold> TicketLegalHolds => Set<TicketLegalHold>();
    public DbSet<EmailWording> EmailWordings => Set<EmailWording>();
    public DbSet<AssetType> AssetTypes => Set<AssetType>();
    public DbSet<Asset> Assets => Set<Asset>();
    public DbSet<AssetEvent> AssetEvents => Set<AssetEvent>();
    public DbSet<AssetCounter> AssetCounters => Set<AssetCounter>();
    public DbSet<WorkOrder> WorkOrders => Set<WorkOrder>();
    public DbSet<WorkOrderTask> WorkOrderTasks => Set<WorkOrderTask>();
    public DbSet<WorkOrderTime> WorkOrderTimes => Set<WorkOrderTime>();
    public DbSet<WorkOrderCost> WorkOrderCosts => Set<WorkOrderCost>();
    public DbSet<ChecklistTemplate> ChecklistTemplates => Set<ChecklistTemplate>();
    public DbSet<Vendor> Vendors => Set<Vendor>();
    public DbSet<VendorContact> VendorContacts => Set<VendorContact>();
    public DbSet<VendorSite> VendorSites => Set<VendorSite>();
    public DbSet<VendorContract> VendorContracts => Set<VendorContract>();
    public DbSet<VendorDocument> VendorDocuments => Set<VendorDocument>();
    public DbSet<MaintenancePlan> MaintenancePlans => Set<MaintenancePlan>();
    public DbSet<MaintenanceVisit> MaintenanceVisits => Set<MaintenanceVisit>();
    public DbSet<TicketRating> TicketRatings => Set<TicketRating>();
    public DbSet<KbCategory> KbCategories => Set<KbCategory>();
    public DbSet<KbArticle> KbArticles => Set<KbArticle>();
    public DbSet<KbArticleVersion> KbArticleVersions => Set<KbArticleVersion>();
    public DbSet<KbArticleService> KbArticleServices => Set<KbArticleService>();
    public DbSet<KbFeedback> KbFeedback => Set<KbFeedback>();
    public DbSet<KbFile> KbFiles => Set<KbFile>();
    public DbSet<CostSettings> CostSettings => Set<CostSettings>();
    public DbSet<CostCenter> CostCenters => Set<CostCenter>();
    public DbSet<CostCenterPlace> CostCenterPlaces => Set<CostCenterPlace>();
    public DbSet<Budget> Budgets => Set<Budget>();
    public DbSet<InboundMailbox> InboundMailboxes => Set<InboundMailbox>();
    public DbSet<InboundMessage> InboundMessages => Set<InboundMessage>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
    public DbSet<ApiIdempotency> ApiIdempotency => Set<ApiIdempotency>();
    public DbSet<WebhookEndpoint> WebhookEndpoints => Set<WebhookEndpoint>();
    public DbSet<WebhookDelivery> WebhookDeliveries => Set<WebhookDelivery>();
    public DbSet<VendorUser> VendorUsers => Set<VendorUser>();
    public DbSet<BookableResource> BookableResources => Set<BookableResource>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<BookingFeed> BookingFeeds => Set<BookingFeed>();
    public DbSet<VehicleTrip> VehicleTrips => Set<VehicleTrip>();
    public DbSet<ResourceDocument> ResourceDocuments => Set<ResourceDocument>();
    public DbSet<DriverLicence> DriverLicences => Set<DriverLicence>();
    public DbSet<Visit> Visits => Set<Visit>();
    public DbSet<VisitorBlock> VisitorBlocks => Set<VisitorBlock>();
    public DbSet<VisitorSettings> VisitorSettings => Set<VisitorSettings>();
    public DbSet<MailItem> MailItems => Set<MailItem>();
    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();
    public DbSet<StockBalance> StockBalances => Set<StockBalance>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<StockTake> StockTakes => Set<StockTake>();
    public DbSet<StockTakeLine> StockTakeLines => Set<StockTakeLine>();
    public DbSet<SupplyRequest> SupplyRequests => Set<SupplyRequest>();
    public DbSet<SupplyRequestLine> SupplyRequestLines => Set<SupplyRequestLine>();
    public DbSet<SupplySettings> SupplySettings => Set<SupplySettings>();
    public DbSet<ReportSchedule> ReportSchedules => Set<ReportSchedule>();
    public DbSet<DirectoryGroupMapping> DirectoryGroupMappings => Set<DirectoryGroupMapping>();
    public DbSet<ScimGroup> ScimGroups => Set<ScimGroup>();
    public DbSet<ChannelSetting> ChannelSettings => Set<ChannelSetting>();
    public DbSet<UserChannel> UserChannels => Set<UserChannel>();
    public DbSet<ChannelDelivery> ChannelDeliveries => Set<ChannelDelivery>();
    public DbSet<OfflineOp> OfflineOps => Set<OfflineOp>();

    private sealed record SequenceRow(int Last);

    public async Task<int> NextTicketSequenceAsync(Guid tenantId, int year, CancellationToken ct = default)
    {
        var rows = await Database.SqlQuery<SequenceRow>($"""
            INSERT INTO ticket_counters (tenant_id, year, last) VALUES ({tenantId}, {year}, 1)
            ON CONFLICT (tenant_id, year) DO UPDATE SET last = ticket_counters.last + 1
            RETURNING last
            """).ToListAsync(ct); // ToListAsync: INSERT…RETURNING is not composable
        return rows.Single().Last;
    }

    public async Task<int> NextAssetNumberAsync(Guid tenantId, CancellationToken ct = default)
    {
        var rows = await Database.SqlQuery<SequenceRow>($"""
            INSERT INTO asset_counters (tenant_id, last) VALUES ({tenantId}, 1)
            ON CONFLICT (tenant_id) DO UPDATE SET last = asset_counters.last + 1
            RETURNING last
            """).ToListAsync(ct);
        return rows.Single().Last;
    }

    /// <summary>Set by the scoped factory registration; null for workers and design time (then nothing is filtered).</summary>
    public ITenantContext? Tenant { get; set; }
    private bool TenantScoped => Tenant?.Scoped == true;
    private Guid CurrentTenant => Tenant?.TenantId ?? Guid.Empty;

    protected override void OnConfiguring(DbContextOptionsBuilder o) => o.ConfigureWarnings(w => w.Ignore(CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning));

    private void Filter<T>(ModelBuilder b) where T : class, ITenantScoped => b.Entity<T>().HasQueryFilter(e => !TenantScoped || e.TenantId == CurrentTenant);

    /// <summary>In a tenant scope every new row belongs to that tenant, whatever the caller set. Unscoped writers (sign-in, registration) keep what they set, and audit/security rows take their person's tenant.</summary>
    private async Task StampTenantAsync(CancellationToken ct)
    {
        var added = ChangeTracker.Entries<ITenantScoped>().Where(e => e.State == EntityState.Added).ToList();
        if (added.Count == 0) return;
        if (TenantScoped) { foreach (var e in added) e.Entity.TenantId = CurrentTenant; return; }
        var loose = added.Where(e => e.Entity.TenantId == Domain.Entities.Tenant.DefaultId && e.Entity is AuditEvent or SecurityEvent).ToList();
        if (loose.Count == 0) return;
        Guid? Who(ITenantScoped t) => t is AuditEvent a ? a.ActorId : t is SecurityEvent x ? x.UserId : null;
        var ids = loose.Select(e => Who(e.Entity)).Where(i => i is not null).Select(i => i!.Value).Distinct().ToList();
        if (ids.Count == 0) return;
        // People created in this same save (accepting an invitation) aren't in the database yet: take their organisation from the tracked row.
        var map = ChangeTracker.Entries<User>().Where(e => e.State == EntityState.Added && ids.Contains(e.Entity.Id)).ToDictionary(e => e.Entity.Id, e => e.Entity.TenantId);
        var rest = ids.Where(i => !map.ContainsKey(i)).ToList();
        if (rest.Count > 0) foreach (var kv in await Users.IgnoreQueryFilters().Where(u => rest.Contains(u.Id)).Select(u => new { u.Id, u.TenantId }).ToListAsync(ct)) map[kv.Id] = kv.TenantId;
        foreach (var e in loose) if (Who(e.Entity) is { } id && map.TryGetValue(id, out var tid)) e.Entity.TenantId = tid;
    }

    /// <summary>Raised after a save that changed system settings, organisation settings or organisations (clears the short-lived caches).</summary>
    public Action? ReferenceDataChanged { get; set; }
    public event Action? PermissionDataChanged;

    private (bool Reference, bool Permissions) Touched()
    {
        var reference = false; var permissions = false;
        foreach (var e in ChangeTracker.Entries())
        {
            if (e.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted)) continue;
            if (e.Entity is Domain.Entities.Tenant or SystemSetting or TenantSetting) reference = true;
            else if (e.Entity is UserRole or RolePermission or Role or User) permissions = true;
        }
        return (reference, permissions);
    }

    private void Raise((bool Reference, bool Permissions) t) { if (t.Reference) ReferenceDataChanged?.Invoke(); if (t.Permissions) PermissionDataChanged?.Invoke(); }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        await StampTenantAsync(cancellationToken);
        var touched = Touched();
        var n = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        Raise(touched); return n;
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampTenantAsync(CancellationToken.None).GetAwaiter().GetResult();
        var touched = Touched();
        var n = base.SaveChanges(acceptAllChangesOnSuccess);
        Raise(touched); return n;
    }

    public void ResetTracking() => ChangeTracker.Clear();

    public Task<int> ExecuteSqlAsync(FormattableString sql, CancellationToken ct = default) => Database.ExecuteSqlAsync(sql, ct);

    public async Task<T> InTransactionAsync<T>(Func<Task<T>> work, CancellationToken ct = default)
    {
        if (Database.CurrentTransaction is not null) return await work();
        await using IDbContextTransaction tx = await Database.BeginTransactionAsync(ct);
        try { var r = await work(); await tx.CommitAsync(ct); return r; }
        catch { await tx.RollbackAsync(ct); throw; }
    }

    /// <summary>Locations, business calendars and teams. Every table is tenant-scoped (filtered, stamped, tenant FK, tenant-leading indexes).</summary>
    private void ConfigureHelpdeskSetup(ModelBuilder b)
    {
        b.Entity<Location>(e =>
        {
            e.ToTable("locations", t =>
            {
                t.HasCheckConstraint("ck_locations_capacity", "capacity IS NULL OR capacity >= 0");
                t.HasCheckConstraint("ck_locations_not_own_parent", "parent_id IS NULL OR parent_id <> id");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Code).HasMaxLength(30).IsRequired();
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
            e.Property(x => x.Address).HasMaxLength(300);
            e.Property(x => x.TimeZone).HasMaxLength(64);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique().HasFilter("archived_at IS NULL"); // a code is free again once its place is archived
            e.HasIndex(x => new { x.TenantId, x.ParentId });
            e.HasOne<Location>().WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<BusinessCalendar>().WithMany().HasForeignKey(x => x.CalendarId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.ManagerUserId).OnDelete(DeleteBehavior.SetNull);
        });
        b.Entity<BusinessCalendar>(e =>
        {
            e.ToTable("business_calendars");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.TimeZone).HasMaxLength(64).IsRequired();
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => new { x.TenantId, x.Name }).IsUnique().HasFilter("archived_at IS NULL");
            e.HasIndex(x => x.TenantId).IsUnique().HasFilter("is_default AND archived_at IS NULL"); // exactly one default per organisation
            e.HasMany(x => x.Periods).WithOne().HasForeignKey(p => p.CalendarId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Holidays).WithOne().HasForeignKey(h => h.CalendarId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<CalendarPeriod>(e =>
        {
            e.ToTable("calendar_periods", t =>
            {
                t.HasCheckConstraint("ck_calendar_periods_range", "start_minute >= 0 AND end_minute <= 1440 AND end_minute > start_minute");
                t.HasCheckConstraint("ck_calendar_periods_day", "day >= 0 AND day <= 6");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Day).HasConversion<int>();
            e.HasIndex(x => new { x.CalendarId, x.Day });
        });
        b.Entity<CalendarHoliday>(e =>
        {
            e.ToTable("calendar_holidays");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.HasIndex(x => new { x.CalendarId, x.Date }).IsUnique();
        });
        b.Entity<Team>(e =>
        {
            e.ToTable("teams");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.Description).HasMaxLength(300);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => new { x.TenantId, x.Name }).IsUnique().HasFilter("archived_at IS NULL");
            e.HasMany(x => x.Members).WithOne().HasForeignKey(m => m.TeamId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<TeamMember>(e =>
        {
            e.ToTable("team_members");
            e.HasKey(x => new { x.TeamId, x.UserId });
            e.HasIndex(x => x.UserId);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        foreach (var t in new[] { typeof(Location), typeof(BusinessCalendar), typeof(CalendarPeriod), typeof(CalendarHoliday), typeof(Team), typeof(TeamMember) })
            b.Entity(t).HasOne(typeof(Tenant)).WithMany().HasForeignKey("TenantId").OnDelete(DeleteBehavior.Restrict);
        Filter<Location>(b); Filter<BusinessCalendar>(b); Filter<CalendarPeriod>(b); Filter<CalendarHoliday>(b); Filter<Team>(b); Filter<TeamMember>(b);
    }

    /// <summary>Service catalog and versioned forms (CAT-001/003). Tenant-scoped like everything else.</summary>
    private void ConfigureCatalog(ModelBuilder b)
    {
        b.Entity<ServiceCategory>(e =>
        {
            e.ToTable("service_categories", t => t.HasCheckConstraint("ck_service_categories_not_own_parent", "parent_id IS NULL OR parent_id <> id"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.Description).HasMaxLength(300);
            e.Property(x => x.Icon).HasMaxLength(40);
            e.Property(x => x.Version).IsConcurrencyToken();
            // a name is unique among live siblings; NULL parents never collide in a unique index, so roots get their own index
            e.HasIndex(x => new { x.TenantId, x.Name }).IsUnique().HasFilter("parent_id IS NULL AND archived_at IS NULL").HasDatabaseName("ux_service_categories_root_name");
            e.HasIndex(x => new { x.TenantId, x.ParentId, x.Name }).IsUnique().HasFilter("parent_id IS NOT NULL AND archived_at IS NULL").HasDatabaseName("ux_service_categories_child_name");
            e.HasOne<ServiceCategory>().WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<ServiceItem>(e =>
        {
            e.ToTable("services", t => t.HasCheckConstraint("ck_services_form_version", "current_form_version >= 0"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
            e.Property(x => x.Description).HasMaxLength(500);
            e.Property(x => x.Icon).HasMaxLength(40);
            e.Property(x => x.DefaultPriority).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Audience).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => new { x.TenantId, x.CategoryId, x.Name }).IsUnique().HasFilter("archived_at IS NULL");
            e.HasIndex(x => new { x.TenantId, x.IsPublished });
            e.HasOne<ServiceCategory>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Team>().WithMany().HasForeignKey(x => x.OwningTeamId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<ServiceFormVersion>(e =>
        {
            e.ToTable("service_form_versions", t => t.HasCheckConstraint("ck_service_form_versions_number", "number >= 1"));
            e.HasKey(x => x.Id);
            e.Property(x => x.FieldsJson).HasColumnType("jsonb").IsRequired();
            e.Property(x => x.Note).HasMaxLength(200);
            e.HasIndex(x => new { x.ServiceId, x.Number }).IsUnique(); // a version number exists once: concurrent saves can't both claim it
            e.HasOne<ServiceItem>().WithMany().HasForeignKey(x => x.ServiceId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.SetNull);
        });
        foreach (var t in new[] { typeof(ServiceCategory), typeof(ServiceItem), typeof(ServiceFormVersion) })
            b.Entity(t).HasOne(typeof(Tenant)).WithMany().HasForeignKey("TenantId").OnDelete(DeleteBehavior.Restrict);
        Filter<ServiceCategory>(b); Filter<ServiceItem>(b); Filter<ServiceFormVersion>(b);
    }

    /// <summary>Tickets, their timeline and the per-year number counter (TKT-001/002/004). Tenant-scoped like everything else.</summary>
    private void ConfigureTickets(ModelBuilder b)
    {
        b.Entity<Ticket>(e =>
        {
            e.ToTable("tickets", t =>
            {
                t.HasCheckConstraint("ck_tickets_sequence", "sequence >= 1");
                t.HasCheckConstraint("ck_tickets_form_version", "form_version >= 0");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Number).HasMaxLength(30).IsRequired();
            e.Property(x => x.ServiceName).HasMaxLength(120).IsRequired();
            e.Property(x => x.Subject).HasMaxLength(200).IsRequired();
            e.Property(x => x.Description).HasMaxLength(4000);
            e.Property(x => x.AnswersJson).HasColumnType("jsonb").IsRequired();
            e.Property(x => x.Priority).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.HoldReason).HasMaxLength(500);
            e.Property(x => x.ResolutionCode).HasMaxLength(30);
            e.Property(x => x.ResolutionNote).HasMaxLength(2000);
            e.Property(x => x.CancelReason).HasMaxLength(500);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => new { x.TenantId, x.Number }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.Year, x.Sequence }).IsUnique();
            e.Property(x => x.SlaState).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => new { x.TenantId, x.SlaState });
            e.HasIndex(x => x.ResolutionDueAt).HasFilter("sla_policy_id IS NOT NULL AND resolved_at IS NULL"); // what the periodic check scans
            e.HasOne<SlaPolicy>().WithMany().HasForeignKey(x => x.SlaPolicyId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<Asset>().WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.TenantId, x.AssetId }).HasFilter("asset_id IS NOT NULL"); // an asset's request history
            e.HasIndex(x => new { x.TenantId, x.Status, x.UpdatedAt });
            // the queue's default view (everything not finished, newest first) and its count read this small index instead of the whole table
            e.HasIndex(x => new { x.TenantId, x.UpdatedAt, x.Id }).IsDescending(false, true, true).HasFilter("status NOT IN ('Closed', 'Cancelled', 'Rejected')").HasDatabaseName("ix_tickets_tenant_id_open_updated_at");
            e.HasIndex(x => new { x.TenantId, x.RequesterId, x.UpdatedAt });
            e.HasIndex(x => new { x.TenantId, x.AssigneeId });
            e.HasIndex(x => new { x.TenantId, x.TeamId });
            e.HasOne<ServiceItem>().WithMany().HasForeignKey(x => x.ServiceId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.RequesterId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedById).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.AssigneeId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<Team>().WithMany().HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<Location>().WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.SetNull);
        });
        b.Entity<TicketEvent>(e =>
        {
            e.ToTable("ticket_events");
            e.HasKey(x => x.Id);
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.FromStatus).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.ToStatus).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.ActorLabel).HasMaxLength(200).IsRequired();
            e.Property(x => x.Body).HasMaxLength(4000);
            e.HasIndex(x => new { x.TicketId, x.CreatedAt });
            e.HasOne<Ticket>().WithMany().HasForeignKey(x => x.TicketId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.SetNull);
        });
        b.Entity<TicketCounter>(e =>
        {
            e.ToTable("ticket_counters", t => t.HasCheckConstraint("ck_ticket_counters_last", "last >= 0"));
            e.HasKey(x => new { x.TenantId, x.Year });
        });
        b.Entity<SlaPolicy>(e =>
        {
            e.ToTable("sla_policies", t =>
            {
                t.HasCheckConstraint("ck_sla_policies_targets", "first_response_minutes >= 1 AND resolution_minutes >= first_response_minutes");
                t.HasCheckConstraint("ck_sla_policies_escalation", "escalate1percent BETWEEN 1 AND 98 AND escalate2percent > escalate1percent AND escalate2percent <= 99");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.Priority).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => new { x.TenantId, x.Name }).IsUnique().HasFilter("archived_at IS NULL");
            e.HasIndex(x => new { x.TenantId, x.Position });
            e.HasOne<ServiceItem>().WithMany().HasForeignKey(x => x.ServiceId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ServiceCategory>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Location>().WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<BusinessCalendar>().WithMany().HasForeignKey(x => x.CalendarId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<AssignmentRule>(e =>
        {
            e.ToTable("assignment_rules");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.Priority).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Strategy).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => new { x.TenantId, x.Name }).IsUnique().HasFilter("archived_at IS NULL");
            e.HasIndex(x => new { x.TenantId, x.Position });
            e.HasOne<ServiceItem>().WithMany().HasForeignKey(x => x.ServiceId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ServiceCategory>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Location>().WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Team>().WithMany().HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<ApprovalPolicy>(e =>
        {
            e.ToTable("approval_policies");
            e.HasKey(x => x.Id);
            e.Property(x => x.StepsJson).HasColumnType("jsonb").IsRequired();
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => new { x.TenantId, x.ServiceId }).IsUnique().HasFilter("archived_at IS NULL"); // one live policy per service
            e.HasOne<ServiceItem>().WithMany().HasForeignKey(x => x.ServiceId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.FallbackUserId).OnDelete(DeleteBehavior.SetNull);
        });
        b.Entity<TicketApproval>(e =>
        {
            e.ToTable("ticket_approvals", t => t.HasCheckConstraint("ck_ticket_approvals_step", "step_no >= 1"));
            e.HasKey(x => x.Id);
            e.Property(x => x.StepName).HasMaxLength(60).IsRequired();
            e.Property(x => x.Rule).HasMaxLength(10).IsRequired();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Comment).HasMaxLength(1000);
            e.Property(x => x.DecidedVia).HasMaxLength(10);
            e.Property(x => x.Ip).HasMaxLength(64);
            e.Property(x => x.Device).HasMaxLength(120);
            e.Property(x => x.TokenHash).HasMaxLength(64);
            e.HasIndex(x => new { x.TicketId, x.StepNo });
            e.HasIndex(x => new { x.TenantId, x.ApproverId, x.Status });
            e.HasIndex(x => x.TokenHash).IsUnique().HasFilter("token_hash IS NOT NULL");
            e.HasIndex(x => new { x.TicketId, x.StepNo, x.ApproverId }).IsUnique(); // one row per person per step
            e.HasOne<Ticket>().WithMany().HasForeignKey(x => x.TicketId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.ApproverId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<Notification>(e =>
        {
            e.ToTable("notifications");
            e.HasKey(x => x.Id);
            e.Property(x => x.Kind).HasMaxLength(40).IsRequired();
            e.Property(x => x.Title).HasMaxLength(200).IsRequired();
            e.Property(x => x.Body).HasMaxLength(500);
            e.Property(x => x.Link).HasMaxLength(200);
            e.HasIndex(x => new { x.TenantId, x.UserId, x.ReadAt, x.CreatedAt });
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Ticket>().WithMany().HasForeignKey(x => x.TicketId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<Attachment>(e =>
        {
            e.ToTable("attachments", t => t.HasCheckConstraint("ck_attachments_size", "size > 0"));
            e.HasKey(x => x.Id);
            e.Property(x => x.FileName).HasMaxLength(150).IsRequired();
            e.Property(x => x.ContentType).HasMaxLength(120).IsRequired();
            e.Property(x => x.Sha256).HasMaxLength(64).IsRequired();
            e.Property(x => x.StorageKey).HasMaxLength(80).IsRequired();
            e.HasIndex(x => x.TicketId);
            e.HasIndex(x => new { x.TenantId, x.CreatedAt }).HasFilter("ticket_id IS NULL AND deleted_at IS NULL"); // what the purge of unclaimed uploads scans
            e.Property(x => x.Stage).HasMaxLength(10);
            e.HasIndex(x => x.WorkOrderId).HasFilter("work_order_id IS NOT NULL");
            e.HasOne<WorkOrder>().WithMany().HasForeignKey(x => x.WorkOrderId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<Ticket>().WithMany().HasForeignKey(x => x.TicketId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UploadedById).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<NotificationPreference>(e =>
        {
            e.ToTable("notification_preferences");
            e.HasKey(x => x.Id);
            e.Property(x => x.Kind).HasMaxLength(40).IsRequired();
            e.HasIndex(x => new { x.TenantId, x.UserId, x.Kind }).IsUnique();
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<ApprovalDelegation>(e =>
        {
            e.ToTable("approval_delegations", t => { t.HasCheckConstraint("ck_approval_delegations_period", "ends_at > starts_at"); t.HasCheckConstraint("ck_approval_delegations_self", "user_id <> delegate_id"); });
            e.HasKey(x => x.Id);
            e.Property(x => x.Note).HasMaxLength(300);
            e.HasIndex(x => new { x.TenantId, x.UserId, x.StartsAt });
            e.HasIndex(x => new { x.TenantId, x.DelegateId });
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.DelegateId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<TicketView>(e =>
        {
            e.ToTable("ticket_views");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(60).IsRequired();
            e.Property(x => x.FilterJson).HasMaxLength(2000).IsRequired();
            e.HasIndex(x => new { x.TenantId, x.OwnerId, x.Name }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.Shared });
            e.HasOne<User>().WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<RetentionPolicy>(e =>
        {
            e.ToTable("retention_policies", t =>
            {
                t.HasCheckConstraint("ck_retention_policies_action", "ticket_action IN ('anonymise', 'delete')");
                t.HasCheckConstraint("ck_retention_policies_periods", "(finished_ticket_days IS NULL OR finished_ticket_days BETWEEN 30 AND 3650) AND (attachment_days IS NULL OR attachment_days BETWEEN 7 AND 3650) AND (notification_days IS NULL OR notification_days BETWEEN 7 AND 3650)");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.TicketAction).HasMaxLength(12).IsRequired();
            e.Property(x => x.LastRunJson).HasMaxLength(500);
            e.HasIndex(x => x.TenantId).IsUnique(); // one policy per organisation
        });
        b.Entity<TicketLegalHold>(e =>
        {
            e.ToTable("ticket_legal_holds");
            e.HasKey(x => x.Id);
            e.Property(x => x.Reason).HasMaxLength(300).IsRequired();
            e.HasIndex(x => x.TicketId).IsUnique(); // a ticket is either under hold or not
            e.HasIndex(x => new { x.TenantId, x.PlacedAt });
            e.HasOne<Ticket>().WithMany().HasForeignKey(x => x.TicketId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.PlacedById).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<AssetType>(e =>
        {
            e.ToTable("asset_types");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.Description).HasMaxLength(300);
            e.Property(x => x.FieldsJson).HasColumnType("jsonb").IsRequired();
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => new { x.TenantId, x.Name }).IsUnique().HasFilter("archived_at IS NULL");
        });
        b.Entity<Asset>(e =>
        {
            e.ToTable("assets", t => t.HasCheckConstraint("ck_assets_cost", "purchase_cost IS NULL OR purchase_cost >= 0"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Tag).HasMaxLength(40).IsRequired();
            e.Property(x => x.Name).HasMaxLength(150).IsRequired();
            e.Property(x => x.Serial).HasMaxLength(100);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Department).HasMaxLength(100);
            e.Property(x => x.PurchaseCost).HasPrecision(15, 2);
            e.Property(x => x.VendorName).HasMaxLength(120);
            e.Property(x => x.Notes).HasMaxLength(2000);
            e.Property(x => x.RetireReason).HasMaxLength(300);
            e.Property(x => x.FieldsJson).HasColumnType("jsonb").IsRequired();
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => new { x.TenantId, x.Tag }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.AssetTypeId });
            e.HasIndex(x => new { x.TenantId, x.LocationId });
            e.HasIndex(x => new { x.TenantId, x.AssignedToId });
            e.HasIndex(x => new { x.TenantId, x.WarrantyEnd }).HasFilter("retired_at IS NULL AND warranty_end IS NOT NULL");
            e.HasOne<AssetType>().WithMany().HasForeignKey(x => x.AssetTypeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Location>().WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.AssignedToId).OnDelete(DeleteBehavior.SetNull);
        });
        b.Entity<AssetEvent>(e =>
        {
            e.ToTable("asset_events");
            e.HasKey(x => x.Id);
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.ActorLabel).HasMaxLength(200).IsRequired();
            e.Property(x => x.Body).HasMaxLength(1000);
            e.HasIndex(x => new { x.AssetId, x.CreatedAt });
            e.HasOne<Asset>().WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.SetNull);
        });
        b.Entity<AssetCounter>(e =>
        {
            e.ToTable("asset_counters", t => t.HasCheckConstraint("ck_asset_counters_last", "last >= 0"));
            e.HasKey(x => x.TenantId);
        });
        b.Entity<WorkOrder>(e =>
        {
            e.ToTable("work_orders", t => t.HasCheckConstraint("ck_work_orders_seq", "seq >= 1"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Number).HasMaxLength(40).IsRequired();
            e.Property(x => x.Title).HasMaxLength(150).IsRequired();
            e.Property(x => x.Instructions).HasMaxLength(4000);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.HoldReason).HasMaxLength(500);
            e.Property(x => x.CompletionNote).HasMaxLength(2000);
            e.Property(x => x.CancelReason).HasMaxLength(500);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => new { x.TicketId, x.Seq }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.AssigneeId, x.Status });
            e.HasIndex(x => new { x.TenantId, x.Status, x.UpdatedAt });
            e.HasOne<Ticket>().WithMany().HasForeignKey(x => x.TicketId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.AssigneeId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<Team>().WithMany().HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<Asset>().WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.PurchaseOrder).HasMaxLength(50);
            e.HasIndex(x => new { x.TenantId, x.VendorId }).HasFilter("vendor_id IS NOT NULL");
            e.HasOne<Vendor>().WithMany().HasForeignKey(x => x.VendorId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<WorkOrderTask>(e =>
        {
            e.ToTable("work_order_tasks");
            e.HasKey(x => x.Id);
            e.Property(x => x.Text).HasMaxLength(300).IsRequired();
            e.Property(x => x.DoneByLabel).HasMaxLength(200);
            e.Property(x => x.Note).HasMaxLength(500);
            e.HasIndex(x => new { x.WorkOrderId, x.Position });
            e.HasOne<WorkOrder>().WithMany().HasForeignKey(x => x.WorkOrderId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<WorkOrderTime>(e =>
        {
            e.ToTable("work_order_times", t => t.HasCheckConstraint("ck_work_order_times_minutes", "minutes BETWEEN 1 AND 1440"));
            e.HasKey(x => x.Id);
            e.Property(x => x.UserLabel).HasMaxLength(200).IsRequired();
            e.Property(x => x.Note).HasMaxLength(500);
            e.HasIndex(x => x.WorkOrderId);
            e.HasIndex(x => new { x.TenantId, x.UserId, x.WorkDate });
            e.HasOne<WorkOrder>().WithMany().HasForeignKey(x => x.WorkOrderId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<WorkOrderCost>(e =>
        {
            e.ToTable("work_order_costs", t => t.HasCheckConstraint("ck_work_order_costs_amounts", "quantity > 0 AND unit_cost >= 0"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Kind).HasMaxLength(20).IsRequired();
            e.Property(x => x.Description).HasMaxLength(200).IsRequired();
            e.Property(x => x.Quantity).HasPrecision(12, 2);
            e.Property(x => x.UnitCost).HasPrecision(15, 2);
            e.Property(x => x.CreatedByLabel).HasMaxLength(200).IsRequired();
            e.HasIndex(x => x.WorkOrderId);
            e.HasOne<WorkOrder>().WithMany().HasForeignKey(x => x.WorkOrderId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<InventoryItem>().WithMany().HasForeignKey(x => x.StockItemId).OnDelete(DeleteBehavior.Restrict);   // INV-005 parts from stock
            e.HasOne<Location>().WithMany().HasForeignKey(x => x.StockLocationId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<ChecklistTemplate>(e =>
        {
            e.ToTable("checklist_templates");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.ItemsJson).HasColumnType("jsonb").IsRequired();
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => new { x.TenantId, x.Name }).IsUnique().HasFilter("archived_at IS NULL");
        });
        b.Entity<Vendor>(e =>
        {
            e.ToTable("vendors");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(150).IsRequired();
            e.Property(x => x.Services).HasMaxLength(300);
            e.Property(x => x.Email).HasMaxLength(200);
            e.Property(x => x.Phone).HasMaxLength(40);
            e.Property(x => x.Website).HasMaxLength(200);
            e.Property(x => x.Address).HasMaxLength(300);
            e.Property(x => x.TaxId).HasMaxLength(40);
            e.Property(x => x.Notes).HasMaxLength(2000);
            e.Property(x => x.BlockReason).HasMaxLength(300);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
        });
        b.Entity<VendorContact>(e =>
        {
            e.ToTable("vendor_contacts");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.Role).HasMaxLength(100);
            e.Property(x => x.Email).HasMaxLength(200);
            e.Property(x => x.Phone).HasMaxLength(40);
            e.HasIndex(x => x.VendorId);
            e.HasOne<Vendor>().WithMany().HasForeignKey(x => x.VendorId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<VendorSite>(e =>
        {
            e.ToTable("vendor_sites");
            e.HasKey(x => new { x.VendorId, x.LocationId });
            e.HasIndex(x => new { x.TenantId, x.LocationId });
            e.HasOne<Vendor>().WithMany().HasForeignKey(x => x.VendorId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Location>().WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<VendorContract>(e =>
        {
            e.ToTable("vendor_contracts", t => t.HasCheckConstraint("ck_vendor_contracts_dates", "ends_on IS NULL OR ends_on >= starts_on"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(150).IsRequired();
            e.Property(x => x.Reference).HasMaxLength(60);
            e.Property(x => x.Value).HasPrecision(15, 2);
            e.Property(x => x.Notes).HasMaxLength(2000);
            e.HasIndex(x => x.VendorId);
            e.HasIndex(x => new { x.TenantId, x.EndsOn }).HasFilter("ends_on IS NOT NULL");
            e.HasOne<Vendor>().WithMany().HasForeignKey(x => x.VendorId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<VendorDocument>(e =>
        {
            e.ToTable("vendor_documents", t => t.HasCheckConstraint("ck_vendor_documents_size", "size > 0"));
            e.HasKey(x => x.Id);
            e.Property(x => x.FileName).HasMaxLength(150).IsRequired();
            e.Property(x => x.ContentType).HasMaxLength(120).IsRequired();
            e.Property(x => x.Sha256).HasMaxLength(64).IsRequired();
            e.Property(x => x.StorageKey).HasMaxLength(80).IsRequired();
            e.HasIndex(x => x.VendorId);
            e.HasOne<Vendor>().WithMany().HasForeignKey(x => x.VendorId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<VendorContract>().WithMany().HasForeignKey(x => x.ContractId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UploadedById).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<MaintenancePlan>(e =>
        {
            e.ToTable("maintenance_plans", t =>
            {
                t.HasCheckConstraint("ck_maintenance_plans_every", "every >= 1");
                t.HasCheckConstraint("ck_maintenance_plans_lead", "lead_days >= 0 AND lead_days <= 90");
                t.HasCheckConstraint("ck_maintenance_plans_dates", "ends_on IS NULL OR ends_on >= starts_on");
                t.HasCheckConstraint("ck_maintenance_plans_target", "asset_id IS NOT NULL OR asset_type_id IS NOT NULL OR location_id IS NOT NULL");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
            e.Property(x => x.Instructions).HasMaxLength(4000);
            e.Property(x => x.Unit).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
            e.HasIndex(x => x.NextDueOn).HasFilter("paused_at IS NULL AND next_due_on IS NOT NULL");
            e.HasOne<ServiceItem>().WithMany().HasForeignKey(x => x.ServiceId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Asset>().WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AssetType>().WithMany().HasForeignKey(x => x.AssetTypeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Location>().WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ChecklistTemplate>().WithMany().HasForeignKey(x => x.ChecklistTemplateId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<Team>().WithMany().HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.AssigneeId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<Vendor>().WithMany().HasForeignKey(x => x.VendorId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<MaintenanceVisit>(e =>
        {
            e.ToTable("maintenance_visits");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.PlanId, x.TargetKey, x.DueOn }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.DueOn });
            e.HasIndex(x => x.WorkOrderId).HasFilter("work_order_id IS NOT NULL");
            e.HasOne<MaintenancePlan>().WithMany().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Asset>().WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<Location>().WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<Ticket>().WithMany().HasForeignKey(x => x.TicketId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<WorkOrder>().WithMany().HasForeignKey(x => x.WorkOrderId).OnDelete(DeleteBehavior.SetNull);
        });
        b.Entity<TicketRating>(e =>
        {
            e.ToTable("ticket_ratings", t => t.HasCheckConstraint("ck_ticket_ratings_score", "score BETWEEN 1 AND 5"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Comment).HasMaxLength(1000);
            e.HasIndex(x => x.TicketId).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.UpdatedAt });
            e.HasIndex(x => new { x.TenantId, x.AssigneeId, x.UpdatedAt });
            e.HasOne<Ticket>().WithMany().HasForeignKey(x => x.TicketId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.RaterId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.AssigneeId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<Team>().WithMany().HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<Location>().WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.SetNull);
        });
        b.Entity<KbCategory>(e =>
        {
            e.ToTable("kb_categories");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(60).IsRequired();
            e.HasIndex(x => new { x.TenantId, x.Name }).IsUnique().HasFilter("archived_at IS NULL");
        });
        b.Entity<KbArticle>(e =>
        {
            e.ToTable("kb_articles");
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(150).IsRequired();
            e.Property(x => x.Summary).HasMaxLength(300);
            e.Property(x => x.Body).IsRequired();
            e.Property(x => x.Visibility).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => new { x.TenantId, x.Status, x.UpdatedAt });
            e.HasIndex(x => x.CategoryId);
            e.HasOne<KbCategory>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedById).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<KbArticleVersion>(e =>
        {
            e.ToTable("kb_article_versions");
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(150).IsRequired();
            e.Property(x => x.Summary).HasMaxLength(300);
            e.Property(x => x.Body).IsRequired();
            e.Property(x => x.Note).HasMaxLength(200);
            e.HasIndex(x => new { x.ArticleId, x.Number }).IsUnique();
            e.HasOne<KbArticle>().WithMany().HasForeignKey(x => x.ArticleId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedById).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<KbArticleService>(e =>
        {
            e.ToTable("kb_article_services");
            e.HasKey(x => new { x.ArticleId, x.ServiceId });
            e.HasIndex(x => new { x.TenantId, x.ServiceId });
            e.HasOne<KbArticle>().WithMany().HasForeignKey(x => x.ArticleId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<ServiceItem>().WithMany().HasForeignKey(x => x.ServiceId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<KbFeedback>(e =>
        {
            e.ToTable("kb_feedback");
            e.HasKey(x => new { x.ArticleId, x.UserId });
            e.HasOne<KbArticle>().WithMany().HasForeignKey(x => x.ArticleId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<KbFile>(e =>
        {
            e.ToTable("kb_files", t => t.HasCheckConstraint("ck_kb_files_size", "size > 0"));
            e.HasKey(x => x.Id);
            e.Property(x => x.FileName).HasMaxLength(150).IsRequired();
            e.Property(x => x.ContentType).HasMaxLength(120).IsRequired();
            e.Property(x => x.Sha256).HasMaxLength(64).IsRequired();
            e.Property(x => x.StorageKey).HasMaxLength(80).IsRequired();
            e.HasIndex(x => x.ArticleId);
            e.HasOne<KbArticle>().WithMany().HasForeignKey(x => x.ArticleId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UploadedById).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<CostSettings>(e =>
        {
            e.ToTable("cost_settings", t => t.HasCheckConstraint("ck_cost_settings_rate", "labour_rate_per_hour IS NULL OR labour_rate_per_hour >= 0"));
            e.HasKey(x => x.TenantId);
            e.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            e.Property(x => x.LabourRatePerHour).HasPrecision(15, 2);
        });
        b.Entity<CostCenter>(e =>
        {
            e.ToTable("cost_centers");
            e.HasKey(x => x.Id);
            e.Property(x => x.Code).HasMaxLength(20).IsRequired();
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        });
        b.Entity<CostCenterPlace>(e =>
        {
            e.ToTable("cost_center_places");
            e.HasKey(x => x.LocationId);
            e.HasIndex(x => x.CostCenterId);
            e.HasOne<CostCenter>().WithMany().HasForeignKey(x => x.CostCenterId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Location>().WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<Budget>(e =>
        {
            e.ToTable("budgets", t => { t.HasCheckConstraint("ck_budgets_amount", "amount > 0"); t.HasCheckConstraint("ck_budgets_year", "year BETWEEN 2000 AND 2100"); });
            e.HasKey(x => x.Id);
            e.Property(x => x.Scope).HasConversion<string>().HasMaxLength(12);
            e.Property(x => x.Amount).HasPrecision(15, 2);
            e.Property(x => x.Note).HasMaxLength(300);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => new { x.TenantId, x.Scope, x.ScopeId, x.Year }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.Year });
        });
        b.Entity<InboundMailbox>(e =>
        {
            e.ToTable("inbound_mailboxes");
            e.HasKey(x => x.Id);
            e.Property(x => x.Address).HasMaxLength(200).IsRequired();
            e.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
            e.Property(x => x.TokenHint).HasMaxLength(8).IsRequired();
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => x.TenantId).IsUnique();                       // one mailbox per organisation
            e.HasIndex(x => x.TokenHash).IsUnique().HasFilter("token_hash <> ''");
            e.HasOne<ServiceItem>().WithMany().HasForeignKey(x => x.DefaultServiceId).OnDelete(DeleteBehavior.SetNull);
        });
        b.Entity<InboundMessage>(e =>
        {
            e.ToTable("inbound_messages");
            e.HasKey(x => x.Id);
            e.Property(x => x.MessageId).HasMaxLength(300).IsRequired();
            e.Property(x => x.FromAddress).HasMaxLength(200).IsRequired();
            e.Property(x => x.Subject).HasMaxLength(200).IsRequired();
            e.Property(x => x.Outcome).HasMaxLength(10).IsRequired();
            e.Property(x => x.Reason).HasMaxLength(300);
            e.HasIndex(x => new { x.MailboxId, x.MessageId }).IsUnique();
            e.HasIndex(x => new { x.MailboxId, x.ReceivedAt });
            e.HasIndex(x => new { x.MailboxId, x.UserId, x.ReceivedAt });
            e.HasOne<InboundMailbox>().WithMany().HasForeignKey(x => x.MailboxId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Ticket>().WithMany().HasForeignKey(x => x.TicketId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.SetNull);
        });
        b.Entity<ApiKey>(e =>
        {
            e.ToTable("api_keys");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(80).IsRequired();
            e.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
            e.Property(x => x.Prefix).HasMaxLength(12).IsRequired();
            e.Property(x => x.Scopes).HasMaxLength(200).IsRequired();
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<ApiIdempotency>(e =>
        {
            e.ToTable("api_idempotency");
            e.HasKey(x => new { x.ApiKeyId, x.Key });
            e.Property(x => x.Key).HasMaxLength(100);
            e.HasOne<ApiKey>().WithMany().HasForeignKey(x => x.ApiKeyId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Ticket>().WithMany().HasForeignKey(x => x.TicketId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<WebhookEndpoint>(e =>
        {
            e.ToTable("webhook_endpoints");
            e.HasKey(x => x.Id);
            e.Property(x => x.Url).HasMaxLength(500).IsRequired();
            e.Property(x => x.Description).HasMaxLength(200);
            e.Property(x => x.Events).HasMaxLength(300).IsRequired();
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedById).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<WebhookDelivery>(e =>
        {
            e.ToTable("webhook_deliveries");
            e.HasKey(x => x.Id);
            e.Property(x => x.Event).HasMaxLength(40).IsRequired();
            e.Property(x => x.PayloadJson).HasColumnType("jsonb").IsRequired();
            e.Property(x => x.LastError).HasMaxLength(300);
            e.HasIndex(x => x.NextAttemptAt).HasFilter("delivered_at IS NULL AND failed_at IS NULL");
            e.HasIndex(x => new { x.EndpointId, x.CreatedAt });
            e.HasOne<WebhookEndpoint>().WithMany().HasForeignKey(x => x.EndpointId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<VendorUser>(e =>
        {
            e.ToTable("vendor_users");
            e.HasKey(x => x.UserId);                                       // one vendor per person
            e.HasIndex(x => x.VendorId);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Vendor>().WithMany().HasForeignKey(x => x.VendorId).OnDelete(DeleteBehavior.Cascade);
        });
        // R3-1 bookings (BKG-001/002). The no-overlap rule is an EXCLUDE constraint added in the migration (EF cannot model it).
        b.Entity<BookableResource>(e =>
        {
            e.ToTable("bookable_resources", t => { t.HasCheckConstraint("ck_bookable_resources_hours", "(open_from IS NULL) = (open_until IS NULL) AND (open_from IS NULL OR open_until > open_from)"); t.HasCheckConstraint("ck_bookable_resources_days", "open_days BETWEEN 1 AND 127"); t.HasCheckConstraint("ck_bookable_resources_buffer", "buffer_minutes BETWEEN 0 AND 240"); });
            e.HasKey(x => x.Id);
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Name).HasMaxLength(120);
            e.Property(x => x.Code).HasMaxLength(30);
            e.Property(x => x.Description).HasMaxLength(1000);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique().HasFilter("code IS NOT NULL");
            e.HasIndex(x => new { x.TenantId, x.Kind, x.Name });
            e.HasOne<Location>().WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<Booking>(e =>
        {
            e.ToTable("bookings", t => t.HasCheckConstraint("ck_bookings_range", "ends_at > starts_at AND blocked_until >= ends_at"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Title).HasMaxLength(150);
            e.Property(x => x.Note).HasMaxLength(1000);
            e.Property(x => x.CancelReason).HasMaxLength(500);
            e.Property(x => x.Repeat).HasMaxLength(20);
            e.Property(x => x.DecisionNote).HasMaxLength(500);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => x.SeriesId).HasFilter("series_id IS NOT NULL");
            e.HasIndex(x => new { x.Status, x.StartsAt });                         // the background run looks for pending and confirmed bookings near now
            e.HasIndex(x => new { x.TenantId, x.ResourceId, x.StartsAt });
            e.HasIndex(x => new { x.TenantId, x.BookedForId, x.StartsAt });
            e.HasIndex(x => new { x.TenantId, x.CreatedById, x.StartsAt });
            e.HasOne<BookableResource>().WithMany().HasForeignKey(x => x.ResourceId).OnDelete(DeleteBehavior.Restrict);
        });
        // R3-3 vehicles (BKG-004)
        b.Entity<VehicleTrip>(e =>
        {
            e.ToTable("vehicle_trips", t =>
            {
                t.HasCheckConstraint("ck_vehicle_trips_odometer", "odometer_in IS NULL OR (odometer_out IS NOT NULL AND odometer_in >= odometer_out)");
                t.HasCheckConstraint("ck_vehicle_trips_costs", "coalesce(fuel_cost, 0) >= 0 AND coalesce(toll_cost, 0) >= 0 AND coalesce(parking_cost, 0) >= 0");
            });
            e.HasKey(x => x.BookingId);                                     // one trip per booking
            e.Property(x => x.Destination).HasMaxLength(200);
            e.Property(x => x.Purpose).HasMaxLength(300);
            e.Property(x => x.DamageNotes).HasMaxLength(2000);
            e.Property(x => x.ChecklistJson).HasColumnType("jsonb");
            e.Property(x => x.FuelCost).HasPrecision(14, 2); e.Property(x => x.TollCost).HasPrecision(14, 2); e.Property(x => x.ParkingCost).HasPrecision(14, 2);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => new { x.TenantId, x.ResourceId, x.ReturnedAt });
            e.HasIndex(x => new { x.TenantId, x.DriverId });
            e.HasOne<Booking>().WithOne().HasForeignKey<VehicleTrip>(x => x.BookingId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<BookableResource>().WithMany().HasForeignKey(x => x.ResourceId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<ResourceDocument>(e =>
        {
            e.ToTable("resource_documents");
            e.HasKey(x => x.Id);
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Number).HasMaxLength(60);
            e.Property(x => x.Note).HasMaxLength(500);
            e.Property(x => x.Reminded).HasMaxLength(10);
            e.HasIndex(x => new { x.TenantId, x.ResourceId });
            e.HasIndex(x => x.ExpiresOn);
            e.HasOne<BookableResource>().WithMany().HasForeignKey(x => x.ResourceId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<DriverLicence>(e =>
        {
            e.ToTable("driver_licences");
            e.HasKey(x => x.UserId);                                        // one licence per person
            e.Property(x => x.Number).HasMaxLength(40);
            e.Property(x => x.Class).HasMaxLength(10);
            e.Property(x => x.Reminded).HasMaxLength(10);
            e.HasIndex(x => x.ExpiresOn);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        // R3-4 visitors (VIS-001/002/005)
        b.Entity<Visit>(e =>
        {
            e.ToTable("visits", t => t.HasCheckConstraint("ck_visits_window", "expected_until > expected_from"));
            e.HasKey(x => x.Id);
            e.Property(x => x.VisitorName).HasMaxLength(150);
            e.Property(x => x.Company).HasMaxLength(150);
            e.Property(x => x.Email).HasMaxLength(200);
            e.Property(x => x.Phone).HasMaxLength(40);
            e.Property(x => x.VehiclePlate).HasMaxLength(20);
            e.Property(x => x.Purpose).HasMaxLength(300);
            e.Property(x => x.Flag).HasMaxLength(400);
            e.Property(x => x.BadgeNumber).HasMaxLength(20);
            e.Property(x => x.IdLast4).HasMaxLength(4);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => new { x.TenantId, x.ExpectedFrom });
            e.HasIndex(x => new { x.TenantId, x.HostId, x.ExpectedFrom });
            e.HasIndex(x => new { x.Status, x.ExpectedUntil });                  // no-shows and retention
            e.HasIndex(x => new { x.TenantId, x.BadgeNumber }).IsUnique().HasFilter("status = 'CheckedIn'");   // one badge per visitor on site
            e.HasOne<Location>().WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
        });
        // R3-6 inventory (INV-001/003/004): the balance is a cache of the ledger and may never go below zero
        b.Entity<InventoryItem>(e =>
        {
            e.ToTable("inventory_items", t => t.HasCheckConstraint("ck_inventory_items_levels", "reorder_level >= 0 AND (reorder_quantity IS NULL OR reorder_quantity > 0) AND (unit_cost IS NULL OR unit_cost >= 0)"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Sku).HasMaxLength(40);
            e.Property(x => x.Name).HasMaxLength(150);
            e.Property(x => x.Category).HasMaxLength(20);
            e.Property(x => x.Unit).HasMaxLength(20);
            e.Property(x => x.Description).HasMaxLength(1000);
            e.Property(x => x.UnitCost).HasPrecision(14, 2);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => new { x.TenantId, x.Sku }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.Category, x.Name });
        });
        b.Entity<StockBalance>(e =>
        {
            e.ToTable("stock_balances", t => t.HasCheckConstraint("ck_stock_balances_not_negative", "quantity >= 0"));
            e.HasKey(x => new { x.ItemId, x.LocationId });
            e.HasIndex(x => new { x.TenantId, x.LocationId });
            e.HasOne<InventoryItem>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Location>().WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<StockMovement>(e =>
        {
            e.ToTable("stock_movements", t => t.HasCheckConstraint("ck_stock_movements_quantity", "quantity <> 0 AND balance_after >= 0"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.UnitCost).HasPrecision(14, 2);
            e.Property(x => x.Reference).HasMaxLength(300);
            e.HasIndex(x => new { x.TenantId, x.ItemId, x.At });
            e.HasIndex(x => new { x.TenantId, x.LocationId, x.At });
            e.HasIndex(x => new { x.TenantId, x.Kind, x.At });
            e.HasIndex(x => x.GroupId).HasFilter("group_id IS NOT NULL");
            e.HasOne<InventoryItem>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Location>().WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<StockTake>(e =>
        {
            e.ToTable("stock_takes");
            e.HasKey(x => x.Id);
            e.Property(x => x.Note).HasMaxLength(300);
            e.HasIndex(x => new { x.TenantId, x.LocationId }).IsUnique().HasFilter("closed_at IS NULL");   // one open count per place
            e.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.StockTakeId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<StockTakeLine>(e =>
        {
            e.ToTable("stock_take_lines", t => t.HasCheckConstraint("ck_stock_take_lines_counted", "counted IS NULL OR counted >= 0"));
            e.HasKey(x => new { x.StockTakeId, x.ItemId });
            e.HasOne<InventoryItem>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
        });
        // R3-11 field mode (ADR-GA-07): idempotency keys of replayed changes, per person
        b.Entity<OfflineOp>(e =>
        {
            e.ToTable("offline_ops");
            e.HasKey(x => new { x.UserId, x.Key });
            e.Property(x => x.Key).HasMaxLength(100);
            e.HasIndex(x => new { x.UserId, x.CreatedAt });
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        // R3-10 notification channels (NTF-003)
        b.Entity<ChannelSetting>(e =>
        {
            e.ToTable("channel_settings");
            e.HasKey(x => x.Id);
            e.Property(x => x.Kind).HasMaxLength(20);
            e.Property(x => x.SettingsJson).HasMaxLength(4000);
            e.HasIndex(x => new { x.TenantId, x.Kind }).IsUnique();
        });
        b.Entity<UserChannel>(e =>
        {
            e.ToTable("user_channels");
            e.HasKey(x => x.Id);
            e.Property(x => x.Kind).HasMaxLength(20);
            e.Property(x => x.Hint).HasMaxLength(320);
            e.Property(x => x.CodeHash).HasMaxLength(200);
            e.Property(x => x.LastError).HasMaxLength(300);
            e.HasIndex(x => new { x.UserId, x.Kind }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.Kind });
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<ChannelDelivery>(e =>
        {
            e.ToTable("channel_deliveries");
            e.HasKey(x => x.Id);
            e.Property(x => x.Channel).HasMaxLength(20);
            e.Property(x => x.Purpose).HasMaxLength(10);
            e.Property(x => x.NotificationKind).HasMaxLength(60);
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.Body).HasMaxLength(1000);
            e.Property(x => x.Link).HasMaxLength(500);
            e.Property(x => x.LastError).HasMaxLength(300);
            e.HasIndex(x => x.NextAttemptAt).HasFilter("delivered_at IS NULL AND failed_at IS NULL");   // the dispatcher's due query, across organisations
            e.HasIndex(x => x.GroupId);
            e.HasIndex(x => new { x.TenantId, x.CreatedAt });
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        // R3-9 directory (INT-003, ADM-004 sync): group → role mappings and SCIM groups
        b.Entity<DirectoryGroupMapping>(e =>
        {
            e.ToTable("directory_group_mappings");
            e.HasKey(x => x.Id);
            e.Property(x => x.Group).HasMaxLength(200);
            e.HasIndex(x => new { x.TenantId, x.Group, x.RoleId }).IsUnique();
            e.HasOne<Role>().WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<ScimGroup>(e =>
        {
            e.ToTable("scim_groups");
            e.HasKey(x => x.Id);
            e.Property(x => x.DisplayName).HasMaxLength(200);
            e.Property(x => x.ExternalId).HasMaxLength(200);
            e.HasIndex(x => new { x.TenantId, x.DisplayName }).IsUnique();
            e.HasIndex(x => x.MemberIds).HasMethod("gin");                   // "which groups is this person in"
        });
        // R3-8 scheduled reports (RPT-007)
        b.Entity<ReportSchedule>(e =>
        {
            e.ToTable("report_schedules", t =>
            {
                t.HasCheckConstraint("ck_report_schedules_when", "hour BETWEEN 0 AND 23 AND weekday BETWEEN 1 AND 7 AND month_day BETWEEN 1 AND 28 AND days BETWEEN 1 AND 365");
                t.HasCheckConstraint("ck_report_schedules_kinds", "format IN ('csv','xlsx') AND frequency IN ('daily','weekly','monthly')");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(120);
            e.Property(x => x.Report).HasMaxLength(30);
            e.Property(x => x.Format).HasMaxLength(10);
            e.Property(x => x.Frequency).HasMaxLength(10);
            e.Property(x => x.LastResult).HasMaxLength(300);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => new { x.TenantId, x.OwnerId });
            e.HasIndex(x => x.NextRunAt).HasFilter("active");                  // the worker's due query, across organisations
            e.HasOne<User>().WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Cascade);
        });
        // R3-7 supply requests (INV-002): lines are issued through the stock ledger (movements share the request id as group id)
        b.Entity<SupplyRequest>(e =>
        {
            e.ToTable("supply_requests", t => t.HasCheckConstraint("ck_supply_requests_cost", "estimated_cost >= 0"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Number).HasMaxLength(20);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Note).HasMaxLength(500);
            e.Property(x => x.DecisionNote).HasMaxLength(500);
            e.Property(x => x.EstimatedCost).HasPrecision(14, 2);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => new { x.TenantId, x.Number }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.RequesterId, x.CreatedAt });
            e.HasIndex(x => new { x.TenantId, x.Status, x.CreatedAt });
            e.HasIndex(x => new { x.TenantId, x.ApproverId }).HasFilter("status = 'Pending'");
            e.HasOne<User>().WithMany().HasForeignKey(x => x.RequesterId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.ApproverId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<Location>().WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.RequestId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<SupplyRequestLine>(e =>
        {
            e.ToTable("supply_request_lines", t => t.HasCheckConstraint("ck_supply_request_lines_qty", "quantity > 0 AND issued >= 0 AND issued <= quantity"));
            e.HasKey(x => new { x.RequestId, x.ItemId });
            e.Property(x => x.UnitCost).HasPrecision(14, 2);
            e.HasIndex(x => new { x.TenantId, x.ItemId });
            e.HasOne<InventoryItem>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<SupplySettings>(e =>
        {
            e.ToTable("supply_settings");
            e.HasKey(x => x.TenantId);                                      // one row per organisation
            e.Property(x => x.ApprovalAbove).HasPrecision(14, 2);
            e.HasOne<Location>().WithMany().HasForeignKey(x => x.DefaultLocationId).OnDelete(DeleteBehavior.SetNull);
        });
        // R3-5 mail and courier (VIS-004)
        b.Entity<MailItem>(e =>
        {
            e.ToTable("mail_items");
            e.HasKey(x => x.Id);
            e.Property(x => x.Direction).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(12);
            e.Property(x => x.OtherParty).HasMaxLength(200);
            e.Property(x => x.Address).HasMaxLength(500);
            e.Property(x => x.Carrier).HasMaxLength(60);
            e.Property(x => x.Waybill).HasMaxLength(60);
            e.Property(x => x.Description).HasMaxLength(300);
            e.Property(x => x.CollectedBy).HasMaxLength(150);
            e.Property(x => x.Note).HasMaxLength(300);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => new { x.TenantId, x.PersonId, x.LoggedAt });
            e.HasIndex(x => new { x.TenantId, x.Status, x.LoggedAt });
            e.HasIndex(x => new { x.TenantId, x.Waybill });
            e.HasOne<Location>().WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<VisitorBlock>(e =>
        {
            e.ToTable("visitor_blocks");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(150);
            e.Property(x => x.Company).HasMaxLength(150);
            e.Property(x => x.Reason).HasMaxLength(300);
            e.HasIndex(x => x.TenantId);
        });
        b.Entity<VisitorSettings>(e =>
        {
            e.ToTable("visitor_settings");
            e.HasKey(x => x.TenantId);                                      // one row per organisation
            e.Property(x => x.Acknowledgement).HasMaxLength(4000);
        });
        b.Entity<BookingFeed>(e =>
        {
            e.ToTable("booking_feeds");
            e.HasKey(x => x.UserId);                                        // one feed per person
            e.Property(x => x.TokenHash).HasMaxLength(64);
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<EmailWording>(e =>
        {
            e.ToTable("email_wordings");
            e.HasKey(x => x.Id);
            e.Property(x => x.SenderName).HasMaxLength(60);
            e.Property(x => x.SubjectPrefix).HasMaxLength(30);
            e.Property(x => x.FooterEn).HasMaxLength(400);
            e.Property(x => x.FooterId).HasMaxLength(400);
            e.HasIndex(x => x.TenantId).IsUnique(); // one wording per organisation
        });
        foreach (var t in new[] { typeof(ApprovalPolicy), typeof(TicketApproval), typeof(Notification), typeof(Attachment), typeof(TicketView), typeof(ApprovalDelegation), typeof(NotificationPreference), typeof(RetentionPolicy), typeof(TicketLegalHold), typeof(EmailWording), typeof(AssetType), typeof(Asset), typeof(AssetEvent), typeof(AssetCounter), typeof(WorkOrder), typeof(WorkOrderTask), typeof(WorkOrderTime), typeof(WorkOrderCost), typeof(ChecklistTemplate), typeof(Vendor), typeof(VendorContact), typeof(VendorSite), typeof(VendorContract), typeof(VendorDocument), typeof(MaintenancePlan), typeof(MaintenanceVisit), typeof(TicketRating), typeof(KbCategory), typeof(KbArticle), typeof(KbArticleVersion), typeof(KbArticleService), typeof(KbFeedback), typeof(KbFile), typeof(CostSettings), typeof(CostCenter), typeof(CostCenterPlace), typeof(Budget), typeof(InboundMailbox), typeof(InboundMessage), typeof(ApiKey), typeof(ApiIdempotency), typeof(WebhookEndpoint), typeof(WebhookDelivery), typeof(VendorUser), typeof(BookableResource), typeof(Booking), typeof(BookingFeed), typeof(VehicleTrip), typeof(ResourceDocument), typeof(DriverLicence), typeof(Visit), typeof(VisitorBlock), typeof(VisitorSettings), typeof(MailItem), typeof(InventoryItem), typeof(StockBalance), typeof(StockMovement), typeof(StockTake), typeof(StockTakeLine), typeof(SupplyRequest), typeof(SupplyRequestLine), typeof(SupplySettings), typeof(ReportSchedule), typeof(DirectoryGroupMapping), typeof(ScimGroup), typeof(ChannelSetting), typeof(UserChannel), typeof(ChannelDelivery), typeof(OfflineOp) })
            b.Entity(t).HasOne(typeof(Tenant)).WithMany().HasForeignKey("TenantId").OnDelete(DeleteBehavior.Restrict);
        Filter<ApprovalPolicy>(b); Filter<TicketApproval>(b); Filter<Notification>(b); Filter<Attachment>(b); Filter<TicketView>(b); Filter<ApprovalDelegation>(b); Filter<NotificationPreference>(b); Filter<RetentionPolicy>(b); Filter<TicketLegalHold>(b); Filter<EmailWording>(b); Filter<AssetType>(b); Filter<Asset>(b); Filter<AssetEvent>(b); Filter<AssetCounter>(b); Filter<WorkOrder>(b); Filter<WorkOrderTask>(b); Filter<WorkOrderTime>(b); Filter<WorkOrderCost>(b); Filter<ChecklistTemplate>(b); Filter<Vendor>(b); Filter<VendorContact>(b); Filter<VendorSite>(b); Filter<VendorContract>(b); Filter<VendorDocument>(b); Filter<MaintenancePlan>(b); Filter<MaintenanceVisit>(b); Filter<TicketRating>(b); Filter<KbCategory>(b); Filter<KbArticle>(b); Filter<KbArticleVersion>(b); Filter<KbArticleService>(b); Filter<KbFeedback>(b); Filter<KbFile>(b); Filter<CostSettings>(b); Filter<CostCenter>(b); Filter<CostCenterPlace>(b); Filter<Budget>(b); Filter<InboundMailbox>(b); Filter<InboundMessage>(b); Filter<ApiKey>(b); Filter<ApiIdempotency>(b); Filter<WebhookEndpoint>(b); Filter<WebhookDelivery>(b); Filter<VendorUser>(b); Filter<BookableResource>(b); Filter<Booking>(b); Filter<BookingFeed>(b); Filter<VehicleTrip>(b); Filter<ResourceDocument>(b); Filter<DriverLicence>(b); Filter<Visit>(b); Filter<VisitorBlock>(b); Filter<VisitorSettings>(b); Filter<MailItem>(b); Filter<InventoryItem>(b); Filter<StockBalance>(b); Filter<StockMovement>(b); Filter<StockTake>(b); Filter<StockTakeLine>(b); Filter<SupplyRequest>(b); Filter<SupplyRequestLine>(b); Filter<SupplySettings>(b); Filter<ReportSchedule>(b); Filter<DirectoryGroupMapping>(b); Filter<ScimGroup>(b); Filter<ChannelSetting>(b); Filter<UserChannel>(b); Filter<ChannelDelivery>(b); Filter<OfflineOp>(b);
        foreach (var t in new[] { typeof(SlaPolicy), typeof(AssignmentRule) })
            b.Entity(t).HasOne(typeof(Tenant)).WithMany().HasForeignKey("TenantId").OnDelete(DeleteBehavior.Restrict);
        Filter<SlaPolicy>(b); Filter<AssignmentRule>(b);
        foreach (var t in new[] { typeof(Ticket), typeof(TicketEvent), typeof(TicketCounter) })
            b.Entity(t).HasOne(typeof(Tenant)).WithMany().HasForeignKey("TenantId").OnDelete(DeleteBehavior.Restrict);
        Filter<Ticket>(b); Filter<TicketEvent>(b); Filter<TicketCounter>(b);
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.ToTable("users", t =>
            {
                t.HasCheckConstraint("ck_users_email_lower", "email = lower(email)");
                t.HasCheckConstraint("ck_users_name_len", "length(trim(name)) >= 2");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Email).HasMaxLength(254).IsRequired();
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => x.Email).IsUnique();
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.CreatedAt);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasOne(x => x.Credential).WithOne().HasForeignKey<UserCredential>(c => c.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Profile).WithOne().HasForeignKey<UserProfile>(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.UserRoles).WithOne(r => r.User).HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.MfaFactors).WithOne().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<UserCredential>(e => { e.ToTable("user_credentials"); e.HasKey(x => x.UserId); e.Property(x => x.PasswordHash).IsRequired(); });
        b.Entity<UserProfile>(e =>
        {
            e.ToTable("user_profiles", t => t.HasCheckConstraint("ck_profile_language", "language in ('en','id')"));
            e.HasKey(x => x.UserId);
            e.HasIndex(x => x.ManagerId);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.ManagerId).OnDelete(DeleteBehavior.SetNull);
            e.Property(x => x.EmployeeId).HasMaxLength(50);
            e.Property(x => x.ExternalId).HasMaxLength(200);
            e.HasIndex(x => x.ExternalId).HasFilter("external_id IS NOT NULL");
            e.HasOne<Location>().WithMany().HasForeignKey(x => x.SiteId).OnDelete(DeleteBehavior.SetNull);
        });
        b.Entity<Avatar>(e =>
        {
            e.ToTable("avatars", t =>
            {
                t.HasCheckConstraint("ck_avatar_type", "content_type in ('image/png','image/jpeg','image/webp')");
                t.HasCheckConstraint("ck_avatar_size", "octet_length(bytes) <= 262144");
            });
            e.HasKey(x => x.Id); e.HasIndex(x => x.UserId).IsUnique();
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<ExternalIdentity>(e =>
        {
            e.ToTable("external_identities"); e.HasKey(x => x.Id); e.HasIndex(x => new { x.Provider, x.Subject }).IsUnique();
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<EmailVerificationToken>(e =>
        {
            e.ToTable("email_verification_tokens"); e.HasKey(x => x.Id); e.HasIndex(x => x.TokenHash).IsUnique(); e.HasIndex(x => new { x.UserId, x.CreatedAt });
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<PasswordResetToken>(e =>
        {
            e.ToTable("password_reset_tokens"); e.HasKey(x => x.Id); e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<MfaFactor>(e =>
        {
            e.ToTable("mfa_factors"); e.HasKey(x => x.Id); e.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => new { x.UserId, x.Type }).IsUnique();
        });
        b.Entity<MfaRecoveryCode>(e =>
        {
            e.ToTable("mfa_recovery_codes"); e.HasKey(x => x.Id); e.HasIndex(x => x.UserId);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<Permission>(e =>
        {
            e.ToTable("permissions", t => t.HasCheckConstraint("ck_permission_key", "key ~ '^[a-z_]+\\.[a-z_]+$'"));
            e.HasKey(x => x.Key);
        });
        b.Entity<Role>(e =>
        {
            e.ToTable("roles"); e.HasKey(x => x.Id); e.HasIndex(x => new { x.TenantId, x.Key }).IsUnique(); e.Property(x => x.Version).HasDefaultValue(1);
            e.HasIndex("TenantId", "Name").IsUnique().HasFilter("archived_at IS NULL");
            e.HasMany(x => x.Permissions).WithOne().HasForeignKey(p => p.RoleId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<RolePermission>(e =>
        {
            e.ToTable("role_permissions"); e.HasKey(x => new { x.RoleId, x.PermissionKey });
            e.HasOne<Permission>().WithMany().HasForeignKey(x => x.PermissionKey).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<UserRole>(e =>
        {
            e.ToTable("user_roles"); e.HasKey(x => new { x.UserId, x.RoleId }); e.HasIndex(x => x.RoleId);
            e.HasOne(x => x.Role).WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<Session>(e =>
        {
            e.ToTable("sessions"); e.HasKey(x => x.Id); e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => x.UserId).HasFilter("revoked_at IS NULL"); e.HasIndex(x => x.AbsoluteExpiresAt);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<Invitation>(e =>
        {
            e.ToTable("invitations", t => t.HasCheckConstraint("ck_invitation_email_lower", "email = lower(email)"));
            e.HasKey(x => x.Id); e.HasIndex(x => x.TokenHash).IsUnique();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => x.Email).IsUnique().HasFilter("status = 'Pending'");
            e.HasMany(x => x.Roles).WithOne().HasForeignKey(r => r.InvitationId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<InvitationRole>(e => { e.ToTable("invitation_roles"); e.HasKey(x => new { x.InvitationId, x.RoleId }); });
        b.Entity<AuditEvent>(e =>
        {
            e.ToTable("audit_events"); e.HasKey(x => x.Id); e.Property(x => x.Id).UseIdentityAlwaysColumn();
            e.Property(x => x.Outcome).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.MetaJson).HasColumnType("jsonb"); e.Property(x => x.DiffJson).HasColumnType("jsonb");
            e.HasIndex(x => x.Ts).IsDescending(); e.HasIndex(x => new { x.TenantId, x.Ts }).IsDescending(false, true); /* each organisation's audit list, newest first */ e.HasIndex(x => new { x.ActorId, x.Ts }); e.HasIndex(x => new { x.Action, x.Ts }); e.HasIndex(x => new { x.TargetType, x.TargetId });
        });
        b.Entity<SecurityEvent>(e =>
        {
            e.ToTable("security_events"); e.HasKey(x => x.Id); e.Property(x => x.Severity).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.MetaJson).HasColumnType("jsonb");
            e.HasIndex(x => x.Ts).IsDescending(); e.HasIndex(x => new { x.TenantId, x.Ts }).IsDescending(false, true); e.HasIndex(x => x.AcknowledgedAt).HasFilter("acknowledged_at IS NULL"); e.HasIndex(x => new { x.UserId, x.Ts });
        });
        b.Entity<SystemSetting>(e => { e.ToTable("system_settings"); e.HasKey(x => x.Key); e.Property(x => x.ValueJson).HasColumnType("jsonb"); });
        b.Entity<RateLimitBucket>(e => { e.ToTable("rate_limit_buckets"); e.HasKey(x => x.Key); });
        b.Entity<OutboxEmail>(e =>
        {
            e.ToTable("outbox_emails"); e.HasKey(x => x.Id);
            e.Property(x => x.ToAddress).HasMaxLength(320);
            e.Property(x => x.Subject).HasMaxLength(500);
            e.Property(x => x.LastError).HasMaxLength(200);
            e.Property(x => x.AttachmentName).HasMaxLength(200);
            e.Property(x => x.AttachmentType).HasMaxLength(120);
            e.HasIndex(x => x.NextAttemptAt).HasFilter("sent_at IS NULL AND failed_at IS NULL"); // the dispatcher's claim query
            e.HasIndex(x => new { x.TenantId, x.CreatedAt }); // each organisation's Email Delivery page
            e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<AccessRequest>(e =>
        {
            e.ToTable("access_requests"); e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Reason).HasMaxLength(500).IsRequired();
            e.Property(x => x.DecisionNote).HasMaxLength(500);
            e.HasOne(x => x.Requester).WithMany().HasForeignKey(x => x.RequesterId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Role).WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.RequesterId, x.RoleId }).IsUnique().HasFilter("status = 'Pending'"); // one open request per person and role
            e.HasIndex(x => new { x.Status, x.CreatedAt });
        });
        b.Entity<AccessReview>(e =>
        {
            e.ToTable("access_reviews"); e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.HasIndex(x => x.TenantId).IsUnique().HasFilter("status = 'Open'"); // one open review at a time, per organisation
            e.HasMany(x => x.Items).WithOne(i => i.Review).HasForeignKey(i => i.ReviewId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<AccountClosureRequest>(e =>
        {
            e.ToTable("account_closure_requests"); e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Reason).HasMaxLength(500);
            e.Property(x => x.DecisionNote).HasMaxLength(500);
            e.HasIndex(x => x.UserId).IsUnique().HasFilter("status IN ('Pending', 'Approved')"); // one open request per person
            e.HasIndex(x => x.ExecuteAfter).HasFilter("status = 'Approved'"); // the closing worker's due query
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<TenantSetting>(e =>
        {
            e.ToTable("tenant_settings"); e.HasKey(x => new { x.TenantId, x.Key });
            e.Property(x => x.Key).HasMaxLength(100);
            e.Property(x => x.ValueJson).HasColumnType("jsonb");
            e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<AccessReviewSchedule>(e =>
        {
            e.ToTable("access_review_schedules"); e.HasKey(x => x.Id);
            e.HasIndex(x => x.TenantId).IsUnique(); // one schedule per organisation
            e.HasIndex(x => x.NextRunAt).HasFilter("enabled"); // the scheduler's due query
            e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<AccessReviewItem>(e =>
        {
            e.ToTable("access_review_items"); e.HasKey(x => x.Id);
            e.Property(x => x.Decision).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Note).HasMaxLength(500);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Role).WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.ReviewId, x.Decision });
            e.HasIndex(x => new { x.ReviewId, x.UserId, x.RoleId }).IsUnique();
        });
        b.Entity<UserIdentity>(e =>
        {
            e.ToTable("user_identities"); e.HasKey(x => x.Id);
            e.Property(x => x.Issuer).HasMaxLength(500).IsRequired();
            e.Property(x => x.Subject).HasMaxLength(255).IsRequired();
            e.Property(x => x.Email).HasMaxLength(320);
            e.HasIndex(x => new { x.Issuer, x.Subject }).IsUnique();
            e.HasIndex(x => new { x.UserId, x.Issuer }).IsUnique(); // one identity per person per provider
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<Tenant>(e =>
        {
            e.ToTable("tenants"); e.HasKey(x => x.Id);
            e.Property(x => x.Slug).HasMaxLength(40).IsRequired(); e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => x.Slug).IsUnique();
        });
        ConfigureHelpdeskSetup(b);
        ConfigureCatalog(b);
        ConfigureTickets(b);
        foreach (var t in new[] { typeof(User), typeof(Role), typeof(Invitation), typeof(Session), typeof(AuditEvent), typeof(SecurityEvent), typeof(AccessRequest), typeof(AccessReview), typeof(AccessReviewItem) })
            b.Entity(t).HasOne(typeof(Tenant)).WithMany().HasForeignKey("TenantId").OnDelete(DeleteBehavior.Restrict);
        Filter<User>(b); Filter<Role>(b); Filter<Invitation>(b); Filter<Session>(b); Filter<AuditEvent>(b); Filter<SecurityEvent>(b); Filter<AccessRequest>(b); Filter<AccessReview>(b); Filter<AccessReviewItem>(b); Filter<OutboxEmail>(b); Filter<AccessReviewSchedule>(b); Filter<TenantSetting>(b); Filter<AccountClosureRequest>(b);
        foreach (var t in new[] { typeof(User), typeof(Role), typeof(Invitation), typeof(Session), typeof(AuditEvent), typeof(SecurityEvent), typeof(AccessRequest), typeof(AccessReview), typeof(AccessReviewItem) })
            b.Entity(t).HasIndex("TenantId");
    }
}
