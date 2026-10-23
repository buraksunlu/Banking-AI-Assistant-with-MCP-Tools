using BankingAI.Application.Analytics;
using Xunit;
using static BankingAI.Application.Tests.DemoData;

namespace BankingAI.Application.Tests;

public sealed class PeriodResolverTests
{
    private const string Istanbul = "Europe/Istanbul";

    private static PeriodResolver ResolverAt(DateTimeOffset utcNow) => new(new FixedTimeProvider(utcNow));

    private static DateTimeOffset Utc(int year, int month, int day, int hour) =>
        new(year, month, day, hour, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ThisMonth_runs_from_local_month_start_through_end_of_today()
    {
        var period = ResolverAt(DemoNowUtc).Resolve(PeriodKind.ThisMonth, Istanbul);

        Assert.Equal(Utc(2026, 9, 30, 21), period.StartUtc);
        Assert.Equal(Utc(2026, 10, 10, 21), period.EndUtc);
        Assert.Equal(new DateOnly(2026, 10, 1), period.StartDate);
        Assert.Equal(new DateOnly(2026, 10, 10), period.EndDate);
    }

    [Fact]
    public void LastMonth_is_the_previous_full_local_month()
    {
        var period = ResolverAt(DemoNowUtc).Resolve(PeriodKind.LastMonth, Istanbul);

        Assert.Equal(Utc(2026, 8, 31, 21), period.StartUtc);
        Assert.Equal(Utc(2026, 9, 30, 21), period.EndUtc);
        Assert.Equal(new DateOnly(2026, 9, 1), period.StartDate);
        Assert.Equal(new DateOnly(2026, 9, 30), period.EndDate);
    }

    [Fact]
    public void LastMonth_in_january_is_december_of_previous_year()
    {
        var period = ResolverAt(Utc(2027, 1, 15, 9)).Resolve(PeriodKind.LastMonth, Istanbul);

        Assert.Equal(new DateOnly(2026, 12, 1), period.StartDate);
        Assert.Equal(new DateOnly(2026, 12, 31), period.EndDate);
    }

    [Theory]
    [InlineData(PeriodKind.Last3Months, 2026, 7, 10)]
    [InlineData(PeriodKind.Last6Months, 2026, 4, 10)]
    public void LastNMonths_is_a_rolling_window_ending_today(PeriodKind kind, int year, int month, int day)
    {
        var period = ResolverAt(DemoNowUtc).Resolve(kind, Istanbul);

        Assert.Equal(new DateOnly(year, month, day), period.StartDate);
        Assert.Equal(new DateOnly(2026, 10, 10), period.EndDate);
        Assert.Equal(Utc(2026, 10, 10, 21), period.EndUtc);
    }

    [Fact]
    public void Last3Months_clamps_to_the_last_day_of_a_shorter_month()
    {
        var period = ResolverAt(Utc(2026, 5, 31, 9)).Resolve(PeriodKind.Last3Months, Istanbul);

        Assert.Equal(new DateOnly(2026, 2, 28), period.StartDate);
    }

    [Fact]
    public void Local_calendar_day_is_used_when_it_differs_from_the_utc_day()
    {
        // 31 Ekim 22:30 UTC = 1 Kasım 01:30 Istanbul.
        var utcNow = new DateTimeOffset(2026, 10, 31, 22, 30, 0, TimeSpan.Zero);

        var period = ResolverAt(utcNow).Resolve(PeriodKind.ThisMonth, Istanbul);

        Assert.Equal(new DateOnly(2026, 11, 1), period.StartDate);
        Assert.Equal(Utc(2026, 10, 31, 21), period.StartUtc);
    }

    [Fact]
    public void Boundaries_are_returned_with_zero_offset()
    {
        var period = ResolverAt(DemoNowUtc).Resolve(PeriodKind.ThisMonth, Istanbul);

        Assert.Equal(TimeSpan.Zero, period.StartUtc.Offset);
        Assert.Equal(TimeSpan.Zero, period.EndUtc.Offset);
    }

    [Fact]
    public void Custom_range_includes_both_local_dates()
    {
        var period = ResolverAt(DemoNowUtc).Resolve(
            PeriodKind.Custom, Istanbul, new DateOnly(2026, 1, 15), new DateOnly(2026, 2, 15));

        Assert.Equal(Utc(2026, 1, 14, 21), period.StartUtc);
        Assert.Equal(Utc(2026, 2, 15, 21), period.EndUtc);
    }

    [Fact]
    public void Custom_range_of_exactly_24_months_is_accepted()
    {
        var period = ResolverAt(DemoNowUtc).Resolve(
            PeriodKind.Custom, Istanbul, new DateOnly(2024, 10, 10), new DateOnly(2026, 10, 10));

        Assert.Equal(new DateOnly(2024, 10, 10), period.StartDate);
    }

    [Theory]
    [InlineData(2026, 2, 16, 2026, 2, 15)] // from > to
    [InlineData(2026, 10, 1, 2026, 10, 11)] // to yerel olarak gelecekte
    [InlineData(2024, 10, 9, 2026, 10, 10)] // 24 aydan uzun
    public void Invalid_custom_range_is_rejected(int fy, int fm, int fd, int ty, int tm, int td)
    {
        var resolver = ResolverAt(DemoNowUtc);

        Assert.Throws<ArgumentException>(() => resolver.Resolve(
            PeriodKind.Custom, Istanbul, new DateOnly(fy, fm, fd), new DateOnly(ty, tm, td)));
    }

    [Fact]
    public void Custom_without_dates_is_rejected()
    {
        var resolver = ResolverAt(DemoNowUtc);

        Assert.Throws<ArgumentException>(() => resolver.Resolve(PeriodKind.Custom, Istanbul, new DateOnly(2026, 1, 1)));
    }

    [Fact]
    public void Named_period_with_dates_is_rejected()
    {
        // Model hem "bu ay" hem tarih gönderirse hangisinin geçerli olduğunu tahmin etmeyiz.
        var resolver = ResolverAt(DemoNowUtc);

        Assert.Throws<ArgumentException>(() => resolver.Resolve(
            PeriodKind.ThisMonth, Istanbul, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31)));
    }

