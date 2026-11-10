using AnnabaDigital.Domain;
using AnnabaDigital.Domain.Entities;
using Xunit;

namespace AnnabaDigital.Tests;

/// <summary>SLA-004/005/006 and TKT-003: the clock, the escalation levels and the routing logic are pure, so they are tested without a database.</summary>
public class SlaClockTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 10, 2, 0, 0, TimeSpan.Zero);      // a Tuesday, 09:00 in Jakarta
    private static readonly WorkingCalendar Always = WorkingCalendar.Always();
    private static readonly TimeZoneInfo Jakarta = TimeZoneInfo.FindSystemTimeZoneById("Asia/Jakarta");
    private static WorkingCalendar Office() => new(Jakarta, Enumerable.Range(1, 5).Select(d => new WorkingWindow((DayOfWeek)d, 8 * 60, 17 * 60)));

    /// <summary>A ticket with a 60-minute first response and a 600-minute (10 h) resolution target; thresholds 50 % and 80 %.</summary>
    private static Ticket Ticket(DateTimeOffset? start = null) => new()
    {
        CreatedAt = start ?? T0, SlaStartAt = start ?? T0, SlaPolicyId = Guid.NewGuid(), SlaFirstResponseMinutes = 60, SlaResolutionMinutes = 600, SlaEscalate1Percent = 50, SlaEscalate2Percent = 80,
        Status = TicketStatus.Submitted,
    };

    [Fact]
    public void Due_times_count_only_working_time_and_include_time_spent_paused()
    {
        Assert.Equal(T0.AddMinutes(90), SlaClock.DueAt(Always, T0, 60, 30));
        var fridayAfternoon = new DateTimeOffset(2026, 11, 13, 9, 0, 0, TimeSpan.Zero);          // Friday 16:00 Jakarta
        Assert.Equal(new DateTimeOffset(2026, 11, 16, 2, 0, 0, TimeSpan.Zero), SlaClock.DueAt(Office(), fridayAfternoon, 120, 0)); // one hour Friday, one hour Monday → Monday 09:00 Jakarta
        Assert.Equal(new DateTimeOffset(2026, 11, 16, 3, 0, 0, TimeSpan.Zero), SlaClock.DueAt(Office(), fridayAfternoon, 120, 60));
        var never = new WorkingCalendar(Jakarta, []);
        Assert.Null(SlaClock.DueAt(never, T0, 60, 0));                                           // a calendar that never opens has no due time
    }

    [Theory]
    [InlineData(0, 0)] [InlineData(299, 0)] [InlineData(300, 1)] [InlineData(479, 1)] [InlineData(480, 2)] [InlineData(599, 2)] [InlineData(600, 3)] [InlineData(5000, 3)]
    public void Escalation_levels_follow_the_thresholds(int minutesUsedOf600, int expected)
        => Assert.Equal(expected, SlaClock.Level(SlaClock.Used(Always, T0, T0.AddMinutes(minutesUsedOf600), 600, 0), 50, 80));

    [Fact]
    public void A_zero_target_counts_as_used_and_paused_time_is_not_counted()
    {
        Assert.Equal(1, SlaClock.Used(Always, T0, T0.AddMinutes(5), 0, 0));
        Assert.Equal(0.5, SlaClock.Used(Always, T0, T0.AddMinutes(400), 600, 100), 3);          // 400 − 100 paused = 300 of 600
        Assert.Equal(0, SlaClock.Used(Always, T0, T0.AddMinutes(50), 600, 100), 3);             // never negative
    }

    [Fact]
    public void A_ticket_moves_from_on_track_through_at_risk_to_breached()
    {
        var t = Ticket(); t.FirstRespondedAt = T0.AddMinutes(10);                               // answered quickly: only the resolution target is open
        Assert.Equal(0, SlaClock.Refresh(t, Always, T0.AddMinutes(60)));
        Assert.Equal(SlaState.OnTrack, t.SlaState); Assert.Equal(T0.AddMinutes(600), t.ResolutionDueAt);

        Assert.Equal(1, SlaClock.Refresh(t, Always, T0.AddMinutes(300)));                        // 50 % → level 1 (state still on track)
        Assert.Equal(SlaState.OnTrack, t.SlaState);
        Assert.Equal(2, SlaClock.Refresh(t, Always, T0.AddMinutes(500)));                        // 80 % → level 2: at risk
        Assert.Equal(SlaState.AtRisk, t.SlaState);
        Assert.Equal(0, SlaClock.Refresh(t, Always, T0.AddMinutes(520)));                        // nothing new: no repeated escalation
        Assert.Equal(3, SlaClock.Refresh(t, Always, T0.AddMinutes(601)));
        Assert.Equal(SlaState.Breached, t.SlaState); Assert.Equal(T0.AddMinutes(600), t.SlaBreachedAt);
        Assert.Equal(0, SlaClock.Refresh(t, Always, T0.AddDays(3)));                             // already breached: stays breached, no new level
        Assert.Equal(SlaState.Breached, t.SlaState);
    }

    [Fact]
    public void An_unanswered_ticket_breaches_its_first_response_target_before_its_resolution_target()
    {
        var t = Ticket();
        SlaClock.Refresh(t, Always, T0.AddMinutes(61));
        Assert.Equal(SlaState.Breached, t.SlaState); Assert.Equal(T0.AddMinutes(60), t.SlaBreachedAt);
        var late = Ticket(); late.FirstRespondedAt = T0.AddMinutes(90);                          // answered, but too late
        SlaClock.Refresh(late, Always, T0.AddMinutes(100));
        Assert.Equal(SlaState.Breached, late.SlaState);
    }

    [Fact]
    public void Resolving_in_time_is_met_and_resolving_late_stays_breached_whatever_the_time_now()
    {
        var ok = Ticket(); ok.FirstRespondedAt = T0.AddMinutes(5); ok.ResolvedAt = T0.AddMinutes(500); ok.Status = TicketStatus.Resolved;
        SlaClock.Refresh(ok, Always, T0.AddDays(30));
        Assert.Equal(SlaState.Met, ok.SlaState);
        var late = Ticket(); late.FirstRespondedAt = T0.AddMinutes(5); late.ResolvedAt = T0.AddMinutes(700); late.Status = TicketStatus.Resolved;
        SlaClock.Refresh(late, Always, T0.AddMinutes(701));
        Assert.Equal(SlaState.Breached, late.SlaState); Assert.Equal(T0.AddMinutes(600), late.SlaBreachedAt);
    }

    [Fact]
    public void Pausing_stops_the_clock_and_resuming_pushes_both_deadlines_out_by_the_time_paused()
    {
        var t = Ticket(); t.FirstRespondedAt = T0.AddMinutes(5);
        SlaClock.Pause(t, T0.AddMinutes(100));
        SlaClock.Refresh(t, Always, T0.AddDays(2));                                              // paused for two days
        Assert.Equal(SlaState.Paused, t.SlaState); Assert.Equal(T0.AddMinutes(600), t.ResolutionDueAt); // the due time only moves once the clock restarts

        SlaClock.Resume(t, Always, T0.AddMinutes(100 + 240));                                    // held for four hours
        Assert.Equal(240, t.SlaPausedMinutes); Assert.Null(t.SlaPausedAt);
        SlaClock.Refresh(t, Always, T0.AddMinutes(400));
        Assert.Equal(T0.AddMinutes(840), t.ResolutionDueAt); Assert.Equal(SlaState.OnTrack, t.SlaState);
        SlaClock.Pause(t, T0.AddMinutes(500)); SlaClock.Pause(t, T0.AddMinutes(700));            // a second pause call changes nothing
        Assert.Equal(T0.AddMinutes(500), t.SlaPausedAt);
    }

    [Fact]
    public void Time_spent_paused_in_non_working_hours_does_not_move_the_deadline()
    {
        var cal = Office(); var start = new DateTimeOffset(2026, 11, 10, 2, 0, 0, TimeSpan.Zero);          // Tuesday 09:00 Jakarta
        var t = Ticket(start); t.FirstRespondedAt = start.AddMinutes(5);
        SlaClock.Pause(t, new DateTimeOffset(2026, 11, 10, 10, 0, 0, TimeSpan.Zero));                      // Tuesday 17:00 (closing time)
        SlaClock.Resume(t, cal, new DateTimeOffset(2026, 11, 11, 1, 0, 0, TimeSpan.Zero));                 // Wednesday 08:00 (opening): the whole night was non-working
        Assert.Equal(0, t.SlaPausedMinutes);
    }

    [Fact]
    public void A_paused_ticket_that_already_missed_a_target_is_breached_not_paused()
    {
        var t = Ticket();                                                                        // never answered
        SlaClock.Pause(t, T0.AddMinutes(120));
        SlaClock.Refresh(t, Always, T0.AddMinutes(200));
        Assert.Equal(SlaState.Breached, t.SlaState);
    }

    [Fact]
    public void Reopening_restarts_the_resolution_clock_but_keeps_the_breach_on_record()
    {
        var t = Ticket(); t.FirstRespondedAt = T0.AddMinutes(5); t.ResolvedAt = T0.AddMinutes(700); t.Status = TicketStatus.Resolved;
        SlaClock.Refresh(t, Always, T0.AddMinutes(700));
        var breachedAt = t.SlaBreachedAt; Assert.NotNull(breachedAt);

        var reopenedAt = T0.AddMinutes(800); t.Status = TicketStatus.Reopened;
        SlaClock.Restart(t, reopenedAt);
        SlaClock.Refresh(t, Always, reopenedAt.AddMinutes(10));
        Assert.Equal(reopenedAt.AddMinutes(600), t.ResolutionDueAt); Assert.Equal(SlaState.OnTrack, t.SlaState); Assert.Equal(0, t.SlaEscalationLevel);
        Assert.Equal(breachedAt, t.SlaBreachedAt);                                                // the earlier breach is permanent
    }

    [Fact]
    public void No_policy_a_cancelled_ticket_or_a_calendar_that_never_opens_means_no_sla()
    {
        var none = Ticket(); none.SlaPolicyId = null; none.SlaState = SlaState.Breached; SlaClock.Refresh(none, Always, T0.AddDays(9));
        Assert.Equal(SlaState.None, none.SlaState); Assert.Null(none.ResolutionDueAt);
        var cancelled = Ticket(); cancelled.Status = TicketStatus.Cancelled; SlaClock.Refresh(cancelled, Always, T0.AddDays(9));
        Assert.Equal(SlaState.None, cancelled.SlaState);
        var closed = Ticket(); SlaClock.Refresh(closed, new WorkingCalendar(Jakarta, []), T0);
        Assert.Equal(SlaState.None, closed.SlaState);
    }

    [Fact]
    public void The_escalation_level_never_goes_down_within_a_cycle()
    {
        var t = Ticket(); t.FirstRespondedAt = T0;
        SlaClock.Refresh(t, Always, T0.AddMinutes(500)); Assert.Equal(2, t.SlaEscalationLevel);
        SlaClock.Refresh(t, Always, T0.AddMinutes(100)); Assert.Equal(2, t.SlaEscalationLevel); // e.g. the clock was recalculated with a longer target
    }
}

