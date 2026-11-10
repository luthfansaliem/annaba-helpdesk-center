namespace AnnabaDigital.Domain.Entities;

/// <summary>
/// TKT-004 (by e-mail). The organisation's inbound mailbox: a mail service (Mailgun, Postmark, SendGrid, Cloudflare Email Routing…) forwards every e-mail sent to
/// <see cref="Address"/> as raw MIME to a secret webhook URL. Only the SHA-256 of the URL's token is stored. New e-mails become requests on <see cref="DefaultServiceId"/>;
/// replies whose subject carries a ticket number become comments on that ticket.
/// </summary>
public class InboundMailbox : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    /// <summary>The address people write to (shown in the app and the e-mails; the routing itself is the mail service's).</summary>
    public string Address { get; set; } = "";
    public Guid? DefaultServiceId { get; set; }
    public bool Enabled { get; set; }
    public string TokenHash { get; set; } = "";
    /// <summary>The last four characters of the token, to recognise it.</summary>
    public string TokenHint { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int Version { get; set; } = 1;
}

/// <summary>One e-mail the mailbox received and what became of it. Unique per mailbox and Message-ID, so a provider's retry never files a request twice.</summary>
public class InboundMessage : ITenantScoped
{
    public Guid TenantId { get; set; } = Tenant.DefaultId;
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid MailboxId { get; set; }
    public string MessageId { get; set; } = "";
    public string FromAddress { get; set; } = "";
    public string Subject { get; set; } = "";
    public long Size { get; set; }
    /// <summary><c>created</c>, <c>replied</c>, <c>ignored</c> or <c>rejected</c>.</summary>
    public string Outcome { get; set; } = "";
    public string? Reason { get; set; }
    public Guid? TicketId { get; set; }
    public Guid? UserId { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
}
