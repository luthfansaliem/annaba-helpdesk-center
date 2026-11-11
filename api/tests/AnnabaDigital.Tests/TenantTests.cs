using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using AnnabaDigital.Application.Users;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Tests.Infra;
using Xunit;

namespace AnnabaDigital.Tests;

[Collection("db")]
public sealed class TenantTests(PostgresFixture fx)
{
    private readonly Seed _seed = new(fx.Factory, fx.Clock);
    private const string StrongPw = "Cobalt-Meadow-Harvest-58";
    private static string Slug() => "t" + Guid.NewGuid().ToString("N")[..10];

    private async Task<(TestBrowser b, User u)> Platform(string role = "super_admin", bool reauth = true)
    {
        var u = await _seed.UserAsync(role: role); var b = TestBrowser.Create(fx.Factory); await b.LoginAsync(u.Email, Seed.Password);
        if (reauth) await b.Post("/api/auth/reauth", new { password = Seed.Password });
        return (b, u);
    }

    /// <summary>Creates a tenant through the API, accepts the first administrator's invitation, and signs that administrator in.</summary>
    private async Task<(Guid tenantId, string slug, TestBrowser admin, string adminEmail)> NewTenant(TestBrowser platform)
    {
        var slug = Slug(); var adminEmail = $"{slug}.admin@test.local";
        var res = await platform.Post("/api/tenants", new { name = $"Org {slug}", slug, adminEmail });
        Assert.True(res.StatusCode == HttpStatusCode.Created, await res.Content.ReadAsStringAsync());
        var id = (await res.Body()).GetProperty("id").GetGuid();
        var mail = await _seed.Mail.WaitFor(adminEmail, "invited");
        var token = InMemoryEmailSender.TokenIn(mail);
        var accept = await TestBrowser.Create(fx.Factory).Post("/api/invitations/accept", new { token, name = "Tenant Admin", password = StrongPw }, csrf: false);
        Assert.Equal(HttpStatusCode.OK, accept.StatusCode);
        var b = TestBrowser.Create(fx.Factory); Assert.Equal(HttpStatusCode.OK, (await b.LoginAsync(adminEmail, StrongPw)).StatusCode);
        await b.Post("/api/auth/reauth", new { password = StrongPw });
        return (id, slug, b, adminEmail);
    }

    private static IEnumerable<JsonElement> Rows(JsonElement list) => list.ValueKind == JsonValueKind.Array ? list.EnumerateArray() : list.GetProperty("data").EnumerateArray();

