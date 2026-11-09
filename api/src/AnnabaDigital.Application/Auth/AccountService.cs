using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Application.Common;
using AnnabaDigital.Application.Email;
using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;
using AnnabaDigital.Domain.Security;

namespace AnnabaDigital.Application.Auth;

public sealed record RegisterRequest(string? Name, string? Email, string? Password);

/// <summary>Self-service account lifecycle: register → verify email → forgot/reset password. Every public method is enumeration-safe.</summary>
public sealed partial class AccountService(
    IAppDb db, IPasswordHasher hasher, ITokenService tokens, IRateLimiter limiter, IClock clock, SettingsProvider settings,
    PasswordPolicy policy, AuditWriter audit, SessionService sessions, IEmailQueue email, IAppLinks links)
{
    public static readonly TimeSpan VerifyTtl = TimeSpan.FromHours(24);
    public static readonly TimeSpan ResetTtl = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    [GeneratedRegex(@"^[^\s@]+@[^\s@]+\.[^\s@]+$")] private static partial Regex EmailShape();

    private async Task Throttle(string key, int limit, CancellationToken ct)
    {
        var hit = await limiter.HitAsync(key, limit, Window, ct);
        if (!hit.Allowed) throw DomainException.RateLimited(hit.RetryAfter);
    }

    private static string Link(IAppLinks l, string path, string token) => $"{l.Origin.TrimEnd('/')}{path}?token={Uri.EscapeDataString(token)}";

    /// <summary>Creates a fresh verification token and invalidates earlier unconsumed ones. Returns the raw token (only ever emailed).</summary>
    private async Task<string> IssueVerificationTokenAsync(User user, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var old = await db.EmailVerificationTokens.Where(t => t.UserId == user.Id && t.ConsumedAt == null).ToListAsync(ct);
        foreach (var t in old) t.ConsumedAt = now;
        var raw = tokens.NewToken();
        db.EmailVerificationTokens.Add(new EmailVerificationToken { UserId = user.Id, TokenHash = tokens.Hash(raw), ExpiresAt = now + VerifyTtl, CreatedAt = now });
        return raw;
    }

    private async Task<string> IssueResetTokenAsync(User user, ClientInfo client, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var old = await db.PasswordResetTokens.Where(t => t.UserId == user.Id && t.ConsumedAt == null).ToListAsync(ct);
        foreach (var t in old) t.ConsumedAt = now;
        var raw = tokens.NewToken();
        db.PasswordResetTokens.Add(new PasswordResetToken { UserId = user.Id, TokenHash = tokens.Hash(raw), ExpiresAt = now + ResetTtl, RequestIp = client.Ip, CreatedAt = now });
        return raw;
    }

    // ---------------------------------------------------------------- register
    /// <summary>
    /// AUTH-002. 404 unless registration is open. The caller always gets the same "check your inbox" answer whether or not the address
    /// already has an account; existing owners get a notice email instead.
    /// </summary>
    public async Task RegisterAsync(RegisterRequest req, ClientInfo client, CancellationToken ct = default)
    {
        var s = await settings.GetAsync(ct);
        if (s.RegistrationMode != "open") throw DomainException.NotFound("Registration"); // invitation-only: the endpoint does not exist

        var emailAddr = AuthService.NormalizeEmail(req.Email);
        var name = (req.Name ?? "").Trim();
        var fields = new Dictionary<string, string>();
        if (name.Length < 2 || name.Length > 200) fields["name"] = "Enter your full name.";
        if (emailAddr.Length > 254 || !EmailShape().IsMatch(emailAddr)) fields["email"] = "Enter a valid email address.";
        if (fields.Count > 0) throw DomainException.Validation("Please correct the highlighted fields.", fields);

        await Throttle($"register:ip:{client.Ip ?? "unknown"}", 5, ct);
        await Throttle($"register:acct:{tokens.Hash(emailAddr)}", 3, ct);
        await policy.ValidateAsync(req.Password, emailAddr, name, s, ct);

        var passwordHash = hasher.Hash(req.Password!); // always hash so both branches cost the same
        var existing = await db.Users.FirstOrDefaultAsync(u => u.Email == emailAddr, ct);
        if (existing is not null)
        {
            audit.Record("auth.register", "user", null, Outcome.Failure, client, null, emailAddr, new Dictionary<string, string> { ["reason"] = "duplicate_email (generic response sent)" });
            var reset = existing.Status == UserStatus.Active ? await IssueResetTokenAsync(existing, client, ct) : null;
            await db.SaveChangesAsync(ct);
            if (reset is not null) email.Enqueue(EmailTemplates.AlreadyRegistered(existing.Email, existing.Name, Link(links, "/reset-password", reset)));
            return;
        }

        var now = clock.UtcNow;
        var role = await db.Roles.FirstOrDefaultAsync(r => r.TenantId == Tenant.DefaultId && r.Key == SystemRoles.User && r.ArchivedAt == null, ct) ?? throw new InvalidOperationException("System role 'user' is missing; run the seed.");
        var user = new User { Email = emailAddr, Name = name, Status = UserStatus.Pending, CreatedAt = now, UpdatedAt = now };
        user.Credential = new UserCredential { PasswordHash = passwordHash, PasswordChangedAt = now };
        user.Profile = new UserProfile();
        user.UserRoles.Add(new UserRole { RoleId = role.Id, AssignedAt = now });
        db.Users.Add(user);
        var raw = await IssueVerificationTokenAsync(user, ct);
        audit.Record("auth.register", "user", user.Id.ToString(), Outcome.Success, client, user.Id, emailAddr);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { return; } // lost a race with another registration of the same email: same generic outcome, no leak
        email.Enqueue(EmailTemplates.VerifyEmail(emailAddr, name, Link(links, "/verify-email", raw)));
    }

    // ---------------------------------------------------------------- verify email
    public async Task VerifyEmailAsync(string? token, ClientInfo client, CancellationToken ct = default)
    {
        await Throttle($"verify:ip:{client.Ip ?? "unknown"}", 20, ct); // token guessing is infeasible (256-bit) but we still bound it
        var invalid = DomainException.Expired("This verification link is invalid or has expired.");
        if (string.IsNullOrEmpty(token) || token.Length > 200) throw invalid;
        var hash = tokens.Hash(token);
        var now = clock.UtcNow;
        var row = await db.EmailVerificationTokens.AsNoTracking().FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (row is null || row.ConsumedAt is not null || row.ExpiresAt <= now) throw invalid;

        (string Old, string Name, string New)? pendingNotice = null;
        await db.InTransactionAsync(async () =>
        {
            // Atomic single-use consumption: of two concurrent requests exactly one updates a row.
            var consumed = await db.EmailVerificationTokens.Where(t => t.TokenHash == hash && t.ConsumedAt == null && t.ExpiresAt > now)
                .ExecuteUpdateAsync(u => u.SetProperty(t => t.ConsumedAt, now), ct);
            if (consumed == 0) throw invalid;
            var user = await db.Users.FirstAsync(u => u.Id == row.UserId, ct);
            if (row.NewEmail is not null)
            {
                // Email-change confirmation: proves ownership of the NEW address. Re-check that it is still free at the moment of switching.
                if (user.Status is UserStatus.Suspended or UserStatus.Deactivated || await db.Users.AnyAsync(x => x.Email == row.NewEmail && x.Id != user.Id, ct)) throw invalid;
                var old = user.Email;
                user.Email = row.NewEmail; user.EmailVerifiedAt = now; user.UpdatedAt = now; user.Version++;
                audit.Record("profile.email_changed", "user", user.Id.ToString(), Outcome.Success, client, user.Id, row.NewEmail, diff: [new AuditDiff("email", old, row.NewEmail)]);
                audit.Security("email.changed", Severity.Medium, "Account email address changed after confirmation", user.Id);
                try { await db.SaveChangesAsync(ct); } catch (DbUpdateException) { throw invalid; } // lost a race for the address
                pendingNotice = (old, user.Name, row.NewEmail);
                return 0;
            }
            user.EmailVerifiedAt ??= now;
            // Only accounts that already own credentials (self-registered) activate here. Admin-created placeholders have no password and
            // become active solely by accepting their invitation. Suspended/deactivated stay as they are.
            if (user.Status == UserStatus.Pending && await db.Credentials.AnyAsync(c => c.UserId == user.Id, ct)) user.Status = UserStatus.Active;
            user.UpdatedAt = now;
            audit.Record("auth.email.verify", "user", user.Id.ToString(), Outcome.Success, client, user.Id, user.Email);
            await db.SaveChangesAsync(ct);
            return 0;
        }, ct);
        if (pendingNotice is { } n) email.Enqueue(EmailTemplates.EmailChanged(n.Old, n.Name, n.New)); // tell the previous address

    }

    public async Task ResendVerificationAsync(string? emailInput, ClientInfo client, CancellationToken ct = default)
    {
        var addr = AuthService.NormalizeEmail(emailInput);
        await Throttle($"resend:ip:{client.Ip ?? "unknown"}", 10, ct);
        await Throttle($"resend:acct:{tokens.Hash(addr)}", 3, ct);
        var user = addr.Length is > 0 and <= 254 ? await db.Users.FirstOrDefaultAsync(u => u.Email == addr, ct) : null;
        if (user is null || user.Status != UserStatus.Pending) return; // same silent outcome for unknown / already active / blocked
        var raw = await IssueVerificationTokenAsync(user, ct);
        audit.Record("auth.email.resend", "user", user.Id.ToString(), Outcome.Success, client, user.Id, user.Email);
        await db.SaveChangesAsync(ct);
        email.Enqueue(EmailTemplates.VerifyEmail(user.Email, user.Name, Link(links, "/verify-email", raw)));
    }

    // ---------------------------------------------------------------- forgot / reset
    public async Task ForgotPasswordAsync(string? emailInput, ClientInfo client, CancellationToken ct = default)
    {
        var addr = AuthService.NormalizeEmail(emailInput);
        if (addr.Length == 0 || addr.Length > 254 || !EmailShape().IsMatch(addr)) throw DomainException.Validation("Enter a valid email address.", new Dictionary<string, string> { ["email"] = "Enter a valid email address." });
        await Throttle($"forgot:ip:{client.Ip ?? "unknown"}", 10, ct);
        await Throttle($"forgot:acct:{tokens.Hash(addr)}", 3, ct);

        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == addr, ct);
        EmailMessage? message = null;
        if (user is { Status: UserStatus.Active })
        {
            var raw = await IssueResetTokenAsync(user, client, ct);
            message = EmailTemplates.ResetPassword(user.Email, user.Name, Link(links, "/reset-password", raw));
            audit.Record("auth.password.forgot", "user", user.Id.ToString(), Outcome.Success, client, user.Id, user.Email);
        }
        else if (user is { Status: UserStatus.Pending } && await db.Credentials.AnyAsync(c => c.UserId == user.Id, ct))
        {
            var raw = await IssueVerificationTokenAsync(user, ct); // an unverified account must verify first
            message = EmailTemplates.VerifyEmail(user.Email, user.Name, Link(links, "/verify-email", raw));
            audit.Record("auth.password.forgot", "user", user.Id.ToString(), Outcome.Success, client, user.Id, user.Email, new Dictionary<string, string> { ["note"] = "pending account → verification email" });
        }
        else audit.Record("auth.password.forgot", "user", user?.Id.ToString(), Outcome.Failure, client, user?.Id, addr, new Dictionary<string, string> { ["reason"] = user is null ? "unknown_account" : "account_not_active" });
        await db.SaveChangesAsync(ct);
        if (message is not null) email.Enqueue(message);
    }

    public async Task ResetPasswordAsync(string? token, string? newPassword, ClientInfo client, CancellationToken ct = default)
    {
        await Throttle($"reset:ip:{client.Ip ?? "unknown"}", 10, ct);
        var invalid = DomainException.Expired("This reset link is invalid or has expired.");
        if (string.IsNullOrEmpty(token) || token.Length > 200) throw invalid;
        var hash = tokens.Hash(token);
        var now = clock.UtcNow;
        var row = await db.PasswordResetTokens.AsNoTracking().FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (row is null || row.ConsumedAt is not null || row.ExpiresAt <= now) throw invalid;
        var user = await db.Users.Include(u => u.Credential).FirstOrDefaultAsync(u => u.Id == row.UserId, ct);
        if (user?.Credential is null || user.Status != UserStatus.Active) throw invalid;

        // Policy first: a rejected password must NOT burn the one-time token.
        var s = await settings.GetForAsync(user.TenantId, ct); // anonymous: the person's own organisation sets the password rules
        await policy.ValidateAsync(newPassword, user.Email, user.Name, s, ct);
        var newHash = hasher.Hash(newPassword!);

        await db.InTransactionAsync(async () =>
        {
            var consumed = await db.PasswordResetTokens.Where(t => t.TokenHash == hash && t.ConsumedAt == null && t.ExpiresAt > now)
                .ExecuteUpdateAsync(u => u.SetProperty(t => t.ConsumedAt, now), ct);
            if (consumed == 0) throw invalid;
            await db.PasswordResetTokens.Where(t => t.UserId == user.Id && t.ConsumedAt == null).ExecuteUpdateAsync(u => u.SetProperty(t => t.ConsumedAt, now), ct);

            user.Credential.PasswordHash = newHash; user.Credential.PasswordChangedAt = now; user.Credential.MustChange = false;
            user.FailedLoginCount = 0; user.LockedUntil = null; user.UpdatedAt = now;
            var revoked = await sessions.RevokeAllForUserAsync(user.Id, RevokeReason.PasswordReset, null, ct); // policy: reset ends every session
            audit.Record("auth.password.reset", "user", user.Id.ToString(), Outcome.Success, client, user.Id, user.Email, new Dictionary<string, string> { ["sessionsRevoked"] = revoked.ToString() });
            audit.Security("password.reset", Severity.Low, $"Password reset completed; {revoked} session(s) revoked", user.Id);
            await db.SaveChangesAsync(ct);
            return 0;
        }, ct);
        email.Enqueue(EmailTemplates.PasswordChanged(user.Email, user.Name));
    }
}
