using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Tests.Infra;
using Xunit;

namespace AnnabaDigital.Tests;

[Collection("db")]
public sealed class ProfileSettingsTests(PostgresFixture fx)
{
    private readonly Seed _seed = new(fx.Factory, fx.Clock);
    private const string NewPw = "Quartz-Ferry-Meadow-83";
    private static string U() => Guid.NewGuid().ToString("N")[..10];
    private static async Task<JsonElement> J(HttpResponseMessage r) => await r.Body();
    private async Task<(TestBrowser b, User u)> Actor(string role = "user", bool reauth = false)
    {
        var u = await _seed.UserAsync(role: role); var b = TestBrowser.Create(fx.Factory); await b.LoginAsync(u.Email, Seed.Password);
        if (reauth) await b.Post("/api/auth/reauth", new { password = Seed.Password });
        return (b, u);
    }

    /// <summary>PUT raw bytes (the endpoint ignores the declared type and inspects the bytes).</summary>
    private static async Task<HttpResponseMessage> PutAvatar(TestBrowser b, byte[] data, string declared = "image/png")
    {
        var req = new HttpRequestMessage(HttpMethod.Put, "/api/me/avatar") { Content = new ByteArrayContent(data) };
        req.Content.Headers.ContentType = new MediaTypeHeaderValue(declared);
        req.Headers.Add("X-Internal-Proxy-Key", ApiFactory.ProxyKey); req.Headers.Add("X-Forwarded-For", b.Ip); req.Headers.Add("Origin", ApiFactory.Origin); req.Headers.Add("Cookie", $"sid={b.CookieValue}"); req.Headers.Add("X-CSRF-Token", b.CsrfToken!);
        return await b.Http.SendAsync(req);
    }

    // ============================================================ profile
    [Fact]
    public async Task Profile_shows_completion_and_edits_are_validated_audited_and_versioned()
    {
        var (b, u) = await Actor();
        var p = await J(await b.Get("/api/me/profile"));
        Assert.Equal(u.Email, p.GetProperty("email").GetString()); Assert.False(p.GetProperty("hasAvatar").GetBoolean());
        var before = p.GetProperty("completion").GetProperty("percent").GetInt32();
        var upd = await J(await b.SendAsync(HttpMethod.Patch, "/api/me/profile", new { name = "Renamed Self", phone = "+62 812 0000", department = "Ops", jobTitle = "Lead" }));
        Assert.Equal("Renamed Self", upd.GetProperty("name").GetString()); Assert.True(upd.GetProperty("completion").GetProperty("percent").GetInt32() > before);
        Assert.Equal(p.GetProperty("version").GetInt32() + 1, upd.GetProperty("version").GetInt32());
        var a = await _seed.Db(db => db.AuditEvents.Where(x => x.Action == "profile.update" && x.ActorId == u.Id).OrderByDescending(x => x.Id).FirstAsync());
        Assert.Contains(JsonDocument.Parse(a.DiffJson!).RootElement.EnumerateArray(), d => d.GetProperty("Field").GetString() == "name" && d.GetProperty("After").GetString() == "Renamed Self");

        var bad = await b.SendAsync(HttpMethod.Patch, "/api/me/profile", new { name = "x", phone = new string('9', 90) });
        Assert.Equal((HttpStatusCode)422, bad.StatusCode); var f = (await J(bad)).GetProperty("error").GetProperty("fields"); Assert.True(f.TryGetProperty("name", out _)); Assert.True(f.TryGetProperty("phone", out _));
        var cleared = await J(await b.SendAsync(HttpMethod.Patch, "/api/me/profile", new { phone = "" })); Assert.Equal(JsonValueKind.Null, cleared.GetProperty("phone").ValueKind);
        var noop = await J(await b.SendAsync(HttpMethod.Patch, "/api/me/profile", new { name = "Renamed Self" })); Assert.Equal(cleared.GetProperty("version").GetInt32(), noop.GetProperty("version").GetInt32());
        Assert.Equal(HttpStatusCode.Unauthorized, (await TestBrowser.Create(fx.Factory).Get("/api/me/profile")).StatusCode);
    }

