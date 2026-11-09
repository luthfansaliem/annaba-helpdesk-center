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
        foreach (var t in new[] { typeof(User), typeof(Role), typeof(Invitation), typeof(Session), typeof(AuditEvent), typeof(SecurityEvent), typeof(AccessRequest), typeof(AccessReview), typeof(AccessReviewItem) })
            b.Entity(t).HasOne(typeof(Tenant)).WithMany().HasForeignKey("TenantId").OnDelete(DeleteBehavior.Restrict);
        Filter<User>(b); Filter<Role>(b); Filter<Invitation>(b); Filter<Session>(b); Filter<AuditEvent>(b); Filter<SecurityEvent>(b); Filter<AccessRequest>(b); Filter<AccessReview>(b); Filter<AccessReviewItem>(b); Filter<OutboxEmail>(b); Filter<AccessReviewSchedule>(b); Filter<TenantSetting>(b); Filter<AccountClosureRequest>(b);
        foreach (var t in new[] { typeof(User), typeof(Role), typeof(Invitation), typeof(Session), typeof(AuditEvent), typeof(SecurityEvent), typeof(AccessRequest), typeof(AccessReview), typeof(AccessReviewItem) })
            b.Entity(t).HasIndex("TenantId");
    }
}
