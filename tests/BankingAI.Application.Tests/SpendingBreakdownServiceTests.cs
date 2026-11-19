using BankingAI.Application.Analytics;
using BankingAI.Application.Investment;
using BankingAI.Data;
using Xunit;
using static BankingAI.Application.Tests.DemoData;

namespace BankingAI.Application.Tests;

// DEMO-02 on the fixed demo data (business "today" 10 October 2026). Murat's September 2026 is hand-listed in the plan
// (docs/superpowers/plans/2026-11-19-demo-02-routine-breakdown.md): 20 purchases, 53.388,39; Can Kırtasiye (OTHER) and the cinema are
// the only non-routine ones; prices of 11 September and 9 October 2026 come from the fixed market table.
public sealed class SpendingBreakdownServiceTests
{
    private static readonly Guid Cinema = Guid.Parse("30000000-0000-0000-0000-000000012207");
    private static readonly Guid Netflix = Guid.Parse("30000000-0000-0000-0000-000000012205");

    // Data tests briefly commit a temporary active asset; only the three real ones are compared.
    private static readonly string[] Symbols = ["USDTRY", "EURTRY", "GRAM_GOLD_TRY"];

    private static IEnumerable<AssetWhatIf> Real(IEnumerable<AssetWhatIf> assets) => assets.Where(a => Symbols.Contains(a.Symbol));

    private static BankingDbContext Db()
    {
        Assert.False(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BANKINGAI_CONNECTION_STRING")),
            "PostgreSQL testi için önce .env ortam değişkenlerini yükleyin.");
        return new BankingDbContext();
    }

    private static SpendingBreakdownService Service(BankingDbContext db) =>
        new(db, new PeriodResolver(new FixedTimeProvider(DemoNowUtc)), new MarketPriceReader(db));

    private static async Task<SpendingBreakdown> BreakdownAsync(Guid customer, PeriodKind period)
    {
        await using var db = Db();
        return await Service(db).GetAsync(new SpendingBreakdownQuery(customer, period));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Murat_last_month_splits_into_routine_and_non_routine()
    {
        var lira = Assert.Single((await BreakdownAsync(Murat, PeriodKind.LastMonth)).Currencies);

        Assert.Equal(("TRY", 51495.89m, 18, 1892.50m, 2),
            (lira.CurrencyCode, lira.Routine.Total, lira.Routine.Count, lira.NonRoutine.Total, lira.NonRoutine.Count));
        RoutineReason Reason(string merchant) => Assert.Single(lira.Routine.Items, i => i.Transaction.Merchant == merchant).Reason;
        Assert.Equal(RoutineReason.Subscription, Reason("Netflix"));
        Assert.Equal(RoutineReason.Bill, Reason("Su Faturası"));
        Assert.Equal(RoutineReason.Essential, Reason("Ev İnterneti"));
        Assert.Equal(RoutineReason.Essential, Reason("Okul Taksidi"));
        Assert.All(lira.Routine.Items, i => Assert.Null(i.Installment));
        Assert.False(Assert.Single(lira.NonRoutine.Items, i => i.Transaction.Merchant == "Can Kırtasiye").Eligible);
        Assert.True(Assert.Single(lira.NonRoutine.Items, i => i.Transaction.Merchant == "Sinema").Eligible);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Murat_last_month_investment_is_the_cinema_alone()
    {
        var lira = Assert.Single((await BreakdownAsync(Murat, PeriodKind.LastMonth)).Currencies);
        var investment = Assert.IsType<BreakdownInvestment>(lira.NonRoutine.Investment);

        Assert.Equal((642.50m, 0m, "EachOnItsOwnDay", new DateOnly(2026, 10, 10), WhatIfMath.Disclaimer),
            (investment.Amount, investment.ExcludedRecent, investment.Method, investment.ValueDate, investment.Disclaimer));
        Assert.Equal(
            [("USDTRY", 651.75m, 9.25m, 1.44m), ("EURTRY", 630.33m, -12.17m, -1.89m), ("GRAM_GOLD_TRY", 627.58m, -14.92m, -2.32m)],
            Real(investment.Assets).Select(a => (a.Symbol, a.Value!.Value, a.Difference!.Value, a.DifferencePercent!.Value)));
        // The cinema is 1,12 times its usual month: no advice.
        Assert.Empty(lira.NonRoutine.Advice);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task This_month_has_no_non_routine_spending()
    {
        var lira = Assert.Single((await BreakdownAsync(Murat, PeriodKind.ThisMonth)).Currencies);

        Assert.Equal((0, (BreakdownInvestment?)null), (lira.NonRoutine.Count, lira.NonRoutine.Investment));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task A_cancelled_subscription_is_routine_in_the_months_it_was_paid()
    {
        // Can's Netflix ends on 12 June 2026: routine at the end of June, though not "today" (10 October).
        await using var db = Db();
        var june = await Service(db).GetAsync(
            new SpendingBreakdownQuery(Can, PeriodKind.Custom, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30)));

        var netflix = Assert.Single(Assert.Single(june.Currencies).Routine.Items, i => i.Transaction.Merchant == "Netflix");
        Assert.Equal(RoutineReason.Subscription, netflix.Reason);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task A_long_period_is_not_broken_down()
    {
        var result = await BreakdownAsync(Murat, PeriodKind.Last3Months);

        Assert.True(result.PeriodTooLong);
        Assert.Empty(result.Currencies);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Two_currencies_stay_apart()
    {
        var result = await BreakdownAsync(Zeynep, PeriodKind.LastMonth);

        Assert.Equal(["TRY", "USD"], result.Currencies.Select(c => c.CurrencyCode));
        Assert.Null(result.Currencies[1].NonRoutine.Investment);
        Assert.Empty(result.Currencies[1].NonRoutine.Advice);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Insight_of_the_cinema_and_of_netflix()
    {
        await using var db = Db();
        var cinema = await Service(db).InsightAsync(Murat, Cinema);
        var netflix = await Service(db).InsightAsync(Murat, Netflix);

        Assert.Equal(("Sinema", SpendingGroup.Discretionary, (RoutineReason?)null, (string?)null),
            (cinema.Transaction.Merchant, cinema.Group, cinema.Routine, cinema.Note));
        Assert.Equal([651.75m, 630.33m, 627.58m], Real(cinema.Investment!.Assets).Select(a => a.Value!.Value));
        Assert.Equal(("Netflix", RoutineReason.Subscription, (BreakdownInvestment?)null, "Rutin ödeme"),
            (netflix.Transaction.Merchant, netflix.Routine!.Value, netflix.Investment, netflix.Note));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Insight_of_another_customers_transaction_is_not_found()
    {
        await using var db = Db();

        await Assert.ThrowsAsync<KeyNotFoundException>(() => Service(db).InsightAsync(Ada, Cinema));
    }
}
