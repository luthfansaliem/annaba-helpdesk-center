using AnnabaDigital.Domain.Entities;

namespace AnnabaDigital.Domain;

/// <summary>ADM-006. The pure rules of the retention policy: what a period may be, and which tickets count as finished.</summary>
public static class RetentionRules
{
    public const string Anonymise = "anonymise", Delete = "delete";

    public const int MinTicketDays = 30, MinFileDays = 7, MinNotificationDays = 7, MaxDays = 3650;

    /// <summary>The statuses from which a ticket does not move again. A ticket is aged from its last update, which for these is the day it finished.</summary>
    public static readonly TicketStatus[] Finished = [TicketStatus.Closed, TicketStatus.Cancelled, TicketStatus.Rejected];

    /// <summary>What is left in the subject of an anonymised ticket (a subject can never be empty).</summary>
    public const string RemovedSubject = "[Content removed under the retention policy]";

    /// <summary>The label that replaces a person's name on a timeline entry of an anonymised ticket. "System" entries keep their label.</summary>
    public const string RemovedActor = "Removed";

    /// <summary>The problems with a proposed policy, by field. Empty when it is acceptable.</summary>
    public static Dictionary<string, string> Validate(bool enabled, int? ticketDays, string? action, int? fileDays, int? notificationDays)
    {
        var f = new Dictionary<string, string>();
        if (ticketDays is { } t && (t < MinTicketDays || t > MaxDays)) f["finishedTicketDays"] = $"Use {MinTicketDays}–{MaxDays} days, or leave it empty to keep tickets.";
        if (fileDays is { } a && (a < MinFileDays || a > MaxDays)) f["attachmentDays"] = $"Use {MinFileDays}–{MaxDays} days, or leave it empty to keep files.";
        if (notificationDays is { } n && (n < MinNotificationDays || n > MaxDays)) f["notificationDays"] = $"Use {MinNotificationDays}–{MaxDays} days, or leave it empty to keep notifications.";
        if (action is not (Anonymise or Delete)) f["ticketAction"] = "Use anonymise or delete.";
        if (ticketDays is { } td && fileDays is { } fd && fd > td && !f.ContainsKey("attachmentDays")) f["attachmentDays"] = "Files can't be kept longer than the ticket they belong to.";
        if (enabled && ticketDays is null && fileDays is null && notificationDays is null) f["enabled"] = "Set at least one period before switching the policy on.";
        return f;
    }

    public static string Normalise(string? action) => (action ?? "").Trim().ToLowerInvariant();
}
