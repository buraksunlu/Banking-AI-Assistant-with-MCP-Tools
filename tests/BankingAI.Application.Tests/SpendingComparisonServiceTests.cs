using BankingAI.Application.Analytics;
using BankingAI.Data;
using Xunit;
using static BankingAI.Application.Tests.DemoData;

namespace BankingAI.Application.Tests;

// Reads the seeded demo data. Expected values are hand-computed acceptance fixtures.
public sealed class SpendingComparisonServiceTests
{
    private static PeriodResolver Periods() => new(new FixedTimeProvider(DemoNowUtc));

    private static BankingDbContext Db()
    {
        Assert.False(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BANKINGAI_CONNECTION_STRING")),
            "PostgreSQL testi için önce .env ortam değişkenlerini yükleyin.");
        return new BankingDbContext();
    }

    private static async Task<SpendingComparison> CompareAsync(SpendingComparisonQuery query)
    {
        await using var db = Db();
        return await new SpendingComparisonService(db, Periods()).CompareAsync(query);
    }

    private static async Task<SpendingTrend> TrendAsync(SpendingTrendQuery query)
    {
        await using var db = Db();
        return await new SpendingComparisonService(db, Periods()).GetMonthlyTrendAsync(query);
    }

    private static (string?, decimal, decimal, decimal)[] Changes(CurrencyComparison currency) =>
        currency.Categories.Select(c => (c.CategoryCode, c.CurrentAmount, c.PreviousAmount, c.Change)).ToArray();

