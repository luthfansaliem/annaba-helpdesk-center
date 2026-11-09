namespace AnnabaDigital.Domain;

/// <summary>Stable error codes shared with the HTTP envelope (see docs/08-api-spec.md).</summary>
public static class ErrorCodes
{
    public const string Unauthenticated = "unauthenticated";
    public const string InvalidCredentials = "invalid_credentials";
    public const string Denied = "denied";
    public const string Locked = "locked";
    public const string NotFound = "not_found";
    public const string Validation = "validation";
    public const string Conflict = "conflict";
    public const string Invariant = "invariant";
    public const string Expired = "expired";
    public const string RateLimited = "rate_limited";
    public const string MfaRequired = "mfa_required";
    public const string MfaEnrollmentRequired = "mfa_enrollment_required";
    public const string InvalidCode = "invalid_code";
    public const string ReauthRequired = "reauth_required";
}

public sealed class DomainException(string code, string message, int status, IDictionary<string, string>? fields = null, TimeSpan? retryAfter = null) : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
    public IDictionary<string, string>? Fields { get; } = fields;
    public TimeSpan? RetryAfter { get; } = retryAfter;

    public static DomainException Unauthenticated(string message = "Authentication required.") => new(ErrorCodes.Unauthenticated, message, 401);
    public static DomainException InvalidCredentials() => new(ErrorCodes.InvalidCredentials, "Email or password is incorrect.", 401);
    public static DomainException InvalidCode() => new(ErrorCodes.InvalidCode, "That code is incorrect or has already been used.", 401);
    public static DomainException ReauthRequired() => new(ErrorCodes.ReauthRequired, "Please confirm your password again to continue.", 403);
    public static DomainException MfaEnrollmentRequired() => new(ErrorCodes.MfaEnrollmentRequired, "Your organisation requires two-step verification. Set it up to continue.", 403);
    public static DomainException DeniedBecause(string reason) => new(ErrorCodes.Denied, reason, 403);
    public static DomainException Denied(string permission) => new(ErrorCodes.Denied, $"You don't have permission ({permission}).", 403);
    public static DomainException SessionLocked() => new(ErrorCodes.Locked, "Session is locked. Unlock to continue.", 423);
    public static DomainException NotFound(string what = "Resource") => new(ErrorCodes.NotFound, $"{what} not found.", 404);
    public static DomainException Validation(string message, IDictionary<string, string>? fields = null) => new(ErrorCodes.Validation, message, 422, fields);
    public static DomainException Conflict(string message) => new(ErrorCodes.Conflict, message, 409);
    public static DomainException Invariant(string message) => new(ErrorCodes.Invariant, message, 409);
    public static DomainException Expired(string message) => new(ErrorCodes.Expired, message, 410);
    public static DomainException RateLimited(TimeSpan retryAfter) => new(ErrorCodes.RateLimited, "Too many attempts. Try again later.", 429, retryAfter: retryAfter);
}
