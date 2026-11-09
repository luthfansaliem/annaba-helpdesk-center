using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Domain.Security;
using AnnabaDigital.Tests.Infra;
using Xunit;

namespace AnnabaDigital.Tests;

[Collection("db")]
public sealed class SelfServiceTests(PostgresFixture fx)
{
    private readonly Seed _seed = new(fx.Factory, fx.Clock);
    private static string U() => Guid.NewGuid().ToString("N")[..10];
    private static async Task<JsonElement> J(HttpResponseMessage r) => await r.Body();
    private async Task<(TestBrowser b, User u)> Actor(string role = "user", bool reauth = false)
    {
        var u = await _seed.UserAsync(role: role); var b = TestBrowser.Create(fx.Factory); await b.LoginAsync(u.Email, Seed.Password);
        if (reauth) await b.Post("/api/auth/reauth", new { password = Seed.Password });
        return (b, u);
    }
    private async Task<TestBrowser> SecondSession(User u) { var b = TestBrowser.Create(fx.Factory); await b.LoginAsync(u.Email, Seed.Password); return b; }

    // ============================================================ my sessions
    [Fact]
    public async Task My_sessions_show_labels_mark_the_current_one_and_never_expose_tokens_or_other_peoples_sessions()
    {
        var (b1, u) = await Actor(); var b2 = await SecondSession(u); await Actor(); // someone else's session must not appear
        var list = await J(await b1.Get("/api/sessions/me"));
        Assert.Equal(2, list.GetArrayLength());
        Assert.Equal(1, list.EnumerateArray().Count(x => x.GetProperty("current").GetBoolean()));
        var raw = list.GetRawText(); Assert.DoesNotContain(b1.CookieValue!, raw); Assert.DoesNotContain("tokenHash", raw, StringComparison.OrdinalIgnoreCase);
        Assert.All(list.EnumerateArray(), x => { Assert.Equal("Mac", x.GetProperty("device").GetString()); Assert.Equal("Chrome", x.GetProperty("browser").GetString()); });
        Assert.Equal(HttpStatusCode.Unauthorized, (await TestBrowser.Create(fx.Factory).Get("/api/sessions/me")).StatusCode);
    }

