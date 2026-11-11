using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using AnnabaDigital.Infrastructure.Email;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Application.Mfa;
using AnnabaDigital.Application.Monitoring;
using AnnabaDigital.Application.Profile;
using AnnabaDigital.Application.Settings;
using AnnabaDigital.Application.Roles;
using AnnabaDigital.Application.Users;
using AnnabaDigital.Infrastructure.Persistence;
using AnnabaDigital.Infrastructure.Security;

namespace AnnabaDigital.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddAnnabaDigitalInfrastructure(this IServiceCollection services, IConfiguration config, string? connectionStringOverride = null)
    {
        services.Configure<SecurityOptions>(config.GetSection("Security"));
        // Resolve the connection string lazily so configuration added after startup (tests, host overrides) is honoured.
        services.AddDbContextFactory<AnnabaDigitalDbContext>((sp, o) =>
        {
            var cs = connectionStringOverride ?? sp.GetRequiredService<IConfiguration>().GetConnectionString("Default")
                     ?? throw new InvalidOperationException("ConnectionStrings:Default is required.");
            // Every query carries the tenant filter as "(@bypass OR tenant_id = @tenant)". A prepared statement that PostgreSQL has switched to a generic plan cannot use the
            // tenant index for it, which made counts and lists scan far more than they need to. Planning each statement with its actual values costs a fraction of a millisecond
            // and lets the filter fold away.
            var csb = new Npgsql.NpgsqlConnectionStringBuilder(cs);
            if (!(csb.Options ?? "").Contains("plan_cache_mode")) csb.Options = ((csb.Options ?? "") + " -c plan_cache_mode=force_custom_plan").Trim();
            o.UseNpgsql(csb.ConnectionString).UseSnakeCaseNamingConvention().AddInterceptors(new CommandCounter());
        });
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddScoped(sp =>
        {
            var db = sp.GetRequiredService<IDbContextFactory<AnnabaDigitalDbContext>>().CreateDbContext();
            db.Tenant = sp.GetRequiredService<ITenantContext>();
            var cache = sp.GetRequiredService<AppCache>(); db.ReferenceDataChanged = cache.Clear; // settings or organisations changed: forget what was cached
            return db;
        });
        services.AddScoped<IAppDb>(sp => sp.GetRequiredService<AnnabaDigitalDbContext>());
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IPasswordHasher, Argon2PasswordHasher>();
        services.AddSingleton<ITokenService, TokenService>();
        services.AddSingleton<ISecretProtector, AesGcmSecretProtector>();
        services.AddSingleton<IRateLimiter, PostgresRateLimiter>();
        services.AddSingleton(sp => new AppCache(TimeSpan.FromSeconds(sp.GetRequiredService<IConfiguration>().GetValue<int?>("Cache:Seconds") ?? 30))); // 0 switches the short-lived caches off
        services.AddScoped<TenantDirectory>();
        services.AddScoped<SettingsProvider>();
        services.AddScoped<AuditWriter>();
        services.AddScoped<SessionService>();
        services.AddScoped<AuthorizationService>();
        services.AddScoped<MfaService>();
        services.AddScoped<AuthService>();
        services.AddScoped<PasswordPolicy>();
        services.AddScoped<AccountService>();
        services.AddScoped<InvitationAdminService>();
        services.AddScoped<UserAdminService>();
        services.AddScoped<UserImportService>();
        services.AddScoped<ApprovalService>();
        services.AddScoped<AccountClosureService>();
        services.AddScoped<AnnabaDigital.Application.Profile.AccountExportService>();
        services.AddScoped<AccessRequestService>();
        services.AddScoped<AccessReviewService>();
        services.AddScoped<TenantService>();
        services.AddScoped<RoleAdminService>();
        services.AddScoped<AnnabaDigital.Application.Helpdesk.LocationService>();
        services.AddScoped<AnnabaDigital.Application.Helpdesk.CalendarService>();
        services.AddScoped<AnnabaDigital.Application.Helpdesk.TeamService>();
        services.AddScoped<AnnabaDigital.Application.Helpdesk.CatalogService>();
        services.AddScoped<AnnabaDigital.Application.Helpdesk.ServiceFormService>();
        services.AddScoped<AnnabaDigital.Application.Helpdesk.PortalCatalogService>();
        services.AddScoped<AnnabaDigital.Application.Helpdesk.NotificationService>();
        services.AddScoped<AnnabaDigital.Application.Helpdesk.ApprovalPolicyService>();
        services.AddScoped<AnnabaDigital.Application.Helpdesk.TicketApprovalService>();
        services.AddScoped<AnnabaDigital.Application.Helpdesk.SlaService>();
        services.AddScoped<AnnabaDigital.Application.Helpdesk.AssignmentService>();
        services.AddScoped<AnnabaDigital.Application.Helpdesk.TicketService>();
        services.AddScoped<AnnabaDigital.Application.Helpdesk.ReportService>();
        services.Configure<StorageOptions>(config.GetSection("Storage"));
        services.AddSingleton<AnnabaDigital.Application.Abstractions.IFileStorage, LocalFileStorage>();
        services.AddSingleton<AnnabaDigital.Application.Abstractions.IAttachmentScanner>(sp =>
            string.IsNullOrWhiteSpace(sp.GetRequiredService<IOptions<StorageOptions>>().Value.ClamAvHost)
                ? new NoopAttachmentScanner()
                : ActivatorUtilities.CreateInstance<ClamAvAttachmentScanner>(sp));
        services.AddScoped<AnnabaDigital.Application.Helpdesk.SetupService>();
        services.AddScoped<AnnabaDigital.Application.Helpdesk.TicketViewService>();
        services.AddScoped<AnnabaDigital.Application.Helpdesk.ApprovalDelegationService>();
        services.AddScoped<AnnabaDigital.Application.Helpdesk.AttachmentService>();
        services.AddScoped<AnnabaDigital.Application.Helpdesk.RetentionService>();
        services.AddScoped<AnnabaDigital.Application.Helpdesk.FileCheckService>();
        services.AddScoped<AnnabaDigital.Application.Email.EmailWordingService>();
        services.AddScoped<AnnabaDigital.Application.Helpdesk.AssetService>();
        services.AddScoped<AnnabaDigital.Application.Helpdesk.WorkOrderService>();
        services.AddScoped<AnnabaDigital.Application.Helpdesk.VendorService>();
        services.AddScoped<AnnabaDigital.Application.Helpdesk.BookingService>();
        services.AddScoped<AnnabaDigital.Application.Helpdesk.VehicleService>();
        services.AddScoped<AnnabaDigital.Application.Helpdesk.VisitService>();
        services.AddScoped<AnnabaDigital.Application.Helpdesk.MailService>();
        services.AddScoped<AnnabaDigital.Application.Helpdesk.InventoryService>();
        services.AddScoped<AnnabaDigital.Application.Helpdesk.SupplyService>();
        services.AddScoped<AnnabaDigital.Application.Helpdesk.ReportExportService>();
        services.AddScoped<AnnabaDigital.Application.Users.DirectoryRoleSync>();
        services.AddScoped<AnnabaDigital.Application.Users.DirectoryService>();
        services.AddScoped<AnnabaDigital.Application.Users.ScimService>();
        services.AddScoped<AnnabaDigital.Application.Helpdesk.ChannelService>();
        services.AddScoped<AnnabaDigital.Application.Helpdesk.OfflineOpService>();
        services.AddSingleton<AnnabaDigital.Application.Abstractions.IChannelSender, AnnabaDigital.Infrastructure.Integrations.WhatsAppSender>();
        services.AddSingleton<AnnabaDigital.Application.Abstractions.IChannelSender, AnnabaDigital.Infrastructure.Integrations.SlackSender>();
        services.AddSingleton<AnnabaDigital.Application.Abstractions.IChannelSender, AnnabaDigital.Infrastructure.Integrations.TeamsSender>();
        services.AddHostedService<AnnabaDigital.Infrastructure.Integrations.ChannelWorker>();
        services.AddHostedService<AnnabaDigital.Infrastructure.Email.BookingWorker>();
        services.AddHostedService<AnnabaDigital.Infrastructure.Email.ReportScheduleWorker>();
        services.AddScoped<AnnabaDigital.Application.Helpdesk.MaintenanceService>();
        services.AddScoped<AnnabaDigital.Application.Helpdesk.RatingService>();
        services.AddScoped<AnnabaDigital.Application.Helpdesk.KnowledgeService>();
        services.AddScoped<AnnabaDigital.Application.Helpdesk.CostService>();
        services.AddScoped<AnnabaDigital.Application.Helpdesk.EmailIntakeService>();
        services.AddScoped<AnnabaDigital.Application.Integrations.IntegrationService>();
        AnnabaDigital.Infrastructure.Integrations.WebhookHttp.AddWebhookHttp(services, config);
        services.AddHostedService<AnnabaDigital.Infrastructure.Integrations.WebhookDispatcher>();
        services.AddSingleton<AnnabaDigital.Application.Helpdesk.IInboundMailParser, AnnabaDigital.Infrastructure.Email.InboundMailParser>();
        services.AddScoped<SessionsService>();
        services.AddScoped<MonitoringService>();
        services.AddScoped<DashboardService>();
        services.AddScoped<ProfileService>();
        services.AddScoped<SettingsService>();
        services.AddScoped<AnnabaDigital.Application.Email.EmailDeliveryService>();
        services.Configure<RetentionOptions>(config.GetSection("Retention"));
        services.AddHostedService<RetentionWorker>();
        services.Configure<ReviewSchedulerOptions>(config.GetSection("ReviewScheduler"));
        services.AddHostedService<AccessReviewScheduler>();
        services.AddHostedService<AccountClosureWorker>();
        services.AddHostedService<TicketAutoCloseWorker>();
        services.Configure<SlaWorkerOptions>(config.GetSection("Sla"));
        services.AddHostedService<SlaWorker>();
        services.AddSingleton<IEmailInfo, EmailInfo>();
        services.AddScoped<IEmailCredentials, DbEmailCredentials>();
        services.AddScoped<InvitationAcceptService>();

        services.Configure<EmailOptions>(config.GetSection("Email"));
        services.AddSingleton<OutboxEmailQueue>();
        services.AddScoped<IEmailQueue, TenantEmailQueue>();
        services.AddHostedService<EmailDispatcherService>();
        services.AddHttpClient<ResendEmailSender>();
        services.AddScoped<ConsoleEmailSender>();
        services.AddScoped<SmtpEmailSender>();
        services.AddScoped<IEmailSender>(sp => sp.GetRequiredService<IOptions<EmailOptions>>().Value.Provider.Trim().ToLowerInvariant() switch
        {
            "resend" => sp.GetRequiredService<ResendEmailSender>(),
            "smtp" => sp.GetRequiredService<SmtpEmailSender>(),
            _ => sp.GetRequiredService<ConsoleEmailSender>(),
        });
        services.AddHttpClient<IBreachedPasswordChecker, HibpBreachedPasswordChecker>();
        services.Configure<SsoOptions>(config.GetSection("Sso"));
        services.AddHttpClient<IOidcClient, OidcClient>(c => c.Timeout = TimeSpan.FromSeconds(15));
        return services;
    }
}
