namespace AnnabaDigital.Domain;

/// <summary>A weekly working window in minutes after local midnight (0–1440).</summary>
public readonly record struct WorkingWindow(DayOfWeek Day, int StartMinute, int EndMinute);

/// <summary>
/// Pure working-time arithmetic for service-level clocks: how much working time lies between two instants, and when a given amount of working time
/// has passed. No I/O, no clock — everything is a function of its inputs, so it is exhaustively unit-tested (time zones, DST, holidays, breaks).
/// Local wall-clock times that do not exist (spring-forward gap) are moved forward; ambiguous ones (fall-back) use standard time.
/// </summary>
public sealed class WorkingCalendar
{
    /// <summary>Safety stop: a calendar with working windows but every day a holiday must not loop for ever.</summary>
    public const int MaxSearchDays = 3660;

    private readonly TimeZoneInfo _tz;
    private readonly Dictionary<DayOfWeek, (int Start, int End)[]> _byDay;
    private readonly HashSet<DateOnly> _holidays;
    public bool AlwaysOpen { get; }

    public WorkingCalendar(TimeZoneInfo timeZone, IEnumerable<WorkingWindow> windows, IEnumerable<DateOnly>? holidays = null, bool alwaysOpen = false)
    {
        _tz = timeZone; AlwaysOpen = alwaysOpen; _holidays = [.. holidays ?? []];
        _byDay = windows.Where(w => w.EndMinute > w.StartMinute).GroupBy(w => w.Day).ToDictionary(g => g.Key, g => Merge(g.Select(w => (Math.Max(0, w.StartMinute), Math.Min(1440, w.EndMinute))).OrderBy(w => w.Item1)));
    }

    public static WorkingCalendar Always(TimeZoneInfo? tz = null) => new(tz ?? TimeZoneInfo.Utc, [], null, true);

    /// <summary>True when the calendar has at least one working window (so time can ever pass on its clock).</summary>
    public bool HasWorkingTime => AlwaysOpen || _byDay.Count > 0;

    private static (int Start, int End)[] Merge(IEnumerable<(int Start, int End)> sorted)
    {
        var result = new List<(int, int)>();
        foreach (var (s, e) in sorted)
        {
            if (e <= s) continue;
            if (result.Count > 0 && s <= result[^1].Item2) result[^1] = (result[^1].Item1, Math.Max(result[^1].Item2, e));
            else result.Add((s, e));
        }
        return [.. result];
    }

    private DateTimeOffset ToInstant(DateOnly date, int minute)
    {
        var local = date.ToDateTime(TimeOnly.MinValue).AddMinutes(minute);
        if (_tz.IsInvalidTime(local)) local = local.AddHours(1);
        var utc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), _tz);
        return new DateTimeOffset(utc, TimeSpan.Zero);
    }

    private DateOnly LocalDate(DateTimeOffset at) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, _tz).DateTime);

    /// <summary>Working intervals (UTC, ascending) that end after <paramref name="from"/>, starting with the day <paramref name="from"/> falls on.</summary>
    private IEnumerable<(DateTimeOffset Start, DateTimeOffset End)> IntervalsFrom(DateTimeOffset from)
    {
        var date = LocalDate(from);
        for (var i = 0; i < MaxSearchDays; i++, date = date.AddDays(1))
        {
            if (_holidays.Contains(date) || !_byDay.TryGetValue(date.DayOfWeek, out var windows)) continue;
            foreach (var (s, e) in windows)
            {
                var start = ToInstant(date, s); var end = ToInstant(date, e);
                if (end > start) yield return (start, end);
            }
        }
    }

    public bool IsOpen(DateTimeOffset at)
    {
        if (AlwaysOpen) return true;
        foreach (var (s, e) in IntervalsFrom(at))
        {
            if (s > at) return false;
            if (at < e) return true;
        }
        return false;
    }

    /// <summary>First moment at or after <paramref name="at"/> when work happens; null when the calendar has no working time at all.</summary>
    public DateTimeOffset? NextOpening(DateTimeOffset at)
    {
        if (AlwaysOpen) return at;
        foreach (var (s, e) in IntervalsFrom(at))
        {
            if (e <= at) continue;
            return s > at ? s : at;
        }
        return null;
    }

    /// <summary>Working time between two instants (zero when <paramref name="to"/> is not after <paramref name="from"/>).</summary>
    public TimeSpan WorkingTimeBetween(DateTimeOffset from, DateTimeOffset to)
    {
        if (to <= from) return TimeSpan.Zero;
        if (AlwaysOpen) return to - from;
        var total = TimeSpan.Zero;
        foreach (var (s, e) in IntervalsFrom(from))
        {
            if (s >= to) break;
            var a = s > from ? s : from; var b = e < to ? e : to;
            if (b > a) total += b - a;
        }
        return total;
    }

    /// <summary>The instant at which <paramref name="amount"/> of working time has elapsed since <paramref name="from"/>.</summary>
    /// <exception cref="InvalidOperationException">The calendar has no working time within about ten years.</exception>
    public DateTimeOffset AddWorkingTime(DateTimeOffset from, TimeSpan amount)
    {
        if (amount < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(amount), "Amount must not be negative.");
        if (AlwaysOpen || amount == TimeSpan.Zero) return from + amount;
        var remaining = amount;
        foreach (var (s, e) in IntervalsFrom(from))
        {
            var start = s > from ? s : from;
            if (e <= start) continue;
            var available = e - start;
            if (remaining <= available) return start + remaining;
            remaining -= available;
        }
        throw new InvalidOperationException("This calendar has no working time.");
    }
}
