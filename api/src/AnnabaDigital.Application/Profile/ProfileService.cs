using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Application.Email;
using AnnabaDigital.Application.Monitoring;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;

namespace AnnabaDigital.Application.Profile;

public sealed record ProfileDto(string Name, string Email, bool EmailVerified, string? Phone, string? Department, string? JobTitle, bool HasAvatar, long? AvatarVersion, bool MfaEnabled, ProfileCompletion Completion, int Version);
public sealed record UpdateProfileRequest(string? Name, string? Phone, string? Department, string? JobTitle);
public sealed record PreferencesDto(string Language, string Timezone, bool NotifyEmail, bool NotifySecurity, bool NotifyProduct, int AutoLockMinutes);
public sealed record UpdatePreferencesRequest(string? Language, string? Timezone, bool? NotifyEmail, bool? NotifySecurity, bool? NotifyProduct, int? AutoLockMinutes);
public sealed record AvatarFile(byte[] Bytes, string ContentType, string ETag);

/// <summary>PRF-001/002 + SET-001. Sensitive account changes (password, email) always need a fresh re-authentication.</summary>
public sealed partial class ProfileService(
    IAppDb db, IClock clock, IPasswordHasher hasher, ITokenService tokens, IRateLimiter limiter, SettingsProvider settings, PasswordPolicy policy,
    AuditWriter audit, SessionService sessions, IEmailQueue email, IAppLinks links)
{
    [GeneratedRegex(@"^[^\s@]+@[^\s@]+\.[^\s@]+$")] private static partial Regex EmailShape();

    private void RequireFreshReauth(SessionPrincipal who)
    {
        if (who.ReauthAt is not { } t || clock.UtcNow - t > Guards.ReauthWindow) throw DomainException.ReauthRequired();
    }

    private async Task<User> MeAsync(Guid id, CancellationToken ct) =>
        await db.Users.Include(u => u.Profile).Include(u => u.Credential).Include(u => u.MfaFactors).FirstAsync(u => u.Id == id, ct);

    // ---------------------------------------------------------------- profile
    public async Task<ProfileDto> GetAsync(Guid userId, CancellationToken ct = default)
    {
        var u = await db.Users.AsNoTracking().Include(x => x.Profile).Include(x => x.MfaFactors).FirstAsync(x => x.Id == userId, ct);
        var avatar = u.Profile?.AvatarId is { } aid ? await db.Avatars.AsNoTracking().Where(a => a.Id == aid).Select(a => (DateTimeOffset?)a.CreatedAt).FirstOrDefaultAsync(ct) : null;
        var mfa = u.MfaFactors.Any(f => f.ConfirmedAt != null);
        return new ProfileDto(u.Name, u.Email, u.EmailVerifiedAt != null, u.Profile?.Phone, u.Profile?.Department, u.Profile?.JobTitle, avatar != null, avatar?.ToUnixTimeMilliseconds(), mfa,
            DashboardService.Completion(u, avatar != null, mfa), u.Version);
    }

    public async Task<ProfileDto> UpdateAsync(SessionPrincipal who, UpdateProfileRequest req, ClientInfo client, CancellationToken ct = default)
    {
        var f = new Dictionary<string, string>();
        if (req.Name is not null && (req.Name.Trim().Length < 2 || req.Name.Trim().Length > 200)) f["name"] = "Enter your full name.";
        foreach (var (k, v, max) in new[] { ("phone", req.Phone, 40), ("department", req.Department, 100), ("jobTitle", req.JobTitle, 100) }) if (v is { } t && t.Length > max) f[k] = $"Use at most {max} characters.";
        if (f.Count > 0) throw DomainException.Validation("Please correct the highlighted fields.", f);

        var u = await MeAsync(who.UserId, ct); u.Profile ??= new UserProfile { UserId = u.Id };
        var diff = new List<AuditDiff>();
        void Set(string field, string? before, string? after, Action<string?> apply) { var a = after?.Trim(); if (after is not null && (a ?? "") != (before ?? "")) { diff.Add(new AuditDiff(field, before ?? "", a ?? "")); apply(string.IsNullOrEmpty(a) ? null : a); } }
        Set("name", u.Name, req.Name, v => u.Name = v!);
        Set("phone", u.Profile.Phone, req.Phone, v => u.Profile.Phone = v);
        Set("department", u.Profile.Department, req.Department, v => u.Profile.Department = v);
        Set("jobTitle", u.Profile.JobTitle, req.JobTitle, v => u.Profile.JobTitle = v);
        if (diff.Count > 0)
        {
            u.Version++; u.UpdatedAt = clock.UtcNow;
            audit.Record("profile.update", "user", u.Id.ToString(), Outcome.Success, client, u.Id, u.Email, diff: diff);
            await db.SaveChangesAsync(ct);
        }
        return await GetAsync(who.UserId, ct);
    }

    // ---------------------------------------------------------------- avatar
    public async Task<ProfileDto> SetAvatarAsync(SessionPrincipal who, byte[] bytes, ClientInfo client, CancellationToken ct = default)
    {
        var info = ImageInspector.Inspect(bytes); // throws 422 for anything that is not a sane PNG/JPEG/WebP
        var u = await MeAsync(who.UserId, ct); u.Profile ??= new UserProfile { UserId = u.Id };
        await db.InTransactionAsync(async () =>
        {
            await db.Avatars.Where(a => a.UserId == u.Id).ExecuteDeleteAsync(ct);
            var a = new Avatar { UserId = u.Id, ContentType = info.ContentType, Bytes = bytes, CreatedAt = clock.UtcNow };
            db.Avatars.Add(a); u.Profile.AvatarId = a.Id; u.Version++;
            audit.Record("profile.avatar", "user", u.Id.ToString(), Outcome.Success, client, u.Id, u.Email, new Dictionary<string, string> { ["type"] = info.ContentType, ["bytes"] = bytes.Length.ToString(), ["size"] = $"{info.Width}x{info.Height}" });
            await db.SaveChangesAsync(ct);
            return 0;
        }, ct);
        return await GetAsync(who.UserId, ct);
    }

    public async Task RemoveAvatarAsync(SessionPrincipal who, ClientInfo client, CancellationToken ct = default)
    {
        var u = await MeAsync(who.UserId, ct);
        if (u.Profile?.AvatarId is null) return;
        u.Profile.AvatarId = null; await db.Avatars.Where(a => a.UserId == u.Id).ExecuteDeleteAsync(ct); u.Version++;
        audit.Record("profile.avatar", "user", u.Id.ToString(), Outcome.Success, client, u.Id, u.Email, new Dictionary<string, string> { ["removed"] = "true" });
        await db.SaveChangesAsync(ct);
    }

    public async Task<AvatarFile?> AvatarAsync(Guid userId, CancellationToken ct = default, bool own = false)
    {
        if (!own && !await db.Users.AnyAsync(u => u.Id == userId, ct)) return null; // the person must belong to the caller's organisation (users are tenant-filtered)
        var a = await db.Avatars.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId, ct);
        return a is null ? null : new AvatarFile(a.Bytes, a.ContentType, $"\"{a.Id:N}\"");
    }

    // ---------------------------------------------------------------- password
    /// <summary>Requires a re-authentication in the last 5 minutes (which already covers password + second factor). Other sessions are signed out.</summary>
    public async Task ChangePasswordAsync(SessionPrincipal who, string? newPassword, ClientInfo client, CancellationToken ct = default)
    {
        RequireFreshReauth(who);
        var u = await MeAsync(who.UserId, ct);
        await policy.ValidateAsync(newPassword, u.Email, u.Name, await settings.GetAsync(ct), ct);
        if (u.Credential is null) throw DomainException.Invariant("This account has no password to change.");
        if (hasher.Verify(newPassword!, u.Credential.PasswordHash)) throw DomainException.Validation("Choose a password you haven't used just now.", new Dictionary<string, string> { ["password"] = "The new password must differ from the current one." });

        await db.InTransactionAsync(async () =>
        {
            u.Credential.PasswordHash = hasher.Hash(newPassword!); u.Credential.PasswordChangedAt = clock.UtcNow; u.Credential.MustChange = false; u.UpdatedAt = clock.UtcNow;
            var others = await sessions.RevokeAllForUserAsync(u.Id, "password_changed", who.SessionId, ct);
            audit.Record("auth.password.change", "user", u.Id.ToString(), Outcome.Success, client, u.Id, u.Email, new Dictionary<string, string> { ["otherSessionsRevoked"] = others.ToString() });
            audit.Security("password.changed", Severity.Low, $"Password changed; {others} other session(s) signed out", u.Id);
            await db.SaveChangesAsync(ct);
            return 0;
        }, ct);
        email.Enqueue(EmailTemplates.PasswordChanged(u.Email, u.Name));
    }

    // ---------------------------------------------------------------- email change
    /// <summary>The address only changes after the owner of the *new* address confirms; the *old* address is told about the request.</summary>
    public async Task RequestEmailChangeAsync(SessionPrincipal who, string? newEmail, ClientInfo client, CancellationToken ct = default)
    {
        RequireFreshReauth(who);
        var addr = AuthService.NormalizeEmail(newEmail);
        if (addr.Length == 0 || addr.Length > 254 || !EmailShape().IsMatch(addr)) throw DomainException.Validation("Enter a valid email address.", new Dictionary<string, string> { ["email"] = "Enter a valid email address." });
        var hit = await limiter.HitAsync($"emailchange:{who.UserId}", 3, TimeSpan.FromHours(1), ct);
        if (!hit.Allowed) throw DomainException.RateLimited(hit.RetryAfter);
        var u = await MeAsync(who.UserId, ct);
        if (addr == u.Email) throw DomainException.Validation("That is already your email address.", new Dictionary<string, string> { ["email"] = "That is already your email address." });
        var taken = await db.Users.IgnoreQueryFilters().AnyAsync(x => x.Email == addr, ct); // generic response either way: we do not tell the caller whether the address belongs to someone else

        var now = clock.UtcNow; string? raw = null;
        if (!taken)
        {
            foreach (var old in await db.EmailVerificationTokens.Where(t => t.UserId == u.Id && t.NewEmail != null && t.ConsumedAt == null).ToListAsync(ct)) old.ConsumedAt = now;
            raw = tokens.NewToken();
            db.EmailVerificationTokens.Add(new EmailVerificationToken { UserId = u.Id, NewEmail = addr, TokenHash = tokens.Hash(raw), ExpiresAt = now + AccountService.VerifyTtl, CreatedAt = now });
        }
        audit.Record("profile.email_change_requested", "user", u.Id.ToString(), taken ? Outcome.Failure : Outcome.Success, client, u.Id, u.Email, new Dictionary<string, string> { ["newEmail"] = addr, ["reason"] = taken ? "address in use (generic response sent)" : "confirmation sent" });
        await db.SaveChangesAsync(ct);
        if (raw is not null)
        {
            email.Enqueue(EmailTemplates.ConfirmEmailChange(addr, u.Name, $"{links.Origin.TrimEnd('/')}/verify-email?token={Uri.EscapeDataString(raw)}"));
            email.Enqueue(EmailTemplates.EmailChangeRequested(u.Email, u.Name, addr));
        }
    }

    // ---------------------------------------------------------------- preferences
    public async Task<PreferencesDto> PreferencesAsync(Guid userId, CancellationToken ct = default)
    {
        var p = await db.Profiles.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId, ct) ?? new UserProfile { UserId = userId };
        return new PreferencesDto(p.Language, p.Timezone, p.NotifyEmail, p.NotifySecurity, p.NotifyProduct, p.AutoLockMinutes);
    }

    public async Task<PreferencesDto> UpdatePreferencesAsync(SessionPrincipal who, UpdatePreferencesRequest req, ClientInfo client, CancellationToken ct = default)
    {
        var s = await settings.GetAsync(ct); var f = new Dictionary<string, string>();
        if (req.Language is not null && req.Language is not ("en" or "id")) f["language"] = "Use en or id.";
        if (req.Timezone is not null && (req.Timezone.Length > 64 || !TimeZoneInfo.TryFindSystemTimeZoneById(req.Timezone, out _))) f["timezone"] = "Unknown time zone.";
        if (req.AutoLockMinutes is { } m && (m < 1 || m > s.SessionIdleMinutes)) f["autoLockMinutes"] = $"Use 1–{s.SessionIdleMinutes} minutes (the organisation's idle limit).";
        if (f.Count > 0) throw DomainException.Validation("Please correct the highlighted fields.", f);

        var u = await MeAsync(who.UserId, ct); u.Profile ??= new UserProfile { UserId = u.Id }; var p = u.Profile;
        var diff = new List<AuditDiff>();
        void D<T>(string field, T before, T? after, Action<T> apply) where T : struct { if (after is { } a && !EqualityComparer<T>.Default.Equals(before, a)) { diff.Add(new AuditDiff(field, before.ToString()!, a.ToString()!)); apply(a); } }
        if (req.Language is not null && req.Language != p.Language) { diff.Add(new("language", p.Language, req.Language)); p.Language = req.Language; }
        if (req.Timezone is not null && req.Timezone != p.Timezone) { diff.Add(new("timezone", p.Timezone, req.Timezone)); p.Timezone = req.Timezone; }
        D("notifyEmail", p.NotifyEmail, req.NotifyEmail, v => p.NotifyEmail = v); D("notifySecurity", p.NotifySecurity, req.NotifySecurity, v => p.NotifySecurity = v);
        D("notifyProduct", p.NotifyProduct, req.NotifyProduct, v => p.NotifyProduct = v); D("autoLockMinutes", p.AutoLockMinutes, req.AutoLockMinutes, v => p.AutoLockMinutes = v);
        if (diff.Count > 0) { audit.Record("prefs.update", "user", u.Id.ToString(), Outcome.Success, client, u.Id, u.Email, diff: diff); await db.SaveChangesAsync(ct); }
        return await PreferencesAsync(who.UserId, ct);
    }
}
