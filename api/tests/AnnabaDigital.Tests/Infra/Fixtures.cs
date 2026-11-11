using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using AnnabaDigital.Api.Http;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Domain.Security;
using AnnabaDigital.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace AnnabaDigital.Tests.Infra;

/// <summary>Controllable clock so expiry/lockout/rate-limit windows can be tested deterministically.</summary>
public sealed class TestClock : IClock
{
    private DateTimeOffset _now = DateTimeOffset.UtcNow;
    public DateTimeOffset UtcNow => _now;
    public void Advance(TimeSpan by) => _now += by;
}

/// <summary>Probe endpoints (only registered in tests) to exercise permission enforcement end-to-end.</summary>
public sealed class ProbeModule : IEndpointModule
{
    public void Map(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/_probe/open", () => Results.Ok(new { ok = true })).RequireAuthorization();
        app.MapGet("/api/_probe/users-read", () => Results.Ok(new { ok = true })).RequirePermission(Permissions.UsersRead);
        app.MapGet("/api/_probe/settings", () => Results.Ok(new { ok = true })).RequirePermission(Permissions.SettingsManage);
        app.MapPost("/api/_probe/mutate", () => Results.NoContent()).RequireAuthorization();
    }
}

/// <summary>Captures outgoing email so tests can read one-time links (the real adapters are never called).</summary>
/// <summary>A stand-in for an antivirus: refuses any file containing the marker text (the real hook is the same interface).</summary>
public sealed class MarkerScanner : IAttachmentScanner
{
    public const string Marker = "EICAR-TEST-MARKER";
    public async Task<ScanResult> ScanAsync(Stream content, string fileName, CancellationToken ct = default)
    {
        using var ms = new MemoryStream(); await content.CopyToAsync(ms, ct);
        return System.Text.Encoding.Latin1.GetString(ms.ToArray()).Contains(Marker) ? new ScanResult(false, "test marker") : new ScanResult(true);
    }
}

public sealed class InMemoryEmailSender : IEmailSender
{
    public System.Collections.Concurrent.ConcurrentQueue<EmailMessage> Sent { get; } = new();
    public Task SendAsync(EmailMessage message, CancellationToken ct = default) { Sent.Enqueue(message); return Task.CompletedTask; }

    public async Task<EmailMessage> WaitFor(string to, string subjectContains, int timeoutMs = 5000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            var m = Sent.LastOrDefault(x => x.To.Equals(to, StringComparison.OrdinalIgnoreCase) && x.Subject.Contains(subjectContains, StringComparison.OrdinalIgnoreCase));
            if (m is not null) return m;
            await Task.Delay(25);
        }
        var recent = string.Join(" | ", Sent.TakeLast(8).Select(x => $"{x.To}: {x.Subject}"));
        throw new TimeoutException($"No email to {to} with subject containing '{subjectContains}'. Last sent: {recent}");
    }

    public async Task AssertNone(string to, int waitMs = 400)
    {
        await Task.Delay(waitMs);
        Assert.DoesNotContain(Sent, x => x.To.Equals(to, StringComparison.OrdinalIgnoreCase));
    }

    public static string TokenIn(EmailMessage m) => System.Text.RegularExpressions.Regex.Match(m.Text, @"token=([A-Za-z0-9_\-%]+)").Groups[1].Value;
}

public sealed class FakeBreachChecker : IBreachedPasswordChecker
{
    public HashSet<string> Breached { get; } = ["Tr0ub4dor&3-breached-pass"];
    public Task<bool> IsBreachedAsync(string password, CancellationToken ct = default) => Task.FromResult(Breached.Contains(password));
}

public sealed class PostgresFixture : IAsyncLifetime
{
    /// <summary>Set ANNABA_TEST_PG to an Npgsql connection string (a server where the user may CREATE DATABASE) to run without Docker; otherwise Testcontainers starts postgres:16-alpine.</summary>
    private static readonly string? External = Environment.GetEnvironmentVariable("ANNABA_TEST_PG") is { Length: > 0 } e ? e : null;
    // max_connections raised: every isolated test host keeps its own pool and pollers alive for the whole run
    private readonly PostgreSqlContainer? _pg = External is null ? new PostgreSqlBuilder().WithImage("postgres:16-alpine").WithCommand("-c", "max_connections=400").Build() : null;
    public ApiFactory Factory { get; private set; } = null!;
    public string AdminConnectionString => External ?? _pg!.GetConnectionString();