    [Fact]
    public void Unknown_time_zone_is_rejected()
    {
        var resolver = ResolverAt(DemoNowUtc);

        Assert.Throws<ArgumentException>(() => resolver.Resolve(PeriodKind.ThisMonth, "Europe/Imaginary"));
    }

    [Fact]
    public void Undefined_period_kind_is_rejected()
    {
        var resolver = ResolverAt(DemoNowUtc);

        Assert.Throws<ArgumentException>(() => resolver.Resolve((PeriodKind)99, Istanbul));
    }

    // --- ANA-07: comparison periods ---

    private static (DateOnly, DateOnly, DateOnly, DateOnly) Dates(ComparisonPeriods p) =>
        (p.Current.StartDate, p.Current.EndDate, p.Previous.StartDate, p.Previous.EndDate);

    [Fact]
    public void Previous_full_month_compares_the_last_two_complete_months()
    {
        var periods = ResolverAt(DemoNowUtc).ResolveComparison(ComparisonKind.PreviousFullMonth, Istanbul);

        Assert.Equal((new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31)),
            Dates(periods));
        Assert.Equal(Utc(2026, 9, 30, 21), periods.Current.EndUtc);
        Assert.Equal(periods.Previous.EndUtc, periods.Current.StartUtc); // adjacent, no gap or overlap
    }

    [Fact]
    public void Previous_full_month_crosses_the_year_in_january()
    {
        var periods = ResolverAt(Utc(2027, 1, 15, 9)).ResolveComparison(ComparisonKind.PreviousFullMonth, Istanbul);

        Assert.Equal((new DateOnly(2026, 12, 1), new DateOnly(2026, 12, 31), new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30)),
            Dates(periods));
    }

    [Fact]
    public void Month_to_date_compares_the_same_days_of_the_previous_month()
    {
        var periods = ResolverAt(DemoNowUtc).ResolveComparison(ComparisonKind.MonthToDate, Istanbul);

        Assert.Equal((new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 10), new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 10)),
            Dates(periods));
    }

    [Fact]
    public void Month_to_date_clamps_to_a_shorter_previous_month()
    {
        var periods = ResolverAt(Utc(2026, 3, 31, 9)).ResolveComparison(ComparisonKind.MonthToDate, Istanbul);

        Assert.Equal((new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28)),
            Dates(periods));
    }

    [Fact]
    public void Custom_comparison_uses_both_given_ranges()
    {
        var periods = ResolverAt(DemoNowUtc).ResolveComparison(ComparisonKind.Custom, Istanbul,
            new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31));

        Assert.Equal((new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31)),
            Dates(periods));
    }

    [Fact]
    public void Invalid_comparisons_are_rejected()
    {
        var resolver = ResolverAt(DemoNowUtc);

        // Overlapping ranges would count one purchase in both periods.
        Assert.Throws<ArgumentException>(() => resolver.ResolveComparison(ComparisonKind.Custom, Istanbul,
            new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), new DateOnly(2026, 9, 15), new DateOnly(2026, 10, 5)));
        Assert.Throws<ArgumentException>(() => resolver.ResolveComparison(ComparisonKind.Custom, Istanbul,
            new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 11), new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)));
        Assert.Throws<ArgumentException>(() => resolver.ResolveComparison(ComparisonKind.Custom, Istanbul,
            new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)));
        Assert.Throws<ArgumentException>(() => resolver.ResolveComparison(ComparisonKind.PreviousFullMonth, Istanbul,
            new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)));
        Assert.Throws<ArgumentException>(() => resolver.ResolveComparison((ComparisonKind)9, Istanbul));
    }

    // --- ANA-07: monthly windows ---

    [Fact]
    public void Monthly_windows_are_calendar_months_plus_the_running_month()
    {
        var windows = ResolverAt(DemoNowUtc).ResolveMonthlyWindows(6, Istanbul);

        Assert.Equal(new[] { (2026, 4, false), (2026, 5, false), (2026, 6, false), (2026, 7, false), (2026, 8, false), (2026, 9, false), (2026, 10, true) },
            windows.Select(w => (w.Year, w.Month, w.IsPartial)).ToArray());
        Assert.Equal((new DateOnly(2026, 4, 1), Utc(2026, 3, 31, 21)), (windows[0].Period.StartDate, windows[0].Period.StartUtc));
        Assert.Equal((new DateOnly(2026, 10, 10), Utc(2026, 10, 10, 21)), (windows[^1].Period.EndDate, windows[^1].Period.EndUtc));
        // Consecutive windows touch exactly: no gap, no overlap.
        Assert.All(windows.Zip(windows.Skip(1)), pair => Assert.Equal(pair.First.Period.EndUtc, pair.Second.Period.StartUtc));
    }

    [Fact]
    public void Monthly_windows_cross_the_year()
    {
        var windows = ResolverAt(Utc(2027, 1, 15, 9)).ResolveMonthlyWindows(3, Istanbul);

        Assert.Equal(new[] { (2026, 10), (2026, 11), (2026, 12), (2027, 1) }, windows.Select(w => (w.Year, w.Month)).ToArray());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(25)]
    public void Monthly_window_count_is_limited(int months) =>
        Assert.Throws<ArgumentException>(() => ResolverAt(DemoNowUtc).ResolveMonthlyWindows(months, Istanbul));

    // --- API-17: the whole history of a transaction list ---

    [Theory]
    // 21:30 UTC is already 1 October in Istanbul (UTC+3) but still 30 September in Berlin (UTC+2 in summer).
    [InlineData(Istanbul, 2025, 10, 1, 21, 21)]
    [InlineData("Europe/Berlin", 2025, 9, 30, 22, 22)]
    public void All_time_starts_on_the_local_day_of_the_oldest_transaction(
        string timeZone, int year, int month, int day, int startHour, int endHour)
    {
        var oldest = new DateTimeOffset(2025, 9, 30, 21, 30, 0, TimeSpan.Zero);

        var period = ResolverAt(DemoNowUtc).ResolveAllTime(timeZone, oldest);

        Assert.Equal(PeriodKind.AllTime, period.Kind);
        Assert.Equal((new DateOnly(year, month, day), new DateOnly(2026, 10, 10)), (period.StartDate, period.EndDate));
        Assert.Equal(new DateOnly(year, month, day).AddDays(-1).ToDateTime(new TimeOnly(startHour, 0)), period.StartUtc.UtcDateTime);
        Assert.Equal(Utc(2026, 10, 10, endHour), period.EndUtc);
    }

    [Fact]
    public void All_time_without_transactions_is_today()
    {
        var period = ResolverAt(DemoNowUtc).ResolveAllTime(Istanbul, oldestUtc: null);

        Assert.Equal((PeriodKind.AllTime, new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 10)),
            (period.Kind, period.StartDate, period.EndDate));
        Assert.Equal((Utc(2026, 10, 9, 21), Utc(2026, 10, 10, 21)), (period.StartUtc, period.EndUtc));
    }

    [Fact]
    public void All_time_is_not_a_period_for_analyses()
    {
        // Its start depends on the data, so only a transaction list resolves it (ResolveAllTime); a spending total
        // over "everything" is not a question the analyses answer.
        var error = Assert.Throws<ArgumentException>(() => ResolverAt(DemoNowUtc).Resolve(PeriodKind.AllTime, Istanbul));

        Assert.Equal("period", error.ParamName);
    }
}
