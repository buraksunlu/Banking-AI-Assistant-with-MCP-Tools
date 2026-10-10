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
}
