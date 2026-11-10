namespace AnnabaDigital.Domain.Entities;

/// <summary>
/// Where a request stands. Stored as text. Approval: a request that needs approval waits as <c>PendingApproval</c>; approving it makes it <c>Submitted</c> (and routes it), rejecting it makes it <c>Rejected</c> (final);
/// the transitions themselves live in <see cref="AnnabaDigital.Domain.TicketWorkflow"/>.
/// </summary>
public enum TicketStatus { Submitted = 0, Assigned = 1, InProgress = 2, OnHold = 3, Resolved = 4, Closed = 5, Cancelled = 6, Reopened = 7, PendingApproval = 8, Rejected = 9 }

/// <summary>
/// One request for something in the service catalog (TKT-001). It records the service name and the <i>form version</i> it was created with, so later catalog
/// edits never rewrite history, and keeps the requester's normalised answers as JSON (labels are re-derived from that form version when shown).
/// </summary>
public class Ticket : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    /// <summary>Human number, unique per organisation, e.g. <c>GA-2026-000123</c>.</summary>
    public string Number { get; set; } = "";
    public int Year { get; set; }
    public int Sequence { get; set; }

    public Guid ServiceId { get; set; }
    public string ServiceName { get; set; } = "";
    public int FormVersion { get; set; }
    public string Subject { get; set; } = "";
    public string? Description { get; set; }
    /// <summary>Normalised answers keyed by question key.</summary>
    public string AnswersJson { get; set; } = "{}";

    /// <summary>Who the request is for. Differs from <see cref="CreatedById"/> when staff file it on someone's behalf.</summary>
    public Guid RequesterId { get; set; }
    public Guid CreatedById { get; set; }
    public Guid? LocationId { get; set; }
    /// <summary>AST-003. The asset the request is about, if any (set by staff; a retired asset can no longer be chosen).</summary>
    public Guid? AssetId { get; set; }
    /// <summary>TKT-006. Resolve this ticket by itself when its last required work order is done.</summary>
    public bool ResolveWithWorkOrders { get; set; }
    public TicketPriority Priority { get; set; } = TicketPriority.Medium;
    public TicketStatus Status { get; set; } = TicketStatus.Submitted;
    public Guid? AssigneeId { get; set; }
    public Guid? TeamId { get; set; }

    public string? HoldReason { get; set; }
    public string? ResolutionCode { get; set; }
    public string? ResolutionNote { get; set; }
    public string? CancelReason { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public int ReopenCount { get; set; }
    /// <summary>When the current assignee was given the ticket (feeds round-robin and least-loaded routing).</summary>
    public DateTimeOffset? AssignedAt { get; set; }

    // ---- SLA (SLA-002/004/005/006). The policy's numbers are copied here, so editing a policy later never moves the goalposts of open tickets.
    public Guid? SlaPolicyId { get; set; }
    public Guid? SlaCalendarId { get; set; }
    /// <summary>Where the clock starts: creation, or the moment of the latest reopening.</summary>
    public DateTimeOffset? SlaStartAt { get; set; }
    public int SlaFirstResponseMinutes { get; set; }
    public int SlaResolutionMinutes { get; set; }
    public int SlaEscalate1Percent { get; set; }
    public int SlaEscalate2Percent { get; set; }
    public DateTimeOffset? FirstResponseDueAt { get; set; }
    public DateTimeOffset? ResolutionDueAt { get; set; }
    public DateTimeOffset? FirstRespondedAt { get; set; }
    /// <summary>Set while the clock is paused (ticket on hold).</summary>
    public DateTimeOffset? SlaPausedAt { get; set; }
    /// <summary>Working minutes spent paused so far in this cycle; added to both targets.</summary>
    public int SlaPausedMinutes { get; set; }
    public SlaState SlaState { get; set; } = SlaState.None;
    /// <summary>0 = none, 1 = first threshold, 2 = second threshold, 3 = breached. Only ever goes up within a cycle.</summary>
    public int SlaEscalationLevel { get; set; }
    /// <summary>The instant a target was first missed. Never cleared: a breach is a permanent fact.</summary>
    public DateTimeOffset? SlaBreachedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int Version { get; set; } = 1;
    /// <summary>ADM-006. Set when the retention policy removed everything a person wrote on this ticket; the ticket stays as a statistic and can no longer be reopened.</summary>
    public DateTimeOffset? AnonymisedAt { get; set; }
}

public enum TicketEventKind { Created = 0, Comment = 1, Status = 2, Assigned = 3, Updated = 4, Escalation = 5, Approval = 6 }

/// <summary>One line of a ticket's timeline: a comment, a status change, an assignment or an edit. Append-only. Internal rows are hidden from requesters.</summary>
public class TicketEvent : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TicketId { get; set; }
    public TicketEventKind Kind { get; set; }
    public Guid? ActorId { get; set; }
    /// <summary>The actor's name at the time (also "System" for automatic changes), so history survives renames and account closure.</summary>
    public string ActorLabel { get; set; } = "";
    public bool Internal { get; set; }
    public string? Body { get; set; }
    public TicketStatus? FromStatus { get; set; }
    public TicketStatus? ToStatus { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Last ticket sequence handed out per organisation and year. Incremented only through one atomic upsert.</summary>
public class TicketCounter : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public int Year { get; set; }
    public int Last { get; set; }
}

/// <summary>
/// TKT-005. A file attached to a ticket. The bytes live outside the database behind the storage port; this row holds what we know about them.
/// A file uploaded for a request form that is not submitted yet has no ticket (<see cref="TicketId"/> null): it belongs to its uploader until the ticket claims it,
/// and unclaimed uploads are purged after a day. Deleting is soft (the row stays for the audit trail, the bytes go).
/// </summary>
public class Attachment : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid? TicketId { get; set; }
    public string FileName { get; set; } = "";
    /// <summary>The type we serve it as (from our allow-list), never the one the sender claimed.</summary>
    public string ContentType { get; set; } = "";
    public long Size { get; set; }
    /// <summary>SHA-256 of the content, hex.</summary>
    public string Sha256 { get; set; } = "";
    public string StorageKey { get; set; } = "";
    public Guid UploadedById { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    /// <summary>TKT-007. Evidence on a work order of the ticket: its work order and whether it shows the situation <c>before</c> or <c>after</c> the work.</summary>
    public Guid? WorkOrderId { get; set; }
    public string? Stage { get; set; }
}

/// <summary>
/// TKT-011. A saved filter of the ticket queue. Personal by default; staff who may assign tickets can share one with everyone who reads the queue.
/// The filter is a small set of named values (see <c>TicketViewService</c>), stored as JSON text so a view survives changes to the screen.
/// </summary>
public class TicketView : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid OwnerId { get; set; }
    public string Name { get; set; } = "";
    public string FilterJson { get; set; } = "{}";
    public bool Shared { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
