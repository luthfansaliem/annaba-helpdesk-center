namespace AnnabaDigital.Domain.Entities;

/// <summary>Binds a person to one identity at an external provider by its stable subject — never by e-mail alone after the first sign-in.</summary>
public class UserIdentity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    public string Issuer { get; set; } = "";
    public string Subject { get; set; } = "";
    public string? Email { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
    public User? User { get; set; }
}
