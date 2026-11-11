namespace AnnabaDigital.Domain.Entities;

public enum MailDirection { Incoming = 0, Outgoing = 1 }
public enum MailKind { Letter = 0, Document = 1, Parcel = 2, Other = 3 }
/// <summary>Incoming: Received → (recipient notified) → Collected. Outgoing: Registered → Sent (handed to the courier). Either can be voided when logged by mistake.</summary>
public enum MailStatus { Received = 0, Collected = 1, Registered = 2, Sent = 3, Voided = 4 }

/// <summary>
/// VIS-004/005. One letter or parcel through the mailroom. Incoming items are for a recipient (an employee), who is told at once and reminded after two days;
/// whoever collects it is recorded by name. Outgoing items are for an addressee outside, from an employee, and get the courier and waybill when they leave.
/// Names, addresses and descriptions are blanked after the visitor retention period (<see cref="VisitorSettings.RetentionDays"/>).
/// </summary>
public class MailItem : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public MailDirection Direction { get; set; }
    public MailKind Kind { get; set; } = MailKind.Parcel;
    public MailStatus Status { get; set; }
    /// <summary>Incoming: the employee it is for. Outgoing: the employee sending it.</summary>
    public Guid PersonId { get; set; }
    /// <summary>Incoming: who sent it (as printed on it). Outgoing: the addressee.</summary>
    public string? OtherParty { get; set; }
    /// <summary>Outgoing: the address it goes to.</summary>
    public string? Address { get; set; }
    public string? Carrier { get; set; }
    public string? Waybill { get; set; }
    public string? Description { get; set; }
    public Guid? LocationId { get; set; }
    public DateTimeOffset LoggedAt { get; set; }
    public Guid LoggedById { get; set; }
    public DateTimeOffset? RemindedAt { get; set; }
    /// <summary>Incoming: when it was collected and the name of who took it (the recipient or a colleague). Outgoing: when it left with the courier.</summary>
    public DateTimeOffset? DoneAt { get; set; }
    public Guid? DoneById { get; set; }
    public string? CollectedBy { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset? AnonymisedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int Version { get; set; } = 1;
}

public static class MailRules
{
    public const int ReminderDays = 2;
}