public class RoutingTests
{
    private static readonly Guid Svc = Guid.NewGuid(), Cat = Guid.NewGuid(), ParentCat = Guid.NewGuid(), Site = Guid.NewGuid(), Floor = Guid.NewGuid();
    private static MatchFacts Facts(TicketPriority p = TicketPriority.High) => new(Svc, new HashSet<Guid> { Cat, ParentCat }, new HashSet<Guid> { Floor, Site }, p);

    [Fact]
    public void An_empty_condition_matches_everything_and_every_given_condition_must_hold()
    {
        Assert.True(new Conditions(null, null, null, null).Matches(Facts()));
        Assert.True(new Conditions(Svc, Cat, Site, TicketPriority.High).Matches(Facts()));
        Assert.False(new Conditions(Guid.NewGuid(), null, null, null).Matches(Facts()));
        Assert.False(new Conditions(Svc, null, null, TicketPriority.Low).Matches(Facts()));
        Assert.False(new Conditions(null, null, Guid.NewGuid(), null).Matches(Facts()));
        Assert.Equal(3, new Conditions(Svc, null, Site, TicketPriority.Low).Count);
    }

    [Fact]
    public void A_category_matches_through_its_parent_and_a_place_matches_everything_inside_it()
    {
        Assert.True(new Conditions(null, ParentCat, null, null).Matches(Facts()));              // the service sits in a sub-category of this one
        Assert.True(new Conditions(null, null, Site, null).Matches(Facts()));                   // the ticket is on a floor of this site
        Assert.True(new Conditions(null, null, Floor, null).Matches(Facts()));
    }