    [Fact]
    public async Task Revoking_one_of_my_sessions_ends_it_and_revoking_the_current_one_signs_me_out()
    {
        var (b1, u) = await Actor(); var b2 = await SecondSession(u);
        var other = (await J(await b1.Get("/api/sessions/me"))).EnumerateArray().Single(x => !x.GetProperty("current").GetBoolean()).GetProperty("id").GetGuid();
        var r = await J(await b1.SendAsync(HttpMethod.Delete, $"/api/sessions/me/{other}"));
        Assert.Equal(1, r.GetProperty("revoked").GetInt32()); Assert.False(r.GetProperty("signedOut").GetBoolean());
        Assert.Equal(HttpStatusCode.Unauthorized, (await b2.Get("/api/auth/me")).StatusCode); Assert.Equal(HttpStatusCode.OK, (await b1.Get("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b1.SendAsync(HttpMethod.Delete, $"/api/sessions/me/{other}")).StatusCode); // already gone

        var cur = (await J(await b1.Get("/api/sessions/me"))).EnumerateArray().Single().GetProperty("id").GetGuid();
        var gone = await b1.SendAsync(HttpMethod.Delete, $"/api/sessions/me/{cur}");
        Assert.True((await J(gone)).GetProperty("signedOut").GetBoolean()); Assert.Contains("sid=;", gone.Headers.GetValues("Set-Cookie").First().ToLowerInvariant().Replace(" ", "").Replace("sid=;", "sid=;")); b1.CookieValue = "x";
    }

    [Fact]
    public async Task A_user_cannot_revoke_someone_elses_session_and_gets_the_same_answer_as_for_an_unknown_id()
    {
        var (a, ua) = await Actor(); var (b, ub) = await Actor();
        var theirs = (await J(await b.Get("/api/sessions/me"))).EnumerateArray().Single().GetProperty("id").GetGuid();
        var r = await a.SendAsync(HttpMethod.Delete, $"/api/sessions/me/{theirs}");
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await b.Get("/api/auth/me")).StatusCode); // untouched
        Assert.Equal(HttpStatusCode.NotFound, (await a.SendAsync(HttpMethod.Delete, $"/api/sessions/me/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task Revoke_all_other_sessions_keeps_this_one_and_sign_out_everywhere_ends_all()
    {
        var (b1, u) = await Actor(); var b2 = await SecondSession(u); var b3 = await SecondSession(u);
        var r = await J(await b1.SendAsync(HttpMethod.Delete, "/api/sessions/me?except=current"));
        Assert.Equal(2, r.GetProperty("revoked").GetInt32());
        Assert.Equal(HttpStatusCode.OK, (await b1.Get("/api/auth/me")).StatusCode); Assert.Equal(HttpStatusCode.Unauthorized, (await b2.Get("/api/auth/me")).StatusCode); Assert.Equal(HttpStatusCode.Unauthorized, (await b3.Get("/api/auth/me")).StatusCode);
        var all = await J(await b1.SendAsync(HttpMethod.Delete, "/api/sessions/me"));
        Assert.True(all.GetProperty("signedOut").GetBoolean()); Assert.Equal(1, all.GetProperty("revoked").GetInt32());
        Assert.Empty(await _seed.Db(db => db.Sessions.Where(s => s.UserId == u.Id && s.RevokedAt == null).ToListAsync()));
        Assert.True(await _seed.Db(db => db.AuditEvents.AnyAsync(a => a.ActorId == u.Id && a.Action == "session.revoke_all")));
    }

    [Fact]
    public async Task Session_revocation_endpoints_need_csrf()
    {
        var (b, _) = await Actor(); b.CsrfToken = null;
        Assert.Equal(HttpStatusCode.Forbidden, (await b.SendAsync(HttpMethod.Delete, "/api/sessions/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await b.SendAsync(HttpMethod.Delete, "/api/sessions/me?except=current")).StatusCode);
    }

    [Fact]
    public async Task All_sessions_is_for_sessions_read_all_is_paged_filterable_and_token_free()
    {
        var target = await _seed.UserAsync(); var t1 = await SecondSession(target); var t2 = await SecondSession(target);
        var (aud, _) = await Actor("security_auditor");
        var page = await J(await aud.Get($"/api/sessions?userId={target.Id}&pageSize=1"));
        Assert.Equal(2, page.GetProperty("total").GetInt32()); Assert.Equal(1, page.GetProperty("data").GetArrayLength()); Assert.Equal(target.Email, page.GetProperty("data")[0].GetProperty("userEmail").GetString());
        Assert.DoesNotContain(t1.CookieValue!, page.GetRawText());
        Assert.Equal(HttpStatusCode.Forbidden, (await (await Actor("user")).b.Get("/api/sessions")).StatusCode);
        Assert.Equal(100, (await J(await aud.Get("/api/sessions?pageSize=9999"))).GetProperty("pageSize").GetInt32());
    }

    // ============================================================ audit log
    [Fact]
    public async Task The_audit_log_is_filterable_paged_newest_first_and_needs_audit_read()
    {
        var u = await _seed.UserAsync(); var tag = $"tag{U()}";
        var (b, _) = await Actor("security_auditor");
        for (var i = 0; i < 3; i++) await TestBrowser.Create(fx.Factory).LoginAsync(u.Email, "wrong-" + i);
        await TestBrowser.Create(fx.Factory).LoginAsync(u.Email, Seed.Password);
        var failures = await J(await b.Get($"/api/audit?q={u.Email}&outcome=failure&pageSize=2"));
        Assert.Equal(3, failures.GetProperty("total").GetInt32()); Assert.Equal(2, failures.GetProperty("data").GetArrayLength());
        var ts = failures.GetProperty("data").EnumerateArray().Select(x => x.GetProperty("ts").GetDateTimeOffset()).ToList(); Assert.True(ts[0] >= ts[1]);
        Assert.All(failures.GetProperty("data").EnumerateArray(), x => { Assert.Equal("failure", x.GetProperty("outcome").GetString()); Assert.Equal("auth.login.failure", x.GetProperty("action").GetString()); });
        Assert.Equal(1, (await J(await b.Get($"/api/audit?q={u.Email}&category=auth&outcome=success"))).GetProperty("total").GetInt32());
        Assert.Equal(0, (await J(await b.Get($"/api/audit?q={u.Email}&category=user"))).GetProperty("total").GetInt32());
        Assert.Equal(HttpStatusCode.OK, (await b.Get($"/api/audit?actorId={u.Id}&from={Uri.EscapeDataString(fx.Clock.UtcNow.AddDays(-1).ToString("O"))}&to={Uri.EscapeDataString(fx.Clock.UtcNow.AddDays(1).ToString("O"))}")).StatusCode);
        Assert.Equal((HttpStatusCode)422, (await b.Get("/api/audit?outcome=banana")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await Actor("user_admin")).b.Get("/api/audit")).StatusCode);
        Assert.Equal(0, (await J(await b.Get("/api/audit?q=%25"))).GetProperty("total").GetInt32()); // wildcard-safe
    }

    [Fact]
    public async Task Audit_detail_shows_metadata_and_diff_but_never_secrets()
    {
        var (admin, _) = await Actor("super_admin", reauth: true); var target = await _seed.UserAsync();
        await admin.SendAsync(HttpMethod.Patch, $"/api/users/{target.Id}", new { name = "Audited Person" });
        var (aud, _) = await Actor("security_auditor");
        var list = await J(await aud.Get($"/api/audit?q={target.Id}&category=user"));
        var id = list.GetProperty("data")[0].GetProperty("id").GetInt64();
        var d = await J(await aud.Get($"/api/audit/{id}"));
        Assert.Equal("user.update", d.GetProperty("action").GetString());
        Assert.Contains(d.GetProperty("diff").EnumerateArray(), x => x.GetProperty("Field").GetString() == "name" && x.GetProperty("After").GetString() == "Audited Person");
        Assert.False(string.IsNullOrEmpty(d.GetProperty("requestId").GetString())); Assert.False(string.IsNullOrEmpty(d.GetProperty("ip").GetString()));
        Assert.DoesNotContain(Seed.Password, d.GetRawText());
        Assert.Equal(HttpStatusCode.NotFound, (await aud.Get("/api/audit/999999999")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await aud.Get("/api/audit/abc")).StatusCode);
    }

    [Fact]
    public async Task The_audit_log_has_no_write_endpoints()
    {
        var (aud, _) = await Actor("super_admin");
        foreach (var m in new[] { HttpMethod.Post, HttpMethod.Put, HttpMethod.Patch, HttpMethod.Delete })
        {
            Assert.True((await aud.SendAsync(m, "/api/audit", new { })).StatusCode is HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotFound);
            Assert.True((await aud.SendAsync(m, "/api/audit/1", new { })).StatusCode is HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotFound);
        }
    }

    // ============================================================ security events
    [Fact]
    public async Task Security_events_filter_by_severity_and_acknowledgement_and_acknowledging_is_audited_and_single_shot()
    {
        var u = await _seed.UserAsync(); for (var i = 0; i < 5; i++) await TestBrowser.Create(fx.Factory).LoginAsync(u.Email, "wrong-" + i); // → login.failed (low) ×5 + account.locked (high)
        var (aud, actor) = await Actor("security_auditor");
        var high = await J(await aud.Get("/api/security-events?severity=high&acknowledged=false&pageSize=100"));
        var ev = high.GetProperty("data").EnumerateArray().First(x => x.GetProperty("userId").GetGuid() == u.Id);
        Assert.Equal("account.locked", ev.GetProperty("type").GetString()); Assert.Equal("high", ev.GetProperty("severity").GetString()); Assert.False(ev.GetProperty("acknowledged").GetBoolean());
        var id = ev.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await aud.Post($"/api/security-events/{id}/ack")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await aud.Post($"/api/security-events/{id}/ack")).StatusCode);
        var acked = (await J(await aud.Get("/api/security-events?acknowledged=true&pageSize=100"))).GetProperty("data").EnumerateArray().First(x => x.GetProperty("id").GetGuid() == id);
        Assert.Equal(actor.Name, acked.GetProperty("acknowledgedBy").GetString());
        Assert.True(await _seed.Db(db => db.AuditEvents.AnyAsync(a => a.Action == "security.ack" && a.ActorId == actor.Id && a.TargetId == id.ToString())));
        Assert.Equal(HttpStatusCode.NotFound, (await aud.Post($"/api/security-events/{Guid.NewGuid()}/ack")).StatusCode);
        Assert.Equal((HttpStatusCode)422, (await aud.Get("/api/security-events?severity=extreme")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await Actor("user_admin")).b.Get("/api/security-events")).StatusCode);
    }

    [Fact]
    public async Task Two_reviewers_acknowledging_the_same_event_concurrently_produce_one_success()
    {
        var u = await _seed.UserAsync(); for (var i = 0; i < 5; i++) await TestBrowser.Create(fx.Factory).LoginAsync(u.Email, "wrong-" + i);
        var (a, _) = await Actor("security_auditor"); var (b, _) = await Actor("security_auditor");
        var id = (await J(await a.Get("/api/security-events?severity=high&acknowledged=false&pageSize=100"))).GetProperty("data").EnumerateArray().First(x => x.GetProperty("userId").GetGuid() == u.Id).GetProperty("id").GetGuid();
        var res = await Task.WhenAll(a.Post($"/api/security-events/{id}/ack"), b.Post($"/api/security-events/{id}/ack"));
        Assert.Equal(1, res.Count(r => r.StatusCode == HttpStatusCode.NoContent)); Assert.Equal(1, res.Count(r => r.StatusCode == HttpStatusCode.Conflict));
    }

    // ============================================================ dashboards
    [Fact]
    public async Task Admin_dashboard_numbers_equal_the_database_for_a_super_admin()
    {
        var (b, _) = await Actor("super_admin"); await _seed.UserAsync(status: UserStatus.Suspended);
        var d = await J(await b.Get("/api/dashboard/admin")); var k = d.GetProperty("kpis");
        Assert.Equal(await _seed.Db(db => db.Users.CountAsync(u => u.TenantId == Tenant.DefaultId)), k.GetProperty("totalUsers").GetInt32());
        Assert.Equal(await _seed.Db(db => db.Users.CountAsync(u => u.TenantId == Tenant.DefaultId && u.Status == UserStatus.Active)), k.GetProperty("activeUsers").GetInt32());
        Assert.Equal(await _seed.Db(db => db.Users.CountAsync(u => u.TenantId == Tenant.DefaultId && u.Status == UserStatus.Suspended)), k.GetProperty("suspendedUsers").GetInt32());
        Assert.Equal(await _seed.Db(db => db.Roles.CountAsync(r => r.TenantId == Tenant.DefaultId && r.ArchivedAt == null)), k.GetProperty("totalRoles").GetInt32());
        var now = fx.Clock.UtcNow;
        Assert.Equal(await _seed.Db(db => db.Sessions.CountAsync(s => s.TenantId == Tenant.DefaultId && s.RevokedAt == null && s.IdleExpiresAt > now && s.AbsoluteExpiresAt > now)), k.GetProperty("activeSessions").GetInt32());
        Assert.Equal(await _seed.Db(db => db.Invitations.CountAsync(i => i.TenantId == Tenant.DefaultId && i.Status == InvitationStatus.Pending && i.ExpiresAt > now)), k.GetProperty("pendingInvitations").GetInt32());
        Assert.Equal(12, d.GetProperty("userGrowth").GetArrayLength()); Assert.Equal(k.GetProperty("totalUsers").GetInt32(), d.GetProperty("userGrowth")[11].GetInt32());
        Assert.Equal(k.GetProperty("totalUsers").GetInt32(), d.GetProperty("userStatus").EnumerateArray().Sum(x => x.GetProperty("count").GetInt32()));
        var login = d.GetProperty("loginActivity"); Assert.Equal(14, login.GetProperty("days").GetArrayLength());
        Assert.True(login.GetProperty("success").EnumerateArray().Sum(x => x.GetInt32()) > 0); // the actors above just signed in
        Assert.Equal(5, d.GetProperty("recentRegistrations").GetArrayLength());
        foreach (var panel in new[] { "recentLogins", "recentRoleChanges", "recentSecurityEvents", "pendingInvitations" }) Assert.True(d.TryGetProperty(panel, out _), panel);
        Assert.Contains("add_user", d.GetProperty("quickActions").EnumerateArray().Select(x => x.GetString())); Assert.Contains("system_settings", d.GetProperty("quickActions").EnumerateArray().Select(x => x.GetString()));
    }

    [Fact]
    public async Task Each_widget_is_present_only_if_the_caller_holds_the_permission_behind_it()
    {
        var (aud, _) = await Actor("security_auditor"); var a = await J(await aud.Get("/api/dashboard/admin"));
        Assert.True(a.GetProperty("kpis").TryGetProperty("totalUsers", out _)); Assert.True(a.GetProperty("kpis").TryGetProperty("activeSessions", out _)); Assert.True(a.TryGetProperty("loginActivity", out _)); Assert.True(a.TryGetProperty("recentSecurityEvents", out _));
        Assert.False(a.GetProperty("kpis").TryGetProperty("pendingInvitations", out _)); Assert.False(a.TryGetProperty("pendingInvitations", out _)); // auditors hold no invitations.read

        var (ua, _) = await Actor("user_admin"); var u = await J(await ua.Get("/api/dashboard/admin"));
        Assert.True(u.GetProperty("kpis").TryGetProperty("pendingInvitations", out _)); Assert.True(u.GetProperty("kpis").TryGetProperty("totalRoles", out _));
        Assert.False(u.TryGetProperty("loginActivity", out _)); Assert.False(u.TryGetProperty("recentLogins", out _)); Assert.False(u.TryGetProperty("recentSecurityEvents", out _)); // no audit/security permission

        var (plain, _) = await Actor("user"); var p = await J(await plain.Get("/api/dashboard/admin"));
        Assert.Empty(p.GetProperty("kpis").EnumerateObject()); Assert.Equal(0, p.GetProperty("visible").GetArrayLength()); Assert.Equal(0, p.GetProperty("quickActions").GetArrayLength());
        Assert.False(p.TryGetProperty("userGrowth", out _)); Assert.False(p.TryGetProperty("recentRegistrations", out _));
        Assert.Equal(HttpStatusCode.Unauthorized, (await TestBrowser.Create(fx.Factory).Get("/api/dashboard/admin")).StatusCode);
    }

    [Fact]
    public async Task A_users_own_dashboard_has_only_their_data()
    {
        var (b, u) = await Actor("user"); await SecondSession(u);
        var d = await J(await b.Get("/api/dashboard/me"));
        Assert.Equal(u.Email, d.GetProperty("email").GetString()); Assert.Equal("active", d.GetProperty("status").GetString()); Assert.Equal(0, d.GetProperty("permissionCount").GetInt32());
        Assert.Equal(2, d.GetProperty("activeSessions").GetInt32()); Assert.False(d.GetProperty("mfaEnabled").GetBoolean()); Assert.Equal("user", d.GetProperty("roles")[0].GetProperty("key").GetString());
        Assert.All(d.GetProperty("recentActivity").EnumerateArray(), x => Assert.True(x.TryGetProperty("action", out _)));
        var c = d.GetProperty("profileCompletion"); Assert.InRange(c.GetProperty("percent").GetInt32(), 1, 99); Assert.Contains("phone", c.GetProperty("missing").EnumerateArray().Select(x => x.GetString())); Assert.Contains("mfa", c.GetProperty("missing").EnumerateArray().Select(x => x.GetString()));
        Assert.DoesNotContain("/users/all", d.GetProperty("quickLinks").EnumerateArray().Select(x => x.GetString()));
        var raw = d.GetRawText(); Assert.DoesNotContain("totalUsers", raw); // nothing organisation-wide
    }

    // ============================================================ retention
    [Fact]
    public async Task Retention_purges_old_evidence_only_via_the_maintenance_context_keeps_recent_rows_and_records_itself()
    {
        var iso = await fx.NewIsolatedAsync(); var seed = new Seed(iso.Factory, iso.Clock);
        var u = await seed.UserAsync();
        await seed.Db(async db =>
        {
            var now = iso.Clock.UtcNow;
            db.AuditEvents.Add(new AuditEvent { Ts = now.AddDays(-400), Action = "old.event", TargetType = "t", ActorLabel = "x", Outcome = Outcome.Success });
            db.AuditEvents.Add(new AuditEvent { Ts = now.AddDays(-10), Action = "recent.event", TargetType = "t", ActorLabel = "x", Outcome = Outcome.Success });
            db.SecurityEvents.Add(new SecurityEvent { Ts = now.AddDays(-400), Type = "old", Severity = Severity.Low, Detail = "old" }); db.SecurityEvents.Add(new SecurityEvent { Ts = now.AddDays(-5), Type = "recent", Severity = Severity.Low, Detail = "recent" });
            db.EmailVerificationTokens.Add(new EmailVerificationToken { UserId = u.Id, TokenHash = "t-old", ExpiresAt = now.AddDays(-20), CreatedAt = now.AddDays(-21) }); db.EmailVerificationTokens.Add(new EmailVerificationToken { UserId = u.Id, TokenHash = "t-new", ExpiresAt = now.AddHours(5), CreatedAt = now });
            db.PasswordResetTokens.Add(new PasswordResetToken { UserId = u.Id, TokenHash = "p-old", ExpiresAt = now.AddDays(-20), CreatedAt = now.AddDays(-21) });
            db.RateLimitBuckets.Add(new RateLimitBucket { Key = "old-bucket", Count = 1, WindowStart = now.AddDays(-3) }); db.RateLimitBuckets.Add(new RateLimitBucket { Key = "new-bucket", Count = 1, WindowStart = now });
            db.Sessions.Add(new Session { TokenHash = "s-old", UserId = u.Id, CreatedAt = now.AddDays(-60), LastActiveAt = now.AddDays(-60), IdleExpiresAt = now.AddDays(-59), AbsoluteExpiresAt = now.AddDays(-59), RevokedAt = now.AddDays(-45) });
            return await db.SaveChangesAsync();
        });
        var r = await iso.Factory.Services.CreateScope().ServiceProvider.GetRequiredService<AnnabaDigital.Application.Monitoring.MonitoringService>().PurgeAsync();
        Assert.True(r.Audit >= 1); Assert.Equal(1, r.Security); Assert.Equal(2, r.Tokens); Assert.Equal(1, r.Sessions); Assert.Equal(1, r.RateBuckets);
        await seed.Db(async db =>
        {
            Assert.False(await db.AuditEvents.AnyAsync(a => a.Action == "old.event")); Assert.True(await db.AuditEvents.AnyAsync(a => a.Action == "recent.event"));
            Assert.False(await db.SecurityEvents.AnyAsync(e => e.Type == "old")); Assert.True(await db.SecurityEvents.AnyAsync(e => e.Type == "recent"));
            Assert.False(await db.EmailVerificationTokens.AnyAsync(t => t.TokenHash == "t-old")); Assert.True(await db.EmailVerificationTokens.AnyAsync(t => t.TokenHash == "t-new"));
            Assert.False(await db.RateLimitBuckets.AnyAsync(b => b.Key == "old-bucket")); Assert.True(await db.RateLimitBuckets.AnyAsync(b => b.Key == "new-bucket"));
            var log = await db.AuditEvents.SingleAsync(a => a.Action == "retention.purge"); Assert.Contains("\"audit\"", log.MetaJson); return 0;
        });
        // the append-only guarantee still holds outside the purge: ordinary deletes remain impossible
        await Assert.ThrowsAnyAsync<Exception>(() => seed.Db(db => db.Database.ExecuteSqlRawAsync("DELETE FROM audit_events WHERE action = 'recent.event'")));
    }

    [Fact]
    public async Task Retention_honours_the_configured_window()
    {
        var iso = await fx.NewIsolatedAsync(); var seed = new Seed(iso.Factory, iso.Clock);
        await seed.Db(async db => { var r = await db.Settings.SingleAsync(s => s.Key == "auditRetentionDays"); r.ValueJson = "60"; db.AuditEvents.Add(new AuditEvent { Ts = iso.Clock.UtcNow.AddDays(-90), Action = "ninety", TargetType = "t", ActorLabel = "x", Outcome = Outcome.Success }); db.AuditEvents.Add(new AuditEvent { Ts = iso.Clock.UtcNow.AddDays(-30), Action = "thirty", TargetType = "t", ActorLabel = "x", Outcome = Outcome.Success }); return await db.SaveChangesAsync(); });
        await iso.Factory.Services.CreateScope().ServiceProvider.GetRequiredService<AnnabaDigital.Application.Monitoring.MonitoringService>().PurgeAsync();
        await seed.Db(async db => { Assert.False(await db.AuditEvents.AnyAsync(a => a.Action == "ninety")); Assert.True(await db.AuditEvents.AnyAsync(a => a.Action == "thirty")); return 0; });
    }
}
