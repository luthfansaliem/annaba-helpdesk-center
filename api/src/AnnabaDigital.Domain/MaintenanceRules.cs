using AnnabaDigital.Domain.Entities;

namespace AnnabaDigital.Domain;

/// <summary>PM-001/002. The pure date rules of maintenance plans: occurrence dates, limits, and how a visit is judged in the compliance report.</summary>
public static class MaintenanceRules
{
    public const int NameMin = 3, NameMax = 120, InstructionsMax = 4000, MaxLeadDays = 90, MaxTargets = 100, MaxMissedPerRun = 500, MaxStepsPerRun = 1000;

    public static string UnitName(PmUnit u) => u.ToString().ToLowerInvariant();
    public static bool TryUnit(string? s, out PmUnit u) => Enum.TryParse((s ?? "").Trim(), true, out u) && Enum.IsDefined(u) && !int.TryParse(s, out _);

    /// <summary>The largest "every" per unit: daily up to a year, weekly up to a year, monthly up to two years, yearly up to five.</summary>
    public static int MaxEvery(PmUnit u) => u switch { PmUnit.Day => 365, PmUnit.Week => 52, PmUnit.Month => 24, _ => 5 };

    /// <summary>The date of occurrence <paramref name="index"/> (0 = the start), always counted from the start so month ends do not drift (31 Jan → 28 Feb → 31 Mar).</summary>
    public static DateOnly DueOn(DateOnly start, PmUnit unit, int every, int index) => unit switch
    {
        PmUnit.Day => start.AddDays(every * index),
        PmUnit.Week => start.AddDays(7 * every * index),
        PmUnit.Month => start.AddMonths(every * index),
        _ => start.AddYears(every * index),
    };

    /// <summary>The first occurrence on or after <paramref name="date"/>.</summary>
    public static int FirstIndexOnOrAfter(DateOnly start, PmUnit unit, int every, DateOnly date)
    {
        if (date <= start) return 0;
        var days = date.DayNumber - start.DayNumber;
        var guess = unit switch
        {
            PmUnit.Day => days / every,
            PmUnit.Week => days / (7 * every),
            PmUnit.Month => ((date.Year - start.Year) * 12 + date.Month - start.Month) / every,
            _ => (date.Year - start.Year) / every,
        };
        var i = Math.Max(0, guess - 1);
        while (DueOn(start, unit, every, i) < date) i++;
        return i;
    }

    /// <summary>The next <paramref name="count"/> dates from occurrence <paramref name="from"/>, stopping at the end date.</summary>
    public static IReadOnlyList<DateOnly> Upcoming(DateOnly start, PmUnit unit, int every, int from, DateOnly? endsOn, int count)
    {
        var list = new List<DateOnly>();
        for (var i = from; list.Count < count; i++)
        {
            var d = DueOn(start, unit, every, i);
            if (endsOn is { } e && d > e) break;
            list.Add(d);
        }
        return list;
    }

    /// <summary>How a visit stands. <paramref name="today"/> is the organisation's date. Done is on time when finished by the end of the due date (the work order's due time).</summary>
    public static string VisitState(bool missed, WorkOrderStatus? status, DateTimeOffset? completedAt, DateTimeOffset? dueAt, DateOnly dueOn, DateOnly today) => (missed, status) switch
    {
        (true, _) => "missed",
        (_, WorkOrderStatus.Done) => dueAt is { } d && completedAt > d ? "late" : "on_time",
        (_, WorkOrderStatus.Cancelled) => "cancelled",
        (_, null) => "removed",
        _ => dueOn < today ? "overdue" : "open",
    };
}
