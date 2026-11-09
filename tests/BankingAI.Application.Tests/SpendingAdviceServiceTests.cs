using BankingAI.Application.Analytics;
using BankingAI.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static BankingAI.Application.Tests.DemoData;

namespace BankingAI.Application.Tests;

// INC-02 on the seeded demo data at the demo moment (10 October 2026): the month is September 2026, the usual months are
// March–August 2026 and the savings window is April–September 2026. Expected values come from an independent SQL query in each
// customer's time zone.
public sealed class SpendingAdviceServiceTests
{
    private static async Task<SpendingAdvice> GetAsync(SpendingAdviceQuery query)
    {
        Assert.False(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BANKINGAI_CONNECTION_STRING")),
            "PostgreSQL testi için önce .env ortam değişkenlerini yükleyin.");
        await using var db = new BankingDbContext();
        return await Create(db).GetAsync(query);
    }

    private static SpendingAdviceService Create(BankingDbContext db)
    {
        var periods = new PeriodResolver(new FixedTimeProvider(DemoNowUtc));
        return new SpendingAdviceService(db, periods, new IncomeShareService(db, periods, new CategoryBreakdownService(db, periods)));
    }

    private static Task<SpendingAdvice> LastMonthAsync(Guid customer) => GetAsync(new(customer, PeriodKind.LastMonth));

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task The_month_the_usual_months_and_the_savings_window_are_reported()
    {
        var result = await LastMonthAsync(Selin);

        Assert.Equal((new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)), (result.Period.StartDate, result.Period.EndDate));
        Assert.Equal((new DateOnly(2026, 7, 1), new DateOnly(2026, 9, 30)), (result.IncomeFrom, result.IncomeTo));
        Assert.Equal((new DateOnly(2026, 3, 1), new DateOnly(2026, 8, 31)), (result.UsualFrom, result.UsualTo));
        Assert.Equal((new DateOnly(2026, 4, 1), new DateOnly(2026, 9, 30)), (result.SavingsFrom, result.SavingsTo));
        Assert.Equal(new AdviceThresholds(30, 1.5m, 2, 6, 20, 2), result.Thresholds);
        Assert.Equal("RuleBasedAdvice", result.Method);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Selin_gets_one_piece_of_advice_for_entertainment()
    {
        var c = Assert.Single((await LastMonthAsync(Selin)).Currencies);

        // Essential FOOD 3.746,50 + BILLS 350 + TRANSPORTATION 338,50; discretionary ENTERTAINMENT 2.253,50 + SUBSCRIPTION 59,99;
        // unknown OTHER 480. Base 8.000: 2.313,49 = %28,918… → 28,92, below the 30 % limit.
        Assert.Equal(
            [(SpendingGroup.Essential, 4435.00m, (decimal?)55.44m), (SpendingGroup.Discretionary, 2313.49m, 28.92m),
             (SpendingGroup.Unknown, 480m, 6.00m)],
            c.Groups.Select(g => (g.Group, g.Amount, g.IncomeShare)));
        // Usual months: 5.567 / 6 = 927,83; 2.253,50 is 2,43 times; 1.325,67 above = %16,57 of income. Savings window
        // 7.085,50 × 20 % / 6 = 236,18 a month, 2.834,20 a year.
        var advice = Assert.Single(c.Advice);
        Assert.Equal(
            (AdviceRule.CategoryAboveUsual, "ENTERTAINMENT", "Eğlence", 2253.50m, 927.83m, (decimal?)2.43m, 1325.67m, 16.57m, 20, 236.18m, 2834.20m),
            (advice.Rule, advice.CategoryCode, advice.CategoryDisplayName, advice.Amount, advice.Reference, advice.Ratio,
             advice.Excess, advice.ExcessShare, advice.ReductionPercent, advice.MonthlySaving, advice.AnnualSaving));
        Assert.Null(c.Reason);
    }

