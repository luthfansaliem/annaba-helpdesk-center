using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Domain.Security;
using AnnabaDigital.Tests.Infra;
using Xunit;

namespace AnnabaDigital.Tests;

[Collection("db")]
public sealed class UserAdminTests(PostgresFixture fx)
{
    private readonly Seed _seed = new(fx.Factory, fx.Clock);
    private const string StrongPw = "Cobalt-Meadow-Harvest-58";
    private static string Mail() => $"m{Guid.NewGuid():N}@test.local";

    private async Task<(TestBrowser b, User u)> Actor(string role = "user_admin", bool reauth = true)
    {
        var u = await _seed.UserAsync(role: role); var b = TestBrowser.Create(fx.Factory);
        await b.LoginAsync(u.Email, Seed.Password);
        if (reauth) await b.Post("/api/auth/reauth", new { password = Seed.Password });
        return (b, u);
    }
    private static async Task<JsonElement> J(HttpResponseMessage r) => await r.Body();
    private async Task<Guid[]> Roles(params string[] keys) { var l = new List<Guid>(); foreach (var k in keys) l.Add(await _seed.RoleId(k)); return [.. l]; }
    private async Task<HttpResponseMessage> CreateUser(TestBrowser b, string email, params string[] roles) => await b.Post("/api/users", new { name = "Created Person", email, roleIds = await Roles(roles.Length == 0 ? ["user"] : roles), mode = "invite" });

    // ============================================================ reads
    [Fact]
    public async Task User_list_requires_users_read_and_returns_paged_filtered_sorted_data()
    {
        var tag = $"zq{Guid.NewGuid():N}"[..10];
        foreach (var (n, st) in new[] { ($"{tag}-bravo", UserStatus.Active), ($"{tag}-alpha", UserStatus.Suspended), ($"{tag}-charlie", UserStatus.Active) }) await _seed.UserAsync(email: $"{n}@test.local", status: st);
        var (anon, _) = await Actor("user", reauth: false);
        Assert.Equal(HttpStatusCode.Forbidden, (await anon.Get("/api/users")).StatusCode);

        var (b, _) = await Actor("security_auditor", reauth: false);
        var all = await J(await b.Get($"/api/users?q={tag}&sort=email&dir=asc&pageSize=2"));
        Assert.Equal(3, all.GetProperty("total").GetInt32()); Assert.Equal(2, all.GetProperty("data").GetArrayLength()); Assert.Equal(2, all.GetProperty("pageSize").GetInt32());
        Assert.StartsWith($"{tag}-alpha", all.GetProperty("data")[0].GetProperty("email").GetString());
        var p2 = await J(await b.Get($"/api/users?q={tag}&sort=email&dir=asc&pageSize=2&page=2"));
        Assert.Single(p2.GetProperty("data").EnumerateArray()); Assert.StartsWith($"{tag}-charlie", p2.GetProperty("data")[0].GetProperty("email").GetString());
        var desc = await J(await b.Get($"/api/users?q={tag}&sort=email&dir=desc"));
        Assert.StartsWith($"{tag}-charlie", desc.GetProperty("data")[0].GetProperty("email").GetString());
        var susp = await J(await b.Get($"/api/users?q={tag}&status=suspended"));
        Assert.Equal(1, susp.GetProperty("total").GetInt32()); Assert.Equal("suspended", susp.GetProperty("data")[0].GetProperty("status").GetString());
        var big = await J(await b.Get($"/api/users?pageSize=100000")); Assert.Equal(100, big.GetProperty("pageSize").GetInt32()); // clamped
        Assert.Equal((HttpStatusCode)422, (await b.Get("/api/users?status=banana")).StatusCode);
        var item = all.GetProperty("data")[0]; Assert.False(item.TryGetProperty("passwordHash", out _)); Assert.False(item.TryGetProperty("credential", out _));
    }

    [Fact]
    public async Task Search_is_case_insensitive_matches_department_and_treats_wildcards_literally()
    {
        var tag = $"Zx{Guid.NewGuid():N}"[..8];
        var u = await _seed.UserAsync(email: $"dept{Guid.NewGuid():N}@test.local");
        await _seed.Db(async db => { (await db.Profiles.SingleAsync(p => p.UserId == u.Id)).Department = $"Dept-{tag}"; return await db.SaveChangesAsync(); });
        var (b, _) = await Actor("user_admin", reauth: false);
        Assert.Equal(1, (await J(await b.Get($"/api/users?q={tag.ToLowerInvariant()}"))).GetProperty("total").GetInt32());
        Assert.Equal(1, (await J(await b.Get($"/api/users?q={tag.ToUpperInvariant()}"))).GetProperty("total").GetInt32());
        Assert.Equal(0, (await J(await b.Get("/api/users?q=%25"))).GetProperty("total").GetInt32()); // "%" is not a wildcard
        Assert.Equal(0, (await J(await b.Get("/api/users?q=_%27%3B%20DROP%20TABLE%20users%3B--"))).GetProperty("total").GetInt32()); // injection text is just text
    }

    [Fact]
    public async Task Filters_by_role_and_verification_and_exposes_roles_mfa_and_lock_flags()
    {
        var aud = await _seed.UserAsync(role: "security_auditor"); var unver = await _seed.UserAsync(status: UserStatus.Pending);
        var (b, _) = await Actor("user_admin", reauth: false);
        var byRole = await J(await b.Get("/api/users?role=security_auditor&pageSize=100"));
        Assert.Contains(byRole.GetProperty("data").EnumerateArray(), x => x.GetProperty("email").GetString() == aud.Email);
        Assert.All(byRole.GetProperty("data").EnumerateArray(), x => Assert.Contains("security_auditor", x.GetProperty("roles").EnumerateArray().Select(r => r.GetProperty("key").GetString())));
        var unverified = await J(await b.Get("/api/users?verified=false&pageSize=100"));
        Assert.Contains(unverified.GetProperty("data").EnumerateArray(), x => x.GetProperty("email").GetString() == unver.Email);
        Assert.All(unverified.GetProperty("data").EnumerateArray(), x => Assert.False(x.GetProperty("verified").GetBoolean()));
    }

