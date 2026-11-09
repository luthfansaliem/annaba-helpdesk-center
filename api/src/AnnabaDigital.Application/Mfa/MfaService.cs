using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Auth;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Application.Email;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;

namespace AnnabaDigital.Application.Mfa;

public sealed record MfaStatus(bool Enrolled, DateTimeOffset? ConfirmedAt, int RecoveryCodesRemaining, bool Required);
public sealed record EnrollStart(string Secret, string OtpAuthUri);
public sealed record VerifiedSession(string RawToken, bool Remember, Guid UserId);
public enum SecondFactorKind { Totp, Recovery }

/// <summary>AUTH-007: TOTP enrolment, second-factor verification, recovery codes. Secrets are encrypted at rest; recovery codes are keyed-hashed; nothing is ever returned twice.</summary>
public sealed class MfaService(
    IAppDb db, ISecretProtector protector, IPasswordHasher hasher, IClock clock, SettingsProvider settings, IRateLimiter limiter,
    AuditWriter audit, SessionService sessions, IEmailQueue email)
{
    public const string Issuer = "Annaba Digital";
    public static readonly TimeSpan ReauthWindow = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(15);
    private const int AttemptLimit = 5;

    private static byte[] Aad(Guid userId) => [.. userId.ToByteArray(), .. "totp"u8.ToArray()];

    private void RequireRecentReauth(SessionPrincipal who)
    {
        if (who.ReauthAt is not { } t || clock.UtcNow - t > ReauthWindow) throw DomainException.ReauthRequired();
    }

    private async Task Throttle(string key, ClientInfo client, SessionPrincipal? who, CancellationToken ct)
    {
        var hit = await limiter.HitAsync(key, AttemptLimit, Window, ct);
        if (hit.Allowed) return;
        if (who is not null)
        {
            await sessions.RevokeAsync(who.SessionId, RevokeReason.TooManyUnlockFailures, ct);
            audit.Record("auth.mfa.throttled", "session", who.SessionId.ToString(), Outcome.Failure, client, who.UserId, who.Email, new Dictionary<string, string> { ["reason"] = "too_many_failures_session_revoked" });
            audit.Security("session.revoked", Severity.Medium, "Session revoked after repeated failed second-factor attempts", who.UserId);
            await db.SaveChangesAsync(ct);
        }
        throw DomainException.RateLimited(hit.RetryAfter);
    }

    public async Task<MfaStatus> StatusAsync(Guid userId, CancellationToken ct = default)
    {
        var s = await settings.GetAsync(ct);
        var factor = await db.MfaFactors.AsNoTracking().FirstOrDefaultAsync(f => f.UserId == userId && f.ConfirmedAt != null, ct);
        var remaining = factor is null ? 0 : await db.MfaRecoveryCodes.CountAsync(c => c.UserId == userId && c.UsedAt == null, ct);
        return new MfaStatus(factor is not null, factor?.ConfirmedAt, remaining, s.MfaPolicy == "required");
    }

    // ---------------------------------------------------------------- enrolment
    /// <summary>Step 1. Requires a recent re-authentication. The seed is returned once (to render the QR) and stored encrypted.</summary>
    public async Task<EnrollStart> EnrollAsync(SessionPrincipal who, ClientInfo client, CancellationToken ct = default)
    {
        RequireRecentReauth(who);
        if (await db.MfaFactors.AnyAsync(f => f.UserId == who.UserId && f.ConfirmedAt != null, ct)) throw DomainException.Conflict("Two-step verification is already enabled.");

        var secret = Totp.NewSecret();
        var enc = protector.Protect(secret, Aad(who.UserId));
        var stale = await db.MfaFactors.Where(f => f.UserId == who.UserId && f.ConfirmedAt == null).ToListAsync(ct); // restarting enrolment replaces the old seed
        db.MfaFactors.RemoveRange(stale);
        await db.SaveChangesAsync(ct); // unique (user, type): remove before add
        db.MfaFactors.Add(new MfaFactor { UserId = who.UserId, SecretCiphertext = enc.Ciphertext, SecretNonce = enc.Nonce, KeyVersion = enc.KeyVersion, CreatedAt = clock.UtcNow });
        audit.Record("mfa.enroll.start", "user", who.UserId.ToString(), Outcome.Success, client, who.UserId, who.Email);
        await db.SaveChangesAsync(ct);
        var b32 = Base32.Encode(secret);
        return new EnrollStart(b32, Totp.OtpAuthUri(Issuer, who.Email, b32));
    }

    /// <summary>Step 2. A correct code proves the user's app holds the seed; only then is MFA active. Returns the one-time recovery codes.</summary>
    public async Task<string[]> ConfirmAsync(SessionPrincipal who, string? code, ClientInfo client, CancellationToken ct = default)
    {
        await Throttle($"mfa-confirm:{who.UserId}", client, null, ct);
        var factor = await db.MfaFactors.FirstOrDefaultAsync(f => f.UserId == who.UserId && f.ConfirmedAt == null, ct) ?? throw DomainException.Conflict("Start enrolment first.");
        var secret = protector.Unprotect(new ProtectedSecret(factor.SecretCiphertext, factor.SecretNonce, factor.KeyVersion), Aad(who.UserId));
        if (!Totp.TryVerify(secret, code?.Trim(), clock.UtcNow, 0, out var step))
        {
            audit.Record("mfa.enroll", "user", who.UserId.ToString(), Outcome.Failure, client, who.UserId, who.Email, new Dictionary<string, string> { ["reason"] = "bad_code" });
            await db.SaveChangesAsync(ct);
            throw DomainException.InvalidCode();
        }
        var codes = await db.InTransactionAsync(async () =>
        {
            factor.ConfirmedAt = clock.UtcNow; factor.LastUsedStep = step;
            var list = await ReplaceRecoveryCodesAsync(who.UserId, ct);
            var s = await db.Sessions.FirstAsync(x => x.Id == who.SessionId, ct);
            s.MfaVerifiedAt = clock.UtcNow;
            audit.Record("mfa.enroll", "user", who.UserId.ToString(), Outcome.Success, client, who.UserId, who.Email);
            audit.Security("mfa.enrolled", Severity.Low, "Two-step verification enabled", who.UserId);
            await db.SaveChangesAsync(ct);
            return list;
        }, ct);
        email.Enqueue(EmailTemplates.MfaEnabled(who.Email, who.Name));
        return codes;
    }

    private async Task<string[]> ReplaceRecoveryCodesAsync(Guid userId, CancellationToken ct)
    {
        await db.MfaRecoveryCodes.Where(c => c.UserId == userId).ExecuteDeleteAsync(ct);
        var plain = Enumerable.Range(0, RecoveryCodes.Count).Select(_ => RecoveryCodes.New()).ToArray();
        foreach (var c in plain)
            db.MfaRecoveryCodes.Add(new MfaRecoveryCode { UserId = userId, CodeHash = protector.KeyedHash("recovery", RecoveryCodes.Normalize(c)), CreatedAt = clock.UtcNow });
        return plain;
    }

    public async Task<string[]> RegenerateRecoveryCodesAsync(SessionPrincipal who, ClientInfo client, CancellationToken ct = default)
    {
        RequireRecentReauth(who);
        if (!await db.MfaFactors.AnyAsync(f => f.UserId == who.UserId && f.ConfirmedAt != null, ct)) throw DomainException.Conflict("Two-step verification is not enabled.");
        var codes = await db.InTransactionAsync(async () =>
        {
            var list = await ReplaceRecoveryCodesAsync(who.UserId, ct);
            audit.Record("mfa.recovery.regenerate", "user", who.UserId.ToString(), Outcome.Success, client, who.UserId, who.Email);
            audit.Security("mfa.recovery_regenerated", Severity.Low, "Recovery codes regenerated; previous codes are invalid", who.UserId);
            await db.SaveChangesAsync(ct);
            return list;
        }, ct);
        return codes;
    }

    // ---------------------------------------------------------------- verification (shared by login, unlock, reauth, disable)
    /// <summary>
    /// Verifies a TOTP code *or* a recovery code for <paramref name="userId"/>. TOTP steps are single-use (replay-proof); recovery codes are
    /// consumed atomically. Throws <c>invalid_code</c> otherwise. The caller owns rate limiting and failure counters.
    /// </summary>
    public async Task<(SecondFactorKind Kind, int RecoveryRemaining)> CheckSecondFactorAsync(Guid userId, string? code, string? recoveryCode, ClientInfo client, CancellationToken ct = default)
    {
        var factor = await db.MfaFactors.AsNoTracking().FirstOrDefaultAsync(f => f.UserId == userId && f.ConfirmedAt != null, ct) ?? throw DomainException.Validation("Two-step verification is not enabled.");

        if (!string.IsNullOrWhiteSpace(recoveryCode))
        {
            if (!RecoveryCodes.LooksLikeCode(recoveryCode)) throw DomainException.InvalidCode();
            var hash = protector.KeyedHash("recovery", RecoveryCodes.Normalize(recoveryCode));
            var now = clock.UtcNow;
            var used = await db.MfaRecoveryCodes.Where(c => c.UserId == userId && c.CodeHash == hash && c.UsedAt == null).ExecuteUpdateAsync(u => u.SetProperty(c => c.UsedAt, now), ct);
            if (used == 0) throw DomainException.InvalidCode();
            var remaining = await db.MfaRecoveryCodes.CountAsync(c => c.UserId == userId && c.UsedAt == null, ct);
            audit.Security("mfa.recovery_used", Severity.Medium, $"A recovery code was used ({remaining} left)", userId);
            var u = await db.Users.AsNoTracking().FirstAsync(x => x.Id == userId, ct);
            email.Enqueue(EmailTemplates.RecoveryCodeUsed(u.Email, u.Name, remaining));
            return (SecondFactorKind.Recovery, remaining);
        }

        var secret = protector.Unprotect(new ProtectedSecret(factor.SecretCiphertext, factor.SecretNonce, factor.KeyVersion), Aad(userId));
        if (!Totp.TryVerify(secret, code?.Trim(), clock.UtcNow, factor.LastUsedStep, out var step)) throw DomainException.InvalidCode();
        // Atomic replay guard: only the request that advances last_used_step wins (two simultaneous uses of one code cannot both pass).
        var advanced = await db.MfaFactors.Where(f => f.Id == factor.Id && f.LastUsedStep < step).ExecuteUpdateAsync(u => u.SetProperty(f => f.LastUsedStep, step), ct);
        if (advanced == 0) throw DomainException.InvalidCode();
        return (SecondFactorKind.Totp, -1);
    }

    /// <summary>Second step of sign-in. On success the pre-auth session is replaced by a brand-new one (rotation).</summary>
    public async Task<VerifiedSession> VerifyLoginAsync(SessionPrincipal who, string? code, string? recoveryCode, ClientInfo client, CancellationToken ct = default)
    {
        await Throttle($"mfa:{who.SessionId}", client, who, ct);
        var s = await settings.GetAsync(ct);
        var user = await db.Users.FirstAsync(u => u.Id == who.UserId, ct);
        try { await CheckSecondFactorAsync(who.UserId, code, recoveryCode, client, ct); }
        catch (DomainException e) when (e.Code == ErrorCodes.InvalidCode)
        {
            user.FailedLoginCount++; // wrong second factors count toward the account lockout, like wrong passwords
            if (user.FailedLoginCount >= s.LockoutAttempts)
            {
                user.LockedUntil = clock.UtcNow.AddMinutes(s.LockoutMinutes);
                await sessions.RevokeAsync(who.SessionId, RevokeReason.TooManyUnlockFailures, ct);
                audit.Security("account.locked", Severity.High, $"Account locked after {user.FailedLoginCount} failed attempts (second factor)", user.Id);
            }
            audit.Record("auth.mfa.verify", "session", who.SessionId.ToString(), Outcome.Failure, client, who.UserId, who.Email);
            audit.Security("login.failed", Severity.Low, "Failed second-factor verification", who.UserId);
            await db.SaveChangesAsync(ct);
            throw;
        }

        user.FailedLoginCount = 0; user.LockedUntil = null;
        var created = await sessions.CreateAsync(user, who.Remember, mfaRequired: false, client, ct);
        created.Session.MfaVerifiedAt = clock.UtcNow;
        await sessions.RevokeAsync(who.SessionId, RevokeReason.Rotated, ct);
        audit.Record("auth.mfa.verify", "session", created.Session.Id.ToString(), Outcome.Success, client, who.UserId, who.Email, new Dictionary<string, string> { ["method"] = string.IsNullOrWhiteSpace(recoveryCode) ? "totp" : "recovery" });
        await db.SaveChangesAsync(ct);
        return new VerifiedSession(created.RawToken, who.Remember, who.UserId);
    }

    // ---------------------------------------------------------------- disable
    public async Task DisableAsync(SessionPrincipal who, string? password, string? code, string? recoveryCode, ClientInfo client, CancellationToken ct = default)
    {
        var s = await settings.GetAsync(ct);
        if (s.MfaPolicy == "required") throw DomainException.Invariant("Your organisation requires two-step verification, so it can't be turned off.");
        await Throttle($"mfa-disable:{who.UserId}", client, who, ct);

        var user = await db.Users.Include(u => u.Credential).FirstAsync(u => u.Id == who.UserId, ct);
        if (string.IsNullOrEmpty(password) || user.Credential is null || !hasher.Verify(password, user.Credential.PasswordHash))
        {
            audit.Record("mfa.disable", "user", user.Id.ToString(), Outcome.Failure, client, user.Id, user.Email, new Dictionary<string, string> { ["reason"] = "bad_password" });
            await db.SaveChangesAsync(ct);
            throw DomainException.InvalidCredentials();
        }
        try { await CheckSecondFactorAsync(user.Id, code, recoveryCode, client, ct); }
        catch (DomainException e) when (e.Code == ErrorCodes.InvalidCode)
        {
            audit.Record("mfa.disable", "user", user.Id.ToString(), Outcome.Failure, client, user.Id, user.Email, new Dictionary<string, string> { ["reason"] = "bad_code" });
            await db.SaveChangesAsync(ct);
            throw;
        }

        await db.InTransactionAsync(async () =>
        {
            await db.MfaRecoveryCodes.Where(c => c.UserId == user.Id).ExecuteDeleteAsync(ct);
            await db.MfaFactors.Where(f => f.UserId == user.Id).ExecuteDeleteAsync(ct);
            var others = await sessions.RevokeAllForUserAsync(user.Id, "mfa_disabled", who.SessionId, ct);
            audit.Record("mfa.disable", "user", user.Id.ToString(), Outcome.Success, client, user.Id, user.Email, new Dictionary<string, string> { ["otherSessionsRevoked"] = others.ToString() });
            audit.Security("mfa.removed", Severity.Medium, "Two-step verification disabled after re-authentication", user.Id);
            await db.SaveChangesAsync(ct);
            return 0;
        }, ct);
        email.Enqueue(EmailTemplates.MfaDisabled(user.Email, user.Name));
    }
}
