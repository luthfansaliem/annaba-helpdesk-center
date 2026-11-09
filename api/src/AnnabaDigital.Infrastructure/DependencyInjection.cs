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
            o.UseNpgsql(cs).UseSnakeCaseNamingConvention().AddInterceptors(new CommandCounter());
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
