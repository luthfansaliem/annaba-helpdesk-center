using AnnabaDigital.Domain.Entities;

namespace AnnabaDigital.Domain;

/// <summary>
/// The SLA clock (SLA-004/005/006), pure: given a ticket's recorded facts, a business calendar and "now", it works out the due times, how much of the target is used,
/// the escalation level and the state. Everything derives from four stored facts — where the clock started, how long it was paused, when it was answered and when it was
/// resolved — so the same inputs always give the same answer and a recalculation (new priority, new policy) is just another call.
/// </summary>
public static class SlaClock
{
    /// <summary>The instant <paramref name="targetMinutes"/> of working time (plus the time already spent paused) have passed since <paramref name="start"/>; <c>null</c> if the calendar never opens.</summary>
    public static DateTimeOffset? DueAt(WorkingCalendar cal, DateTimeOffset start, int targetMinutes, int pausedMinutes)
    {
        try { return cal.AddWorkingTime(start, TimeSpan.FromMinutes(Math.Max(0, targetMinutes) + Math.Max(0, pausedMinutes))); }
        catch (InvalidOperationException) { return null; }
    }

    /// <summary>Share of the target already used (1.0 = exactly on the deadline; can exceed 1). A zero target counts as fully used.</summary>
    public static double Used(WorkingCalendar cal, DateTimeOffset start, DateTimeOffset until, int targetMinutes, int pausedMinutes)
        => targetMinutes <= 0 ? 1 : Math.Max(0, cal.WorkingTimeBetween(start, until).TotalMinutes - pausedMinutes) / targetMinutes;

    /// <summary>0 none · 1 first threshold · 2 second threshold · 3 breached.</summary>
    public static int Level(double used, int escalate1Percent, int escalate2Percent)
        => used >= 1 ? 3 : escalate2Percent > 0 && used * 100 >= escalate2Percent ? 2 : escalate1Percent > 0 && used * 100 >= escalate1Percent ? 1 : 0;

    /// <summary>Stops the clock (a held ticket). No effect if already stopped.</summary>
    public static void Pause(Ticket t, DateTimeOffset now) => t.SlaPausedAt ??= now;

    /// <summary>Restarts the clock, adding the working time spent paused to both targets.</summary>
    public static void Resume(Ticket t, WorkingCalendar cal, DateTimeOffset now)
    {
        if (t.SlaPausedAt is not { } since) return;
        t.SlaPausedMinutes += (int)Math.Round(cal.WorkingTimeBetween(since, now).TotalMinutes);
        t.SlaPausedAt = null;
    }

    /// <summary>Starts a fresh cycle (a reopened ticket): the resolution clock runs again from <paramref name="now"/>; the first response, once given, stays given.</summary>
    public static void Restart(Ticket t, DateTimeOffset now)
    {
        t.SlaStartAt = now; t.SlaPausedAt = null; t.SlaPausedMinutes = 0; t.SlaEscalationLevel = 0; t.ResolvedAt = null;
    }

    /// <summary>
    /// Recomputes due times, escalation level and state from the ticket's facts. Returns the new escalation level when it <i>rose</i> (so the caller can record an
    /// escalation), otherwise 0. A ticket without a policy, or a cancelled one, has no SLA.
    /// </summary>
    public static int Refresh(Ticket t, WorkingCalendar cal, DateTimeOffset now)
    {
        if (t.SlaPolicyId is null || t.Status is TicketStatus.Cancelled or TicketStatus.Rejected)
        {
            t.SlaState = SlaState.None; t.FirstResponseDueAt = null; t.ResolutionDueAt = null; return 0;
        }
        var start = t.SlaStartAt ?? t.CreatedAt;
        t.FirstResponseDueAt = DueAt(cal, start, t.SlaFirstResponseMinutes, t.SlaPausedMinutes);
        t.ResolutionDueAt = DueAt(cal, start, t.SlaResolutionMinutes, t.SlaPausedMinutes);
        if (t.FirstResponseDueAt is null || t.ResolutionDueAt is null) { t.SlaState = SlaState.None; return 0; }

        var at = t.SlaPausedAt ?? now;                      // a stopped clock reads the moment it stopped
        var responded = t.FirstRespondedAt; var resolved = t.ResolvedAt;
        var frMissed = responded is { } r ? r > t.FirstResponseDueAt : at > t.FirstResponseDueAt;
        var resMissed = resolved is { } d ? d > t.ResolutionDueAt : at > t.ResolutionDueAt;

        // the level rises with the targets still open; a finished target no longer pushes it
        var level = t.SlaEscalationLevel;
        if (responded is null) level = Math.Max(level, Level(Used(cal, start, at, t.SlaFirstResponseMinutes, t.SlaPausedMinutes), t.SlaEscalate1Percent, t.SlaEscalate2Percent));
        if (resolved is null) level = Math.Max(level, Level(Used(cal, start, at, t.SlaResolutionMinutes, t.SlaPausedMinutes), t.SlaEscalate1Percent, t.SlaEscalate2Percent));
        if (frMissed || resMissed) level = 3;
        var rose = level > t.SlaEscalationLevel ? level : 0;
        t.SlaEscalationLevel = level;

        if (frMissed || resMissed)
        {
            t.SlaState = SlaState.Breached;
            var when = (frMissed, resMissed) switch { (true, true) => Min(t.FirstResponseDueAt!.Value, t.ResolutionDueAt!.Value), (true, false) => t.FirstResponseDueAt!.Value, _ => t.ResolutionDueAt!.Value };
            t.SlaBreachedAt ??= when;
        }
        else if (resolved is not null) t.SlaState = SlaState.Met;
        else if (t.SlaPausedAt is not null) t.SlaState = SlaState.Paused;
        else t.SlaState = level >= 2 ? SlaState.AtRisk : SlaState.OnTrack;
        return rose;
    }

    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;
}
