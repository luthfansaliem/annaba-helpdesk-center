using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Email;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Application.Mfa;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;

namespace AnnabaDigital.Application.Auth;

public sealed record LoginRequest(string? Email, string? Password, bool Remember);
public sealed record MeDto(Guid Id, string Email, string Name, string[] Roles, string[] Permissions, bool Locked, bool MfaEnrolled, long? AvatarVersion = null, string TenantName = "", bool IsPlatform = false, string? ActingTenant = null, string Language = "en", string? VendorName = null);
public sealed record LoginResult(string Status, string? RawToken, bool Remember, MeDto? User);

public static class LoginStatus
{
    public const string Ok = "ok";
    public const string MfaRequired = "mfa_required";
    public const string VerifyEmail = "verify_email";
    public const string Suspended = "suspended";
    public const string Deactivated = "deactivated";
    public const string Locked = "locked";
    public const string MfaEnrollmentRequired = "mfa_enrollment_required";
    public const string SsoDenied = "sso_denied";
    /// <summary>An administrator required a new password: sign-in is refused (after the right password) until it is set through the e-mailed link.</summary>
    public const string ResetRequired = "password_reset_required";
}

public sealed class AuthService(
    IAppDb db, IPasswordHasher hasher, ITokenService tokens, IRateLimiter limiter, IClock clock,
    SessionService sessions, AuthorizationService authz, AuditWriter audit, SettingsProvider settings, MfaService mfaService, IEmailQueue email, IAppLinks links, TenantDirectory tenants)
{
    /// <summary>A sign-in from a device and browser this person has not used within this many days is "new" and, unless they opted out, they are e-mailed.</summary>
    public const int NewDeviceWindowDays = 90;
    public const int LoginIpLimit = 20;
    public const int LoginAccountLimit = 10;
    public static readonly TimeSpan LoginWindow = TimeSpan.FromMinutes(5);
    public const int UnlockLimit = 5;
    public static readonly TimeSpan UnlockWindow = TimeSpan.FromMinutes(15);

    public static string NormalizeEmail(string? e) => (e ?? "").Trim().ToLowerInvariant();

    public async Task<LoginResult> LoginAsync(LoginRequest req, ClientInfo client, string? existingRawToken, CancellationToken ct = default)
    {
        var email = NormalizeEmail(req.Email);
        if (email.Length == 0 || string.IsNullOrEmpty(req.Password) || email.Length > 254 || req.Password.Length > 256)
            throw DomainException.Validation("Enter your email and password.");

        // Throttle before spending CPU on hashing. Keys use hashes so unknown and known accounts behave identically.
        var ipHit = await limiter.HitAsync($"login:ip:{client.Ip ?? "unknown"}", LoginIpLimit, LoginWindow, ct);
        var acctHit = await limiter.HitAsync($"login:acct:{tokens.Hash(email)}", LoginAccountLimit, LoginWindow, ct);
        if (!ipHit.Allowed || !acctHit.Allowed)
        {
            audit.Record("auth.login.throttled", "user", null, Outcome.Failure, client, meta: new Dictionary<string, string> { ["scope"] = ipHit.Allowed ? "account" : "ip" });
            await db.SaveChangesAsync(ct);
            throw DomainException.RateLimited(ipHit.Allowed ? acctHit.RetryAfter : ipHit.RetryAfter);
        }

        var now = clock.UtcNow;
        var user = await db.Users.Include(u => u.Credential).Include(u => u.MfaFactors).FirstOrDefaultAsync(u => u.Email == email, ct);
        var s = user is null ? await settings.GetGlobalAsync(ct) : await settings.GetForAsync(user.TenantId, ct); // sign-in is anonymous, so the person's own organisation sets the lockout rules

        bool passwordOk;
        if (user?.Credential is null) { hasher.VerifyDummy(req.Password); passwordOk = false; }
        else passwordOk = hasher.Verify(req.Password, user.Credential.PasswordHash);

        if (!passwordOk)
        {
            if (user is not null && !(user.LockedUntil > now))
            {
                user.FailedLoginCount++;
                if (user.FailedLoginCount >= s.LockoutAttempts)
                {
                    user.LockedUntil = now.AddMinutes(s.LockoutMinutes);
                    audit.Security("account.locked", Severity.High, $"Account locked after {user.FailedLoginCount} failed sign-in attempts", user.Id);
                }
            }
            audit.Record("auth.login.failure", "user", user?.Id.ToString(), Outcome.Failure, client, user?.Id, email, new Dictionary<string, string> { ["reason"] = user is null ? "unknown_account" : "bad_password" });
            audit.Security("login.failed", Severity.Low, "Failed sign-in attempt", user?.Id);
            await db.SaveChangesAsync(ct);
            throw DomainException.InvalidCredentials(); // identical for unknown account and wrong password
        }

        // From here on the password is correct, so revealing the account state to the user is safe. (A wrong password or unknown account threw above, so user is not null.)
        if (user!.LockedUntil > now)
        {
            audit.Record("auth.login.blocked", "user", user.Id.ToString(), Outcome.Failure, client, user.Id, email, new Dictionary<string, string> { ["reason"] = "temporarily_locked" });
            await db.SaveChangesAsync(ct);
            return new LoginResult(LoginStatus.Locked, null, false, null);
        }
        if (user.Status != UserStatus.Active)
        {
            var status = user.Status switch { UserStatus.Pending => LoginStatus.VerifyEmail, UserStatus.Suspended => LoginStatus.Suspended, _ => LoginStatus.Deactivated };
            audit.Record("auth.login.blocked", "user", user.Id.ToString(), Outcome.Failure, client, user.Id, email, new Dictionary<string, string> { ["reason"] = status });
            await db.SaveChangesAsync(ct);
            return new LoginResult(status, null, false, null);
        }

        if (!await TenantActiveAsync(user, ct))
        {
            audit.Record("auth.login.blocked", "user", user.Id.ToString(), Outcome.Failure, client, user.Id, email, new Dictionary<string, string> { ["reason"] = "tenant_suspended" });
            await db.SaveChangesAsync(ct);
            return new LoginResult(LoginStatus.Suspended, null, false, null);
        }

        if (user.Credential!.MustChange)
        {
            audit.Record("auth.login.blocked", "user", user.Id.ToString(), Outcome.Failure, client, user.Id, email, new Dictionary<string, string> { ["reason"] = "password_reset_required" });
            await db.SaveChangesAsync(ct);
            return new LoginResult(LoginStatus.ResetRequired, null, false, null);
        }

        // Success path
        if (hasher.NeedsRehash(user.Credential!.PasswordHash)) user.Credential.PasswordHash = hasher.Hash(req.Password);
        var result = await CompleteLoginAsync(user, req.Remember, existingRawToken, client, s, now, null, ct);
        await limiter.ResetAsync($"login:acct:{tokens.Hash(email)}", ct);
        return result;
    }

    /// <summary>Tells the owner about a sign-in from a device we haven't seen, if they kept "Security alerts" on (the default). Best effort: it never blocks the sign-in.</summary>
    private async Task NotifyNewDeviceAsync(User user, string device, string browser, ClientInfo client, DateTimeOffset now, CancellationToken ct)
    {
        var profile = await db.Profiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == user.Id, ct);
        if (profile is { NotifySecurity: false }) return;
        var local = now;
        try { if (profile?.Timezone is { Length: > 0 } tz) local = TimeZoneInfo.ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById(tz)); } catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException) { }
        var when = $"{local:d MMM yyyy, HH:mm} ({(profile?.Timezone is { Length: > 0 } z ? z : "UTC")})";
        email.Enqueue(EmailTemplates.NewSignIn(user.Email, user.Name, device, browser, client.Ip ?? "unknown", when, $"{links.Origin.TrimEnd('/')}/profile?tab=security"));
    }

    private async Task<bool> TenantActiveAsync(User user, CancellationToken ct) => (await tenants.GetAsync(user.TenantId, ct))?.Status == TenantStatus.Active;

    /// <summary>The part both password and single sign-on logins share: reset counters, rotate the session, honour MFA, audit.</summary>
    private async Task<LoginResult> CompleteLoginAsync(User user, bool remember, string? existingRawToken, ClientInfo client, SecuritySettings s, DateTimeOffset now, string? method, CancellationToken ct)
    {
        user.FailedLoginCount = 0; user.LockedUntil = null; user.LastLoginAt = now;

        // Session fixation defence: never reuse an existing session id.
        if (!string.IsNullOrEmpty(existingRawToken))
        {
            var old = await db.Sessions.FirstOrDefaultAsync(x => x.TokenHash == tokens.Hash(existingRawToken) && x.RevokedAt == null, ct);
            if (old is not null) { old.RevokedAt = now; old.RevokedReason = RevokeReason.Rotated; }
        }
        var mfa = user.MfaFactors.Any(f => f.ConfirmedAt is not null);
        // New device: not the person's very first sign-in (nothing to compare with), and no session from this device + browser in the last NewDeviceWindowDays.
        var (device, browser) = DeviceInfo.Describe(client.UserAgent);
        var hadSessions = await db.Sessions.AnyAsync(x => x.UserId == user.Id, ct);
        var newDevice = hadSessions && !await db.Sessions.AnyAsync(x => x.UserId == user.Id && x.DeviceLabel == device && x.BrowserLabel == browser && x.CreatedAt > now.AddDays(-NewDeviceWindowDays), ct);
        var created = await sessions.CreateAsync(user, remember, mfa, client, ct);
        var meta = new Dictionary<string, string> { ["mfa"] = mfa ? "required" : "none" };
        if (method is not null) meta["method"] = method;
        if (newDevice) meta["newDevice"] = "true";
        audit.Record("auth.login.success", "session", created.Session.Id.ToString(), Outcome.Success, client, user.Id, user.Email, meta);
        await db.SaveChangesAsync(ct);
        if (newDevice) await NotifyNewDeviceAsync(user, device, browser, client, now, ct);

        var me = await BuildMeAsync(user, locked: false, mfa, ct);
        var loginStatus = mfa ? LoginStatus.MfaRequired : s.MfaPolicy == "required" ? LoginStatus.MfaEnrollmentRequired : LoginStatus.Ok;
        return new LoginResult(loginStatus, created.RawToken, remember, me);
    }

    /// <summary>
    /// Single sign-on. The provider vouches for who signed in; we only accept a *verified* e-mail that belongs to an existing, active user (nobody is created
    /// here — people arrive by invitation). The first time links the provider's stable subject to that user; after that the subject, not the e-mail, decides.
    /// MFA and the account-state checks apply exactly as for a password sign-in.
    /// </summary>
    public async Task<LoginResult> SsoLoginAsync(OidcIdentity id, bool requireVerifiedEmail, ClientInfo client, string? existingRawToken, CancellationToken ct = default)
    {
        var ipHit = await limiter.HitAsync($"sso:ip:{client.Ip ?? "unknown"}", LoginIpLimit, LoginWindow, ct);
        if (!ipHit.Allowed) throw DomainException.RateLimited(ipHit.RetryAfter);
        var now = clock.UtcNow; var email = NormalizeEmail(id.Email);

        async Task<LoginResult> Deny(string reason, User? u = null)
        {
            audit.Record("auth.sso.denied", "user", u?.Id.ToString(), Outcome.Failure, client, u?.Id, email.Length == 0 ? null : email, new Dictionary<string, string> { ["reason"] = reason });
            await db.SaveChangesAsync(ct);
            return new LoginResult(LoginStatus.SsoDenied, null, false, null);
        }

        var link = await db.UserIdentities.Include(i => i.User!).ThenInclude(u => u.MfaFactors).FirstOrDefaultAsync(i => i.Issuer == id.Issuer && i.Subject == id.Subject, ct);
        var user = link?.User;
        if (user is null)
        {
            if (email.Length == 0) return await Deny("no_email");
            if (requireVerifiedEmail && !id.EmailVerified) return await Deny("email_not_verified");
            user = await db.Users.Include(u => u.MfaFactors).FirstOrDefaultAsync(u => u.Email == email, ct);
            if (user is null) return await Deny("no_matching_user");
            if (await db.UserIdentities.AnyAsync(i => i.UserId == user.Id && i.Issuer == id.Issuer, ct)) return await Deny("different_identity_linked", user);
        }
        if (user.Status == UserStatus.Pending) return await Deny("invitation_not_accepted", user);
        if (user.Status != UserStatus.Active)
        {
            var st = user.Status == UserStatus.Suspended ? LoginStatus.Suspended : LoginStatus.Deactivated;
            audit.Record("auth.login.blocked", "user", user.Id.ToString(), Outcome.Failure, client, user.Id, user.Email, new Dictionary<string, string> { ["reason"] = st, ["method"] = "sso" });
            await db.SaveChangesAsync(ct);
            return new LoginResult(st, null, false, null);
        }
        if (user.LockedUntil > now)
        {
            audit.Record("auth.login.blocked", "user", user.Id.ToString(), Outcome.Failure, client, user.Id, user.Email, new Dictionary<string, string> { ["reason"] = "temporarily_locked", ["method"] = "sso" });
            await db.SaveChangesAsync(ct);
            return new LoginResult(LoginStatus.Locked, null, false, null);
        }

        if (!await TenantActiveAsync(user, ct))
        {
            audit.Record("auth.login.blocked", "user", user.Id.ToString(), Outcome.Failure, client, user.Id, user.Email, new Dictionary<string, string> { ["reason"] = "tenant_suspended", ["method"] = "sso" });
            await db.SaveChangesAsync(ct);
            return new LoginResult(LoginStatus.Suspended, null, false, null);
        }

        if (link is null)
        {
            link = new UserIdentity { UserId = user.Id, Issuer = id.Issuer, Subject = id.Subject, Email = email.Length == 0 ? null : email, CreatedAt = now };
            db.UserIdentities.Add(link);
            audit.Record("auth.sso.link", "user", user.Id.ToString(), Outcome.Success, client, user.Id, user.Email, new Dictionary<string, string> { ["issuer"] = id.Issuer });
        }
        link.LastLoginAt = now;
        return await CompleteLoginAsync(user, false, existingRawToken, client, await settings.GetForAsync(user.TenantId, ct), now, "sso", ct);
    }

    public async Task LogoutAsync(SessionPrincipal who, ClientInfo client, CancellationToken ct = default)
    {
        await sessions.RevokeAsync(who.SessionId, RevokeReason.User, ct);
        audit.Record("auth.logout", "session", who.SessionId.ToString(), Outcome.Success, client, who.UserId, who.Email);
        await db.SaveChangesAsync(ct);
    }

    public async Task<MeDto> MeAsync(SessionPrincipal who, CancellationToken ct = default)
    {
        var user = await db.Users.Include(u => u.MfaFactors).FirstAsync(u => u.Id == who.UserId, ct);
        return (await BuildMeAsync(user, who.Locked, user.MfaFactors.Any(f => f.ConfirmedAt is not null), ct)) with { ActingTenant = who.ActingTenantName };
    }

    public async Task LockAsync(SessionPrincipal who, ClientInfo client, CancellationToken ct = default)
    {
        var s = await db.Sessions.FirstAsync(x => x.Id == who.SessionId, ct);
        s.LockedAt ??= clock.UtcNow;
        audit.Record("auth.lock", "session", s.Id.ToString(), Outcome.Success, client, who.UserId, who.Email);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Unlock needs the password. Five failures revoke the session. Fails closed for MFA users until TOTP verification ships (slice 3).</summary>
    public async Task UnlockAsync(SessionPrincipal who, string? password, string? code, string? recoveryCode, ClientInfo client, CancellationToken ct = default)
    {
        await RequireReauthenticationAsync(who, password, code, recoveryCode, "auth.unlock", clearLock: true, client, ct);
    }

    public async Task ReauthAsync(SessionPrincipal who, string? password, string? code, string? recoveryCode, ClientInfo client, CancellationToken ct = default)
    {
        await RequireReauthenticationAsync(who, password, code, recoveryCode, "auth.reauth", clearLock: false, client, ct);
    }

    private async Task RequireReauthenticationAsync(SessionPrincipal who, string? password, string? code, string? recoveryCode, string action, bool clearLock, ClientInfo client, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(password) || password.Length > 256) throw DomainException.Validation("Enter your password.");
        var hit = await limiter.HitAsync($"{action}:{who.SessionId}", UnlockLimit, UnlockWindow, ct);
        if (!hit.Allowed)
        {
            await sessions.RevokeAsync(who.SessionId, RevokeReason.TooManyUnlockFailures, ct);
            audit.Record(action, "session", who.SessionId.ToString(), Outcome.Failure, client, who.UserId, who.Email, new Dictionary<string, string> { ["reason"] = "too_many_failures_session_revoked" });
            audit.Security("session.revoked", Severity.Medium, "Session revoked after repeated failed re-authentication", who.UserId);
            await db.SaveChangesAsync(ct);
            throw DomainException.Unauthenticated("Too many failed attempts. Please sign in again.");
        }

        var user = await db.Users.Include(u => u.Credential).Include(u => u.MfaFactors).FirstAsync(u => u.Id == who.UserId, ct);
        if (user.Credential is null || !hasher.Verify(password, user.Credential.PasswordHash))
        {
            audit.Record(action, "session", who.SessionId.ToString(), Outcome.Failure, client, who.UserId, who.Email, new Dictionary<string, string> { ["reason"] = "bad_password" });
            await db.SaveChangesAsync(ct);
            throw DomainException.InvalidCredentials();
        }
        if (user.MfaFactors.Any(f => f.ConfirmedAt is not null))
        {
            // Password alone is never enough for an MFA user: the second factor is part of re-authentication.
            if (string.IsNullOrWhiteSpace(code) && string.IsNullOrWhiteSpace(recoveryCode))
                throw DomainException.Validation("Enter your authenticator code.", new Dictionary<string, string> { ["code"] = "Enter your authenticator code." });
            try { await mfaService.CheckSecondFactorAsync(user.Id, code, recoveryCode, client, ct); }
            catch (DomainException e) when (e.Code == ErrorCodes.InvalidCode)
            {
                audit.Record(action, "session", who.SessionId.ToString(), Outcome.Failure, client, who.UserId, who.Email, new Dictionary<string, string> { ["reason"] = "bad_second_factor" });
                await db.SaveChangesAsync(ct);
                throw;
            }
        }

        var s = await db.Sessions.FirstAsync(x => x.Id == who.SessionId, ct);
        if (clearLock) s.LockedAt = null;
        s.ReauthAt = clock.UtcNow;
        audit.Record(action, "session", s.Id.ToString(), Outcome.Success, client, who.UserId, who.Email);
        await db.SaveChangesAsync(ct);
        await limiter.ResetAsync($"{action}:{who.SessionId}", ct);
    }

    public async Task<MeDto> MeByUserIdAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await db.Users.Include(u => u.MfaFactors).FirstAsync(u => u.Id == userId, ct);
        return await BuildMeAsync(user, false, user.MfaFactors.Any(f => f.ConfirmedAt is not null), ct);
    }

    private async Task<MeDto> BuildMeAsync(User user, bool locked, bool mfa, CancellationToken ct)
    {
        var roleNames = await (from ur in db.UserRoles.AsNoTracking() where ur.UserId == user.Id
                               join r in db.Roles on ur.RoleId equals r.Id where r.ArchivedAt == null select r.Key).ToListAsync(ct);
        var perms = await authz.GetEffectivePermissionsAsync(user.Id, ct);
        var avatarAt = await db.Avatars.AsNoTracking().Where(a => a.UserId == user.Id).Select(a => (DateTimeOffset?)a.CreatedAt).FirstOrDefaultAsync(ct);
        var tenant = await tenants.GetAsync(user.TenantId, ct) ?? throw new InvalidOperationException("The person's organisation is missing.");
        var language = await db.Profiles.AsNoTracking().Where(p => p.UserId == user.Id).Select(p => p.Language).FirstOrDefaultAsync(ct) ?? "en";
        var vendor = await db.VendorUsers.AsNoTracking().Where(v => v.UserId == user.Id).Select(v => db.Vendors.Where(x => x.Id == v.VendorId).Select(x => x.Name).FirstOrDefault()).FirstOrDefaultAsync(ct);
        return new MeDto(user.Id, user.Email, user.Name, [.. roleNames.Order()], [.. perms.Order()], locked, mfa, avatarAt?.ToUnixTimeMilliseconds(), tenant.Name, tenant.IsPlatform, null, language, vendor);
    }
}
