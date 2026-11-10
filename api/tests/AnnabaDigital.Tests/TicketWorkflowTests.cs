using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;
using Xunit;
using S = AnnabaDigital.Domain.Entities.TicketStatus;
using A = AnnabaDigital.Domain.TicketAction;

namespace AnnabaDigital.Tests;

/// <summary>TKT-002: the state machine is pure, so every legal and illegal move is checked without a database.</summary>
public class TicketWorkflowTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);
    private static readonly TicketActor Nobody = new(false, false, false, false);
    private static readonly TicketActor Requester = new(true, false, false, false);
    private static readonly TicketActor Agent = new(false, true, false, false);
    private static readonly TicketActor Owner = new(false, true, false, true);
    private static readonly TicketActor Supervisor = new(false, true, true, false);

    private static A[] Can(S s, bool hasAssignee, TicketActor who, DateTimeOffset? closedAt = null) => [.. TicketWorkflow.Allowed(s, hasAssignee, closedAt, Now, who)];

    [Fact]
    public void The_full_transition_table_is_exactly_this()
    {
        var legal = new Dictionary<(S, A), S>
        {
            [(S.Submitted, A.Start)] = S.InProgress, [(S.Assigned, A.Start)] = S.InProgress, [(S.Reopened, A.Start)] = S.InProgress,
            [(S.Assigned, A.Hold)] = S.OnHold, [(S.InProgress, A.Hold)] = S.OnHold,
            [(S.OnHold, A.Resume)] = S.InProgress,
            [(S.InProgress, A.Resolve)] = S.Resolved,
            [(S.PendingApproval, A.Cancel)] = S.Cancelled, [(S.Submitted, A.Cancel)] = S.Cancelled, [(S.Assigned, A.Cancel)] = S.Cancelled, [(S.OnHold, A.Cancel)] = S.Cancelled, [(S.Reopened, A.Cancel)] = S.Cancelled,
            [(S.Resolved, A.Confirm)] = S.Closed,
            [(S.Resolved, A.Reopen)] = S.Reopened, [(S.Closed, A.Reopen)] = S.Reopened,
        };
        foreach (var s in Enum.GetValues<S>())
            foreach (var a in Enum.GetValues<A>().Where(x => x != A.Assign))
                Assert.Equal(legal.TryGetValue((s, a), out var to) ? to : (S?)null, TicketWorkflow.Next(s, a));
    }

    [Fact]
    public void Cancelled_and_rejected_are_final_and_closed_can_only_be_reopened()
    {
        foreach (var a in Enum.GetValues<A>().Where(x => x != A.Assign)) { Assert.Null(TicketWorkflow.Next(S.Cancelled, a)); Assert.Null(TicketWorkflow.Next(S.Rejected, a)); }
        Assert.True(TicketWorkflow.IsFinished(S.Rejected));
        Assert.Equal([A.Reopen], Enum.GetValues<A>().Where(a => a != A.Assign && TicketWorkflow.Next(S.Closed, a) is not null).ToArray());
        Assert.True(TicketWorkflow.IsFinished(S.Closed)); Assert.True(TicketWorkflow.IsFinished(S.Cancelled)); Assert.False(TicketWorkflow.IsFinished(S.Resolved));
    }

    [Theory]
    [InlineData(S.Submitted, true, S.Assigned)] [InlineData(S.Reopened, true, S.Assigned)] [InlineData(S.Assigned, true, S.Assigned)] [InlineData(S.InProgress, true, S.Assigned)] [InlineData(S.OnHold, true, S.OnHold)]
    [InlineData(S.Assigned, false, S.Submitted)] [InlineData(S.InProgress, false, S.Submitted)] [InlineData(S.Submitted, false, S.Submitted)] [InlineData(S.Reopened, false, S.Reopened)] [InlineData(S.OnHold, false, S.OnHold)]
    public void Assigning_moves_the_ticket_to_the_right_status(S from, bool hasAssignee, S expected) => Assert.Equal(expected, TicketWorkflow.AfterAssign(from, hasAssignee));

    [Fact]
    public void Only_open_tickets_can_be_assigned()
    {
        foreach (var s in new[] { S.Submitted, S.Assigned, S.InProgress, S.OnHold, S.Reopened }) Assert.True(TicketWorkflow.CanAssign(s), s.ToString());
        foreach (var s in new[] { S.Resolved, S.Closed, S.Cancelled, S.PendingApproval, S.Rejected }) Assert.False(TicketWorkflow.CanAssign(s), s.ToString());   // nothing is assigned before it is approved
    }

    [Fact]
    public void A_person_with_no_relationship_to_the_ticket_can_do_nothing()
    {
        foreach (var s in Enum.GetValues<S>()) Assert.Empty(Can(s, true, Nobody));
    }

    [Fact]
    public void A_requester_may_cancel_only_before_work_starts_confirm_a_resolution_and_reopen()
    {
        Assert.Equal([A.Cancel], Can(S.Submitted, false, Requester));
        Assert.Equal([A.Cancel], Can(S.Assigned, true, Requester));
        Assert.Empty(Can(S.InProgress, true, Requester));                                   // work has begun: they ask the team instead
        Assert.Empty(Can(S.OnHold, true, Requester));
        Assert.Equal([A.Confirm, A.Reopen], Can(S.Resolved, true, Requester));
        Assert.Equal([A.Cancel], Can(S.Reopened, true, Requester));
        Assert.Equal([A.Cancel], Can(S.PendingApproval, false, Requester));                 // while waiting for approval the requester can withdraw
        Assert.Equal([A.Cancel], Can(S.PendingApproval, false, Agent));
        Assert.Empty(Can(S.PendingApproval, false, Nobody)); Assert.Empty(Can(S.Rejected, false, Supervisor));
    }

    [Fact]
    public void A_requester_can_reopen_a_closed_ticket_only_inside_the_window()
    {
        Assert.Equal([A.Reopen], Can(S.Closed, true, Requester, closedAt: Now - TimeSpan.FromDays(6)));
        Assert.Equal([A.Reopen], Can(S.Closed, true, Requester, closedAt: Now - TicketWorkflow.ReopenWindow));      // the boundary is inclusive
        Assert.Empty(Can(S.Closed, true, Requester, closedAt: Now - TicketWorkflow.ReopenWindow - TimeSpan.FromSeconds(1)));
        Assert.Empty(Can(S.Closed, true, Requester, closedAt: null));                                                // no closing time recorded: refuse rather than guess
        Assert.Equal([A.Reopen], Can(S.Closed, true, Agent, closedAt: Now - TimeSpan.FromDays(90)));                // staff are not bound by the window
    }

    [Fact]
    public void An_agent_works_their_own_or_unowned_tickets_but_not_a_colleagues()
    {
        Assert.Equal([A.Start, A.Cancel], Can(S.Submitted, false, Agent));                  // unowned: anyone who works tickets may take it
        Assert.Equal([A.Start, A.Hold, A.Cancel], Can(S.Assigned, true, Owner));
        Assert.Equal([A.Hold, A.Resolve], Can(S.InProgress, true, Owner));
        Assert.Equal([A.Resume, A.Cancel], Can(S.OnHold, true, Owner));
        Assert.Empty(Can(S.Assigned, true, Agent));                                         // someone else's ticket
        Assert.Empty(Can(S.InProgress, true, Agent));
        Assert.Equal([A.Reopen], Can(S.Resolved, true, Owner));                             // confirming belongs to the requester (or a supervisor)
    }

    [Fact]
    public void A_supervisor_can_act_on_any_ticket_assign_it_and_close_for_the_requester()
    {
        Assert.Equal([A.Assign, A.Start, A.Hold, A.Cancel], Can(S.Assigned, true, Supervisor));
        Assert.Equal([A.Assign, A.Hold, A.Resolve], Can(S.InProgress, true, Supervisor));
        Assert.Equal([A.Confirm, A.Reopen], Can(S.Resolved, true, Supervisor));
        Assert.Empty(Can(S.Cancelled, true, Supervisor));
        Assert.Equal([A.Reopen], Can(S.Closed, true, Supervisor));
    }

    [Fact]
    public void Assign_needs_the_assign_permission_and_an_open_ticket()
    {
        Assert.DoesNotContain(A.Assign, Can(S.Submitted, false, Agent));
        Assert.Contains(A.Assign, Can(S.Submitted, false, Supervisor));
        Assert.DoesNotContain(A.Assign, Can(S.Resolved, true, Supervisor));
    }

    [Fact]
    public void Resolution_codes_are_a_fixed_list()
    {
        Assert.Contains("fixed", TicketWorkflow.ResolutionCodes); Assert.Contains("no_fault_found", TicketWorkflow.ResolutionCodes);
        Assert.Equal(TicketWorkflow.ResolutionCodes.Count, TicketWorkflow.ResolutionCodes.Distinct().Count());
    }
}
