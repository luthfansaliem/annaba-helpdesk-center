using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using AnnabaDigital.Api;
using AnnabaDigital.Api.Endpoints;
using AnnabaDigital.Api.Http;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Infrastructure;
using AnnabaDigital.Infrastructure.Persistence;
using AnnabaDigital.Infrastructure.Security;

// Container health probe (the runtime image has no curl): `dotnet AnnabaDigital.Api.dll --healthcheck` exits 0 when /health answers.
if (args.Contains("--healthcheck"))
{
    try
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var port = Environment.GetEnvironmentVariable("ASPNETCORE_URLS")?.Split(':').Last().TrimEnd('/') is { Length: > 0 } p && int.TryParse(p, out _) ? p : "8080";
        return (await http.GetAsync($"http://localhost:{port}/health")).IsSuccessStatusCode ? 0 : 1;
    }
    catch { return 1; }
}

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<ApiOptions>(builder.Configuration.GetSection("Api"));
builder.Services.AddAnnabaDigitalInfrastructure(builder.Configuration);
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<AnnabaDigital.Application.Abstractions.IAppLinks, AppLinks>();

builder.Services.AddMemoryCache();
builder.Services.AddAuthentication(SessionDefaults.Scheme).AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, SessionAuthenticationHandler>(SessionDefaults.Scheme, _ => { });
builder.Services.AddAuthorizationBuilder().SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build()); // deny by default
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddScoped<IAuthorizationHandler, PermissionHandler>();
builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, EnvelopeAuthorizationResultHandler>();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase);

var app = builder.Build();

StartupChecks.Validate(app.Services, app.Environment);

if (app.Services.GetRequiredService<IOptions<ApiOptions>>().Value.AutoMigrate)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AnnabaDigitalDbContext>();
    await db.Database.MigrateAsync();
    await Seeder.SeedReferenceDataAsync(db, DateTimeOffset.UtcNow);
    if (app.Services.GetRequiredService<IOptions<ApiOptions>>().Value.SeedDevUsers && app.Environment.IsDevelopment())
        await DevData.SeedAsync(scope.ServiceProvider);
    await Bootstrap.RunAsync(scope.ServiceProvider, app.Services.GetRequiredService<IOptions<ApiOptions>>().Value.BootstrapAdminEmail);
}

// Order matters: gate → error envelope → headers → authN → session state → CSRF → authZ
app.UseMiddleware<ProxyGateMiddleware>();
app.UseMiddleware<ExceptionEnvelopeMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseAuthentication();
app.UseMiddleware<ApiKeyGateMiddleware>();
app.UseMiddleware<SessionStateMiddleware>();
app.UseMiddleware<CsrfMiddleware>();
app.UseMiddleware<VendorPortalGateMiddleware>();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
app.MapAuth();
app.MapSso();
app.MapAccount();
app.MapMfa();
app.MapUsers();
app.MapAccessRequests();
app.MapAccessReviews();
app.MapTenants();
app.MapAccountClosures();
app.MapRoles();
app.MapSelfService();
app.MapHelpdeskSetup();
app.MapCatalog();
app.MapTickets();
app.MapSlaAndRules();
app.MapApprovals();
app.MapReports();
app.MapSetup();
app.MapTicketViews();
app.MapRetention();
app.MapEmailWording();
app.MapAssets();
app.MapWorkOrders();
app.MapVendors();
app.MapBookings();
app.MapVehicles();
app.MapVisits();
app.MapMail();
app.MapInventory();
app.MapSupplies();
app.MapReportSchedules();
app.MapDirectory();
app.MapChannels();
app.MapMaintenance();
app.MapKnowledge();
app.MapCosts();
app.MapInboundEmail();
app.MapIntegrations();
foreach (var module in app.Services.GetServices<IEndpointModule>()) module.Map(app);

app.Run();
return 0;

public partial class Program;