    [Fact]
    public async Task User_detail_has_roles_effective_permissions_and_version_and_unknown_ids_404()
    {
        var target = await _seed.UserAsync(role: "security_auditor");
        var (b, _) = await Actor("user_admin", reauth: false);
        var d = await J(await b.Get($"/api/users/{target.Id}"));
        Assert.Equal(target.Email, d.GetProperty("email").GetString()); Assert.Equal(1, d.GetProperty("version").GetInt32());
        Assert.Contains("audit.read", d.GetProperty("effectivePermissions").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(HttpStatusCode.NotFound, (await b.Get($"/api/users/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.Get("/api/users/not-a-guid")).StatusCode);
    }

    [Fact]
    public async Task Stats_match_the_database()
    {
        var (b, _) = await Actor("security_auditor", reauth: false);
        var s = await J(await b.Get("/api/users/stats"));
        var total = await _seed.Db(db => db.Users.CountAsync());
        Assert.Equal(total, s.GetProperty("total").GetInt32());
        Assert.Equal(total, s.GetProperty("byStatus").EnumerateArray().Sum(x => x.GetProperty("count").GetInt32()));
        Assert.Equal(12, s.GetProperty("growthLast12Months").GetArrayLength()); Assert.Equal(total, s.GetProperty("growthLast12Months")[11].GetInt32());
        Assert.Equal(5, s.GetProperty("recentRegistrations").GetArrayLength());
    }

    // ============================================================ create
    [Fact]
    public async Task Creating_a_user_makes_a_credential_less_pending_placeholder_and_emails_a_working_invitation()
    {
        var (b, admin) = await Actor("super_admin"); // a User Admin cannot hand out the Auditor role (see the escalation test)
        var email = Mail().ToUpperInvariant();
        var res = await CreateUser(b, email, "security_auditor");
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var d = await J(res);
        Assert.Equal("pending", d.GetProperty("status").GetString()); Assert.False(d.GetProperty("verified").GetBoolean());
        Assert.Empty(d.GetProperty("effectivePermissions").EnumerateArray()); // a pending user holds nothing
        Assert.Equal("Security Auditor", d.GetProperty("roles")[0].GetProperty("name").GetString());

        var lower = email.ToLowerInvariant();
        Assert.False(await _seed.Db(db => db.Credentials.AnyAsync(c => db.Users.Any(u => u.Id == c.UserId && u.Email == lower)))); // the admin never sets a password
        var mail = await _seed.Mail.WaitFor(lower, "invited");
        var raw = InMemoryEmailSender.TokenIn(mail);
        Assert.StartsWith("http://localhost:3000/invite?token=", mail.Text.Split("Accept invitation: ")[1]);
        var inv = await _seed.Db(db => db.Invitations.SingleAsync(i => i.Email == lower));
        Assert.NotEqual(raw, inv.TokenHash); Assert.Equal(64, inv.TokenHash.Length); Assert.Equal(admin.Id, inv.InvitedBy); Assert.Equal(7, Math.Round((inv.ExpiresAt - inv.CreatedAt).TotalDays));

        // the person accepts → account becomes active with exactly the intended roles and can sign in
        var accept = await TestBrowser.Create(fx.Factory).Post("/api/invitations/accept", new { token = raw, name = "Created Person", password = StrongPw }, csrf: false);
        Assert.Equal(HttpStatusCode.OK, accept.StatusCode);
        var after = await J(await b.Get($"/api/users/{d.GetProperty("id").GetGuid()}"));
        Assert.Equal("active", after.GetProperty("status").GetString()); Assert.Contains("audit.read", after.GetProperty("effectivePermissions").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(HttpStatusCode.OK, (await TestBrowser.Create(fx.Factory).LoginAsync(lower, StrongPw)).StatusCode);
        Assert.NotEmpty(await _seed.Db(db => db.AuditEvents.Where(a => a.Action == "user.create" && a.ActorId == admin.Id).ToListAsync()));
    }

    [Fact]
    public async Task Creating_a_user_needs_a_recent_reauth_unless_the_policy_is_turned_off()
    {
        var (b, _) = await Actor(reauth: false);
        var denied = await CreateUser(b, Mail());
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode); Assert.Equal("reauth_required", await denied.ErrorCode());
        await using (await _seed.SettingAsync("requireReauthForCritical", "false")) Assert.Equal(HttpStatusCode.Created, (await CreateUser(b, Mail())).StatusCode);
        await b.Post("/api/auth/reauth", new { password = Seed.Password });
        Assert.Equal(HttpStatusCode.Created, (await CreateUser(b, Mail())).StatusCode);
        fx.Clock.Advance(TimeSpan.FromMinutes(6)); // window elapsed
        Assert.Equal("reauth_required", await (await CreateUser(b, Mail())).ErrorCode());
    }

    [Fact]
    public async Task Creating_a_user_needs_both_users_create_and_users_assign_role()
    {
        await _seed.RoleAsync("only_create", Permissions.UsersCreate, Permissions.UsersRead);
        await _seed.RoleAsync("only_assign", Permissions.UsersAssignRole, Permissions.UsersRead);
        foreach (var role in new[] { "only_create", "only_assign", "security_auditor" })
        {
            var (b, _) = await Actor(role);
            Assert.Equal(HttpStatusCode.Forbidden, (await CreateUser(b, Mail())).StatusCode);
        }
    }

    [Fact]
    public async Task Privilege_escalation_through_user_creation_is_blocked_and_audited_and_creates_nothing()
    {
        var (b, admin) = await Actor(); // User Admin: lacks permissions.* / audit.* / settings.*
        var before = await _seed.Db(db => db.Users.CountAsync());
        foreach (var role in new[] { "super_admin", "security_auditor" })
        {
            var email = Mail(); var res = await CreateUser(b, email, role);
            Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode); Assert.Equal("denied", await res.ErrorCode());
            Assert.False(await _seed.Db(db => db.Users.AnyAsync(u => u.Email == email))); Assert.False(await _seed.Db(db => db.Invitations.AnyAsync(i => i.Email == email)));
        }
        Assert.Equal(before, await _seed.Db(db => db.Users.CountAsync()));
        Assert.True(await _seed.Db(db => db.AuditEvents.CountAsync(a => a.ActorId == admin.Id && a.Action == "user.create" && a.Outcome == Outcome.Denied)) >= 2);
        // a mixed list is rejected as a whole (no partial grant)
        Assert.Equal(HttpStatusCode.Forbidden, (await CreateUser(b, Mail(), "user", "super_admin")).StatusCode);
    }

    [Fact]
    public async Task Duplicate_emails_and_bad_input_are_rejected_with_clear_errors()
    {
        var (b, _) = await Actor(); var existing = await _seed.UserAsync();
        var dup = await CreateUser(b, existing.Email.ToUpperInvariant());
        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);
        var email = Mail(); Assert.Equal(HttpStatusCode.Created, (await CreateUser(b, email)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await CreateUser(b, email)).StatusCode);

        var bad = await b.Post("/api/users", new { name = "", email = "nope", roleIds = Array.Empty<Guid>(), mode = "active" });
        Assert.Equal((HttpStatusCode)422, bad.StatusCode);
        var f = (await J(bad)).GetProperty("error").GetProperty("fields");
        Assert.True(f.TryGetProperty("name", out _)); Assert.True(f.TryGetProperty("email", out _)); Assert.True(f.TryGetProperty("mode", out _));
        Assert.Equal((HttpStatusCode)422, (await b.Post("/api/users", new { name = "Valid Name", email = Mail(), roleIds = new[] { Guid.NewGuid() } })).StatusCode); // unknown role
        Assert.Equal((HttpStatusCode)422, (await b.Post("/api/users", new { name = "Valid Name", email = Mail(), roleIds = await Roles("user"), phone = new string('9', 200) })).StatusCode);
    }

    [Fact]
    public async Task Concurrent_creation_of_the_same_email_yields_exactly_one_user()
    {
        var email = Mail(); var actors = new List<TestBrowser>();
        for (var i = 0; i < 4; i++) actors.Add((await Actor()).b);
        var res = await Task.WhenAll(actors.Select(a => CreateUser(a, email)));
        Assert.Equal(1, res.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.All(res.Where(r => r.StatusCode != HttpStatusCode.Created), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
        Assert.Equal(1, await _seed.Db(db => db.Users.CountAsync(u => u.Email == email)));
        Assert.Equal(1, await _seed.Db(db => db.Invitations.CountAsync(i => i.Email == email && i.Status == InvitationStatus.Pending)));
    }

    // ============================================================ update
    [Fact]
    public async Task Updating_a_profile_audits_a_diff_bumps_the_version_and_rejects_stale_edits()
    {
        var target = await _seed.UserAsync(); var (b, admin) = await Actor(reauth: false);
        var r1 = await J(await b.SendAsync(HttpMethod.Patch, $"/api/users/{target.Id}", new { name = "Renamed Person", phone = "+62 811", department = "Ops", version = 1 }));
        Assert.Equal("Renamed Person", r1.GetProperty("name").GetString()); Assert.Equal("Ops", r1.GetProperty("department").GetString()); Assert.Equal(2, r1.GetProperty("version").GetInt32());
        var stale = await b.SendAsync(HttpMethod.Patch, $"/api/users/{target.Id}", new { name = "Stale Edit", version = 1 });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("Renamed Person", (await J(await b.Get($"/api/users/{target.Id}"))).GetProperty("name").GetString());
        var a = await _seed.Db(db => db.AuditEvents.Where(x => x.Action == "user.update" && x.TargetId == target.Id.ToString() && x.ActorId == admin.Id).OrderByDescending(x => x.Id).FirstAsync());
        var diff = JsonDocument.Parse(a.DiffJson!).RootElement.EnumerateArray().ToList(); // jsonb re-formats text, so parse instead of string-matching
        Assert.Contains(diff, d => d.GetProperty("Field").GetString() == "name" && d.GetProperty("Before").GetString() == "Test User" && d.GetProperty("After").GetString() == "Renamed Person");
        var same = await J(await b.SendAsync(HttpMethod.Patch, $"/api/users/{target.Id}", new { name = "Renamed Person" })); // no-op keeps the version
        Assert.Equal(2, same.GetProperty("version").GetInt32());
        Assert.Equal((HttpStatusCode)422, (await b.SendAsync(HttpMethod.Patch, $"/api/users/{target.Id}", new { name = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.SendAsync(HttpMethod.Patch, $"/api/users/{Guid.NewGuid()}", new { name = "Nobody Here" })).StatusCode);
    }

    // ============================================================ hierarchy
    [Fact]
    public async Task A_user_admin_cannot_act_on_a_super_admin_in_any_way_and_every_attempt_is_audited()
    {
        var boss = await _seed.UserAsync(role: "super_admin"); var (b, admin) = await Actor();
        var rid = (await Roles("user"))[0];
        var attempts = new (HttpMethod m, string url, object? body)[]
        {
            (HttpMethod.Patch, $"/api/users/{boss.Id}", new { name = "Hacked Boss" }), (HttpMethod.Post, $"/api/users/{boss.Id}/suspend", new { reason = "x" }),
            (HttpMethod.Post, $"/api/users/{boss.Id}/deactivate", null), (HttpMethod.Post, $"/api/users/{boss.Id}/unlock", null),
            (HttpMethod.Put, $"/api/users/{boss.Id}/roles", new { roleIds = new[] { rid } }), (HttpMethod.Delete, $"/api/users/{boss.Id}/sessions", null),
            (HttpMethod.Post, $"/api/users/{boss.Id}/resend-verification", null),
        };
        foreach (var (m, url, body) in attempts)
        {
            var r = await b.SendAsync(m, url, body ?? new { });
            Assert.True(r.StatusCode == HttpStatusCode.Forbidden, $"{m} {url} → {r.StatusCode}"); Assert.Equal("denied", await r.ErrorCode());
        }
        var after = await _seed.Db(db => db.Users.Include(u => u.UserRoles).SingleAsync(u => u.Id == boss.Id));
        Assert.Equal(UserStatus.Active, after.Status); Assert.Equal("Test User", after.Name); Assert.Single(after.UserRoles);
        Assert.True(await _seed.Db(db => db.AuditEvents.CountAsync(a => a.ActorId == admin.Id && a.Outcome == Outcome.Denied && a.TargetId == boss.Id.ToString())) >= attempts.Length);
    }

    [Fact]
    public async Task A_super_admin_can_manage_a_user_admin()
    {
        var target = await _seed.UserAsync(role: "user_admin"); var (b, _) = await Actor("super_admin");
        Assert.Equal(HttpStatusCode.OK, (await b.Post($"/api/users/{target.Id}/suspend", new { reason = "test" })).StatusCode);
    }

    // ============================================================ status changes
    [Fact]
    public async Task Suspending_revokes_sessions_blocks_login_is_audited_and_activate_restores()
    {
        var victim = await _seed.UserAsync(); var vb = TestBrowser.Create(fx.Factory); await vb.LoginAsync(victim.Email, Seed.Password);
        var (b, admin) = await Actor();
        var res = await J(await b.Post($"/api/users/{victim.Id}/suspend", new { reason = "Policy review" }));
        Assert.Equal("suspended", res.GetProperty("status").GetString()); Assert.Equal(0, res.GetProperty("activeSessions").GetInt32());
        Assert.Equal(HttpStatusCode.Unauthorized, (await vb.Get("/api/auth/me")).StatusCode); // existing session dead at once
        Assert.Equal("suspended", (await J(await TestBrowser.Create(fx.Factory).LoginAsync(victim.Email, Seed.Password))).GetProperty("status").GetString());
        var a = await _seed.Db(db => db.AuditEvents.Where(x => x.Action == "user.suspend" && x.TargetId == victim.Id.ToString()).SingleAsync());
        Assert.Contains("Policy review", a.MetaJson); Assert.Contains("suspended", a.DiffJson); Assert.Equal(admin.Id, a.ActorId);
        Assert.NotEmpty(await _seed.Db(db => db.SecurityEvents.Where(e => e.UserId == victim.Id && e.Type == "session.revoked").ToListAsync()));

        Assert.Equal(HttpStatusCode.Conflict, (await b.Post($"/api/users/{victim.Id}/suspend", new { })).StatusCode); // already suspended
        Assert.Equal("active", (await J(await b.Post($"/api/users/{victim.Id}/activate", new { }))).GetProperty("status").GetString());
        Assert.Equal("ok", (await J(await TestBrowser.Create(fx.Factory).LoginAsync(victim.Email, Seed.Password))).GetProperty("status").GetString());
        Assert.Equal("deactivated", (await J(await b.Post($"/api/users/{victim.Id}/deactivate", new { }))).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await b.Post($"/api/users/{Guid.NewGuid()}/suspend", new { })).StatusCode);
    }

    [Fact]
    public async Task A_pending_user_cannot_be_force_activated_and_self_suspension_is_blocked()
    {
        var pending = await _seed.UserAsync(status: UserStatus.Pending); var (b, admin) = await Actor();
        var r = await b.Post($"/api/users/{pending.Id}/activate", new { });
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode); Assert.Equal("invariant", await r.ErrorCode());
        var self = await b.Post($"/api/users/{admin.Id}/suspend", new { });
        Assert.Equal(HttpStatusCode.Conflict, self.StatusCode); Assert.Contains("own account", (await J(self)).GetProperty("error").GetProperty("message").GetString());
        Assert.Equal(UserStatus.Active, (await _seed.Db(db => db.Users.SingleAsync(u => u.Id == admin.Id))).Status);
        Assert.NotEmpty(await _seed.Db(db => db.AuditEvents.Where(a => a.ActorId == admin.Id && a.Action == "user.suspend" && a.Outcome == Outcome.Failure).ToListAsync()));
    }

    [Fact]
    public async Task Unlock_clears_a_temporary_lockout()
    {
        var u = await _seed.UserAsync(); var (b, _) = await Actor();
        for (var i = 0; i < 5; i++) await TestBrowser.Create(fx.Factory).LoginAsync(u.Email, "wrong-" + i);
        Assert.True((await J(await b.Get($"/api/users/{u.Id}"))).GetProperty("locked").GetBoolean());
        Assert.False((await J(await b.Post($"/api/users/{u.Id}/unlock", new { }))).GetProperty("locked").GetBoolean());
        Assert.Equal("ok", (await J(await TestBrowser.Create(fx.Factory).LoginAsync(u.Email, Seed.Password))).GetProperty("status").GetString());
    }

    // ============================================================ resend
    [Fact]
    public async Task Resending_to_a_placeholder_rotates_the_invitation_and_to_a_self_registered_user_sends_a_verification_link()
    {
        var (b, _) = await Actor(); var email = Mail(); var id = (await J(await CreateUser(b, email))).GetProperty("id").GetGuid();
        var first = InMemoryEmailSender.TokenIn(await _seed.Mail.WaitFor(email, "invited"));
        Assert.Equal(HttpStatusCode.Accepted, (await b.Post($"/api/users/{id}/resend-verification", new { })).StatusCode);
        await Task.Delay(300);
        var second = _seed.Mail.Sent.Where(m => m.To == email).Select(InMemoryEmailSender.TokenIn).Last();
        Assert.NotEqual(first, second);
        Assert.Equal(HttpStatusCode.Gone, (await TestBrowser.Create(fx.Factory).Post("/api/invitations/accept", new { token = first, name = "Old Link", password = StrongPw }, csrf: false)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await TestBrowser.Create(fx.Factory).Post("/api/invitations/accept", new { token = second, name = "New Link", password = StrongPw }, csrf: false)).StatusCode);

        var self = await _seed.UserAsync(status: UserStatus.Pending);
        Assert.Equal(HttpStatusCode.Accepted, (await b.Post($"/api/users/{self.Id}/resend-verification", new { })).StatusCode);
        var m = await _seed.Mail.WaitFor(self.Email, "Verify your email"); Assert.Contains("/verify-email?token=", m.Text);
        Assert.Equal(HttpStatusCode.Conflict, (await b.Post($"/api/users/{id}/resend-verification", new { })).StatusCode); // active now
    }

    [Fact]
    public async Task An_email_link_can_never_activate_a_user_who_has_no_credentials()
    {
        // defence for admin-created placeholders: even a verification token for them must not produce an active, password-less account
        var (b, _) = await Actor(); var email = Mail(); var id = (await J(await CreateUser(b, email))).GetProperty("id").GetGuid();
        var raw = "tok-" + Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        await _seed.Db(async db => { db.EmailVerificationTokens.Add(new EmailVerificationToken { UserId = id, TokenHash = new AnnabaDigital.Infrastructure.Security.TokenService(Microsoft.Extensions.Options.Options.Create(new AnnabaDigital.Infrastructure.Security.SecurityOptions())).Hash(raw), ExpiresAt = fx.Clock.UtcNow.AddHours(1), CreatedAt = fx.Clock.UtcNow }); return await db.SaveChangesAsync(); });
        await TestBrowser.Create(fx.Factory).Post("/api/auth/verify-email", new { token = raw }, csrf: false);
        Assert.Equal(UserStatus.Pending, (await _seed.Db(db => db.Users.SingleAsync(u => u.Id == id))).Status);
        await TestBrowser.Create(fx.Factory).Post("/api/auth/forgot-password", new { email }, csrf: false);
        await Task.Delay(300);
        Assert.All(_seed.Mail.Sent.Where(m => m.To == email), m => Assert.Contains("invited", m.Subject, StringComparison.OrdinalIgnoreCase)); // the only mail they ever got is the invitation
    }

    // ============================================================ roles
    [Fact]
    public async Task Role_changes_need_reauth_replace_roles_atomically_apply_immediately_and_are_audited()
    {
        var target = await _seed.UserAsync(role: "user"); var tb = TestBrowser.Create(fx.Factory); await tb.LoginAsync(target.Email, Seed.Password);
        Assert.Equal(HttpStatusCode.Forbidden, (await tb.Get("/api/users")).StatusCode);

        var (b, admin) = await Actor(reauth: false);
        var noReauth = await b.SendAsync(HttpMethod.Put, $"/api/users/{target.Id}/roles", new { roleIds = await Roles("user_admin") });
        Assert.Equal("reauth_required", await noReauth.ErrorCode());
        await b.Post("/api/auth/reauth", new { password = Seed.Password });
        var res = await J(await b.SendAsync(HttpMethod.Put, $"/api/users/{target.Id}/roles", new { roleIds = await Roles("user_admin") }));
        Assert.Equal(["user_admin"], res.GetProperty("roles").EnumerateArray().Select(r => r.GetProperty("key").GetString()).ToArray());
        Assert.Equal(HttpStatusCode.OK, (await tb.Get("/api/users")).StatusCode); // the target's existing session gained access on its very next request

        var a = await _seed.Db(db => db.AuditEvents.Where(x => x.Action == "user.assign_role" && x.TargetId == target.Id.ToString() && x.Outcome == Outcome.Success).SingleAsync());
        Assert.Contains("User Admin", a.DiffJson); Assert.Equal(admin.Id, a.ActorId);
        Assert.NotEmpty(await _seed.Db(db => db.SecurityEvents.Where(e => e.UserId == target.Id && e.Type == "privilege.change").ToListAsync()));

        await b.SendAsync(HttpMethod.Put, $"/api/users/{target.Id}/roles", new { roleIds = await Roles("user") });
        Assert.Equal(HttpStatusCode.Forbidden, (await tb.Get("/api/users")).StatusCode); // and lost again immediately
        Assert.Equal((HttpStatusCode)422, (await b.SendAsync(HttpMethod.Put, $"/api/users/{target.Id}/roles", new { roleIds = Array.Empty<Guid>() })).StatusCode); // at least one role
    }

    [Fact]
    public async Task Assigning_a_role_above_your_own_privileges_is_denied_and_changes_nothing()
    {
        var target = await _seed.UserAsync(role: "user"); var (b, _) = await Actor();
        foreach (var role in new[] { "super_admin", "security_auditor" })
        {
            var r = await b.SendAsync(HttpMethod.Put, $"/api/users/{target.Id}/roles", new { roleIds = await Roles("user", role) });
            Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        }
        var roles = await _seed.Db(db => db.UserRoles.Where(r => r.UserId == target.Id).Select(r => r.Role!.Key).ToListAsync());
        Assert.Equal(["user"], roles);
    }

    [Fact]
    public async Task A_user_cannot_promote_themselves()
    {
        var (b, admin) = await Actor();
        var r = await b.SendAsync(HttpMethod.Put, $"/api/users/{admin.Id}/roles", new { roleIds = await Roles("user_admin", "super_admin") });
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.DoesNotContain("super_admin", await _seed.Db(db => db.UserRoles.Where(x => x.UserId == admin.Id).Select(x => x.Role!.Key).ToListAsync()));
    }

    // ============================================================ sessions & activity
    [Fact]
    public async Task Admins_can_list_and_revoke_user_sessions_without_ever_seeing_tokens()
    {
        var target = await _seed.UserAsync(); var tb1 = TestBrowser.Create(fx.Factory); var tb2 = TestBrowser.Create(fx.Factory);
        await tb1.LoginAsync(target.Email, Seed.Password); await tb2.LoginAsync(target.Email, Seed.Password);
        var (b, _) = await Actor("user_admin", reauth: false);
        var list = await J(await b.Get($"/api/users/{target.Id}/sessions"));
        Assert.Equal(2, list.GetArrayLength());
        var raw = list.GetRawText(); Assert.DoesNotContain(tb1.CookieValue!, raw); Assert.DoesNotContain("tokenHash", raw, StringComparison.OrdinalIgnoreCase);
        var one = list[0].GetProperty("id").GetGuid();
        Assert.Equal(1, (await J(await b.SendAsync(HttpMethod.Delete, $"/api/users/{target.Id}/sessions?sessionId={one}"))).GetProperty("revoked").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, (await b.SendAsync(HttpMethod.Delete, $"/api/users/{target.Id}/sessions?sessionId={Guid.NewGuid()}")).StatusCode);
        Assert.Equal(1, (await J(await b.SendAsync(HttpMethod.Delete, $"/api/users/{target.Id}/sessions"))).GetProperty("revoked").GetInt32());
        Assert.Equal(HttpStatusCode.Unauthorized, (await tb1.Get("/api/auth/me")).StatusCode); Assert.Equal(HttpStatusCode.Unauthorized, (await tb2.Get("/api/auth/me")).StatusCode);

        var (aud, _) = await Actor("security_auditor", reauth: false); // auditors may look but not revoke
        Assert.Equal(HttpStatusCode.OK, (await aud.Get($"/api/users/{target.Id}/sessions")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await aud.SendAsync(HttpMethod.Delete, $"/api/users/{target.Id}/sessions")).StatusCode);
    }

    [Fact]
    public async Task Activity_history_needs_audit_read_and_covers_actions_by_and_on_the_user()
    {
        var target = await _seed.UserAsync(); var (admin, _) = await Actor();
        await admin.Post($"/api/users/{target.Id}/suspend", new { });
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.Get($"/api/users/{target.Id}/activity")).StatusCode); // user_admin has no audit.read
        var (aud, _) = await Actor("security_auditor", reauth: false);
        var items = await J(await aud.Get($"/api/users/{target.Id}/activity"));
        Assert.Contains(items.EnumerateArray(), x => x.GetProperty("action").GetString() == "user.suspend");
    }

    // ============================================================ bulk
    [Fact]
    public async Task Bulk_suspend_reports_per_item_results_with_partial_success_and_checks_the_per_action_permission()
    {
        var ok1 = await _seed.UserAsync(); var ok2 = await _seed.UserAsync(); var boss = await _seed.UserAsync(role: "super_admin"); var (b, admin) = await Actor();
        var res = await J(await b.Post("/api/users/bulk", new { ids = new[] { ok1.Id, boss.Id, admin.Id, ok2.Id, Guid.NewGuid() }, action = "suspend" }));
        var r = res.GetProperty("results").EnumerateArray().ToDictionary(x => x.GetProperty("id").GetGuid(), x => x);
        Assert.True(r[ok1.Id].GetProperty("ok").GetBoolean()); Assert.True(r[ok2.Id].GetProperty("ok").GetBoolean());
        Assert.False(r[boss.Id].GetProperty("ok").GetBoolean()); Assert.False(r[admin.Id].GetProperty("ok").GetBoolean());
        Assert.Equal(3, r.Values.Count(x => !x.GetProperty("ok").GetBoolean())); // outranking target, self, unknown id
        Assert.Equal(UserStatus.Active, (await _seed.Db(db => db.Users.SingleAsync(u => u.Id == boss.Id))).Status);
        Assert.Equal(UserStatus.Suspended, (await _seed.Db(db => db.Users.SingleAsync(u => u.Id == ok1.Id))).Status);

        var (aud, _) = await Actor("security_auditor", reauth: false);
        Assert.Equal(HttpStatusCode.Forbidden, (await aud.Post("/api/users/bulk", new { ids = new[] { ok2.Id }, action = "suspend" })).StatusCode);
        Assert.Equal((HttpStatusCode)422, (await b.Post("/api/users/bulk", new { ids = Array.Empty<Guid>(), action = "suspend" })).StatusCode);
        Assert.Equal((HttpStatusCode)422, (await b.Post("/api/users/bulk", new { ids = new[] { ok1.Id }, action = "delete" })).StatusCode);
        Assert.Equal((HttpStatusCode)422, (await b.Post("/api/users/bulk", new { ids = Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()).ToArray(), action = "suspend" })).StatusCode);
    }

    // ============================================================ authz sweep
    [Fact]
    public async Task Every_user_admin_endpoint_rejects_anonymous_callers_and_callers_without_the_permission()
    {
        var id = Guid.NewGuid();
        var calls = new (HttpMethod m, string url)[]
        {
            (HttpMethod.Get, "/api/users"), (HttpMethod.Get, "/api/users/stats"), (HttpMethod.Post, "/api/users"), (HttpMethod.Get, $"/api/users/{id}"), (HttpMethod.Patch, $"/api/users/{id}"),
            (HttpMethod.Post, $"/api/users/{id}/suspend"), (HttpMethod.Post, $"/api/users/{id}/deactivate"), (HttpMethod.Post, $"/api/users/{id}/activate"), (HttpMethod.Post, $"/api/users/{id}/unlock"),
            (HttpMethod.Post, $"/api/users/{id}/resend-verification"), (HttpMethod.Put, $"/api/users/{id}/roles"), (HttpMethod.Get, $"/api/users/{id}/sessions"),
            (HttpMethod.Delete, $"/api/users/{id}/sessions"), (HttpMethod.Get, $"/api/users/{id}/activity"), (HttpMethod.Post, "/api/users/bulk"),
            (HttpMethod.Get, "/api/invitations"), (HttpMethod.Post, "/api/invitations"), (HttpMethod.Post, $"/api/invitations/{id}/resend"), (HttpMethod.Post, $"/api/invitations/{id}/revoke"),
        };
        var anon = TestBrowser.Create(fx.Factory); var (low, lowUser) = await Actor("user", reauth: false);
        foreach (var (m, url) in calls)
        {
            var a = await anon.SendAsync(m, url, m == HttpMethod.Get ? null : new { }, csrf: false);
            Assert.True(a.StatusCode == HttpStatusCode.Unauthorized, $"anon {m} {url} → {a.StatusCode}");
            var l = await low.SendAsync(m, url, m == HttpMethod.Get ? null : new { });
            Assert.True(l.StatusCode == HttpStatusCode.Forbidden, $"user {m} {url} → {l.StatusCode}");
        }
        Assert.True(await _seed.Db(db => db.AuditEvents.CountAsync(x => x.ActorId == lowUser.Id && x.Action == "authz.denied")) >= calls.Length);
    }

    [Fact]
    public async Task State_changing_user_endpoints_require_the_csrf_token()
    {
        var target = await _seed.UserAsync(); var (b, _) = await Actor();
        b.CsrfToken = null;
        foreach (var (m, url) in new[] { (HttpMethod.Post, $"/api/users/{target.Id}/suspend"), (HttpMethod.Patch, $"/api/users/{target.Id}"), (HttpMethod.Put, $"/api/users/{target.Id}/roles"), (HttpMethod.Delete, $"/api/users/{target.Id}/sessions"), (HttpMethod.Post, "/api/users") })
            Assert.Equal(HttpStatusCode.Forbidden, (await b.SendAsync(m, url, new { })).StatusCode);
        Assert.Equal(UserStatus.Active, (await _seed.Db(db => db.Users.SingleAsync(u => u.Id == target.Id))).Status);
    }

    [Fact]
    public async Task Raw_invitation_tokens_never_reach_the_audit_log()
    {
        var (b, _) = await Actor(); var email = Mail(); await CreateUser(b, email);
        var raw = InMemoryEmailSender.TokenIn(await _seed.Mail.WaitFor(email, "invited"));
        var all = (await _seed.Db(db => db.AuditEvents.ToListAsync())).Select(a => (a.MetaJson ?? "") + (a.DiffJson ?? ""));
        Assert.DoesNotContain(all, t => t.Contains(raw));
    }

    [Fact]
    public async Task The_user_list_marks_who_the_caller_may_manage_following_the_hierarchy_rule()
    {
        var boss = await _seed.UserAsync(role: "super_admin"); var peer = await _seed.UserAsync(role: "user_admin"); var plain = await _seed.UserAsync(role: "user");
        var (b, me) = await Actor(); // a User Admin
        async Task<Dictionary<string, bool>> Flags(TestBrowser who) => (await J(await who.Get("/api/users?pageSize=100"))).GetProperty("data").EnumerateArray()
            .ToDictionary(r => r.GetProperty("email").GetString()!, r => r.GetProperty("manageable").GetBoolean());
        var asAdmin = await Flags(b);
        Assert.False(asAdmin[boss.Email]); // holds permissions a User Admin does not
        Assert.True(asAdmin[peer.Email]); Assert.True(asAdmin[plain.Email]); Assert.True(asAdmin[me.Email]); // equal, lower, and themself
        var (sb, _) = await Actor("super_admin");
        Assert.All(await Flags(sb), kv => Assert.True(kv.Value)); // a Super Admin outranks no one's permissions
    }

    // ======================================================== credential reset (users.reset_credentials)
    private async Task EnrolMfa(Guid userId) => await _seed.Db(async db =>
    {
        db.MfaFactors.Add(new MfaFactor { UserId = userId, SecretCiphertext = [1], SecretNonce = [2], ConfirmedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow });
        db.MfaRecoveryCodes.Add(new MfaRecoveryCode { UserId = userId, CodeHash = "h" + Guid.NewGuid().ToString("N"), CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync(); return 0;
    });

    [Fact]
    public async Task An_administrator_resets_someones_two_step_verification_which_signs_them_out_audits_it_and_tells_them()
    {
        var (admin, adminUser) = await Actor("super_admin");
        var target = await _seed.UserAsync(); var tb = TestBrowser.Create(fx.Factory); await tb.LoginAsync(target.Email, Seed.Password);
        Assert.Equal(HttpStatusCode.OK, (await tb.Get("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.Post($"/api/users/{target.Id}/reset-mfa", new { })).StatusCode); // nothing to reset yet
        await EnrolMfa(target.Id);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.Post($"/api/users/{target.Id}/reset-mfa", new { reason = "Lost phone and recovery codes" })).StatusCode);
        Assert.False(await _seed.Db(db => db.MfaFactors.AnyAsync(f => f.UserId == target.Id)));
        Assert.False(await _seed.Db(db => db.MfaRecoveryCodes.AnyAsync(c => c.UserId == target.Id)));
        Assert.Equal(HttpStatusCode.Unauthorized, (await tb.Get("/api/auth/me")).StatusCode); // signed out everywhere
        var mail = await _seed.Mail.WaitFor(target.Email, "Two-step verification was reset"); Assert.Contains("signed you out", mail.Text);
        var evt = await _seed.Db(db => db.AuditEvents.Where(a => a.Action == "user.mfa.reset" && a.TargetId == target.Id.ToString()).SingleAsync());
        Assert.Equal(adminUser.Id, evt.ActorId); Assert.Contains("Lost phone", evt.MetaJson);
        Assert.NotEmpty(await _seed.Db(db => db.SecurityEvents.Where(e => e.UserId == target.Id && e.Type == "mfa.reset" && e.Severity == Severity.High).ToListAsync()));
        Assert.Equal(HttpStatusCode.Conflict, (await admin.Post($"/api/users/{target.Id}/reset-mfa", new { })).StatusCode); // already gone
    }

    [Fact]
    public async Task Credential_resets_need_the_permission_a_fresh_password_check_the_hierarchy_and_never_apply_to_yourself()
    {
        var target = await _seed.UserAsync(); await EnrolMfa(target.Id);
        var (userAdmin, _) = await Actor("user_admin"); // does not hold users.reset_credentials
        Assert.Equal(HttpStatusCode.Forbidden, (await userAdmin.Post($"/api/users/{target.Id}/reset-mfa", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await userAdmin.Post($"/api/users/{target.Id}/require-password-reset", new { })).StatusCode);

        var (stale, _) = await Actor("super_admin", reauth: false);
        Assert.Equal(HttpStatusCode.Forbidden, (await stale.Post($"/api/users/{target.Id}/reset-mfa", new { })).StatusCode);

        var (boss, bossUser) = await Actor("super_admin");
        Assert.Equal(HttpStatusCode.Conflict, (await boss.Post($"/api/users/{bossUser.Id}/reset-mfa", new { })).StatusCode); // use your own profile

        // holds the permission but not everything the target holds: the hierarchy rule still stops it
        var lower = await _seed.RoleAsync("cred_reset_only_" + Guid.NewGuid().ToString("N")[..6], Permissions.UsersRead, Permissions.UsersResetCredentials);
        var weak = await _seed.UserAsync(role: lower.Key); var wb = TestBrowser.Create(fx.Factory); await wb.LoginAsync(weak.Email, Seed.Password); await wb.Post("/api/auth/reauth", new { password = Seed.Password });
        var otherBoss = await _seed.UserAsync(role: "super_admin"); await EnrolMfa(otherBoss.Id);
        var denied = await wb.Post($"/api/users/{otherBoss.Id}/reset-mfa", new { });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode); Assert.Equal("denied", await denied.ErrorCode());
        Assert.True(await _seed.Db(db => db.MfaFactors.AnyAsync(f => f.UserId == otherBoss.Id)));
    }

    [Fact]
    public async Task Requiring_a_new_password_signs_the_person_out_blocks_the_old_password_and_emails_a_one_time_link()
    {
        var (admin, _) = await Actor("super_admin");
        var target = await _seed.UserAsync(); var tb = TestBrowser.Create(fx.Factory); await tb.LoginAsync(target.Email, Seed.Password);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.Post($"/api/users/{target.Id}/require-password-reset", new { reason = "Suspected leak" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await tb.Get("/api/auth/me")).StatusCode);
        var blocked = await TestBrowser.Create(fx.Factory).LoginAsync(target.Email, Seed.Password); // the right, old password
        Assert.Equal("password_reset_required", (await blocked.Body()).GetProperty("status").GetString());
        Assert.Null(blocked.Headers.TryGetValues("Set-Cookie", out var c) ? c.FirstOrDefault(x => x.StartsWith("sid=") || x.StartsWith("__Host-sid=")) : null); // no session was issued

        var mail = await _seed.Mail.WaitFor(target.Email, "Choose a new password"); var token = InMemoryEmailSender.TokenIn(mail);
        Assert.Contains("/reset-password?token=", mail.Text);
        var done = await TestBrowser.Create(fx.Factory).Post("/api/auth/reset-password", new { token, password = StrongPw }, csrf: false);
        Assert.True(done.IsSuccessStatusCode, await done.Content.ReadAsStringAsync());
        Assert.Equal("ok", (await (await TestBrowser.Create(fx.Factory).LoginAsync(target.Email, StrongPw)).Body()).GetProperty("status").GetString());
        Assert.False(await _seed.Db(db => db.Credentials.Where(x => x.UserId == target.Id).Select(x => x.MustChange).SingleAsync()));
        Assert.NotEmpty(await _seed.Db(db => db.AuditEvents.Where(a => a.Action == "user.password.require_reset" && a.TargetId == target.Id.ToString()).ToListAsync()));
    }
}
