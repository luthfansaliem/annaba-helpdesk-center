namespace AnnabaDigital.Domain.Entities;

/// <summary>A periodic access review (certification): a snapshot of who holds which privileged role, for reviewers to keep or revoke.</summary>
public class AccessReview : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Name { get; set; } = "";
    public AccessReviewStatus Status { get; set; } = AccessReviewStatus.Open;
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset DueAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public Guid? ClosedBy { get; set; }
    public List<AccessReviewItem> Items { get; set; } = [];
}

public class AccessReviewItem : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid ReviewId { get; set; }
    public Guid UserId { get; set; }
    public Guid RoleId { get; set; }
    public ReviewDecision Decision { get; set; } = ReviewDecision.Pending;
    public Guid? ReviewerId { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public string? Note { get; set; }
    public AccessReview? Review { get; set; }
    public User? User { get; set; }
    public Role? Role { get; set; }
}

/// <summary>Opens an access review by itself every <see cref="EveryDays"/> days. One per organisation; the review it creates is the same as a manual one.</summary>
public class AccessReviewSchedule : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public bool Enabled { get; set; }
    public int EveryDays { get; set; } = 90;
    public int DueInDays { get; set; } = 14;
    public DateTimeOffset? NextRunAt { get; set; }
    public DateTimeOffset? LastRunAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