    /// <summary>A brand-new database (migrated + seeded) with its own API host and clock — for tests that need to control *every* Super Admin.</summary>
    public async Task<(ApiFactory Factory, TestClock Clock, string ConnectionString)> NewIsolatedAsync()
    {
        var name = "iso_" + Guid.NewGuid().ToString("N");
        await using (var admin = new Npgsql.NpgsqlConnection(AdminConnectionString))
        {
            await admin.OpenAsync();
            await using var cmd = admin.CreateCommand(); cmd.CommandText = $"CREATE DATABASE {name}"; await cmd.ExecuteNonQueryAsync();
        }
        // small pools that let idle connections go: dozens of isolated hosts (each with its background pollers) share one server
        var cs = new Npgsql.NpgsqlConnectionStringBuilder(AdminConnectionString) { Database = name, MaxPoolSize = 6, ConnectionIdleLifetime = 10, ConnectionPruningInterval = 5 }.ConnectionString;
        var clock = new TestClock(); var f = new ApiFactory(cs, clock);
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnnabaDigitalDbContext>();
        await db.Database.MigrateAsync();
        await Seeder.SeedReferenceDataAsync(db, clock.UtcNow);
        return (f, clock, cs);
    }
    public TestClock Clock { get; } = new();

    public async Task InitializeAsync()
    {
        if (_pg is not null) { await _pg.StartAsync(); ConnectionString = _pg.GetConnectionString(); }
        else await CreateFreshDatabaseAsync();
        Factory = new ApiFactory(ConnectionString, Clock);
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnnabaDigitalDbContext>();
        await db.Database.MigrateAsync();
        await Seeder.SeedReferenceDataAsync(db, Clock.UtcNow);
    }

    private string ConnectionString = "";
    /// <summary>With an external server each test run gets its own database, so runs never see each other's rows.</summary>
    private async Task CreateFreshDatabaseAsync()
    {
        var name = "annaba_test_" + Guid.NewGuid().ToString("N");
        await using (var admin = new Npgsql.NpgsqlConnection(AdminConnectionString))
        {
            await admin.OpenAsync();
            await using var cmd = admin.CreateCommand(); cmd.CommandText = $"CREATE DATABASE {name}"; await cmd.ExecuteNonQueryAsync();
        }
        ConnectionString = new Npgsql.NpgsqlConnectionStringBuilder(AdminConnectionString) { Database = name }.ConnectionString;
    }

    public async Task DisposeAsync() { await Factory.DisposeAsync(); if (_pg is not null) await _pg.DisposeAsync(); }
}

[CollectionDefinition("db")] public sealed class DbCollection : ICollectionFixture<PostgresFixture>;

public sealed class ApiFactory(string connectionString, TestClock clock) : WebApplicationFactory<Program>
{
    public const string ProxyKey = "test-proxy-key-0123456789abcdef0123456789";
    public const string Origin = "http://localhost:3000";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = connectionString, ["Api:ProxyKey"] = ProxyKey, ["Api:Origin"] = Origin,
            ["Api:AutoMigrate"] = "false", ["Retention:Enabled"] = "false", ["ReviewScheduler:Enabled"] = "false", ["Sla:Enabled"] = "false", ["Storage:Path"] = Path.Combine(Path.GetTempPath(), "annaba-test-files-" + Guid.NewGuid().ToString("N")), ["Security:CsrfKey"] = "test-csrf-key-0123456789abcdef0123456789",
            ["Webhooks:AllowPrivateTargets"] = "true", ["Webhooks:PollSeconds"] = "1",   // tests deliver to a receiver on 127.0.0.1; the SSRF rules themselves are unit-tested
        }));
        builder.ConfigureTestServices(s =>
        {
            s.RemoveAll<IClock>(); s.AddSingleton<IClock>(clock);
            s.AddSingleton<IEndpointModule, ProbeModule>();
            s.RemoveAll<IEmailSender>(); s.AddSingleton<InMemoryEmailSender>(); s.AddSingleton<IEmailSender>(sp => sp.GetRequiredService<InMemoryEmailSender>());
            s.RemoveAll<IBreachedPasswordChecker>(); s.AddSingleton<IBreachedPasswordChecker, FakeBreachChecker>();
            s.RemoveAll<IAttachmentScanner>(); s.AddSingleton<IAttachmentScanner, MarkerScanner>();
            // R3-10: WhatsApp, Slack and Teams are never called from tests; a recorder stands in (ChannelTests reads it)
            s.RemoveAll<IChannelSender>(); s.AddSingleton<FakeChannels>();
            foreach (var kind in AnnabaDigital.Domain.Entities.ChannelRules.Kinds) s.AddSingleton<IChannelSender>(sp => sp.GetRequiredService<FakeChannels>().Sender(kind));
        });
    }
}

