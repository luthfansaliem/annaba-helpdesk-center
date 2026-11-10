namespace AnnabaDigital.Domain.Entities;

/// <summary>
/// ADM-006. How long an organisation keeps what its helpdesk collects. One row per organisation. Nothing is ever removed unless <see cref="Enabled"/> is on, and a period
/// left empty (<c>null</c>) means "keep". Tickets are counted from the day they finished (closed, cancelled or rejected); open tickets are never touched.
/// </summary>
public class RetentionPolicy : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public bool Enabled { get; set; }
    /// <summary>Days after a ticket finished before its content is removed (<see cref="TicketAction"/>). Null = keep.</summary>
    public int? FinishedTicketDays { get; set; }
    /// <summary><c>anonymise</c> keeps the ticket as a statistic (number, service, times, SLA) and removes everything a person wrote; <c>delete</c> removes the ticket entirely.</summary>
    public string TicketAction { get; set; } = RetentionRules.Anonymise;
    /// <summary>Days after a ticket finished before its files are removed. Null = keep (until the ticket itself is).</summary>
    public int? AttachmentDays { get; set; }
    /// <summary>Days before an in-app notification is removed, read or not. Null = keep.</summary>
    public int? NotificationDays { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public DateTimeOffset? LastRunAt { get; set; }
    /// <summary>What the last run did, as JSON: <c>{"tickets":n,"files":n,"notifications":n}</c>.</summary>
    public string? LastRunJson { get; set; }
}

/// <summary>ADM-006. A ticket that must be kept whatever the periods say (a dispute, an investigation, a legal request). It is also protected against file removal. Placing and releasing is audited.</summary>
public class TicketLegalHold : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TicketId { get; set; }
    public string Reason { get; set; } = "";
    public Guid PlacedById { get; set; }
    public DateTimeOffset PlacedAt { get; set; }
}
