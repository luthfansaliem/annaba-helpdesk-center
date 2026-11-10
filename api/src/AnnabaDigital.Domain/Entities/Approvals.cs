namespace AnnabaDigital.Domain.Entities;

/// <summary>
/// APR-001. Who must approve a service's requests, as ordered steps (stored as JSON, see <c>ApprovalStepDef</c>). A step names people and/or roles and says whether
/// <i>any one</i> or <i>all</i> of them must approve; steps run in order. At most one live policy per service. The fallback person approves when every named approver
/// is excluded (nobody may approve their own request).
/// </summary>
public class ApprovalPolicy : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid ServiceId { get; set; }
    public string StepsJson { get; set; } = "[]";
    public Guid? FallbackUserId { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int Version { get; set; } = 1;
}

/// <summary><c>Waiting</c> = its step hasn't started; <c>Pending</c> = awaiting this person; <c>Skipped</c> = the step was settled by someone else; <c>Cancelled</c> = the request ended first.</summary>
public enum ApprovalStatus { Waiting = 0, Pending = 1, Approved = 2, Rejected = 3, Skipped = 4, Cancelled = 5 }

/// <summary>
/// One person's decision on one ticket at one step (APR-003/006). Once decided a row never changes: it is the approval trail (who, when, what, from where).
/// The one-time e-mail link is bound to this row; only the token's hash is stored.
/// </summary>
public class TicketApproval : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TicketId { get; set; }
    public int StepNo { get; set; }
    public string StepName { get; set; } = "";
    /// <summary><c>any</c> or <c>all</c>, copied from the policy when the ticket was made.</summary>
    public string Rule { get; set; } = "any";
    public Guid ApproverId { get; set; }
    public ApprovalStatus Status { get; set; }
    public string? Comment { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    /// <summary><c>app</c> or <c>email</c>.</summary>
    public string? DecidedVia { get; set; }
    public string? Ip { get; set; }
    public string? Device { get; set; }
    public string? TokenHash { get; set; }
    public DateTimeOffset? TokenExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    /// <summary>APR-004. Set when this person decides in somebody else's place (a delegation, or the approval was handed over): the person it was originally for.</summary>
    public Guid? OnBehalfOfId { get; set; }
}

/// <summary>
/// APR-004. Out-of-office: between <c>StartsAt</c> and <c>EndsAt</c> approvals that would go to <c>UserId</c> go to <c>DelegateId</c> instead. Applied when an approval is created (and
/// to what is already waiting when the delegation starts); the approval trail records both people. A delegate's own delegation is not followed, so there are no chains or loops.
/// </summary>
public class ApprovalDelegation : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    public Guid DelegateId { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// NTF-002. One person's choice for one kind of notification. Only choices that differ from the default are stored: <c>null</c> means "as usual", so a new kind
/// or a changed default reaches everybody who never touched it.
/// </summary>
public class NotificationPreference : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    public string Kind { get; set; } = "";
    public bool? InApp { get; set; }
    public bool? Email { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>NTF-001. A message for one person inside the app (bell and list). E-mail copies go through the outbox separately.</summary>
public class Notification : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    /// <summary>What happened, e.g. <c>ticket.assigned</c>, <c>approval.needed</c>.</summary>
    public string Kind { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Body { get; set; }
    /// <summary>App path to open, e.g. <c>/tickets/…</c>.</summary>
    public string? Link { get; set; }
    public Guid? TicketId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
}
