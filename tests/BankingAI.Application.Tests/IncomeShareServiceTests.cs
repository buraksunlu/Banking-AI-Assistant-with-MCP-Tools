using BankingAI.Application.Analytics;
using BankingAI.Data;
using Xunit;
using static BankingAI.Application.Tests.DemoData;

namespace BankingAI.Application.Tests;

// INC-01. Reads the seeded demo data at the demo moment (10 October 2026): "last month" is September 2026 and the income
// window is July–September 2026. Expected values were computed by an independent SQL query in each customer's time zone.
public sealed class IncomeShareServiceTests
{
    private static async Task<IncomeShare> GetAsync(IncomeShareQuery query)
    {
        Assert.False(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BANKINGAI_CONNECTION_STRING")),
            "PostgreSQL testi için önce .env ortam değişkenlerini yükleyin.");
        await using var db = new BankingDbContext();
        var periods = new PeriodResolver(new FixedTimeProvider(DemoNowUtc));
        return await new IncomeShareService(db, periods, new CategoryBreakdownService(db, periods)).GetAsync(query);
    }

    private static Task<IncomeShare> LastMonthAsync(Guid customer) => GetAsync(new(customer, PeriodKind.LastMonth));

    // Currency, spent, income total, salary count, base, share.
    private static (string, decimal, decimal, int, decimal?, decimal?) Figures(CurrencyIncomeShare c) =>
        (c.CurrencyCode, c.TotalSpent, c.IncomeTotal, c.IncomeCount, c.IncomeBase, c.SpentShare);

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Last_month_is_september_and_the_income_window_is_the_three_months_ending_with_it()
    {
        var result = await LastMonthAsync(Selin);

        Assert.Equal((new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)), (result.Period.StartDate, result.Period.EndDate));
        Assert.Equal((new DateOnly(2026, 7, 1), new DateOnly(2026, 9, 30), 3), (result.IncomeFrom, result.IncomeTo, result.IncomeMonths));
        Assert.Equal("ThreeFullMonthSalaryAverage", result.Method);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Selins_spending_and_every_category_are_shares_of_her_income()
    {
        var c = Assert.Single((await LastMonthAsync(Selin)).Currencies);

        // 3 × 8.000 = 24.000 → base 8.000; 7.228,49 / 8.000 = %90,356… → 90,36.
        Assert.Equal(("TRY", 7228.49m, 24000m, 3, (decimal?)8000m, (decimal?)90.36m), Figures(c));
        Assert.Equal(
            [("FOOD", 3746.50m, 46.83m), ("ENTERTAINMENT", 2253.50m, 28.17m), ("OTHER", 480m, 6.00m),
             ("BILLS", 350m, 4.38m), ("TRANSPORTATION", 338.50m, 4.23m), ("SUBSCRIPTION", 59.99m, 0.75m)],
            c.Categories.Select(k => (k.CategoryCode!, k.Amount, k.IncomeShare!.Value)));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Category_amounts_equal_the_category_breakdown()
    {
        await using var db = new BankingDbContext();
        var periods = new PeriodResolver(new FixedTimeProvider(DemoNowUtc));
        var breakdown = await new CategoryBreakdownService(db, periods).GetAsync(new SpendingQuery(Murat, PeriodKind.LastMonth));
        var share = await LastMonthAsync(Murat);

        Assert.Equal(
            breakdown.Currencies.Single().Categories.Select(k => (k.CategoryCode, k.Amount, k.TransactionCount)),
            share.Currencies.Single().Categories.Select(k => (k.CategoryCode, k.Amount, k.TransactionCount)));
        // 53.388,39 / 85.000 = %62,809… → 62,81.
        Assert.Equal(("TRY", 53388.39m, 255000m, 3, (decimal?)85000m, (decimal?)62.81m), Figures(share.Currencies.Single()));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task A_month_without_salary_counts_as_zero_and_currencies_stay_apart()
    {
        var result = await LastMonthAsync(Zeynep);

        // Two salaries in three months: 73.000 / 3 = 24.333,33; the share comes from the exact total:
        // 10.490 × 3 / 73.000 = %43,109… → 43,11. USD: 4.050 / 3 = 1.350; 37,99 / 1.350 = %2,814… → 2,81.
        Assert.Equal(
            [("TRY", 10490m, 73000m, 2, (decimal?)24333.33m, (decimal?)43.11m),
             ("USD", 37.99m, 4050m, 2, (decimal?)1350m, (decimal?)2.81m)],
            result.Currencies.Select(Figures));
    }

    [Theory]
    [Trait("Category", "PostgreSQL")]
    [InlineData("Deniz", "TRY", "31000", "165000", 3, "55000", "56.36")]
    [InlineData("Elif", "EUR", "1766.26", "12600", 3, "4200", "42.05")]   // Europe/Berlin months
    [InlineData("Hasan", "TRY", "5755.50", "66000", 3, "22000", "26.16")]
    [InlineData("Ada", "TRY", "624.98", "135000", 3, "45000", "1.39")]
    public async Task Other_customers_match_the_independent_figures(
        string name, string currency, string spent, string income, int count, string incomeBase, string share)
    {
        var customer = name switch { "Deniz" => Deniz, "Elif" => Elif, "Hasan" => Hasan, _ => Ada };

        var c = Assert.Single((await LastMonthAsync(customer)).Currencies);

        Assert.Equal((currency, M(spent), M(income), count, (decimal?)M(incomeBase), (decimal?)M(share)), Figures(c));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Without_salary_the_amounts_stay_and_every_share_is_null()
    {
        // Can only sells in his business account; his personal spending has no personal income to compare with.
        var c = Assert.Single((await LastMonthAsync(Can)).Currencies);

        Assert.Equal(("TRY", 15037.99m, 0m, 0, (decimal?)null, (decimal?)null), Figures(c));
        Assert.NotEmpty(c.Categories);
        Assert.All(c.Categories, k => Assert.Null(k.IncomeShare));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task A_customer_without_accounts_gets_an_empty_list() =>
        Assert.Empty((await LastMonthAsync(EmptyCustomer)).Currencies);

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task A_custom_complete_month_is_the_same_as_last_month_and_another_month_moves_the_window()
    {
        var custom = await GetAsync(new(Selin, PeriodKind.Custom, new(2026, 9, 1), new(2026, 9, 30)));
        var last = await LastMonthAsync(Selin);
        Assert.Equal(last.Currencies.Select(Figures), custom.Currencies.Select(Figures));

        var august = await GetAsync(new(Selin, PeriodKind.Custom, new(2026, 8, 1), new(2026, 8, 31)));
        Assert.Equal((new DateOnly(2026, 6, 1), new DateOnly(2026, 8, 31)), (august.IncomeFrom, august.IncomeTo));
        Assert.Equal((new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31)), (august.Period.StartDate, august.Period.EndDate));
    }

    [Theory]
    [Trait("Category", "PostgreSQL")]
    [InlineData(PeriodKind.ThisMonth)]
    [InlineData(PeriodKind.Last3Months)]
    [InlineData(PeriodKind.Last30Days)]
    [InlineData(PeriodKind.Today)]
    [InlineData(PeriodKind.AllTime)]
    [InlineData(PeriodKind.Yesterday)]   // wholly in the past, but not a month
    [InlineData(PeriodKind.LastWeek)]
    public async Task Only_last_month_or_a_custom_month_is_accepted(PeriodKind period) =>
        await Assert.ThrowsAsync<ArgumentException>(() => GetAsync(new(Selin, period)));

    [Theory]
    [Trait("Category", "PostgreSQL")]
    [InlineData("2026-09-02", "2026-09-30")]   // does not start on the 1st
    [InlineData("2026-09-01", "2026-09-29")]   // does not end on the last day
    [InlineData("2026-08-01", "2026-09-30")]   // two months
    [InlineData("2026-10-01", "2026-10-31")]   // the running month
    public async Task A_custom_range_must_be_one_complete_month(string from, string to) =>
        await Assert.ThrowsAsync<ArgumentException>(() =>
            GetAsync(new(Selin, PeriodKind.Custom, DateOnly.Parse(from), DateOnly.Parse(to))));

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task On_the_last_day_of_a_month_that_month_is_still_running()
    {
        // 30 September 2026, 15:00 Istanbul: September is a whole calendar month but not complete yet.
        await using var db = new BankingDbContext();
        var periods = new PeriodResolver(new FixedTimeProvider(new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero)));
        var service = new IncomeShareService(db, periods, new CategoryBreakdownService(db, periods));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.GetAsync(new(Selin, PeriodKind.Custom, new(2026, 9, 1), new(2026, 9, 30))));
    }

    [Fact]
    public void The_share_uses_the_exact_income_total_not_the_rounded_base()
    {
        // Income 1,00 over three months: the base rounds to 0,33, but 1,00 spent is exactly 300% of 1/3, not 303,03%.
        Assert.Equal(300.00m, IncomeShareService.Share(1m, 1m));
        Assert.Equal(90.36m, IncomeShareService.Share(7228.49m, 24000m));
        Assert.Null(IncomeShareService.Share(100m, 0m));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task A_custom_period_needs_both_dates() =>
        await Assert.ThrowsAsync<ArgumentException>(() => GetAsync(new(Selin, PeriodKind.Custom, new(2026, 9, 1))));

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task An_unknown_customer_is_a_stale_session() =>
        await Assert.ThrowsAsync<CustomerNotFoundException>(() => LastMonthAsync(Guid.NewGuid()));

    private static decimal M(string text) => decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
}