    [Fact]
    public async Task A_user_can_only_ever_edit_their_own_profile_through_these_endpoints()
    {
        var (a, ua) = await Actor(); var (b, ub) = await Actor();
        await a.SendAsync(HttpMethod.Patch, "/api/me/profile", new { name = "Only Mine" });
        Assert.Equal("Test User", (await _seed.Db(db => db.Users.SingleAsync(x => x.Id == ub.Id))).Name);
        Assert.Equal("Only Mine", (await _seed.Db(db => db.Users.SingleAsync(x => x.Id == ua.Id))).Name);
    }

    // ============================================================ avatar
    [Fact]
    public async Task A_valid_avatar_is_stored_served_with_safe_headers_and_replaceable_and_removable()
    {
        var (b, u) = await Actor(); var png = ImageInspectorTests.Png(128, 128, 400);
        var res = await PutAvatar(b, png, "application/octet-stream"); // the declared type is irrelevant
        Assert.Equal(HttpStatusCode.OK, res.StatusCode); Assert.True((await J(res)).GetProperty("hasAvatar").GetBoolean());
        var got = await b.Get($"/api/avatars/{u.Id}");
        Assert.Equal(HttpStatusCode.OK, got.StatusCode); Assert.Equal("image/png", got.Content.Headers.ContentType!.MediaType); Assert.Equal(png, await got.Content.ReadAsByteArrayAsync());
        Assert.Equal("nosniff", got.Headers.GetValues("X-Content-Type-Options").Single()); Assert.Contains("sandbox", got.Headers.GetValues("Content-Security-Policy").Single()); Assert.True(got.Headers.Contains("ETag"));

        var jpg = ImageInspectorTests.Jpeg(300, 200);
        Assert.Equal(HttpStatusCode.OK, (await PutAvatar(b, jpg)).StatusCode);
        Assert.Equal("image/jpeg", (await b.Get($"/api/avatars/{u.Id}")).Content.Headers.ContentType!.MediaType);
        Assert.Equal(1, await _seed.Db(db => db.Avatars.CountAsync(a => a.UserId == u.Id))); // replaced, not accumulated
        Assert.Equal(HttpStatusCode.NoContent, (await b.SendAsync(HttpMethod.Delete, "/api/me/avatar")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.Get($"/api/avatars/{u.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await b.SendAsync(HttpMethod.Delete, "/api/me/avatar")).StatusCode); // idempotent
    }

    [Fact]
    public async Task Avatar_uploads_reject_non_images_oversized_files_and_odd_dimensions_whatever_the_declared_type()
    {
        var (b, u) = await Actor();
        foreach (var (name, data, type) in new[] { ("html", "<html><script>alert(1)</script></html>"u8.ToArray(), "image/png"), ("svg", "<svg xmlns='http://www.w3.org/2000/svg'/>"u8.ToArray(), "image/png"), ("empty", Array.Empty<byte>(), "image/png"),
            ("tiny", ImageInspectorTests.Png(8, 8), "image/png"), ("huge-dims", ImageInspectorTests.Png(9000, 9000), "image/png"), ("exe-as-png", [.. "MZ"u8.ToArray(), .. new byte[500]], "image/png") })
        {
            var r = await PutAvatar(b, data, type);
            Assert.True(r.StatusCode == (HttpStatusCode)422, $"{name} → {r.StatusCode}"); Assert.True((await J(r)).GetProperty("error").GetProperty("fields").TryGetProperty("file", out _), name);
        }
        var big = await PutAvatar(b, ImageInspectorTests.Png(100, 100, 300 * 1024)); Assert.Equal((HttpStatusCode)422, big.StatusCode); Assert.Contains("256 KB", (await J(big)).GetProperty("error").GetProperty("message").GetString());
        Assert.False(await _seed.Db(db => db.Avatars.AnyAsync(a => a.UserId == u.Id)));
        Assert.Equal(HttpStatusCode.Unauthorized, (await PutAvatar(TestBrowser.Create(fx.Factory), ImageInspectorTests.Png(100, 100))).StatusCode);
    }

    [Fact]
    public async Task Avatars_are_visible_to_the_owner_and_to_people_who_may_list_users_but_not_to_everyone()
    {
        var (owner, ou) = await Actor(); await PutAvatar(owner, ImageInspectorTests.Png(100, 100, 50));
        var (peer, _) = await Actor("user"); var (admin, _) = await Actor("user_admin");
        Assert.Equal(HttpStatusCode.NotFound, (await peer.Get($"/api/avatars/{ou.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.Get($"/api/avatars/{ou.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await TestBrowser.Create(fx.Factory).Get($"/api/avatars/{ou.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.Get($"/api/avatars/{Guid.NewGuid()}")).StatusCode);
    }

    // ============================================================ password
    [Fact]
    public async Task Changing_the_password_needs_fresh_reauth_signs_out_other_sessions_and_keeps_this_one()
    {
        var (b, u) = await Actor(); var other = TestBrowser.Create(fx.Factory); await other.LoginAsync(u.Email, Seed.Password);
        var denied = await b.Post("/api/me/password", new { newPassword = NewPw });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode); Assert.Equal("reauth_required", await denied.ErrorCode());
        await b.Post("/api/auth/reauth", new { password = Seed.Password });
        Assert.Equal(HttpStatusCode.NoContent, (await b.Post("/api/me/password", new { newPassword = NewPw })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await b.Get("/api/auth/me")).StatusCode); Assert.Equal(HttpStatusCode.Unauthorized, (await other.Get("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await TestBrowser.Create(fx.Factory).LoginAsync(u.Email, Seed.Password)).StatusCode); // old password dead
        Assert.Equal(HttpStatusCode.OK, (await TestBrowser.Create(fx.Factory).LoginAsync(u.Email, NewPw)).StatusCode);
        await _seed.Mail.WaitFor(u.Email, "password was changed");
        Assert.NotEmpty(await _seed.Db(db => db.SecurityEvents.Where(e => e.UserId == u.Id && e.Type == "password.changed").ToListAsync()));
        var audit = (await _seed.Db(db => db.AuditEvents.Where(a => a.ActorId == u.Id).ToListAsync())).Select(a => a.MetaJson + a.DiffJson);
        Assert.DoesNotContain(audit, t => t != null && (t.Contains(NewPw) || t.Contains(Seed.Password)));
    }

    [Fact]
    public async Task The_new_password_must_pass_the_policy_and_differ_from_the_current_one_and_the_reauth_window_expires()
    {
        var (b, _) = await Actor(reauth: true);
        foreach (var weak in new[] { "short", "password1234", "aaaaaaaaaaaaaa" }) Assert.Equal((HttpStatusCode)422, (await b.Post("/api/me/password", new { newPassword = weak })).StatusCode);
        var same = await b.Post("/api/me/password", new { newPassword = Seed.Password }); Assert.Equal((HttpStatusCode)422, same.StatusCode);
        Assert.Contains("differ", (await J(same)).GetProperty("error").GetProperty("fields").GetProperty("password").GetString());
        fx.Clock.Advance(TimeSpan.FromMinutes(6));
        Assert.Equal("reauth_required", await (await b.Post("/api/me/password", new { newPassword = NewPw })).ErrorCode());
    }

    // ============================================================ email change
    [Fact]
    public async Task Changing_the_email_needs_confirmation_from_the_new_address_notifies_the_old_one_and_switches_only_then()
    {
        var (b, u) = await Actor(reauth: true); var fresh = $"new{U()}@test.local";
        var denied = await (await Actor()).b.Post("/api/me/email", new { newEmail = fresh }); Assert.Equal("reauth_required", await denied.ErrorCode());
        Assert.Equal(HttpStatusCode.Accepted, (await b.Post("/api/me/email", new { newEmail = fresh.ToUpperInvariant() })).StatusCode);
        var mail = await _seed.Mail.WaitFor(fresh, "Confirm your new email"); await _seed.Mail.WaitFor(u.Email, "Email change requested");
        Assert.Equal(u.Email, (await _seed.Db(db => db.Users.SingleAsync(x => x.Id == u.Id))).Email); // nothing changed yet
        Assert.Equal(HttpStatusCode.OK, (await TestBrowser.Create(fx.Factory).LoginAsync(u.Email, Seed.Password)).StatusCode);

        var token = InMemoryEmailSender.TokenIn(mail);
        var stored = await _seed.Db(db => db.EmailVerificationTokens.SingleAsync(t => t.UserId == u.Id && t.NewEmail == fresh)); Assert.NotEqual(token, stored.TokenHash);
        Assert.Equal(HttpStatusCode.OK, (await TestBrowser.Create(fx.Factory).Post("/api/auth/verify-email", new { token }, csrf: false)).StatusCode); // no session needed: the token is the proof
        Assert.Equal(fresh, (await _seed.Db(db => db.Users.SingleAsync(x => x.Id == u.Id))).Email);
        await _seed.Mail.WaitFor(u.Email, "address was changed"); // the previous address is told
        Assert.Equal(HttpStatusCode.Unauthorized, (await TestBrowser.Create(fx.Factory).LoginAsync(u.Email, Seed.Password)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await TestBrowser.Create(fx.Factory).LoginAsync(fresh, Seed.Password)).StatusCode);
        Assert.Equal(HttpStatusCode.Gone, (await TestBrowser.Create(fx.Factory).Post("/api/auth/verify-email", new { token }, csrf: false)).StatusCode); // single use
        Assert.NotEmpty(await _seed.Db(db => db.SecurityEvents.Where(e => e.UserId == u.Id && e.Type == "email.changed").ToListAsync()));
    }

    [Fact]
    public async Task Email_change_requests_do_not_reveal_whether_an_address_is_taken_and_never_switch_to_a_taken_one()
    {
        var (b, u) = await Actor(reauth: true); var other = await _seed.UserAsync();
        var r = await b.Post("/api/me/email", new { newEmail = other.Email });
        Assert.Equal(HttpStatusCode.Accepted, r.StatusCode); // same answer as for a free address
        await Task.Delay(300); Assert.DoesNotContain(_seed.Mail.Sent, m => m.To == other.Email && m.Subject.Contains("Confirm your new email")); // and no mail goes to the other person
        Assert.Equal(u.Email, (await _seed.Db(db => db.Users.SingleAsync(x => x.Id == u.Id))).Email);
        Assert.Equal((HttpStatusCode)422, (await b.Post("/api/me/email", new { newEmail = "nope" })).StatusCode);
        Assert.Equal((HttpStatusCode)422, (await b.Post("/api/me/email", new { newEmail = u.Email.ToUpperInvariant() })).StatusCode); // already yours
    }

    [Fact]
    public async Task A_race_for_the_same_new_address_has_one_winner_and_a_newer_request_cancels_the_older_link()
    {
        var (a, ua) = await Actor(reauth: true); var (b, ub) = await Actor(reauth: true); var target = $"race{U()}@test.local";
        await a.Post("/api/me/email", new { newEmail = target }); var ta = InMemoryEmailSender.TokenIn(await _seed.Mail.WaitFor(target, "Confirm your new email"));
        await b.Post("/api/me/email", new { newEmail = target }); await Task.Delay(300); var tb = _seed.Mail.Sent.Where(m => m.To == target).Select(InMemoryEmailSender.TokenIn).Last();
        var res = await Task.WhenAll(TestBrowser.Create(fx.Factory).Post("/api/auth/verify-email", new { token = ta }, csrf: false), TestBrowser.Create(fx.Factory).Post("/api/auth/verify-email", new { token = tb }, csrf: false));
        Assert.Equal(1, res.Count(r => r.StatusCode == HttpStatusCode.OK)); Assert.Equal(1, await _seed.Db(db => db.Users.CountAsync(x => x.Email == target)));

        var (c, uc) = await Actor(reauth: true); var t2 = $"two{U()}@test.local"; var t3 = $"three{U()}@test.local";
        await c.Post("/api/me/email", new { newEmail = t2 }); var old = InMemoryEmailSender.TokenIn(await _seed.Mail.WaitFor(t2, "Confirm your new email"));
        await c.Post("/api/me/email", new { newEmail = t3 }); await _seed.Mail.WaitFor(t3, "Confirm your new email");
        Assert.Equal(HttpStatusCode.Gone, (await TestBrowser.Create(fx.Factory).Post("/api/auth/verify-email", new { token = old }, csrf: false)).StatusCode);
    }

    [Fact]
    public async Task Email_change_requests_are_rate_limited()
    {
        var (b, _) = await Actor(reauth: true); HttpResponseMessage? last = null;
        for (var i = 0; i < 4; i++) last = await b.Post("/api/me/email", new { newEmail = $"rl{i}{U()}@test.local" });
        Assert.Equal((HttpStatusCode)429, last!.StatusCode);
    }

    // ============================================================ preferences
    [Fact]
    public async Task Preferences_persist_are_validated_and_limited_by_the_organisation_idle_limit()
    {
        var (b, u) = await Actor();
        var d = await J(await b.Get("/api/me/preferences")); Assert.Equal("en", d.GetProperty("language").GetString()); Assert.True(d.GetProperty("notifySecurity").GetBoolean());
        var upd = await J(await b.SendAsync(HttpMethod.Patch, "/api/me/preferences", new { language = "id", timezone = "Asia/Singapore", notifyProduct = true, autoLockMinutes = 10 }));
        Assert.Equal("id", upd.GetProperty("language").GetString()); Assert.Equal("Asia/Singapore", upd.GetProperty("timezone").GetString()); Assert.True(upd.GetProperty("notifyProduct").GetBoolean()); Assert.Equal(10, upd.GetProperty("autoLockMinutes").GetInt32());
        Assert.Equal("id", (await J(await b.Get("/api/me/preferences"))).GetProperty("language").GetString()); // persisted
        Assert.NotEmpty(await _seed.Db(db => db.AuditEvents.Where(a => a.Action == "prefs.update" && a.ActorId == u.Id).ToListAsync()));
        foreach (var bad in new object[] { new { language = "fr" }, new { timezone = "Mars/Olympus" }, new { autoLockMinutes = 0 }, new { autoLockMinutes = 999 } })
            Assert.Equal((HttpStatusCode)422, (await b.SendAsync(HttpMethod.Patch, "/api/me/preferences", bad)).StatusCode);
        Assert.Equal("Asia/Singapore", (await J(await b.Get("/api/me/preferences"))).GetProperty("timezone").GetString()); // bad requests changed nothing
    }

    // ============================================================ system settings
    private async Task<(TestBrowser b, User u)> Admin() => await Actor("super_admin", reauth: true);

    [Fact]
    public async Task Public_settings_expose_only_what_the_sign_in_screens_need()
    {
        var res = await TestBrowser.Create(fx.Factory).Get("/api/settings/public");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var d = await J(res); Assert.Equal(new[] { "emailEnabled", "mfaPolicy", "passwordMinLength", "registrationOpen", "ssoEnabled", "ssoName" }, d.EnumerateObject().Select(p => p.Name).Order().ToArray());
        await using var open = await _seed.SettingAsync("registrationMode", "\"open\"");
        Assert.True((await J(await TestBrowser.Create(fx.Factory).Get("/api/settings/public"))).GetProperty("registrationOpen").GetBoolean());
    }

    [Fact]
    public async Task Only_settings_manage_can_read_or_change_system_settings_and_secrets_are_never_returned()
    {
        foreach (var role in new[] { "user", "user_admin", "security_auditor" })
        {
            var (b, _) = await Actor(role, reauth: true);
            Assert.Equal(HttpStatusCode.Forbidden, (await b.Get("/api/settings")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await b.SendAsync(HttpMethod.Patch, "/api/settings", new { passwordMinLength = 8 })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await b.SendAsync(HttpMethod.Put, "/api/settings/email-api-key", new { value = "re_secret_12345678" })).StatusCode);
        }
        var (admin, _) = await Admin(); var s = await J(await admin.Get("/api/settings"));
        Assert.Equal(12, s.GetProperty("passwordMinLength").GetInt32()); Assert.Equal("console", s.GetProperty("emailProvider").GetString());
        Assert.DoesNotContain("apiKey\":\"", s.GetRawText(), StringComparison.OrdinalIgnoreCase); Assert.True(s.TryGetProperty("emailApiKeySet", out _));
    }

    [Fact]
    public async Task Changing_settings_validates_audits_a_diff_applies_immediately_and_needs_fresh_reauth()
    {
        var (admin, actor) = await Admin();
        try
        {
            var upd = await J(await admin.SendAsync(HttpMethod.Patch, "/api/settings", new { lockoutAttempts = 4, passwordMinLength = 14, invitationExpiryDays = 5 }));
            Assert.Equal(4, upd.GetProperty("lockoutAttempts").GetInt32()); Assert.Equal(14, upd.GetProperty("passwordMinLength").GetInt32());
            var a = await _seed.Db(db => db.AuditEvents.Where(x => x.Action == "settings.update" && x.ActorId == actor.Id).OrderByDescending(x => x.Id).FirstAsync());
            var diff = JsonDocument.Parse(a.DiffJson!).RootElement.EnumerateArray().ToDictionary(d => d.GetProperty("Field").GetString()!, d => (d.GetProperty("Before").GetString(), d.GetProperty("After").GetString()));
            Assert.Equal(("12", "14"), diff["passwordMinLength"]); Assert.Equal(("5", "4").Item2, diff["lockoutAttempts"].Item2);
            Assert.NotEmpty(await _seed.Db(db => db.SecurityEvents.Where(e => e.Type == "settings.changed").ToListAsync()));

            // immediately effective: a 13-character password no longer passes registration policy (min 14), and 4 failures now lock the account
            var u = await _seed.UserAsync(); for (var i = 0; i < 4; i++) await TestBrowser.Create(fx.Factory).LoginAsync(u.Email, "wrong-" + i);
            Assert.Equal("locked", (await J(await TestBrowser.Create(fx.Factory).LoginAsync(u.Email, Seed.Password))).GetProperty("status").GetString());

            fx.Clock.Advance(TimeSpan.FromMinutes(6));
            Assert.Equal("reauth_required", await (await admin.SendAsync(HttpMethod.Patch, "/api/settings", new { lockoutAttempts = 5 })).ErrorCode());
        }
        finally { await _seed.Db(async db => { foreach (var (k, v) in new[] { ("lockoutAttempts", "5"), ("passwordMinLength", "12"), ("invitationExpiryDays", "7") }) (await db.Settings.SingleAsync(s => s.Key == k)).ValueJson = v; return await db.SaveChangesAsync(); }); }
    }

    [Fact]
    public async Task Invalid_or_inconsistent_settings_are_rejected_with_field_errors_and_change_nothing()
    {
        var (admin, _) = await Admin(); var before = (await J(await admin.Get("/api/settings"))).GetRawText();
        foreach (var bad in new object[]
        {
            new { passwordMinLength = 4 }, new { passwordMinLength = 500 }, new { sessionIdleMinutes = 1 }, new { lockoutAttempts = 1 }, new { auditRetentionDays = 5 }, new { invitationExpiryDays = 90 },
            new { mfaPolicy = "sometimes" }, new { registrationMode = "anyone" }, new { emailFrom = "not an email" }, new { sessionIdleMinutes = 600, sessionAbsoluteHours = 2 }, new { rememberIdleDays = 30, rememberAbsoluteDays = 10 },
        })
        {
            var r = await admin.SendAsync(HttpMethod.Patch, "/api/settings", bad);
            Assert.Equal((HttpStatusCode)422, r.StatusCode); Assert.True((await J(r)).GetProperty("error").GetProperty("fields").EnumerateObject().Any());
        }
        Assert.Equal((HttpStatusCode)422, (await admin.SendAsync(HttpMethod.Patch, "/api/settings", new { })).StatusCode); // nothing to change
        Assert.Equal(before, (await J(await admin.Get("/api/settings"))).GetRawText());
    }

    [Fact]
    public async Task The_email_api_key_is_write_only_encrypted_at_rest_never_audited_and_used_by_the_sender()
    {
        var (admin, actor) = await Admin(); var key = "re_live_" + U() + U();
        var set = await J(await admin.SendAsync(HttpMethod.Put, "/api/settings/email-api-key", new { value = key }));
        Assert.True(set.GetProperty("emailApiKeySet").GetBoolean()); Assert.Equal("database", set.GetProperty("emailApiKeySource").GetString()); Assert.DoesNotContain(key, set.GetRawText());
        Assert.DoesNotContain(key, (await J(await admin.Get("/api/settings"))).GetRawText());

        var row = await _seed.Db(db => db.Settings.SingleAsync(s => s.Key == "email.resendApiKey"));
        Assert.True(row.IsSecret); Assert.Null(row.ValueJson); Assert.False(System.Text.Encoding.UTF8.GetString(row.ValueCiphertext!).Contains(key)); Assert.Equal(12 + key.Length + 16, row.ValueCiphertext!.Length);
        var audit = (await _seed.Db(db => db.AuditEvents.Where(a => a.Action.StartsWith("settings.secret")).ToListAsync())).Select(a => a.MetaJson + a.DiffJson + a.TargetId);
        Assert.DoesNotContain(audit, t => t.Contains(key)); Assert.Contains(await _seed.Db(db => db.AuditEvents.Where(a => a.Action == "settings.secret.set" && a.ActorId == actor.Id).ToListAsync()), _ => true);

        using var scope = fx.Factory.Services.CreateScope(); // the sender resolves the decrypted key (internal API only)
        Assert.Equal(key, await scope.ServiceProvider.GetRequiredService<AnnabaDigital.Application.Settings.SettingsService>().ReadEmailApiKeyAsync());

        Assert.Equal((HttpStatusCode)422, (await admin.SendAsync(HttpMethod.Put, "/api/settings/email-api-key", new { value = "has spaces in it" })).StatusCode);
        Assert.Equal((HttpStatusCode)422, (await admin.SendAsync(HttpMethod.Put, "/api/settings/email-api-key", new { value = "short" })).StatusCode);
        Assert.Equal("none", (await J(await admin.SendAsync(HttpMethod.Delete, "/api/settings/email-api-key"))).GetProperty("emailApiKeySource").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await admin.SendAsync(HttpMethod.Delete, "/api/settings/email-api-key")).StatusCode);
        Assert.False(await _seed.Db(db => db.Settings.AnyAsync(s => s.Key == "email.resendApiKey")));
    }

    [Fact]
    public async Task Secret_writes_need_fresh_reauth_and_csrf()
    {
        var (a, _) = await Actor("super_admin"); // no reauth
        Assert.Equal("reauth_required", await (await a.SendAsync(HttpMethod.Put, "/api/settings/email-api-key", new { value = "re_valid_key_12345" })).ErrorCode());
        var (b, _) = await Admin(); b.CsrfToken = null;
        Assert.Equal(HttpStatusCode.Forbidden, (await b.SendAsync(HttpMethod.Patch, "/api/settings", new { lockoutAttempts = 6 })).StatusCode);
    }

    [Fact]
    public async Task Every_self_service_and_settings_endpoint_requires_authentication_and_csrf_on_writes()
    {
        var id = Guid.NewGuid();
        var anonymous = new (HttpMethod m, string url)[]
        {
            (HttpMethod.Get, "/api/me/profile"), (HttpMethod.Patch, "/api/me/profile"), (HttpMethod.Put, "/api/me/avatar"), (HttpMethod.Delete, "/api/me/avatar"), (HttpMethod.Get, $"/api/avatars/{id}"),
            (HttpMethod.Post, "/api/me/password"), (HttpMethod.Post, "/api/me/email"), (HttpMethod.Get, "/api/me/preferences"), (HttpMethod.Patch, "/api/me/preferences"),
            (HttpMethod.Get, "/api/sessions/me"), (HttpMethod.Delete, "/api/sessions/me"), (HttpMethod.Get, "/api/dashboard/admin"), (HttpMethod.Get, "/api/dashboard/me"),
            (HttpMethod.Get, "/api/audit"), (HttpMethod.Get, "/api/security-events"), (HttpMethod.Get, "/api/settings"), (HttpMethod.Get, "/api/sessions"),
        };
        var anon = TestBrowser.Create(fx.Factory);
        foreach (var (m, url) in anonymous) Assert.Equal(HttpStatusCode.Unauthorized, (await anon.SendAsync(m, url, m == HttpMethod.Get ? null : new { }, csrf: false)).StatusCode);
        var (b, _) = await Actor(reauth: true); b.CsrfToken = null;
        foreach (var (m, url) in new[] { (HttpMethod.Patch, "/api/me/profile"), (HttpMethod.Delete, "/api/me/avatar"), (HttpMethod.Post, "/api/me/password"), (HttpMethod.Post, "/api/me/email"), (HttpMethod.Patch, "/api/me/preferences") })
            Assert.Equal(HttpStatusCode.Forbidden, (await b.SendAsync(m, url, new { })).StatusCode);
    }
}
