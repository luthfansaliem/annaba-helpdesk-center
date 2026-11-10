namespace AnnabaDigital.Domain.Entities;

/// <summary>
/// TKT-009. The requester's satisfaction with a resolved request: 1–5 and an optional comment, one per ticket (changing it updates the row). The person, team and place
/// working the ticket when it was rated are kept with it, so the report still credits the right people after a reassignment. A low score alerts the team's leads once.
/// </summary>
public class TicketRating : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TicketId { get; set; }
    public int Score { get; set; }
    public string? Comment { get; set; }
    public Guid RaterId { get; set; }
    public Guid? AssigneeId { get; set; }
    public Guid? TeamId { get; set; }
    public Guid? LocationId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    /// <summary>When the low-score alert went out (never twice for one ticket).</summary>
    public DateTimeOffset? AlertedAt { get; set; }
}
