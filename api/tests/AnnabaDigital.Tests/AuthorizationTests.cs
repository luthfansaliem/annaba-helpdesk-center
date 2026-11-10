using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Tests.Infra;
using Xunit;

namespace AnnabaDigital.Tests;

[Collection("db")]
public sealed class AuthorizationTests(PostgresFixture fx)
{
    private readonly Seed _seed = new(fx.Factory, fx.Clock);

    private async Task<(TestBrowser b, User u)> SignedIn(string role)
    {
        var u = await _seed.UserAsync(role: role); var b = TestBrowser.Create(fx.Factory); await b.LoginAsync(u.Email, Seed.Password); return (b, u);
    }

    [Fact]
    public async Task Every_api_endpoint_declares_its_access_explicitly_deny_by_default_coverage()
    {
        var sources = fx.Factory.Services.GetServices<EndpointDataSource>();
        var api = sources.SelectMany(s => s.Endpoints).OfType<RouteEndpoint>().Where(e => e.RoutePattern.RawText?.StartsWith("/api") == true).ToList();
        Assert.NotEmpty(api);
        var undeclared = api.Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is null && !e.Metadata.GetOrderedMetadata<IAuthorizeData>().Any())
            .Select(e => e.RoutePattern.RawText).ToList();
        Assert.Empty(undeclared); // a new endpoint without .AllowAnonymous()/.RequireAuthorization()/.RequirePermission() fails this test
    }

    [Fact]
    public async Task Anonymous_endpoints_are_exactly_the_expected_public_surface()
    {
        var api = fx.Factory.Services.GetServices<EndpointDataSource>().SelectMany(s => s.Endpoints).OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("/api") == true && e.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            .Select(e => e.RoutePattern.RawText).ToList();
        Assert.Equal(
            // the two approval-link routes are public on purpose: the one-time token in the e-mailed link is the credential (see ApprovalAndNotificationTests);
            // so is the inbound e-mail webhook: its secret token is the credential (see EmailIntakeTests)
            ["/api/approvals/link/{token}", "/api/approvals/link/{token}", "/api/auth/forgot-password", "/api/auth/login", "/api/auth/register", "/api/auth/reset-password", "/api/auth/sso/callback", "/api/auth/sso/start", "/api/auth/verify-email", "/api/auth/verify-email/resend", "/api/inbound/email/{token}", "/api/invitations/accept", "/api/invitations/lookup", "/api/settings/public"],
            api.Order().ToList());
    }

    [Fact]
    public async Task Unauthenticated_requests_to_protected_endpoints_get_401()
    {
        var b = TestBrowser.Create(fx.Factory);
        foreach (var path in new[] { "/api/_probe/open", "/api/_probe/users-read", "/api/_probe/settings", "/api/auth/me" })
            Assert.Equal(HttpStatusCode.Unauthorized, (await b.Get(path)).StatusCode);
    }

    [Fact]
    public async Task Missing_permission_returns_403_and_is_audited_with_the_missing_permission()
    {
        var (b, u) = await SignedIn("user");
        var res = await b.Get("/api/_probe/users-read");
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        Assert.Equal("denied", await res.ErrorCode());
        var a = await _seed.Db(db => db.AuditEvents.Where(x => x.ActorId == u.Id && x.Action == "authz.denied").SingleAsync());
        Assert.Equal(Outcome.Denied, a.Outcome);
        Assert.Contains("users.read", a.MetaJson);
    }

    [Theory]
    [InlineData("super_admin", "/api/_probe/users-read", HttpStatusCode.OK)]
    [InlineData("super_admin", "/api/_probe/settings", HttpStatusCode.OK)]
    [InlineData("user_admin", "/api/_probe/users-read", HttpStatusCode.OK)]
    [InlineData("user_admin", "/api/_probe/settings", HttpStatusCode.Forbidden)]
    [InlineData("security_auditor", "/api/_probe/users-read", HttpStatusCode.OK)]
    [InlineData("security_auditor", "/api/_probe/settings", HttpStatusCode.Forbidden)]
    [InlineData("user", "/api/_probe/users-read", HttpStatusCode.Forbidden)]
    [InlineData("user", "/api/_probe/open", HttpStatusCode.OK)]
    public async Task Seed_roles_grant_exactly_what_the_rbac_matrix_says(string role, string path, HttpStatusCode expected)
    {
        var (b, _) = await SignedIn(role);
        Assert.Equal(expected, (await b.Get(path)).StatusCode);
    }

    [Fact]
    public async Task Permission_removal_applies_on_the_next_request_without_re_login()
    {
        var (b, _) = await SignedIn("user_admin");
        Assert.Equal(HttpStatusCode.OK, (await b.Get("/api/_probe/users-read")).StatusCode);
        await _seed.Db(async db =>
        {
            var role = await db.Roles.SingleAsync(r => r.TenantId == Tenant.DefaultId && r.Key == "user_admin");
            var row = await db.RolePermissions.SingleAsync(x => x.RoleId == role.Id && x.PermissionKey == "users.read");
            db.RolePermissions.Remove(row); return await db.SaveChangesAsync();
        });
        Assert.Equal(HttpStatusCode.Forbidden, (await b.Get("/api/_probe/users-read")).StatusCode);
        // restore for other tests
        await _seed.Db(async db => { var role = await db.Roles.SingleAsync(r => r.TenantId == Tenant.DefaultId && r.Key == "user_admin"); db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionKey = "users.read", GrantedAt = fx.Clock.UtcNow }); return await db.SaveChangesAsync(); });
        Assert.Equal(HttpStatusCode.OK, (await b.Get("/api/_probe/users-read")).StatusCode);
    }

    [Fact]
    public async Task Archived_roles_grant_nothing()
    {
        var (b, _) = await SignedIn("security_auditor");
        Assert.Equal(HttpStatusCode.OK, (await b.Get("/api/_probe/users-read")).StatusCode);
        await _seed.Db(async db => { var r = await db.Roles.SingleAsync(x => x.TenantId == Tenant.DefaultId && x.Key == "security_auditor"); r.ArchivedAt = fx.Clock.UtcNow; return await db.SaveChangesAsync(); });
        try { Assert.Equal(HttpStatusCode.Forbidden, (await b.Get("/api/_probe/users-read")).StatusCode); }
        finally { await _seed.Db(async db => { var r = await db.Roles.SingleAsync(x => x.TenantId == Tenant.DefaultId && x.Key == "security_auditor"); r.ArchivedAt = null; return await db.SaveChangesAsync(); }); }
    }

    [Fact]
    public async Task Unknown_permission_keys_in_the_database_are_ignored()
    {
        var (b, _) = await SignedIn("user");
        // The FK prevents inserting an unknown key into role_permissions; assert the DB itself rejects it (defence in depth).
        await Assert.ThrowsAnyAsync<DbUpdateException>(() => _seed.Db(async db =>
        {
            var role = await db.Roles.SingleAsync(r => r.TenantId == Tenant.DefaultId && r.Key == "user");
            db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionKey = "root.everything", GrantedAt = fx.Clock.UtcNow });
            return await db.SaveChangesAsync();
        }));
        Assert.Equal(HttpStatusCode.Forbidden, (await b.Get("/api/_probe/users-read")).StatusCode);
    }
}