    [Fact]
    public async Task The_platform_creates_a_tenant_with_its_own_roles_and_a_first_administrator_who_can_sign_in()
    {
        var (p, _) = await Platform(); var (id, slug, admin, email) = await NewTenant(p);
        var me = await (await admin.Get("/api/auth/me")).Body();
        Assert.Equal($"Org {slug}", me.GetProperty("tenantName").GetString()); Assert.False(me.GetProperty("isPlatform").GetBoolean());
        Assert.Contains("super_admin", me.GetProperty("roles").EnumerateArray().Select(r => r.GetString()));
        var roles = await _seed.Db(db => db.Roles.IgnoreQueryFilters().Where(r => r.TenantId == id).Select(r => r.Key).ToListAsync());
        Assert.Equal(["ga_agent", "ga_manager", "ga_supervisor", "receptionist", "security_auditor", "super_admin", "user", "user_admin", "vendor"], roles.Order().ToArray()); // every organisation owns its copy of each system role
        Assert.NotEmpty(await _seed.Db(db => db.AuditEvents.Where(a => a.Action == "tenant.create" && a.TargetId == id.ToString()).ToListAsync()));

        // validation and uniqueness
        Assert.Equal(HttpStatusCode.Conflict, (await p.Post("/api/tenants", new { name = "Again", slug, adminEmail = $"x{Slug()}@test.local" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await p.Post("/api/tenants", new { name = "Mail clash", slug = Slug(), adminEmail = email })).StatusCode);
        Assert.Equal((HttpStatusCode)422, (await p.Post("/api/tenants", new { name = "", slug = "Bad Slug!", adminEmail = "nope" })).StatusCode);
    }

    [Fact]
    public async Task Only_platform_administrators_with_recent_reauthentication_manage_tenants()
    {
        var (p, _) = await Platform(); var (_, _, other, _) = await NewTenant(p);
        // a tenant's own super admin holds every permission, yet is not the platform
        Assert.Equal(HttpStatusCode.Forbidden, (await other.Get("/api/tenants")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.Post("/api/tenants", new { name = "Sneaky", slug = Slug(), adminEmail = $"s{Slug()}@test.local" })).StatusCode);
        var (plain, _) = await Platform("user", reauth: false);
        Assert.Equal(HttpStatusCode.Forbidden, (await plain.Get("/api/tenants")).StatusCode);
        var (stale, _) = await Platform(reauth: false);
        var denied = await stale.Post("/api/tenants", new { name = "Needs reauth", slug = Slug(), adminEmail = $"r{Slug()}@test.local" });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode); Assert.Equal("reauth_required", await denied.ErrorCode());
        Assert.Equal(HttpStatusCode.Unauthorized, (await TestBrowser.Create(fx.Factory).Get("/api/tenants")).StatusCode);
        var list = await (await p.Get("/api/tenants")).Body();
        Assert.Contains(list.EnumerateArray(), t => t.GetProperty("isPlatform").GetBoolean());
    }

    [Fact]
    public async Task One_tenant_cannot_see_or_touch_another_tenants_people_roles_invitations_sessions_or_audit()
    {
        var (p, pu) = await Platform(); var (_, _, b, _) = await NewTenant(p);
        var aUser = await _seed.UserAsync(role: "user_admin"); var aBrowser = TestBrowser.Create(fx.Factory); await aBrowser.LoginAsync(aUser.Email, Seed.Password);
        var aRole = await _seed.RoleId("user_admin"); // the default tenant's role
        var aInvite = await p.Post("/api/invitations", new { email = $"inv{Slug()}@test.local", roleIds = new[] { await _seed.RoleId("user") } });
        Assert.Equal(HttpStatusCode.Created, aInvite.StatusCode); var aInviteId = (await aInvite.Body()).GetProperty("id").GetGuid();
        var aAudit = await _seed.Db(db => db.AuditEvents.Where(a => a.ActorId == aUser.Id).Select(a => a.Id).FirstAsync());

        // lists contain only this tenant's rows
        Assert.DoesNotContain(Rows(await (await b.Get("/api/users?pageSize=100")).Body()), u => u.GetProperty("id").GetGuid() == aUser.Id || u.GetProperty("id").GetGuid() == pu.Id);
        Assert.DoesNotContain(Rows(await (await b.Get("/api/roles")).Body()), r => r.GetProperty("id").GetGuid() == aRole);
        Assert.DoesNotContain(Rows(await (await b.Get("/api/invitations")).Body()), i => i.GetProperty("id").GetGuid() == aInviteId);
        Assert.DoesNotContain(Rows(await (await b.Get("/api/sessions?pageSize=100")).Body()), s => s.GetProperty("userId").GetGuid() == aUser.Id);
        Assert.DoesNotContain(Rows(await (await b.Get("/api/audit?pageSize=100")).Body()), e => e.GetProperty("actorId").ValueKind != JsonValueKind.Null && e.GetProperty("actorId").GetGuid() == aUser.Id);

        // by-id access to the other tenant's rows is "not found", for reads and writes alike
        Assert.Equal(HttpStatusCode.NotFound, (await b.Get($"/api/users/{aUser.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.Get($"/api/roles/{aRole}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.Get($"/api/audit/{aAudit}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.Get($"/api/avatars/{aUser.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.SendAsync(HttpMethod.Patch, $"/api/users/{aUser.Id}", new { name = "Hijacked" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.SendAsync(HttpMethod.Put, $"/api/users/{aUser.Id}/roles", new { roleIds = new[] { aRole } })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.Post($"/api/users/{aUser.Id}/suspend", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.Post($"/api/invitations/{aInviteId}/resend")).StatusCode);
        Assert.Equal("Created Person" == "" ? "" : aUser.Name, (await _seed.Db(db => db.Users.SingleAsync(u => u.Id == aUser.Id))).Name); // untouched

        // a role from another tenant can't be attached
        var cross = await b.Post("/api/users", new { name = "Cross Tenant", email = $"x{Slug()}@test.local", roleIds = new[] { aRole }, mode = "invite" });
        Assert.Equal((HttpStatusCode)422, cross.StatusCode);
        // …and e-mail addresses stay unique across organisations
        Assert.Equal(HttpStatusCode.Conflict, (await b.Post("/api/users", new { name = "Dup", email = aUser.Email, roleIds = new[] { (await (await b.Get("/api/roles")).Body()).EnumerateArray().First(r => r.GetProperty("key").GetString() == "user").GetProperty("id").GetGuid() }, mode = "invite" })).StatusCode);
    }

    [Fact]
    public async Task Reviews_requests_and_role_names_are_per_tenant()
    {
        var (p, _) = await Platform(); var (_, _, b, _) = await NewTenant(p);
        await _seed.Db(async db => await db.AccessReviews.IgnoreQueryFilters().Where(r => r.Status == AccessReviewStatus.Open).ExecuteUpdateAsync(u => u.SetProperty(r => r.Status, AccessReviewStatus.Closed)));
        var a = await p.Post("/api/access-reviews", new { name = "Platform review", dueInDays = 7 }); Assert.Equal(HttpStatusCode.Created, a.StatusCode);
        var mine = await b.Post("/api/access-reviews", new { name = "Tenant review", dueInDays = 7 }); // another tenant may run its own at the same time
        Assert.Equal(HttpStatusCode.Created, mine.StatusCode);
        var reviews = await (await b.Get("/api/access-reviews")).Body();
        Assert.Single(reviews.EnumerateArray()); Assert.Equal("Tenant review", reviews[0].GetProperty("name").GetString());
        var reviewId = reviews[0].GetProperty("id").GetGuid();
        var items = await _seed.Db(db => db.AccessReviewItems.IgnoreQueryFilters().Where(i => i.ReviewId == reviewId).Select(i => i.User!.TenantId).Distinct().ToListAsync());
        Assert.Single(items); // only people of that tenant are in its review

        // the platform can't read the tenant's review and vice versa
        var platformReviews = await (await p.Get("/api/access-reviews")).Body();
        Assert.DoesNotContain(platformReviews.EnumerateArray(), r => r.GetProperty("id").GetGuid() == reviewId);

        // the same custom role name can exist in two organisations
        var name = "Shared name " + Slug();
        Assert.Equal(HttpStatusCode.Created, (await p.Post("/api/roles", new { name, description = "x", permissions = Array.Empty<string>() })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await b.Post("/api/roles", new { name, description = "x", permissions = Array.Empty<string>() })).StatusCode);
        await _seed.Db(async db => await db.AccessReviews.IgnoreQueryFilters().Where(r => r.Status == AccessReviewStatus.Open).ExecuteUpdateAsync(u => u.SetProperty(r => r.Status, AccessReviewStatus.Closed)));
    }

    [Fact]
    public async Task Suspending_a_tenant_ends_its_sessions_and_blocks_sign_in_until_it_is_reactivated()
    {
        var (p, _) = await Platform(); var (id, _, admin, email) = await NewTenant(p);
        Assert.Equal(HttpStatusCode.OK, (await admin.Get("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await p.Post($"/api/tenants/{id}/suspend", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await admin.Get("/api/auth/me")).StatusCode);
        var blocked = await (await TestBrowser.Create(fx.Factory).LoginAsync(email, StrongPw)).Body();
        Assert.Equal("suspended", blocked.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await p.Post($"/api/tenants/{id}/suspend", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await p.Post($"/api/tenants/{id}/activate", new { })).StatusCode);
        var again = await (await TestBrowser.Create(fx.Factory).LoginAsync(email, StrongPw)).Body();
        Assert.Equal("ok", again.GetProperty("status").GetString());
        // the platform tenant itself is never suspendable
        Assert.Equal(HttpStatusCode.Conflict, (await p.Post($"/api/tenants/{Tenant.DefaultId}/suspend", new { })).StatusCode);
    }

    [Fact]
    public async Task Sign_in_attempts_are_audited_in_the_users_own_tenant()
    {
        var (p, _) = await Platform(); var (id, _, admin, email) = await NewTenant(p);
        await TestBrowser.Create(fx.Factory).LoginAsync(email, "wrong-password-1");
        var mine = await _seed.Db(db => db.AuditEvents.IgnoreQueryFilters().Where(a => a.Action == "auth.login.failure" && a.ActorLabel == email).Select(a => a.TenantId).ToListAsync());
        Assert.NotEmpty(mine); Assert.All(mine, t => Assert.Equal(id, t));
        var visible = await (await admin.Get($"/api/audit?q={Uri.EscapeDataString(email)}&outcome=failure")).Body();
        Assert.True(visible.GetProperty("total").GetInt32() >= 1);
        var platformView = await (await p.Get($"/api/audit?q={Uri.EscapeDataString(email)}&outcome=failure")).Body();
        Assert.Equal(0, platformView.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Email_delivery_lists_only_the_organisations_own_mail_including_mail_sent_by_anonymous_flows()
    {
        var (p, _) = await Platform(); var (id, _, admin, adminEmail) = await NewTenant(p);
        // the invitation that created the tenant's first administrator belongs to that tenant
        Assert.Equal(id, await _seed.Db(db => db.OutboxEmails.IgnoreQueryFilters().Where(o => o.ToAddress == adminEmail).Select(o => o.TenantId).FirstAsync()));

        // an anonymous flow (password reset, no signed-in tenant) is filed under the recipient's organisation
        Assert.Equal(HttpStatusCode.Accepted, (await TestBrowser.Create(fx.Factory).Post("/api/auth/forgot-password", new { email = adminEmail }, csrf: false)).StatusCode);
        await _seed.Mail.WaitFor(adminEmail, "Reset your password");
        Assert.All(await _seed.Db(db => db.OutboxEmails.IgnoreQueryFilters().Where(o => o.ToAddress == adminEmail).Select(o => o.TenantId).ToListAsync()), t => Assert.Equal(id, t));

        var mine = Rows(await (await admin.Get("/api/email-deliveries?pageSize=100")).Body()).Select(r => r.GetProperty("to").GetString()).ToList();
        Assert.Contains(adminEmail, mine); Assert.All(mine, to => Assert.Equal(adminEmail, to)); // nobody else's mail leaks in

        var platformSees = Rows(await (await p.Get("/api/email-deliveries?pageSize=100")).Body()).Select(r => r.GetProperty("to").GetString()).ToList();
        Assert.DoesNotContain(adminEmail, platformSees); // and the other way round
    }

    [Fact]
    public async Task No_read_endpoint_returns_another_organisations_people_in_either_direction()
    {
        var (p, _) = await Platform(); var (_, slugB, admin, adminEmail) = await NewTenant(p);
        // a recognisable person (with sessions, audit and security activity) in the default organisation
        var marker = await _seed.UserAsync(email: $"{slugB}@dflt-marker.test", role: "user_admin");
        var mb = TestBrowser.Create(fx.Factory); await mb.LoginAsync(marker.Email, Seed.Password);
        await TestBrowser.Create(fx.Factory).Post("/api/auth/login", new { email = marker.Email, password = "wrong-password-1" }, csrf: false); // a failed sign-in: audit + security event
        await mb.Post("/api/access-requests", new { roleId = await _seed.RoleId("security_auditor"), reason = "Marker request for the isolation sweep." });

        var reads = new[]
        {
            "/api/users?pageSize=100", "/api/users/stats", "/api/invitations?pageSize=100", "/api/roles", "/api/permissions", "/api/access-matrix", "/api/audit?pageSize=100", "/api/audit/export?format=json",
            "/api/audit/export?format=csv", "/api/security-events?pageSize=100", "/api/sessions", "/api/sessions/all", "/api/dashboard/admin", "/api/dashboard/me", "/api/access-requests?pageSize=100", "/api/access-requests/mine",
            "/api/access-requests/pending-count", "/api/access-reviews", "/api/email-deliveries?pageSize=100",
        };
        var report = new List<string>();
        foreach (var url in reads)
        {
            var asB = await admin.Get(url); var text = await asB.Content.ReadAsStringAsync();
            if (text.Contains("dflt-marker.test")) report.Add($"tenant B sees default-tenant data in {url}");
            var asDefault = await p.Get(url); var t2 = await asDefault.Content.ReadAsStringAsync();
            // the platform's own audit trail records that IT created tenant B for that address (one tenant.create row) - nothing else of B's may appear
            var allowed = url.StartsWith("/api/audit") ? 1 : 0;
            if (System.Text.RegularExpressions.Regex.Matches(t2, System.Text.RegularExpressions.Regex.Escape(adminEmail)).Count > allowed) report.Add($"default tenant sees tenant B data in {url} ({System.Text.RegularExpressions.Regex.Matches(t2, System.Text.RegularExpressions.Regex.Escape(adminEmail)).Count}x): {string.Join(" | ", t2.Split('\n').Where(l => l.Contains(adminEmail)).Select(l => l.Length > 300 ? l[..300] : l))}");
        }
        Assert.True(report.Count == 0, string.Join("; ", report));

        // approvers are the organisation's own: the default organisation's access request never mails tenant B's administrator
        for (var i = 0; i < 200 && !_seed.Mail.Sent.Any(m => m.Subject.Contains("Access request waiting") && m.Text.Contains("Marker request for the isolation sweep.")); i++) await Task.Delay(25);
        Assert.Contains(_seed.Mail.Sent, m => m.Subject.Contains("Access request waiting") && m.Text.Contains("Marker request for the isolation sweep."));
        Assert.DoesNotContain(_seed.Mail.Sent, m => m.To.Equals(adminEmail, StringComparison.OrdinalIgnoreCase) && m.Subject.Contains("Access request", StringComparison.OrdinalIgnoreCase));

        // headline numbers are the organisation's own: tenant B has exactly one person
        var stats = await (await admin.Get("/api/users/stats")).Body();
        Assert.Equal(1, stats.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task System_wide_settings_belong_to_the_platform_so_another_tenants_super_admin_cannot_read_or_change_them()
    {
        var (p, _) = await Platform(); var (_, _, admin, _) = await NewTenant(p);
        Assert.Equal(HttpStatusCode.OK, (await p.Get("/api/settings")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.Get("/api/settings")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.SendAsync(HttpMethod.Patch, "/api/settings", new { passwordMinLength = 6 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.SendAsync(HttpMethod.Put, "/api/settings/email-api-key", new { value = "re_x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.SendAsync(HttpMethod.Delete, "/api/settings/email-api-key", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.Get("/api/email-deliveries")).StatusCode); // their own mail log stays theirs to read
        Assert.Equal(HttpStatusCode.OK, (await TestBrowser.Create(fx.Factory).Get("/api/settings/public")).StatusCode);
    }

    [Fact]
    public async Task A_scheduled_access_review_opens_by_itself_for_the_organisation_that_asked_for_it_and_never_stacks()
    {
        var (p, _) = await Platform(); var (id, _, admin, adminEmail) = await NewTenant(p);
        var (plain, _) = await Platform("user", reauth: false);
        Assert.Equal(HttpStatusCode.Forbidden, (await plain.Get("/api/access-reviews/schedule")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await plain.SendAsync(HttpMethod.Put, "/api/access-reviews/schedule", new { enabled = true })).StatusCode);
        Assert.False((await (await admin.Get("/api/access-reviews/schedule")).Body()).GetProperty("enabled").GetBoolean());
        Assert.Equal((HttpStatusCode)422, (await admin.SendAsync(HttpMethod.Put, "/api/access-reviews/schedule", new { enabled = true, everyDays = 45, dueInDays = 14 })).StatusCode);
        Assert.Equal((HttpStatusCode)422, (await admin.SendAsync(HttpMethod.Put, "/api/access-reviews/schedule", new { enabled = true, everyDays = 90, dueInDays = 0 })).StatusCode);
        var saved = await (await admin.SendAsync(HttpMethod.Put, "/api/access-reviews/schedule", new { enabled = true, everyDays = 30, dueInDays = 7 })).Body();
        Assert.True(saved.GetProperty("enabled").GetBoolean()); Assert.Equal(30, saved.GetProperty("everyDays").GetInt32()); Assert.True(saved.GetProperty("nextRunAt").ValueKind != JsonValueKind.Null);
        Assert.NotEmpty(await _seed.Db(db => db.AuditEvents.IgnoreQueryFilters().Where(a => a.Action == "access.review.schedule" && a.TenantId == id).ToListAsync()));
        Assert.False((await (await p.Get("/api/access-reviews/schedule")).Body()).GetProperty("enabled").GetBoolean()); // the platform's own schedule is separate

        async Task<int> RunDue() { using var scope = fx.Factory.Services.CreateScope(); return await scope.ServiceProvider.GetRequiredService<AccessReviewService>().RunDueAsync(); }
        async Task MakeDue() => await _seed.Db(async db => await db.AccessReviewSchedules.IgnoreQueryFilters().Where(x => x.TenantId == id).ExecuteUpdateAsync(u => u.SetProperty(x => x.NextRunAt, DateTimeOffset.UtcNow.AddMinutes(-5))));
        async Task<List<JsonElement>> Reviews() => (await (await admin.Get("/api/access-reviews")).Body()).EnumerateArray().ToList();

        await RunDue(); Assert.Empty(await Reviews()); // not due yet: nothing happens
        await MakeDue(); await RunDue();
        var list = await Reviews(); Assert.Single(list);
        Assert.Equal("open", list[0].GetProperty("status").GetString()); Assert.Equal("Scheduled (automatic)", list[0].GetProperty("createdByName").GetString());
        Assert.StartsWith("Scheduled access review", list[0].GetProperty("name").GetString()); Assert.True(list[0].GetProperty("total").GetInt32() >= 1);
        await _seed.Mail.WaitFor(adminEmail, "access review");
        var next = (await (await admin.Get("/api/access-reviews/schedule")).Body()).GetProperty("nextRunAt").GetDateTimeOffset();
        Assert.True(next > DateTimeOffset.UtcNow.AddDays(29)); // claimed: the next run is a period away

        await MakeDue(); await RunDue(); Assert.Single(await Reviews()); // a review is still open: no second one
        Assert.True((await (await admin.Get("/api/access-reviews/schedule")).Body()).GetProperty("nextRunAt").GetDateTimeOffset() > DateTimeOffset.UtcNow.AddDays(29));

        // a suspended organisation gets none
        await admin.Post($"/api/access-reviews/{list[0].GetProperty("id").GetGuid()}/close", new { });
        Assert.Equal(HttpStatusCode.NoContent, (await p.Post($"/api/tenants/{id}/suspend", new { })).StatusCode);
        await MakeDue(); await RunDue();
        Assert.Single(await _seed.Db(db => db.AccessReviews.IgnoreQueryFilters().Where(r => r.TenantId == id).ToListAsync()));
        await p.Post($"/api/tenants/{id}/activate", new { });
    }

    [Fact]
    public async Task Platform_support_mode_lets_a_platform_administrator_administer_one_tenant_with_a_visible_trail_and_a_time_box()
    {
        var (p, platformUser) = await Platform(); var (id, slug, admin, adminEmail) = await NewTenant(p);
        var marker = await _seed.UserAsync(email: $"{slug}@dflt-marker.test");
        async Task<List<string>> UserEmails() => Rows(await (await p.Get("/api/users?pageSize=100")).Body()).Select(r => r.GetProperty("email").GetString()!).ToList();
        async Task<JsonElement> Me() => await (await p.Get("/api/auth/me")).Body();

        // who may enter: not the tenant's own administrator, not without a fresh re-authentication, not the platform's own organisation
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.Post($"/api/tenants/{id}/support", new { })).StatusCode);
        var (stale, _) = await Platform(reauth: false);
        Assert.Equal(HttpStatusCode.Forbidden, (await stale.Post($"/api/tenants/{id}/support", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await p.Post($"/api/tenants/{Tenant.DefaultId}/support", new { })).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await p.Post($"/api/tenants/{id}/support", new { })).StatusCode);
        Assert.Equal($"Org {slug}", (await Me()).GetProperty("actingTenant").GetString());
        Assert.True((await Me()).GetProperty("isPlatform").GetBoolean()); // still the platform person: platform pages keep working
        Assert.Equal(HttpStatusCode.OK, (await p.Get("/api/tenants")).StatusCode);

        // the admin screens now show that organisation, and only it
        var inside = await UserEmails(); Assert.Contains(adminEmail, inside); Assert.DoesNotContain(marker.Email, inside);
        Assert.Equal(1, (await (await p.Get("/api/users/stats")).Body()).GetProperty("total").GetInt32());
        var roles = Rows(await (await p.Get("/api/roles")).Body()).ToList();
        var userRole = roles.First(r => r.GetProperty("key").GetString() == "user").GetProperty("id").GetGuid();
        var invitee = $"{slug}.invitee@test.local";
        Assert.Equal(HttpStatusCode.Created, (await p.Post("/api/invitations", new { email = invitee, roleIds = new[] { userRole } })).StatusCode);
        Assert.Equal(id, await _seed.Db(db => db.Invitations.IgnoreQueryFilters().Where(i => i.Email == invitee).Select(i => i.TenantId).SingleAsync()));

        // the organisation can see who did what
        var trail = await _seed.Db(db => db.AuditEvents.IgnoreQueryFilters().Where(a => a.TenantId == id && (a.Action == "tenant.support.enter" || a.Action == "invitation.create")).Select(a => new { a.Action, a.ActorLabel }).ToListAsync());
        Assert.All(trail, a => Assert.Contains("(platform support)", a.ActorLabel)); Assert.Equal(3, trail.Count); // creating the tenant (its first invitation), entering, and the invitation made while inside
        Assert.NotEmpty(await _seed.Db(db => db.SecurityEvents.IgnoreQueryFilters().Where(e => e.TenantId == id && e.Type == "support.access").ToListAsync()));
        Assert.Contains("tenant.support.enter", await (await admin.Get("/api/audit?pageSize=100")).Content.ReadAsStringAsync());
        Assert.NotEmpty(await _seed.Db(db => db.AuditEvents.IgnoreQueryFilters().Where(a => a.TenantId == Tenant.DefaultId && a.Action == "tenant.support.enter" && a.ActorId == platformUser.Id).ToListAsync())); // and the platform's own trail

        // leaving puts the screens back
        Assert.Equal(HttpStatusCode.NoContent, (await p.Post("/api/tenants/support/exit", new { })).StatusCode);
        Assert.Equal(JsonValueKind.Null, (await Me()).GetProperty("actingTenant").ValueKind);
        Assert.DoesNotContain(adminEmail, await UserEmails());
        Assert.Equal(HttpStatusCode.NoContent, (await p.Post("/api/tenants/support/exit", new { })).StatusCode); // idempotent

        // the time box
        await p.Post($"/api/tenants/{id}/support", new { });
        await _seed.Db(async db => await db.Sessions.IgnoreQueryFilters().Where(x => x.ActingTenantId == id).ExecuteUpdateAsync(u => u.SetProperty(x => x.ActingSince, DateTimeOffset.UtcNow.AddHours(-3))));
        Assert.DoesNotContain(adminEmail, await UserEmails()); Assert.Equal(JsonValueKind.Null, (await Me()).GetProperty("actingTenant").ValueKind);

        // a suspended tenant cannot be entered, and suspending it ends support mode
        await p.Post($"/api/tenants/{id}/support", new { });
        Assert.Equal(HttpStatusCode.NoContent, (await p.Post($"/api/tenants/{id}/suspend", new { })).StatusCode);
        Assert.DoesNotContain(adminEmail, await UserEmails());
        Assert.Equal(HttpStatusCode.Conflict, (await p.Post($"/api/tenants/{id}/support", new { })).StatusCode);
        await p.Post($"/api/tenants/{id}/activate", new { });
    }

    private static JsonElement Item(JsonElement view, string key) => view.GetProperty("items").EnumerateArray().First(i => i.GetProperty("key").GetString() == key);

    [Fact]
    public async Task An_organisation_can_tighten_its_security_policy_but_never_loosen_it_below_the_platforms()
    {
        var (p, _) = await Platform(); var (id, _, admin, adminEmail) = await NewTenant(p);
        var view = await (await admin.Get("/api/settings/tenant")).Body();
        var minLen = Item(view, "passwordMinLength"); var idle = Item(view, "sessionIdleMinutes"); var lockout = Item(view, "lockoutAttempts");
        var platformMin = minLen.GetProperty("platform").GetInt32(); var platformIdle = idle.GetProperty("platform").GetInt32(); var platformLock = lockout.GetProperty("platform").GetInt32();
        Assert.Equal(JsonValueKind.Null, minLen.GetProperty("own").ValueKind); Assert.Equal(platformMin, minLen.GetProperty("effective").GetInt32()); // no choice made: the platform value applies

        // weaker than the platform is refused, field by field, and nothing is saved
        var weaker = await admin.SendAsync(HttpMethod.Patch, "/api/settings/tenant", new { values = new Dictionary<string, object?> { ["passwordMinLength"] = platformMin - 1, ["sessionIdleMinutes"] = platformIdle + 1, ["mfaPolicy"] = "off", ["nope"] = 1 } });
        Assert.Equal((HttpStatusCode)422, weaker.StatusCode);
        var fields = (await weaker.Body()).GetProperty("error").GetProperty("fields");
        Assert.True(fields.TryGetProperty("passwordMinLength", out _)); Assert.True(fields.TryGetProperty("sessionIdleMinutes", out _)); Assert.True(fields.TryGetProperty("nope", out _));
        Assert.Empty(await _seed.Db(db => db.TenantSettings.IgnoreQueryFilters().Where(x => x.TenantId == id).ToListAsync()));

        // stricter is accepted and shows as the effective value
        var lock3 = Math.Min(platformLock, 3);
        var ok = await admin.SendAsync(HttpMethod.Patch, "/api/settings/tenant", new { values = new Dictionary<string, object?> { ["passwordMinLength"] = platformMin + 4, ["lockoutAttempts"] = lock3 } });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var after = await ok.Body();
        Assert.Equal(platformMin + 4, Item(after, "passwordMinLength").GetProperty("effective").GetInt32()); Assert.Equal(platformMin, Item(after, "passwordMinLength").GetProperty("platform").GetInt32());
        Assert.Equal(lock3, Item(after, "lockoutAttempts").GetProperty("effective").GetInt32());
        Assert.NotEmpty(await _seed.Db(db => db.AuditEvents.IgnoreQueryFilters().Where(a => a.TenantId == id && a.Action == "settings.tenant.update").ToListAsync()));

        // going back to the platform value removes the override
        var reset = await (await admin.SendAsync(HttpMethod.Patch, "/api/settings/tenant", new { values = new Dictionary<string, object?> { ["passwordMinLength"] = null } })).Body();
        Assert.Equal(JsonValueKind.Null, Item(reset, "passwordMinLength").GetProperty("own").ValueKind); Assert.Equal(platformMin, Item(reset, "passwordMinLength").GetProperty("effective").GetInt32());

        // it really changes behaviour: the organisation's lockout rule applies to its people, the platform's to everyone else
        if (platformLock > lock3)
        {
            var other = TestBrowser.Create(fx.Factory);
            for (var n = 0; n < lock3; n++) await other.LoginAsync(adminEmail, "wrong-password-" + n);
            Assert.Equal("locked", (await (await TestBrowser.Create(fx.Factory).LoginAsync(adminEmail, StrongPw)).Body()).GetProperty("status").GetString()); // locked after its own, lower limit
            var (_, plainDefault) = await Platform("user", reauth: false);
            for (var n = 0; n < lock3; n++) await TestBrowser.Create(fx.Factory).LoginAsync(plainDefault.Email, "wrong-password-" + n);
            Assert.Equal("ok", (await (await TestBrowser.Create(fx.Factory).LoginAsync(plainDefault.Email, Seed.Password)).Body()).GetProperty("status").GetString()); // the platform's own limit is higher
        }

        // who may use it
        var (plain, _) = await Platform("user", reauth: false);
        Assert.Equal(HttpStatusCode.Forbidden, (await plain.Get("/api/settings/tenant")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await p.Get("/api/settings/tenant")).StatusCode); // the platform organisation edits System Settings instead
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.Get("/api/settings")).StatusCode); // and the global page stays the platform's
    }

    [Fact]
    public async Task Policy_reads_never_go_below_the_platform_even_if_the_platform_tightens_later()
    {
        var platform = new SecuritySettings { PasswordMinLength = 20, SessionIdleMinutes = 10, MfaPolicy = "required", RequireBreachCheck = true, LockoutAttempts = 4 };
        var s = TenantPolicy.Apply(platform, [("passwordMinLength", "14"), ("sessionIdleMinutes", "25"), ("mfaPolicy", "\"optional\""), ("requireBreachCheck", "false"), ("lockoutAttempts", "3"), ("bogus", "1"), ("lockoutMinutes", "\"x\"")]);
        Assert.Equal(20, s.PasswordMinLength); Assert.Equal(10, s.SessionIdleMinutes); Assert.Equal("required", s.MfaPolicy); Assert.True(s.RequireBreachCheck); // an older, looser choice is clamped up
        Assert.Equal(3, s.LockoutAttempts); // a stricter one stands
        Assert.Equal(platform.LockoutMinutes, s.LockoutMinutes); // junk is ignored
        await Task.CompletedTask;
    }

    [Fact]
    public async Task A_platform_administrator_in_support_mode_edits_the_supported_organisations_policy()
    {
        var (p, _) = await Platform(); var (id, _, admin, _) = await NewTenant(p);
        await p.Post($"/api/tenants/{id}/support", new { });
        var platformMin = Item(await (await p.Get("/api/settings/tenant")).Body(), "passwordMinLength").GetProperty("platform").GetInt32();
        var res = await p.SendAsync(HttpMethod.Patch, "/api/settings/tenant", new { values = new Dictionary<string, object?> { ["passwordMinLength"] = platformMin + 2 } });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(platformMin + 2, Item(await (await admin.Get("/api/settings/tenant")).Body(), "passwordMinLength").GetProperty("effective").GetInt32()); // it is the tenant's own, not the platform's
        await p.Post("/api/tenants/support/exit", new { });
        Assert.Equal(HttpStatusCode.Conflict, (await p.Get("/api/settings/tenant")).StatusCode);
    }

    [Fact]
    public async Task The_last_active_super_admin_of_an_organisation_cannot_ask_to_close_their_account()
    {
        var (p, _) = await Platform(); var (_, _, admin, _) = await NewTenant(p);
        var res = await admin.Post("/api/me/closure", new { reason = "bye" });
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode); Assert.Contains("last active Super Admin", await res.Content.ReadAsStringAsync());
    }
}
