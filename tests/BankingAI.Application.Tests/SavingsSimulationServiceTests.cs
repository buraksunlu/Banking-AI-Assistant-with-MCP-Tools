using BankingAI.Application.Analytics;
using BankingAI.Data;
using Xunit;
using static BankingAI.Application.Tests.DemoData;

namespace BankingAI.Application.Tests;

// ANA-14. Reads the seeded demo data; expected values are hand-computed acceptance fixtures. On the demo day the six
// complete months are April–September 2026. Ada spends 514,98 a month (Netflix 229,99 + Spotify 99,99 + Starbucks 185) and
// 624,98 in September (+110 Kahve Durağı); Ege's transport is 600, 650, 700, 650, 900, 1.400 and travel only 1.200 in
// August; Deniz's market purchases are 18.000, 24.000 and 31.000 in July–September.
public sealed class SavingsSimulationServiceTests
{
    private static async Task<SavingsSimulation> SimulateAsync(SavingsSimulationQuery query)
    {
        Assert.False(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BANKINGAI_CONNECTION_STRING")),
            "PostgreSQL testi için önce .env ortam değişkenlerini yükleyin.");
        await using var db = new BankingDbContext();
        var periods = new PeriodResolver(new FixedTimeProvider(DemoNowUtc));
        return await new SavingsSimulationService(new SpendingComparisonService(db, periods)).SimulateAsync(query);
    }

    // Total spent, monthly average, monthly saving, annual saving.
    private static (decimal, decimal, decimal, decimal) Figures(SavingsSimulation result)
    {
        var c = Assert.Single(result.Currencies);
        Assert.Equal("TRY", c.CurrencyCode);
        return (c.TotalSpent, c.MonthlyAverage, c.MonthlySaving, c.AnnualSaving);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Halving_a_merchant_saves_half_of_its_monthly_average()
    {
        var result = await SimulateAsync(new(Ada, 50, Merchant: "starbucks"));

        Assert.Equal((new DateOnly(2026, 4, 1), new DateOnly(2026, 9, 30), 6, 50), (result.From, result.To, result.Months, result.ReductionPercent));
        Assert.Equal((MerchantMatchStatus.Matched, "Starbucks"), (result.MerchantMatch!.Status, result.MerchantMatch.Merchant));
        Assert.Equal("FullMonthAverageTimesReduction", result.Method);
        // The todo's example: 185 a month → 92,50 a month, 1.110 a year.
        Assert.Equal((1110m, 185m, 92.50m, 1110m), Figures(result));
        // The evidence: every complete month used, the running October excluded.
        Assert.Equal(new[] { (4, 185m), (5, 185m), (6, 185m), (7, 185m), (8, 185m), (9, 185m) },
            result.Currencies[0].MonthlyAmounts.Select(m => (m.Month, m.Amount)));
        Assert.All(result.Currencies[0].MonthlyAmounts, m => Assert.False(m.IsPartial));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Without_a_category_or_merchant_the_whole_spending_is_simulated()
    {
        var result = await SimulateAsync(new(Ada, 10));

        // 5 × 514,98 + 624,98 = 3.199,88; / 6 = 533,31; 10 % → 53,33 a month, 639,98 a year.
        Assert.Equal(((string?)null, (MerchantMatch?)null), (result.CategoryCode, result.MerchantMatch));
        Assert.Equal((3199.88m, 533.31m, 53.33m, 639.98m), Figures(result));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Months_without_spending_count_in_the_average()
    {
        var result = await SimulateAsync(new(Ege, 50, CategoryCode: "TRAVEL"));

        // Decision K2-A: one 1.200 trip in six months is 200 a month, not 1.200.
        Assert.Equal("TRAVEL", result.CategoryCode);
        Assert.Equal((1200m, 200m, 100m, 1200m), Figures(result));
        Assert.Equal(new[] { 0m, 0m, 0m, 0m, 1200m, 0m }, result.Currencies[0].MonthlyAmounts.Select(m => m.Amount));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Savings_come_from_the_exact_total_not_the_rounded_average()
    {
        var result = await SimulateAsync(new(Ege, 10, CategoryCode: "TRANSPORTATION"));

        // 4.900 / 6 = 816,666…; 10 % a year = 4.900 × 10 × 12 / 600 = 980,00, not 81,67 × 12 = 980,04.
        Assert.Equal((4900m, 816.67m, 81.67m, 980m), Figures(result));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task The_number_of_months_moves_the_window()
    {
        var market = await SimulateAsync(new(Deniz, 20, CategoryCode: "GROCERY", Months: 3));
        var september = await SimulateAsync(new(Ada, 100, Merchant: "starbucks", Months: 1));

        Assert.Equal((new DateOnly(2026, 7, 1), new DateOnly(2026, 9, 30), 3), (market.From, market.To, market.Months));
        // 18.000 + 24.000 + 31.000 = 73.000 / 3 = 24.333,33; 20 % → 4.866,67 a month, 58.400 a year.
        Assert.Equal((73000m, 24333.33m, 4866.67m, 58400m), Figures(market));
        Assert.Equal((new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)), (september.From, september.To));
        Assert.Equal((185m, 185m, 185m, 2220m), Figures(september));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task The_total_equals_the_spending_summary_of_the_same_months()
    {
        await using var db = new BankingDbContext();
        var periods = new PeriodResolver(new FixedTimeProvider(DemoNowUtc));
        var result = await new SavingsSimulationService(new SpendingComparisonService(db, periods)).SimulateAsync(new(Ada, 10));
        var summary = await new SpendingSummaryService(db, periods).GetAsync(new(Ada, PeriodKind.Custom, result.From, result.To));

        Assert.Equal(Assert.Single(summary.Currencies).TotalAmount, Assert.Single(result.Currencies).TotalSpent);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task No_spending_or_no_match_gives_no_rows_and_no_guess()
    {
        var clothing = await SimulateAsync(new(Ada, 50, CategoryCode: "CLOTHING"));
        var ambiguous = await SimulateAsync(new(Ege, 50, Merchant: "şehir"));

        // No invented "0" row; the window is still stated.
        Assert.Empty(clothing.Currencies);
        Assert.Equal(new DateOnly(2026, 4, 1), clothing.From);
        Assert.Equal(MerchantMatchStatus.Ambiguous, ambiguous.MerchantMatch!.Status);
        Assert.Empty(ambiguous.Currencies);
    }

    [Theory]
    [Trait("Category", "PostgreSQL")]
    [InlineData(0, 6, null, null, "ReductionPercent")]
    [InlineData(101, 6, null, null, "ReductionPercent")]
    [InlineData(50, 0, null, null, "Months")]                   // the monthly trend's own rule
    [InlineData(50, 25, null, null, "Months")]
    [InlineData(50, 6, "FOOD", "Starbucks", "Merchant")]
    [InlineData(50, 6, "YEMEK", null, "CategoryCode")]
    public async Task Invalid_simulations_name_their_field(int percent, int months, string? category, string? merchant, string field)
    {
        var error = await Assert.ThrowsAsync<ArgumentException>(() => SimulateAsync(new(Ada, percent, category, merchant, months)));

        Assert.Equal(field, error.ParamName);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Scope_rules_match_the_other_spending_analyses()
    {
        var own = await SimulateAsync(new(Ada, 10, AccountId: AdaTryAccount));

        Assert.Equal(3199.88m, Assert.Single(own.Currencies).TotalSpent);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => SimulateAsync(new(Ada, 10, AccountId: DenizTryAccount)));
        await Assert.ThrowsAsync<ArgumentException>(() => SimulateAsync(new(Deniz, 10, AccountId: DenizShopAccount)));
    }
}
