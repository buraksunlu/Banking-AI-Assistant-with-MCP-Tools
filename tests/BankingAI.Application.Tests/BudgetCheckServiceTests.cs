using System.Globalization;
using BankingAI.Application.Analytics;
using BankingAI.Data;
using Xunit;
using static BankingAI.Application.Tests.DemoData;

namespace BankingAI.Application.Tests;

// ANA-13. Reads the seeded demo data; expected values are hand-computed acceptance fixtures. On the demo day (10 October,
// 10 of 31 days) Ada's October purchases are 120 + 210 + 95 + 165 (FOOD = 590; Starbucks = 375) and 145 unclassified
// (total 735); Deniz's October GROCERY purchase is 9.500.
public sealed class BudgetCheckServiceTests
{
    private static BudgetCheckService Service(BankingDbContext db, DateTimeOffset now)
    {
        var periods = new PeriodResolver(new FixedTimeProvider(now));
        return new BudgetCheckService(db, new SpendingSummaryService(db, periods), new CategoryBreakdownService(db, periods),
            new MerchantSpendingService(db, periods));
    }

    private static async Task<BudgetCheck> CheckAsync(BudgetCheckQuery query, DateTimeOffset? now = null)
    {
        Assert.False(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BANKINGAI_CONNECTION_STRING")),
            "PostgreSQL testi için önce .env ortam değişkenlerini yükleyin.");
        await using var db = new BankingDbContext();
        return await Service(db, now ?? DemoNowUtc).CheckAsync(query);
    }

