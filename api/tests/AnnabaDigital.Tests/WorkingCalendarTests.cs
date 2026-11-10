using AnnabaDigital.Domain;
using Xunit;

namespace AnnabaDigital.Tests;

/// <summary>Pure unit tests (no database): working-time arithmetic that service-level clocks are built on.</summary>
public sealed class WorkingCalendarTests
{
    private static readonly TimeZoneInfo Jakarta = TimeZoneInfo.FindSystemTimeZoneById("Asia/Jakarta"); // UTC+7, no DST
    private static readonly TimeZoneInfo NewYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    private static WorkingWindow[] Weekdays(int start, int end) =>
        [.. new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday }.Select(d => new WorkingWindow(d, start, end))];

    private static DateTimeOffset Local(TimeZoneInfo tz, int y, int m, int d, int h, int min = 0)
        => new(TimeZoneInfo.ConvertTimeToUtc(new DateTime(y, m, d, h, min, 0, DateTimeKind.Unspecified), tz), TimeSpan.Zero);

    private static readonly WorkingCalendar Office = new(Jakarta, Weekdays(8 * 60, 17 * 60)); // Mon–Fri 08:00–17:00 Jakarta

    [Fact]
    public void Inside_one_day_counts_plain_elapsed_time()
        => Assert.Equal(TimeSpan.FromHours(3), Office.WorkingTimeBetween(Local(Jakarta, 2026, 11, 9, 9), Local(Jakarta, 2026, 11, 9, 12))); // Monday

    [Fact]
    public void Evenings_and_weekends_do_not_count()
    {
        // Friday 16:00 → Monday 09:00 = 1h (Fri) + 1h (Mon)
        Assert.Equal(TimeSpan.FromHours(2), Office.WorkingTimeBetween(Local(Jakarta, 2026, 11, 13, 16), Local(Jakarta, 2026, 11, 16, 9)));
    }

    [Fact]
    public void Holidays_do_not_count()
    {
        var cal = new WorkingCalendar(Jakarta, Weekdays(8 * 60, 17 * 60), [new DateOnly(2026, 11, 10)]); // Tuesday closed
        Assert.Equal(TimeSpan.FromHours(9), cal.WorkingTimeBetween(Local(Jakarta, 2026, 11, 9, 0), Local(Jakarta, 2026, 11, 11, 0))); // only Monday's 9h
    }

    [Fact]
    public void Lunch_breaks_split_the_day()
    {
        var cal = new WorkingCalendar(Jakarta, [new(DayOfWeek.Monday, 8 * 60, 12 * 60), new(DayOfWeek.Monday, 13 * 60, 17 * 60)]);
        Assert.Equal(TimeSpan.FromHours(8), cal.WorkingTimeBetween(Local(Jakarta, 2026, 11, 9, 6), Local(Jakarta, 2026, 11, 9, 20))); // 08–12 and 13–17
        Assert.False(cal.IsOpen(Local(Jakarta, 2026, 11, 9, 12, 30)));
        Assert.True(cal.IsOpen(Local(Jakarta, 2026, 11, 9, 13)));
    }

    [Fact]
    public void Overlapping_windows_are_merged_not_double_counted()
    {
        var cal = new WorkingCalendar(Jakarta, [new(DayOfWeek.Monday, 8 * 60, 12 * 60), new(DayOfWeek.Monday, 11 * 60, 14 * 60)]);
        Assert.Equal(TimeSpan.FromHours(6), cal.WorkingTimeBetween(Local(Jakarta, 2026, 11, 9, 0), Local(Jakarta, 2026, 11, 10, 0)));
    }

    [Fact]
    public void Adding_time_rolls_over_to_the_next_working_day()
    {
        // Monday 16:00 + 2h → Monday has 1h left, so Tuesday 09:00
        Assert.Equal(Local(Jakarta, 2026, 11, 10, 9), Office.AddWorkingTime(Local(Jakarta, 2026, 11, 9, 16), TimeSpan.FromHours(2)));
    }

    [Fact]
    public void Adding_time_that_starts_outside_hours_starts_at_the_next_opening()
    {
        // Saturday 10:00 + 1h → Monday 09:00
        Assert.Equal(Local(Jakarta, 2026, 11, 16, 9), Office.AddWorkingTime(Local(Jakarta, 2026, 11, 14, 10), TimeSpan.FromHours(1)));
    }

    [Fact]
    public void Adding_exactly_the_time_left_ends_at_closing()
        => Assert.Equal(Local(Jakarta, 2026, 11, 9, 17), Office.AddWorkingTime(Local(Jakarta, 2026, 11, 9, 15), TimeSpan.FromHours(2)));

    [Fact]
    public void Adding_zero_returns_the_start()
    {
        var t = Local(Jakarta, 2026, 11, 14, 10);
        Assert.Equal(t, Office.AddWorkingTime(t, TimeSpan.Zero));
    }

    [Fact]
    public void Add_and_between_are_inverse_of_each_other()
    {
        var from = Local(Jakarta, 2026, 11, 12, 15, 30);
        foreach (var hours in new[] { 1, 7, 9, 20, 45, 100 })
        {
            var due = Office.AddWorkingTime(from, TimeSpan.FromHours(hours));
            Assert.Equal(TimeSpan.FromHours(hours), Office.WorkingTimeBetween(from, due));
        }
    }

    [Fact]
    public void Always_open_counts_every_minute()
    {
        var cal = WorkingCalendar.Always();
        var from = new DateTimeOffset(2026, 11, 14, 22, 0, 0, TimeSpan.Zero);
        Assert.Equal(TimeSpan.FromHours(30), cal.WorkingTimeBetween(from, from.AddHours(30)));
        Assert.Equal(from.AddHours(30), cal.AddWorkingTime(from, TimeSpan.FromHours(30)));
        Assert.True(cal.IsOpen(from));
    }

    [Fact]
    public void Next_opening_skips_closed_periods()
    {
        Assert.Equal(Local(Jakarta, 2026, 11, 16, 8), Office.NextOpening(Local(Jakarta, 2026, 11, 13, 18)));  // Fri evening → Mon 08:00
        Assert.Equal(Local(Jakarta, 2026, 11, 9, 10), Office.NextOpening(Local(Jakarta, 2026, 11, 9, 10)));    // already open
    }

    [Fact]
    public void A_calendar_without_windows_has_no_working_time()
    {
        var cal = new WorkingCalendar(Jakarta, []);
        Assert.False(cal.HasWorkingTime);
        Assert.Null(cal.NextOpening(DateTimeOffset.UtcNow));
        Assert.Equal(TimeSpan.Zero, cal.WorkingTimeBetween(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(5)));
        Assert.Throws<InvalidOperationException>(() => cal.AddWorkingTime(DateTimeOffset.UtcNow, TimeSpan.FromHours(1)));
    }

    [Fact]
    public void Spring_forward_loses_the_missing_hour()
    {
        // New York, Sunday 8 Mar 2026: 02:00→03:00 does not exist. A 01:00–04:00 window is therefore 2 real hours.
        var cal = new WorkingCalendar(NewYork, [new(DayOfWeek.Sunday, 60, 240)]);
        var from = Local(NewYork, 2026, 3, 8, 0, 0); var to = Local(NewYork, 2026, 3, 9, 0, 0);
        Assert.Equal(TimeSpan.FromHours(2), cal.WorkingTimeBetween(from, to));
    }

    [Fact]
    public void Fall_back_keeps_the_window_a_normal_length_in_standard_time()
    {
        // 1 Nov 2026 (fall back at 02:00): a 08:00–17:00 window is unaffected by the repeated 01:00 hour.
        var cal = new WorkingCalendar(NewYork, [new(DayOfWeek.Sunday, 8 * 60, 17 * 60)]);
        Assert.Equal(TimeSpan.FromHours(9), cal.WorkingTimeBetween(Local(NewYork, 2026, 11, 1, 0, 0), Local(NewYork, 2026, 11, 2, 0, 0)));
    }

    [Fact]
    public void Local_dates_follow_the_calendar_time_zone_not_utc()
    {
        // Monday 06:00 Jakarta is still Sunday 23:00 UTC; it must count as a Monday before opening.
        var instant = Local(Jakarta, 2026, 11, 9, 6);
        Assert.Equal(DayOfWeek.Sunday, instant.UtcDateTime.DayOfWeek);
        Assert.False(Office.IsOpen(instant));
        Assert.Equal(Local(Jakarta, 2026, 11, 9, 8), Office.NextOpening(instant));
    }

    [Fact]
    public void Everything_closed_for_years_fails_instead_of_looping()
    {
        var holidays = Enumerable.Range(0, WorkingCalendar.MaxSearchDays + 10).Select(i => new DateOnly(2026, 1, 1).AddDays(i));
        var cal = new WorkingCalendar(Jakarta, Weekdays(8 * 60, 17 * 60), holidays);
        Assert.Throws<InvalidOperationException>(() => cal.AddWorkingTime(Local(Jakarta, 2026, 1, 1, 0), TimeSpan.FromHours(1)));
    }
}