/// <summary>Minimal browser-like client: keeps the session cookie, adds proxy key/Origin/CSRF like the real UI proxy would.</summary>
public sealed class TestBrowser(ApiFactory factory, string ip)
{
    private static int _ipSeq;
    public static string NewIp() => $"10.{Interlocked.Increment(ref _ipSeq) / 250 % 250}.{Interlocked.Increment(ref _ipSeq) % 250}.{Random.Shared.Next(1, 250)}";
    public HttpClient Http { get; } = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false });
    public string? CookieValue { get; set; }
    public string? CookieHeader { get; private set; }
    public string? CsrfToken { get; set; }
    public string Ip { get; set; } = ip;
    public string UserAgent { get; set; } = "Mozilla/5.0 (Macintosh; Intel Mac OS X) Chrome/124.0 Safari/537.36";
    public bool SendProxyKey { get; set; } = true;
    public string? Origin { get; set; } = ApiFactory.Origin;

    public static TestBrowser Create(ApiFactory f) => new(f, NewIp());

    public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, object? body = null, bool csrf = true, IDictionary<string, string>? headers = null)
    {
        var req = new HttpRequestMessage(method, url);
        foreach (var (k, v) in headers ?? new Dictionary<string, string>()) req.Headers.Add(k, v);
        if (SendProxyKey) req.Headers.Add("X-Internal-Proxy-Key", ApiFactory.ProxyKey);
        req.Headers.Add("X-Forwarded-For", Ip);
        req.Headers.UserAgent.ParseAdd(UserAgent);
        if (method != HttpMethod.Get && Origin is not null) req.Headers.Add("Origin", Origin);
        if (CookieValue is not null) req.Headers.Add("Cookie", $"sid={CookieValue}");
        if (csrf && CsrfToken is not null && method != HttpMethod.Get) req.Headers.Add("X-CSRF-Token", CsrfToken);
        if (body is not null) req.Content = JsonContent.Create(body);
        var res = await Http.SendAsync(req);
        if (res.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            foreach (var c in cookies)
            {
                CookieHeader = c;
                var kv = c.Split(';')[0].Split('=', 2);
                if (kv[0] is "sid" or "__Host-sid") CookieValue = string.IsNullOrEmpty(kv[1]) || c.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase) ? null : kv[1];
            }
        }
        return res;
    }

    /// <summary>Uploads one file as multipart/form-data (the browser's way), with the same headers and CSRF token as any other write.</summary>
    public async Task<HttpResponseMessage> UploadAsync(string url, string fileName, byte[] bytes, string contentType = "application/octet-stream")
    {
        var req = new HttpRequestMessage(HttpMethod.Post, url);
        if (SendProxyKey) req.Headers.Add("X-Internal-Proxy-Key", ApiFactory.ProxyKey);
        req.Headers.Add("X-Forwarded-For", Ip); req.Headers.UserAgent.ParseAdd(UserAgent);
        if (Origin is not null) req.Headers.Add("Origin", Origin);
        if (CookieValue is not null) req.Headers.Add("Cookie", $"sid={CookieValue}");
        if (CsrfToken is not null) req.Headers.Add("X-CSRF-Token", CsrfToken);
        var part = new ByteArrayContent(bytes); part.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        req.Content = new MultipartFormDataContent { { part, "file", fileName } };
        return await Http.SendAsync(req);
    }

    public Task<HttpResponseMessage> Get(string url) => SendAsync(HttpMethod.Get, url);
    public Task<HttpResponseMessage> Post(string url, object? body = null, bool csrf = true) => SendAsync(HttpMethod.Post, url, body ?? new { }, csrf);

    public async Task<HttpResponseMessage> LoginAsync(string email, string password, bool remember = false)
    {
        var res = await Post("/api/auth/login", new { email, password, remember }, csrf: false);
        if (res.IsSuccessStatusCode && CookieValue is not null)
        {
            var csrf = await Get("/api/auth/csrf");
            CsrfToken = (await csrf.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();
        }
        return res;
    }
}