    [Fact]
    public void The_first_matching_item_in_order_wins()
    {
        var items = new[] { new Item("narrow", new Conditions(null, null, null, TicketPriority.Low)), new Item("site", new Conditions(null, null, Site, null)), new Item("catch-all", new Conditions(null, null, null, null)) };
        Assert.Equal("site", Routing.FirstMatch(items, i => i.C, Facts())!.Name);
        Assert.Equal("narrow", Routing.FirstMatch(items, i => i.C, Facts(TicketPriority.Low))!.Name);
        Assert.Null(Routing.FirstMatch(items.Take(1), i => i.C, Facts()));
    }

    private sealed record Item(string Name, Conditions C);
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid A = new("00000000-0000-0000-0000-00000000000a"), B = new("00000000-0000-0000-0000-00000000000b"), C = new("00000000-0000-0000-0000-00000000000c");

    [Fact]
    public void Round_robin_gives_the_next_ticket_to_whoever_was_assigned_longest_ago()
    {
        var people = new[] { new Candidate(A, 5, Now.AddMinutes(-5)), new Candidate(B, 0, Now.AddMinutes(-60)), new Candidate(C, 9, null) };
        Assert.Equal(C, Routing.Pick(AssignStrategy.RoundRobin, people));                       // never assigned goes first, whatever the load
        Assert.Equal(B, Routing.Pick(AssignStrategy.RoundRobin, people.Take(2).ToArray()));
        Assert.Equal(A, Routing.Pick(AssignStrategy.RoundRobin, [new Candidate(B, 0, Now), new Candidate(A, 0, Now)]));   // a tie falls to the lowest id
        Assert.Equal(B, Routing.Pick(AssignStrategy.RoundRobin, [new Candidate(A, 3, Now), new Candidate(B, 1, Now)]));   // equal times: the lighter load goes first
    }

    [Fact]
    public void Least_loaded_picks_the_fewest_open_tickets_then_the_longest_ago()
    {
        var people = new[] { new Candidate(A, 3, null), new Candidate(B, 1, Now), new Candidate(C, 1, Now.AddHours(-2)) };
        Assert.Equal(C, Routing.Pick(AssignStrategy.LeastLoaded, people));
        Assert.Equal(B, Routing.Pick(AssignStrategy.LeastLoaded, [new Candidate(B, 0, Now), new Candidate(A, 2, null)]));
    }

    [Fact]
    public void No_strategy_or_nobody_to_pick_means_the_ticket_waits_in_the_team_queue()
    {
        Assert.Null(Routing.Pick(AssignStrategy.None, [new Candidate(A, 0, null)]));
        Assert.Null(Routing.Pick(AssignStrategy.RoundRobin, []));
        Assert.Null(Routing.Pick(AssignStrategy.LeastLoaded, []));
    }
}
