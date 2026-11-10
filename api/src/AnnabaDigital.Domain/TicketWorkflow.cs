using AnnabaDigital.Domain.Entities;

namespace AnnabaDigital.Domain;

public enum TicketAction { Assign, Start, Hold, Resume, Resolve, Cancel, Confirm, Reopen }

/// <summary>What the person asking is, relative to one ticket. The service fills this from the ticket and the person's permissions.</summary>
public readonly record struct TicketActor(bool IsRequester, bool CanTransition, bool CanAssign, bool IsAssignee);

/// <summary>
/// The ticket state machine (TKT-002), pure and side-effect free so every rule can be tested without a database. Two questions are answered here:
/// <i>is this move legal from this status</i> (<see cref="Next"/>), and <i>is this person allowed to make it</i> (<see cref="Allowed"/>).
/// </summary>
public static class TicketWorkflow
{
    /// <summary>After a resolution the requester has this long to confirm or reopen before it closes by itself.</summary>
    public static readonly TimeSpan AutoCloseAfter = TimeSpan.FromDays(3);
    /// <summary>After closing, the requester may still reopen for this long.</summary>
    public static readonly TimeSpan ReopenWindow = TimeSpan.FromDays(7);

    public static readonly IReadOnlyList<string> ResolutionCodes = ["fixed", "workaround", "no_fault_found", "cannot_reproduce", "duplicate", "declined", "other"];

    /// <summary>The status a legal move leads to, or <c>null</c> if the move isn't legal from <paramref name="from"/>. (Assign is handled by <see cref="AfterAssign"/>.)</summary>
    public static TicketStatus? Next(TicketStatus from, TicketAction action) => (from, action) switch
    {
        (TicketStatus.Submitted or TicketStatus.Assigned or TicketStatus.Reopened, TicketAction.Start) => TicketStatus.InProgress,
        (TicketStatus.Assigned or TicketStatus.InProgress, TicketAction.Hold) => TicketStatus.OnHold,
        (TicketStatus.OnHold, TicketAction.Resume) => TicketStatus.InProgress,
        (TicketStatus.InProgress, TicketAction.Resolve) => TicketStatus.Resolved,
        (TicketStatus.PendingApproval or TicketStatus.Submitted or TicketStatus.Assigned or TicketStatus.OnHold or TicketStatus.Reopened, TicketAction.Cancel) => TicketStatus.Cancelled,
        (TicketStatus.Resolved, TicketAction.Confirm) => TicketStatus.Closed,
        (TicketStatus.Resolved or TicketStatus.Closed, TicketAction.Reopen) => TicketStatus.Reopened,
        _ => null,
    };

    /// <summary>Statuses in which a ticket can be (re)assigned.</summary>
    public static bool CanAssign(TicketStatus s) => s is TicketStatus.Submitted or TicketStatus.Assigned or TicketStatus.InProgress or TicketStatus.OnHold or TicketStatus.Reopened;

    /// <summary>Status after (re)assigning: someone owns it → Assigned (a held ticket stays held); nobody owns it → back to Submitted (a reopened one stays reopened).</summary>
    public static TicketStatus AfterAssign(TicketStatus from, bool hasAssignee) => hasAssignee
        ? from is TicketStatus.OnHold ? TicketStatus.OnHold : TicketStatus.Assigned
        : from is TicketStatus.Assigned or TicketStatus.InProgress ? TicketStatus.Submitted : from;

    public static bool IsFinished(TicketStatus s) => s is TicketStatus.Closed or TicketStatus.Cancelled or TicketStatus.Rejected;

    /// <summary>Everything this person may do right now. Staff work a ticket that is theirs, unassigned, or that they supervise (they hold the assign permission).</summary>
    public static IReadOnlyList<TicketAction> Allowed(TicketStatus status, bool hasAssignee, DateTimeOffset? closedAt, DateTimeOffset now, TicketActor who)
    {
        var list = new List<TicketAction>();
        var worker = who.CanTransition && (!hasAssignee || who.IsAssignee || who.CanAssign); // an unowned ticket can be picked up by anyone who works tickets
        foreach (var a in Enum.GetValues<TicketAction>())
        {
            if (a == TicketAction.Assign) { if (who.CanAssign && CanAssign(status)) list.Add(a); continue; }
            if (Next(status, a) is null) continue;
            var ok = a switch
            {
                TicketAction.Start or TicketAction.Hold or TicketAction.Resume or TicketAction.Resolve => worker,
                TicketAction.Cancel => worker || who.IsRequester && status is TicketStatus.PendingApproval or TicketStatus.Submitted or TicketStatus.Assigned or TicketStatus.Reopened,
                TicketAction.Confirm => who.IsRequester || who.CanAssign,
                TicketAction.Reopen => who.CanTransition || who.IsRequester && (status != TicketStatus.Closed || closedAt is { } c && now - c <= ReopenWindow),
                _ => false,
            };
            if (ok) list.Add(a);
        }
        return list;
    }
}