    // --- A) Comparison ---

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Default_compares_september_with_august_and_explains_the_increase()
    {
        var result = await CompareAsync(new(Deniz));

        Assert.Equal((new DateOnly(2026, 9, 1), new DateOnly(2026, 8, 1)), (result.Current.StartDate, result.Previous.StartDate));
        var tl = Assert.Single(result.Currencies);
        Assert.Equal(("TRY", 31000m, 1, 24000m, 1, 7000m, (decimal?)29.17m),
            (tl.CurrencyCode, tl.CurrentAmount, tl.CurrentCount, tl.PreviousAmount, tl.PreviousCount, tl.Change, tl.PercentageChange));
        Assert.Equal(new (string?, decimal, decimal, decimal)[] { ("GROCERY", 31000m, 24000m, 7000m) }, Changes(tl));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Unchanged_categories_stay_listed_with_zero_change()
    {
        var result = await CompareAsync(new(Ada));

        // September: Netflix + Spotify + Starbucks + Kahve Durağı 110; August: the first three.
        var tl = Assert.Single(result.Currencies);
        Assert.Equal((624.98m, 4, 514.98m, 3, 110m, (decimal?)21.36m),
            (tl.CurrentAmount, tl.CurrentCount, tl.PreviousAmount, tl.PreviousCount, tl.Change, tl.PercentageChange));
        Assert.Equal(new (string?, decimal, decimal, decimal)[]
        {
            ("FOOD", 295m, 185m, 110m), ("SUBSCRIPTION", 329.98m, 329.98m, 0m)
        }, Changes(tl));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task A_decrease_lists_its_largest_cause_first()
    {
        var result = await CompareAsync(new(Ege));

        // August had the 1.200 bus ticket; September has more transport and two Kahve Durağı purchases.
        var tl = Assert.Single(result.Currencies);
        Assert.Equal((1530m, 2100m, -570m, (decimal?)-27.14m), (tl.CurrentAmount, tl.PreviousAmount, tl.Change, tl.PercentageChange));
        Assert.Equal(new (string?, decimal, decimal, decimal)[]
        {
            ("TRAVEL", 0m, 1200m, -1200m), ("TRANSPORTATION", 1400m, 900m, 500m), ("FOOD", 130m, 0m, 130m)
        }, Changes(tl));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Month_to_date_with_an_empty_previous_period_has_no_percentage()
    {
        var result = await CompareAsync(new(Deniz, ComparisonKind.MonthToDate));

        // 1–10 Oct vs 1–10 Sep: no purchase in the first ten days of September; never divide by zero.
        var tl = Assert.Single(result.Currencies);
        Assert.Equal((11350m, 0m, 11350m, (decimal?)null), (tl.CurrentAmount, tl.PreviousAmount, tl.Change, tl.PercentageChange));
        Assert.Equal(new (string?, decimal, decimal, decimal)[] { ("GROCERY", 9500m, 0m, 9500m), ("BILLS", 1850m, 0m, 1850m) }, Changes(tl));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Transfers_never_enter_a_comparison()
    {
        var result = await CompareAsync(new(Ada, ComparisonKind.MonthToDate));

        // Ada's 1–10 October spending is 735; counting the 1.500 sent transfer would give 2.235.
        Assert.Equal(735m, Assert.Single(result.Currencies).CurrentAmount);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Custom_comparison_uses_the_given_months()
    {
        var result = await CompareAsync(new(Ada, ComparisonKind.Custom,
            new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31)));

        var tl = Assert.Single(result.Currencies);
        Assert.Equal((624.98m, 514.98m), (tl.CurrentAmount, tl.PreviousAmount));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Period_totals_match_the_spending_summary()
    {
        await using var db = Db();
        var summaries = new SpendingSummaryService(db, Periods());
        var comparisons = new SpendingComparisonService(db, Periods());

        foreach (var customer in new[] { Ada, Deniz, Ege })
        foreach (var kind in new[] { ComparisonKind.PreviousFullMonth, ComparisonKind.MonthToDate })
        {
            var comparison = await comparisons.CompareAsync(new(customer, kind));
            foreach (var (period, pick) in new (ResolvedPeriod, Func<CurrencyComparison, decimal>)[]
                     {
                         (comparison.Current, c => c.CurrentAmount), (comparison.Previous, c => c.PreviousAmount)
                     })
            {
                var summary = await summaries.GetAsync(new(customer, PeriodKind.Custom, period.StartDate, period.EndDate));
                foreach (var currency in comparison.Currencies)
                    Assert.Equal(summary.Currencies.SingleOrDefault(s => s.CurrencyCode == currency.CurrencyCode)?.TotalAmount ?? 0m,
                        pick(currency));
            }
        }
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Invalid_comparisons_are_rejected()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => CompareAsync(new(Ada, ComparisonKind.Custom,
            new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), new DateOnly(2026, 9, 15), new DateOnly(2026, 10, 5))));
        await Assert.ThrowsAsync<ArgumentException>(() => CompareAsync(new(Deniz, AccountId: DenizShopAccount)));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => CompareAsync(new(Ada, AccountId: DenizTryAccount)));
    }

    // --- B) Monthly trend ---

    private static (int, int, bool, decimal, int)[] Points(CurrencyTrend trend) =>
        trend.Months.Select(m => (m.Year, m.Month, m.IsPartial, m.Amount, m.TransactionCount)).ToArray();

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Transport_trend_lists_calendar_months_and_marks_the_running_month()
    {
        var trend = await TrendAsync(new(Ege, CategoryCode: "TRANSPORTATION"));

        Assert.Equal(7, trend.Months.Count);
        Assert.Equal(new[]
        {
            (2026, 4, false, 600m, 2), (2026, 5, false, 650m, 2), (2026, 6, false, 700m, 2), (2026, 7, false, 650m, 2),
            (2026, 8, false, 900m, 2), (2026, 9, false, 1400m, 2), (2026, 10, true, 700m, 1)
        }, Points(Assert.Single(trend.Currencies)));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Months_without_spending_appear_as_zero()
    {
        var trend = await TrendAsync(new(Ege, CategoryCode: "TRAVEL"));

        Assert.Equal(new[] { 0m, 0m, 0m, 0m, 1200m, 0m, 0m }, Assert.Single(trend.Currencies).Months.Select(m => m.Amount));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Merchant_trend_reuses_the_matcher()
    {
        var trend = await TrendAsync(new(Ada, Merchant: "starbucks"));
        var ambiguous = await TrendAsync(new(Ege, Merchant: "şehir"));

        Assert.Equal((MerchantMatchStatus.Matched, "Starbucks"), (trend.MerchantMatch!.Status, trend.MerchantMatch.Merchant));
        Assert.Equal(new[] { 185m, 185m, 185m, 185m, 185m, 185m, 375m }, Assert.Single(trend.Currencies).Months.Select(m => m.Amount));
        Assert.Equal(MerchantMatchStatus.Ambiguous, ambiguous.MerchantMatch!.Status);
        Assert.Empty(ambiguous.Currencies);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Full_months_match_the_spending_summary()
    {
        await using var db = Db();
        var trend = await new SpendingComparisonService(db, Periods()).GetMonthlyTrendAsync(new(Deniz));
        var summaries = new SpendingSummaryService(db, Periods());

        var points = Assert.Single(trend.Currencies).Months;
        Assert.Equal(new[] { 12000m, 14000m, 16000m, 18000m, 24000m, 31000m, 11350m }, points.Select(m => m.Amount));
        foreach (var (window, point) in trend.Months.Zip(points))
        {
            var summary = await summaries.GetAsync(new(Deniz, PeriodKind.Custom, window.Period.StartDate, window.Period.EndDate));
            Assert.Equal(Assert.Single(summary.Currencies).TotalAmount, point.Amount);
        }
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Invalid_trend_requests_are_rejected()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => TrendAsync(new(Ada, Months: 0)));
        await Assert.ThrowsAsync<ArgumentException>(() => TrendAsync(new(Ada, Months: 25)));
        await Assert.ThrowsAsync<ArgumentException>(() => TrendAsync(new(Ada, CategoryCode: "FOOD", Merchant: "Starbucks")));
        await Assert.ThrowsAsync<ArgumentException>(() => TrendAsync(new(Ada, CategoryCode: "YEMEK")));
    }
}