    [Theory]
    [Trait("Category", "PostgreSQL")]
    [InlineData("Murat")]
    [InlineData("Zeynep")]
    [InlineData("Hasan")]
    [InlineData("Elif")]
    [InlineData("Ada")]
    [InlineData("Deniz")]
    public async Task Customers_whose_spending_is_usual_get_no_advice(string name)
    {
        var customer = name switch
        {
            "Murat" => Murat, "Zeynep" => Zeynep, "Hasan" => Hasan, "Elif" => Elif, "Ada" => Ada, _ => Deniz,
        };

        var result = await LastMonthAsync(customer);

        Assert.NotEmpty(result.Currencies);
        Assert.All(result.Currencies, c => Assert.Empty(c.Advice));
        Assert.All(result.Currencies, c => Assert.Equal(NoAdviceReason.NoRuleMatched, c.Reason));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Murats_school_instalment_is_essential_and_not_advised()
    {
        var c = Assert.Single((await LastMonthAsync(Murat)).Currencies);

        // BILLS 27.439,90 against a usual month near 2.800 is ten times higher, but bills are essential (K8-A).
        var bills = Assert.Single(c.Categories, k => k.CategoryCode == "BILLS");
        Assert.Equal((SpendingGroup.Essential, 27439.90m), (bills.Group, bills.Amount));
        Assert.Equal(
            [(SpendingGroup.Essential, 51265.90m), (SpendingGroup.Discretionary, 872.49m), (SpendingGroup.Unknown, 1250m)],
            c.Groups.Select(g => (g.Group, g.Amount)));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task A_customer_without_salary_gets_no_advice_and_says_why()
    {
        var c = Assert.Single((await LastMonthAsync(Can)).Currencies);

        Assert.Equal(((decimal?)null, (decimal?)null), (c.IncomeBase, c.SpentShare));
        Assert.Empty(c.Advice);
        Assert.Equal(NoAdviceReason.NoIncome, c.Reason);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Figures_equal_the_income_share_and_the_savings_simulation()
    {
        await using var db = new BankingDbContext();
        var periods = new PeriodResolver(new FixedTimeProvider(DemoNowUtc));
        var share = await new IncomeShareService(db, periods, new CategoryBreakdownService(db, periods))
            .GetAsync(new IncomeShareQuery(Selin, PeriodKind.LastMonth));
        var simulation = await new SavingsSimulationService(db, new SpendingComparisonService(db, periods))
            .SimulateAsync(new SavingsSimulationQuery(Selin, 20, CategoryCode: "ENTERTAINMENT"));

        var c = Assert.Single((await Create(db).GetAsync(new SpendingAdviceQuery(Selin, PeriodKind.LastMonth))).Currencies);

        var expected = share.Currencies.Single();
        Assert.Equal((expected.TotalSpent, expected.IncomeTotal, expected.IncomeBase, expected.SpentShare),
            (c.TotalSpent, c.IncomeTotal, c.IncomeBase, c.SpentShare));
        Assert.Equal(expected.Categories.Select(k => (k.CategoryCode, k.Amount, k.IncomeShare)),
            c.Categories.Select(k => (k.CategoryCode, k.Amount, k.IncomeShare)));
        var saving = simulation.Currencies.Single();
        Assert.Equal((saving.MonthlySaving, saving.AnnualSaving), (c.Advice[0].MonthlySaving, c.Advice[0].AnnualSaving));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Every_category_in_the_database_is_in_the_group_table()
    {
        await using var db = new BankingDbContext();

        var codes = await db.TransactionCategories.Select(c => c.Code).ToListAsync();

        Assert.NotEmpty(codes);
        Assert.All(codes, code => Assert.True(SpendingGroups.Table.ContainsKey(code), $"{code} gruplama tablosunda yok."));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task A_custom_month_moves_the_usual_and_savings_windows_with_it()
    {
        var result = await GetAsync(new(Selin, PeriodKind.Custom, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31)));

        Assert.Equal((new DateOnly(2026, 2, 1), new DateOnly(2026, 7, 31)), (result.UsualFrom, result.UsualTo));
        Assert.Equal((new DateOnly(2026, 3, 1), new DateOnly(2026, 8, 31)), (result.SavingsFrom, result.SavingsTo));
    }

    [Theory]
    [Trait("Category", "PostgreSQL")]
    [InlineData(PeriodKind.ThisMonth, null, null)]
    [InlineData(PeriodKind.Last3Months, null, null)]
    [InlineData(PeriodKind.Custom, "2026-08-01", "2026-08-30")]   // not a whole month
    [InlineData(PeriodKind.Custom, "2026-10-01", "2026-10-31")]   // the running month
    public async Task Only_a_complete_month_is_accepted_as_in_the_income_share(PeriodKind period, string? from, string? to) =>
        await Assert.ThrowsAsync<ArgumentException>(() => GetAsync(new(Selin, period,
            from is null ? null : DateOnly.Parse(from), to is null ? null : DateOnly.Parse(to))));

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task An_unknown_customer_is_refused() =>
        await Assert.ThrowsAsync<CustomerNotFoundException>(() => LastMonthAsync(Guid.NewGuid()));

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task A_customer_without_accounts_has_no_currencies() =>
        Assert.Empty((await LastMonthAsync(EmptyCustomer)).Currencies);
}
