namespace AnnabaDigital.Domain;

/// <summary>
/// NTF-002. Every kind of notification the helpdesk sends, with how it behaves unless a person changes it. In the app is on for everything; e-mail is on only for the
/// events that need attention outside the app. A <c>Locked</c> kind cannot be switched off because the work stops without it (an approval request nobody sees stalls the ticket).
/// </summary>
public static class NotificationCatalog
{
    public sealed record Kind(string Key, string Group, bool DefaultEmail, bool Locked = false);

    public static readonly IReadOnlyList<Kind> All =
    [
        new("ticket.assigned", "tickets", true), new("ticket.reply", "tickets", true), new("ticket.resolved", "tickets", true),
        new("ticket.started", "tickets", false), new("ticket.hold", "tickets", false), new("ticket.closed", "tickets", false),
        new("ticket.cancelled", "tickets", false), new("ticket.reopened", "tickets", false), new("ticket.note", "tickets", false), new("ticket.low_rating", "tickets", true),
        new("sla.at_risk", "sla", false), new("sla.breached", "sla", true),
        new("approval.needed", "approvals", true, Locked: true), new("approval.approved", "approvals", true), new("approval.rejected", "approvals", true), new("approval.delegated", "approvals", false),
        new("asset.warranty", "assets", true),
        new("workorder.assigned", "work", true), new("workorder.done", "work", false),
        new("vendor.contract", "vendors", true),
        new("budget.threshold", "costs", true),
        new("booking.changed", "bookings", true), new("booking.approval", "bookings", true), new("booking.decided", "bookings", true), new("booking.reminder", "bookings", false),
        new("vehicle.alert", "bookings", true), new("resource.document", "bookings", true),
        new("visitor.arrived", "visitors", true), new("visitor.flagged", "visitors", true),
        new("mail.arrived", "visitors", true), new("mail.sent", "visitors", false),
        new("stock.low", "inventory", true),
        new("supply.new", "inventory", true), new("supply.approval", "inventory", true), new("supply.update", "inventory", true),
    ];

    public static Kind? Find(string? key) => All.FirstOrDefault(k => k.Key == key);
}
