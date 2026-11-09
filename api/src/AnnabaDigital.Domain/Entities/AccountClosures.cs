namespace AnnabaDigital.Domain.Entities;

public enum ClosureStatus { Pending, Approved, Rejected, Cancelled, Completed }

/// <summary>
/// A person's request to close their own account. An administrator approves it, a waiting period follows (the person can still cancel), and then the account is
/// deactivated and its personal data erased. One open (pending or approved) request per person.
/// </summary>
public class AccountClosureRequest : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    public string Reason { get; set; } = "";
    public ClosureStatus Status { get; set; } = ClosureStatus.Pending;
    public DateTimeOffset RequestedAt { get; set; }
    public Guid? DecidedBy { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public string? DecisionNote { get; set; }
    /// <summary>Set on approval: when the account is actually closed.</summary>
    public DateTimeOffset? ExecuteAfter { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public User? User { get; set; }
}
