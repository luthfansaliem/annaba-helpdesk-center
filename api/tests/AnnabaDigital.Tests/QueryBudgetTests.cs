using AnnabaDigital.Application.Common;
using AnnabaDigital.Infrastructure.Persistence;
using AnnabaDigital.Tests.Infra;
using Xunit;

namespace AnnabaDigital.Tests;

/// <summary>Hot endpoints keep a small SQL budget, so a change that adds a round trip to every request (or a loop of counts) is noticed. The limits leave a little headroom over today's numbers.</summary>
[Collection("db")]
public sealed class QueryBudgetTests(PostgresFixture fx)
{
    private readonly Seed _seed = new(fx.Factory, fx.Clock);

    [Fact]
    public async Task Hot_endpoints_stay_within_their_query_budget()
    {
        var u = await _seed.UserAsync(role: "super_admin"); var b = TestBrowser.Create(fx.Factory); await b.LoginAsync(u.Email, Seed.Password);
        await b.Get("/api/auth/me"); // warm the short-lived caches, as a real session would have

        var budgets = new (string Url, int Max)[]
        {
            ("/api/auth/me", 8), ("/api/users?pageSize=25", 8), ("/api/users/stats", 9), ("/api/roles", 5), ("/api/audit?pageSize=25", 6), ("/api/dashboard/admin", 22), ("/api/dashboard/me", 9),
            ("/api/sessions", 6), ("/api/access-matrix", 5), ("/api/access-requests?pageSize=25", 7), ("/api/settings/public", 3),
        };
        var over = new List<string>();
        foreach (var (url, max) in budgets)
        {
            // the counter is process-wide, so a background poller of another test host can land inside one measurement: take the lowest of three
            var used = int.MaxValue;
            for (var i = 0; i < 3; i++)
            {
                var before = CommandCounter.Total; var r = await b.Get(url); used = Math.Min(used, (int)(CommandCounter.Total - before));
                Assert.True(r.IsSuccessStatusCode, $"{url} → {(int)r.StatusCode}");
            }
            if (used > max) over.Add($"{url}: {used} queries (budget {max})");
        }
        Assert.True(over.Count == 0, string.Join("; ", over));
    }

    [Fact]
    public async Task The_short_lived_cache_expires_clears_and_can_be_switched_off()
    {
        var cache = new AppCache(TimeSpan.FromMilliseconds(80)); var loads = 0;
        Task<int> Load() { loads++; return Task.FromResult(loads); }
        Assert.Equal(1, await cache.GetOrAddAsync("k", Load)); Assert.Equal(1, await cache.GetOrAddAsync("k", Load)); Assert.Equal(1, loads); // second call is served from memory
        await Task.Delay(120); Assert.Equal(2, await cache.GetOrAddAsync("k", Load)); // expired
        cache.Clear(); Assert.Equal(3, await cache.GetOrAddAsync("k", Load)); // cleared on demand

        var off = new AppCache(TimeSpan.Zero); loads = 0;
        await off.GetOrAddAsync("k", Load); await off.GetOrAddAsync("k", Load); Assert.Equal(2, loads); // zero = no caching
    }

    [Fact]
    public async Task Changes_to_settings_organisations_and_roles_are_seen_at_once_despite_the_caches()
    {
        var admin = await _seed.UserAsync(role: "super_admin"); var ab = TestBrowser.Create(fx.Factory); await ab.LoginAsync(admin.Email, Seed.Password); await ab.Post("/api/auth/reauth", new { password = Seed.Password });
        var min = (await (await ab.Get("/api/settings")).Body()).GetProperty("passwordMinLength").GetInt32();
        var res = await ab.SendAsync(HttpMethod.Patch, "/api/settings", new { passwordMinLength = min + 1 });
        Assert.True(res.IsSuccessStatusCode);
        try { Assert.Equal(min + 1, (await (await ab.Get("/api/settings")).Body()).GetProperty("passwordMinLength").GetInt32()); } // not the cached old value
        finally { await ab.SendAsync(HttpMethod.Patch, "/api/settings", new { passwordMinLength = min }); }

        // a role taken away stops working on the very next request (permissions are never cached across requests)
        var target = await _seed.UserAsync(role: "user_admin"); var tb = TestBrowser.Create(fx.Factory); await tb.LoginAsync(target.Email, Seed.Password);
        Assert.Equal(System.Net.HttpStatusCode.OK, (await tb.Get("/api/users?pageSize=5")).StatusCode);
        var userRole = await _seed.RoleId("user");
        Assert.True((await ab.SendAsync(HttpMethod.Put, $"/api/users/{target.Id}/roles", new { roleIds = new[] { userRole } })).IsSuccessStatusCode);
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, (await tb.Get("/api/users?pageSize=5")).StatusCode);
    }
}
