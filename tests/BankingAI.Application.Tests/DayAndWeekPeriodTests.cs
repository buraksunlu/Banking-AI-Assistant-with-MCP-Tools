using BankingAI.Application.Analytics;
using Xunit;
using static BankingAI.Application.Tests.DemoData;

namespace BankingAI.Application.Tests;

// ANA-20. The demo "now" is Saturday 10 October 2026, 15:00 in Istanbul. Weeks start on Monday (K2-A) and
// "last N days" includes today (K3-A). Expected dates are worked out by hand from the calendar.
public sealed class DayAndWeekPeriodTests
{
    private const string Istanbul = "Europe/Istanbul";

    private static PeriodResolver ResolverAt(DateTimeOffset utcNow) => new(new FixedTimeProvider(utcNow));

    private static DateTimeOffset Utc(int year, int month, int day, int hour, int minute = 0) =>
        new(year, month, day, hour, minute, 0, TimeSpan.Zero);

    private static (DateOnly Start, DateOnly End) Dates(PeriodKind kind, DateTimeOffset utcNow)
    {
        var period = ResolverAt(utcNow).Resolve(kind, Istanbul);
        return (period.StartDate, period.EndDate);
    }

    [Theory]
    [InlineData(PeriodKind.Today, 2026, 10, 10, 2026, 10, 10)]
    [InlineData(PeriodKind.Yesterday, 2026, 10, 9, 2026, 10, 9)]
    [InlineData(PeriodKind.ThisWeek, 2026, 10, 5, 2026, 10, 10)]    // Monday 5 October up to today
    [InlineData(PeriodKind.LastWeek, 2026, 9, 28, 2026, 10, 4)]     // Monday 28 September to Sunday 4 October
    [InlineData(PeriodKind.Last7Days, 2026, 10, 4, 2026, 10, 10)]   // 7 days, today included
    [InlineData(PeriodKind.Last30Days, 2026, 9, 11, 2026, 10, 10)]  // 30 days, today included
    [InlineData(PeriodKind.ThisYear, 2026, 1, 1, 2026, 10, 10)]
    public void Each_period_covers_the_expected_local_days(PeriodKind kind, int fromYear, int fromMonth, int fromDay,
        int toYear, int toMonth, int toDay)
    {
        Assert.Equal((new DateOnly(fromYear, fromMonth, fromDay), new DateOnly(toYear, toMonth, toDay)), Dates(kind, DemoNowUtc));
    }

    [Fact]
    public void Yesterday_is_one_whole_local_day_as_a_utc_range()
    {
        var period = ResolverAt(DemoNowUtc).Resolve(PeriodKind.Yesterday, Istanbul);

        // Istanbul is UTC+3: 9 October 00:00 local is 8 October 21:00 UTC.
        Assert.Equal(Utc(2026, 10, 8, 21), period.StartUtc);
        Assert.Equal(Utc(2026, 10, 9, 21), period.EndUtc);
        Assert.Equal(PeriodKind.Yesterday, period.Kind);
    }

    [Theory]
    [InlineData(PeriodKind.Last7Days, 7)]
    [InlineData(PeriodKind.Last30Days, 30)]
    public void Last_n_days_counts_exactly_n_days(PeriodKind kind, int days)
    {
        var (start, end) = Dates(kind, DemoNowUtc);
        Assert.Equal(days, end.DayNumber - start.DayNumber + 1);
    }

    [Fact]
    public void On_a_monday_this_week_is_only_today()
    {
        var monday = Utc(2026, 10, 5, 9);

        Assert.Equal((new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 5)), Dates(PeriodKind.ThisWeek, monday));
        Assert.Equal((new DateOnly(2026, 9, 28), new DateOnly(2026, 10, 4)), Dates(PeriodKind.LastWeek, monday));
    }

    [Fact]
    public void On_a_sunday_this_week_still_started_on_monday()
    {
        var sunday = Utc(2026, 10, 11, 9);

        Assert.Equal((new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 11)), Dates(PeriodKind.ThisWeek, sunday));
    }

    [Fact]
    public void Yesterday_on_the_first_of_a_month_is_the_last_day_of_the_month_before()
    {
        Assert.Equal((new DateOnly(2026, 10, 31), new DateOnly(2026, 10, 31)), Dates(PeriodKind.Yesterday, Utc(2026, 11, 1, 9)));
    }

    [Fact]
    public void On_new_years_day_yesterday_is_last_year_and_this_year_is_one_day()
    {
        var newYear = Utc(2027, 1, 1, 9);

        Assert.Equal((new DateOnly(2026, 12, 31), new DateOnly(2026, 12, 31)), Dates(PeriodKind.Yesterday, newYear));
        Assert.Equal((new DateOnly(2027, 1, 1), new DateOnly(2027, 1, 1)), Dates(PeriodKind.ThisYear, newYear));
    }

    [Fact]
    public void Today_follows_the_local_calendar_not_the_utc_one()
    {
        // 9 October 21:30 UTC is already 10 October 00:30 in Istanbul.
        var justAfterLocalMidnight = Utc(2026, 10, 9, 21, 30);

        Assert.Equal((new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 10)), Dates(PeriodKind.Today, justAfterLocalMidnight));
        Assert.Equal((new DateOnly(2026, 10, 9), new DateOnly(2026, 10, 9)), Dates(PeriodKind.Yesterday, justAfterLocalMidnight));
    }

    [Fact]
    public void Dates_cannot_be_given_with_a_named_day_or_week_period()
    {
        Assert.Throws<ArgumentException>(() =>
            ResolverAt(DemoNowUtc).Resolve(PeriodKind.ThisWeek, Istanbul, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 2)));
    }
}
