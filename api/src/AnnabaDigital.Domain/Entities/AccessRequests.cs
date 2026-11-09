namespace AnnabaDigital.Domain.Entities;

/// <summary>A person asking for a role they don't hold; an administrator who may grant it approves or rejects with a note.</summary>
public class AccessRequest : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid RequesterId { get; set; }
    public Guid RoleId { get; set; }
    public string Reason { get; set; } = "";
    public AccessRequestStatus Status { get; set; } = AccessRequestStatus.Pending;
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? DecidedBy { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public string? DecisionNote { get; set; }
    public User? Requester { get; set; }
    public Role? Role { get; set; }
}
