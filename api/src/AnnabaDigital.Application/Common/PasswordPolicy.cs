using AnnabaDigital.Application.Abstractions;
using AnnabaDigital.Domain;

namespace AnnabaDigital.Application.Common;

/// <summary>
/// NIST 800-63B style policy (docs/04 D-14): length over composition rules, block common/contextual/breached passwords.
/// Errors are returned as one field message so the UI can show it under the password input.
/// </summary>
public sealed class PasswordPolicy(IBreachedPasswordChecker breach)
{
    public const int MaxLength = 128;

    private static readonly HashSet<string> Common = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "password1", "password12", "password123", "passw0rd", "p@ssw0rd", "passw0rd!", "123456", "1234567", "12345678", "123456789", "1234567890", "12345678910",
        "qwerty", "qwertyuiop", "qwerty123", "abc123", "abcdef", "111111", "000000", "letmein", "welcome", "welcome1", "admin", "administrator", "admin123", "root", "toor",
        "iloveyou", "monkey", "dragon", "football", "baseball", "master", "sunshine", "princess", "trustno1", "changeme", "default", "secret", "login", "annaba", "annabadigital", "annabadigital123",
        "passwordpassword", "123456789012", "qwertyuiopasdf", "aaaaaaaaaaaa", "000000000000", "111111111111", "correcthorsebatterystaple",
    };

    public async Task ValidateAsync(string? password, string email, string? name, SecuritySettings settings, CancellationToken ct = default)
    {
        string? problem = null;
        if (string.IsNullOrEmpty(password)) problem = "Enter a password.";
        else if (password.Length < settings.PasswordMinLength) problem = $"Use at least {settings.PasswordMinLength} characters.";
        else if (password.Length > MaxLength) problem = $"Use at most {MaxLength} characters.";
        else if (IsCommon(password)) problem = "That password is too common. Choose something harder to guess.";
        else if (ContainsPersonalInfo(password, email, name)) problem = "Your password shouldn't contain your name or email.";
        else if (new HashSet<char>(password).Count < 5) problem = "Use a more varied password.";
        else if (settings.RequireBreachCheck && await breach.IsBreachedAsync(password, ct)) problem = "That password has appeared in a data breach. Choose a different one.";

        if (problem is not null) throw DomainException.Validation("Password does not meet the policy.", new Dictionary<string, string> { ["password"] = problem });
    }

    private static bool IsCommon(string p)
    {
        var lower = p.ToLowerInvariant();
        if (Common.Contains(lower)) return true;
        var stripped = new string(lower.Where(char.IsLetter).ToArray());
        return stripped.Length >= 6 && Common.Contains(stripped);
    }

    private static bool ContainsPersonalInfo(string password, string email, string? name)
    {
        var p = password.ToLowerInvariant();
        var local = email.Split('@')[0].ToLowerInvariant();
        if (local.Length >= 4 && p.Contains(local)) return true;
        return (name ?? "").ToLowerInvariant().Split([' ', '.', '-', '_'], StringSplitOptions.RemoveEmptyEntries).Any(t => t.Length >= 4 && p.Contains(t));
    }
}