public static class Json
{
    public static async Task<JsonElement> Body(this HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>();
    public static async Task<string> ErrorCode(this HttpResponseMessage r) => (await r.Body()).GetProperty("error").GetProperty("code").GetString()!;
}

public sealed class Seed(ApiFactory factory, TestClock clock)
{
    public InMemoryEmailSender Mail => factory.Services.GetRequiredService<InMemoryEmailSender>();

    /// <summary>Temporarily flips a system setting (JSON value) and returns an IAsyncDisposable that restores the previous value.</summary>
    public async Task<IAsyncDisposable> SettingAsync(string key, string jsonValue)
    {
        var old = await Db(async db => (await db.Settings.SingleAsync(x => x.Key == key)).ValueJson);
        await Db(async db => { var r = await db.Settings.SingleAsync(x => x.Key == key); r.ValueJson = jsonValue; return await db.SaveChangesAsync(); });
        return new Restore(async () => await Db(async db => { var r = await db.Settings.SingleAsync(x => x.Key == key); r.ValueJson = old; return await db.SaveChangesAsync(); }));
    }
    private sealed class Restore(Func<Task> f) : IAsyncDisposable { public async ValueTask DisposeAsync() => await f(); }

    public const string Password = "Correct-Horse-9!battery";

    public async Task<User> UserAsync(string? email = null, string role = "user", UserStatus status = UserStatus.Active, string password = Password)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AnnabaDigitalDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var now = clock.UtcNow;
        var u = new User { Email = (email ?? $"u{Guid.NewGuid():N}@test.local").ToLowerInvariant(), Name = "Test User", Status = status, CreatedAt = now, UpdatedAt = now, EmailVerifiedAt = status == UserStatus.Pending ? null : now };
        u.Credential = new UserCredential { PasswordHash = hasher.Hash(password), PasswordChangedAt = now };
        u.Profile = new UserProfile();
        var r = await db.Roles.FirstAsync(x => x.TenantId == Tenant.DefaultId && x.Key == role);
        u.UserRoles.Add(new UserRole { RoleId = r.Id, AssignedAt = now });
        db.Users.Add(u);
        await db.SaveChangesAsync();
        return u;
    }

    /// <summary>Creates (or reuses) a custom role holding exactly the given permissions.</summary>
    public async Task<Role> RoleAsync(string key, params string[] permissions)
    {
        return await Db(async db =>
        {
            var r = await db.Roles.FirstOrDefaultAsync(x => x.TenantId == Tenant.DefaultId && x.Key == key);
            if (r is null)
            {
                r = new Role { Key = key, Name = key, Description = "test", CreatedAt = clock.UtcNow, UpdatedAt = clock.UtcNow };
                foreach (var p in permissions) r.Permissions.Add(new RolePermission { PermissionKey = p, GrantedAt = clock.UtcNow });
                db.Roles.Add(r); await db.SaveChangesAsync();
            }
            return r;
        });
    }

    public async Task<Guid> RoleId(string key) => await Db(db => db.Roles.Where(r => r.TenantId == Tenant.DefaultId && r.Key == key).Select(r => r.Id).SingleAsync());

    public async Task<T> Db<T>(Func<AnnabaDigitalDbContext, Task<T>> f)
    {
        using var scope = factory.Services.CreateScope();
        var result = await f(scope.ServiceProvider.GetRequiredService<AnnabaDigitalDbContext>());
        factory.Services.GetRequiredService<AnnabaDigital.Application.Common.AppCache>().Clear(); // a test that edits the database directly is like another instance: nothing cached may outlive it
        return result;
    }
}