    // Spent (count), remaining, over, is over, daily average, projected month end, projected over, will exceed.
    private static (decimal, int, decimal, decimal, bool, decimal, decimal, decimal, bool) Figures(BudgetCheck result)
    {
        var f = result.Figures!;
        return (f.SpentAmount, f.TransactionCount, f.RemainingAmount, f.OverAmount, f.IsOverBudget,
            f.DailyAverage, f.ProjectedMonthEnd, f.ProjectedOverAmount, f.WillExceed);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Category_budget_reports_spent_remaining_and_the_month_end_projection()
    {
        var result = await CheckAsync(new(Ada, 1000m, "TRY", CategoryCode: "FOOD"));

        Assert.Equal((new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 10)), (result.Period.StartDate, result.Period.EndDate));
        Assert.Equal(("TRY", 1000m, "FOOD", "Yeme & İçme"), (result.CurrencyCode, result.BudgetAmount, result.CategoryCode, result.CategoryDisplayName));
        // 590 / 10 days = 59; 59 × 31 days = 1.829: on track to exceed 1.000 by 829.
        Assert.Equal((590m, 4, 410m, 0m, false, 59m, 1829m, 829m, true), Figures(result));
        Assert.Equal((10, 31, "LinearDailyAverage"), (result.Figures!.ElapsedDays, result.Figures.DaysInMonth, result.Figures.ProjectionMethod));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Without_a_category_or_merchant_the_budget_covers_total_spending()
    {
        var result = await CheckAsync(new(Ada, 1000m, "TRY"));

        // 735 includes the unclassified 145; 735 × 31 / 10 = 2.278,50.
        Assert.Equal(((string?)null, (MerchantMatch?)null), (result.CategoryCode, result.MerchantMatch));
        Assert.Equal((735m, 5, 265m, 0m, false, 73.50m, 2278.50m, 1278.50m, true), Figures(result));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Merchant_budget_can_already_be_exceeded()
    {
        var result = await CheckAsync(new(Ada, 300m, "TRY", Merchant: "starbucks"));

        Assert.Equal((MerchantMatchStatus.Matched, "Starbucks"), (result.MerchantMatch!.Status, result.MerchantMatch.Merchant));
        // 210 + 165 = 375: 75 over already; nothing remains.
        Assert.Equal((375m, 2, 0m, 75m, true, 37.50m, 1162.50m, 862.50m, true), Figures(result));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task No_spending_is_a_real_zero_against_the_budget()
    {
        var transport = await CheckAsync(new(Ada, 500m, "TRY", CategoryCode: "TRANSPORTATION"));
        var dollars = await CheckAsync(new(Ada, 100m, "USD"));

        Assert.Equal("Ulaşım", transport.CategoryDisplayName);
        Assert.Equal((0m, 0, 500m, 0m, false, 0m, 0m, 0m, false), Figures(transport));
        // The TRY purchases never count against a USD budget.
        Assert.Equal((0m, 0, 100m, 0m, false, 0m, 0m, 0m, false), Figures(dollars));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Large_grocery_spending_projects_far_beyond_the_budget()
    {
        var result = await CheckAsync(new(Deniz, 10000m, "TRY", CategoryCode: "GROCERY"));

        Assert.Equal((9500m, 1, 500m, 0m, false, 950m, 29450m, 19450m, true), Figures(result));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task On_the_last_day_the_projection_equals_the_spending()
    {
        // 31 October: 31 of 31 days have passed. 590 / 31 = 19,0322… is rounded only for display; the projection uses
        // the exact ratio, so it is 590,00 and not 19,03 × 31 = 589,93.
        var result = await CheckAsync(new(Ada, 1000m, "TRY", CategoryCode: "FOOD"), new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.Zero));

        Assert.Equal((31, 31), (result.Figures!.ElapsedDays, result.Figures.DaysInMonth));
        Assert.Equal((590m, 4, 410m, 0m, false, 19.03m, 590m, 0m, false), Figures(result));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Spent_amounts_equal_what_the_other_analyses_report()
    {
        await using var db = new BankingDbContext();
        var periods = new PeriodResolver(new FixedTimeProvider(DemoNowUtc));
        var service = Service(db, DemoNowUtc);
        var month = new SpendingQuery(Ada, PeriodKind.ThisMonth);

        var food = (await new CategoryBreakdownService(db, periods).GetAsync(month)).Currencies.Single(c => c.CurrencyCode == "TRY")
            .Categories.Single(c => c.CategoryCode == "FOOD");
        var total = (await new SpendingSummaryService(db, periods).GetAsync(month)).Currencies.Single(c => c.CurrencyCode == "TRY");
        var starbucks = (await new MerchantSpendingService(db, periods).GetSpendingAsync(month, "starbucks")).Currencies.Single();

        Assert.Equal((food.Amount, food.TransactionCount),
            Spent(await service.CheckAsync(new(Ada, 1m, "TRY", CategoryCode: "FOOD"))));
        Assert.Equal((total.TotalAmount, total.TransactionCount), Spent(await service.CheckAsync(new(Ada, 1m, "TRY"))));
        Assert.Equal((starbucks.TotalAmount, starbucks.TransactionCount),
            Spent(await service.CheckAsync(new(Ada, 1m, "TRY", Merchant: "starbucks"))));

        static (decimal, int) Spent(BudgetCheck result) => (result.Figures!.SpentAmount, result.Figures.TransactionCount);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task An_unmatched_merchant_gets_no_figures_and_no_guess()
    {
        var result = await CheckAsync(new(Ege, 500m, "TRY", Merchant: "şehir"));

        Assert.Equal(MerchantMatchStatus.Ambiguous, result.MerchantMatch!.Status);
        Assert.Equal(["Şehir Ulaşım", "Şehirlerarası Otobüs"], result.MerchantMatch.Candidates);
        Assert.Null(result.Figures);
    }

    // Exact decimals from text: a double literal such as 1.005 would not survive the conversion unchanged.
    [Theory]
    [Trait("Category", "PostgreSQL")]
    [InlineData("0", "TRY", null, null, "Amount")]                  // below 0.01
    [InlineData("1.005", "TRY", null, null, "Amount")]              // three decimals
    [InlineData("1000", null, null, null, "CurrencyCode")]          // a budget needs a currency
    [InlineData("1000", "JPY", null, null, "CurrencyCode")]
    [InlineData("1000", "TRY", "FOOD", "Starbucks", "Merchant")]    // one target at most
    [InlineData("1000", "TRY", "YEMEK", null, "CategoryCode")]      // no such category
    [InlineData("1000", "TRY", null, "   ", "Merchant")]            // blank merchant text
    public async Task Invalid_budgets_name_their_field(string amount, string? currency, string? category, string? merchant, string field)
    {
        var error = await Assert.ThrowsAsync<ArgumentException>(() => CheckAsync(new(Ada,
            decimal.Parse(amount, CultureInfo.InvariantCulture), currency, category, merchant)));

        Assert.Equal(field, error.ParamName);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Scope_rules_match_the_other_spending_analyses()
    {
        var own = await CheckAsync(new(Ada, 1000m, "TRY", AccountId: AdaTryAccount));

        Assert.Equal(735m, own.Figures!.SpentAmount);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => CheckAsync(new(Ada, 1000m, "TRY", AccountId: DenizTryAccount)));
        // The shop's sales are not spending.
        await Assert.ThrowsAsync<ArgumentException>(() => CheckAsync(new(Deniz, 1000m, "TRY", AccountId: DenizShopAccount)));
    }
}
