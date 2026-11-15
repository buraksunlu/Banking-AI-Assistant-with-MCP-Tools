using BankingAI.Application.Analytics;
using BankingAI.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static BankingAI.Application.Tests.DemoData;

namespace BankingAI.Application.Tests;

// UX-01 PR3. Reads the seeded demo data at the demo moment (Saturday 10 October 2026). Expected values were computed by an
// independent SQL query in Istanbul time; temporary rows are inserted inside the test's own transaction and rolled back.
public sealed class CategorySpendingServiceTests
{
    private const short Grocery = 2;
    private const short Subscription = 6;

    private static PeriodResolver Periods() => new(new FixedTimeProvider(DemoNowUtc));

    private static BankingDbContext Db()
    {
        Assert.False(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BANKINGAI_CONNECTION_STRING")),
            "PostgreSQL testi için önce .env ortam değişkenlerini yükleyin.");
        return new BankingDbContext();
    }

    private static async Task<CategorySpendingDetail> GetAsync(Guid customer, PeriodKind period, string code)
    {
        await using var db = Db();
        return await new CategorySpendingService(db, Periods()).GetAsync(new SpendingQuery(customer, period), code);
    }

    private static Task InsertPurchaseAsync(BankingDbContext db, Guid accountId, string currency, decimal amount,
        string occurredAtUtc, string merchant, short categoryId) =>
        // Raw insert without a balance update: the row only lives until the rollback.
        db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Transactions" ("Id", "AccountId", "Amount", "Direction", "Kind", "CurrencyCode", "RawDescription",
                "NormalizedMerchant", "CategoryId", "CategorizationSource", "CategorizedAt", "OccurredAt", "CreatedAt")
            VALUES ({Guid.NewGuid()}, {accountId}, {amount}, 1, 1, {currency}, 'UX-01 PR3 temporary row',
                {merchant}, {categoryId}, 1, now(), {occurredAtUtc}::timestamptz, now())
            """);

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Murats_grocery_last_month_is_september_against_august()
    {
        var result = await GetAsync(Murat, PeriodKind.LastMonth, "GROCERY");

        Assert.Equal(("GROCERY", "Market"), (result.CategoryCode, result.DisplayName));
        Assert.Equal((new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)), (result.Period.StartDate, result.Period.EndDate));
        Assert.Equal((new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31)), (result.PreviousPeriod.StartDate, result.PreviousPeriod.EndDate));
        var c = Assert.Single(result.Currencies);
        // 18.016,50 / 8 = 2.252,0625; (18.016,50 − 11.944,50) / 11.944,50 = %50,835…; 18.016,50 / 53.388,39 = %33,746…
        Assert.Equal(("TRY", 18016.50m, 8, (decimal?)2252.06m), (c.CurrencyCode, c.Amount, c.TransactionCount, c.AveragePerTransaction));
        Assert.Equal((11944.50m, 5, 6072.00m, (decimal?)50.84m), (c.PreviousAmount, c.PreviousCount, c.Change, c.PercentageChange));
        Assert.Equal((53388.39m, (decimal?)33.75m), (c.TotalSpending, c.ShareOfSpending));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Murats_grocery_merchants_are_shares_of_the_category_largest_first()
    {
        var c = Assert.Single((await GetAsync(Murat, PeriodKind.LastMonth, "GROCERY")).Currencies);

        Assert.Equal(
            [("BİM", 6216.00m, 2, 34.50m), ("Migros Jet", 5317.50m, 3, 29.51m), ("Migros Sanal Market", 3577.50m, 2, 19.86m),
             ("A101", 2905.50m, 1, 16.13m)],
            c.Merchants.Select(m => (m.DisplayName, m.Amount, m.TransactionCount, m.Percentage)));
        Assert.Equal((3476.00m, "BİM", new DateOnly(2026, 9, 3)), (c.Largest!.Amount, c.Largest.Merchant, c.Largest.LocalDate));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Murats_series_is_six_complete_months_and_the_running_one_with_the_average_of_the_complete_ones()
    {
        var c = Assert.Single((await GetAsync(Murat, PeriodKind.LastMonth, "GROCERY")).Currencies);

        Assert.Equal(
            [(2026, 4, false, 8654.00m), (2026, 5, false, 10339.00m), (2026, 6, false, 12896.00m), (2026, 7, false, 16078.50m),
             (2026, 8, false, 11944.50m), (2026, 9, false, 18016.50m), (2026, 10, true, 5133.00m)],
            c.Months.Select(m => (m.Year, m.Month, m.IsPartial, m.Amount)));
        // 77.928,50 / 6 = 12.988,0833…; September is one of those months: 18.016,50 is %38,716… above it.
        Assert.Equal((12988.08m, (decimal?)38.72m), (c.MonthlyAverage, c.PercentageVsAverage));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task This_month_meets_the_same_days_of_last_month_and_has_no_comparison_with_the_average()
    {
        var result = await GetAsync(Murat, PeriodKind.ThisMonth, "GROCERY");

        Assert.Equal((new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 10)), (result.PreviousPeriod.StartDate, result.PreviousPeriod.EndDate));
        var c = Assert.Single(result.Currencies);
        // 1–10 Sep: 3.476,00 + 1.640,50 + 2.060,00 = 7.176,50; −2.043,50 / 7.176,50 = −%28,475…; 5.133 / 8.241,40 = %62,283…
        Assert.Equal((5133.00m, 2, (decimal?)2566.50m), (c.Amount, c.TransactionCount, c.AveragePerTransaction));
        Assert.Equal((7176.50m, 3, -2043.50m, (decimal?)-28.47m), (c.PreviousAmount, c.PreviousCount, c.Change, c.PercentageChange));
        Assert.Equal((8241.40m, (decimal?)62.28m), (c.TotalSpending, c.ShareOfSpending));
        Assert.Equal((3290.50m, "Migros Jet", new DateOnly(2026, 10, 6)), (c.Largest!.Amount, c.Largest.Merchant, c.Largest.LocalDate));
        // A part of a month against an average of whole months would always look low.
        Assert.Null(c.PercentageVsAverage);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Unclassified_spending_is_not_in_the_category_but_is_in_the_total_it_is_a_share_of()
    {
        var c = Assert.Single((await GetAsync(Ada, PeriodKind.ThisMonth, "FOOD")).Currencies);

        // 120 + 210 + 95 + 165 = 590 of 735 (with the unclassified 145): %80,272…; nothing on 1–10 September.
        Assert.Equal((590m, 4, 735m, (decimal?)80.27m), (c.Amount, c.TransactionCount, c.TotalSpending, c.ShareOfSpending));
        Assert.Equal((0m, 0, 590m, (decimal?)null), (c.PreviousAmount, c.PreviousCount, c.Change, c.PercentageChange));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task A_category_without_spending_in_the_period_still_shows_its_real_zero_and_its_months()
    {
        // Ada's subscriptions are paid on the 12th and 18th: none yet in October, none on 1–10 September.
        var c = Assert.Single((await GetAsync(Ada, PeriodKind.ThisMonth, "SUBSCRIPTION")).Currencies);

        Assert.Equal((0m, 0, (decimal?)null, (decimal?)0.00m), (c.Amount, c.TransactionCount, c.AveragePerTransaction, c.ShareOfSpending));
        Assert.Empty(c.Merchants);
        Assert.Null(c.Largest);
        Assert.Equal((0m, (decimal?)null), (c.Change, c.PercentageChange));
        Assert.Equal(329.98m, c.MonthlyAverage);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task A_category_never_used_has_no_currency_row()
    {
        var result = await GetAsync(Ada, PeriodKind.LastMonth, "GROCERY");

        Assert.Empty(result.Currencies);
        Assert.Equal(7, result.Months.Count);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Every_currency_is_its_own_row_and_never_added_to_another()
    {
        await using var db = Db();
        await using var scope = await db.Database.BeginTransactionAsync();
        await InsertPurchaseAsync(db, AdaUsdAccount, "USD", 9.99m, "2026-09-05T10:00:00Z", "Cloud Storage", Subscription);

        var result = await new CategorySpendingService(db, Periods()).GetAsync(new SpendingQuery(Ada, PeriodKind.LastMonth), "SUBSCRIPTION");

        // TRY: Netflix + Spotify 329,98 of September's 624,98 (with 110 + 185 of food): %52,798…; the USD row is its own.
        Assert.Equal(
            [("TRY", 329.98m, 624.98m, (decimal?)52.80m), ("USD", 9.99m, 9.99m, (decimal?)100.00m)],
            result.Currencies.Select(c => (c.CurrencyCode, c.Amount, c.TotalSpending, c.ShareOfSpending)));
        await scope.RollbackAsync();
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Of_two_equal_largest_purchases_the_newest_is_shown_as_in_the_highest_transactions()
    {
        await using var db = Db();
        await using var scope = await db.Database.BeginTransactionAsync();
        await InsertPurchaseAsync(db, AdaTryAccount, "TRY", 50m, "2026-09-05T10:00:00Z", "Bakkal", Grocery);
        await InsertPurchaseAsync(db, AdaTryAccount, "TRY", 50m, "2026-09-07T10:00:00Z", "Manav", Grocery);

        var result = await new CategorySpendingService(db, Periods()).GetAsync(new SpendingQuery(Ada, PeriodKind.LastMonth), "GROCERY");

        var c = Assert.Single(result.Currencies);
        Assert.Equal(("Manav", new DateOnly(2026, 9, 7)), (c.Largest!.Merchant, c.Largest.LocalDate));
        await scope.RollbackAsync();
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task An_unknown_category_code_is_an_error_not_an_empty_result()
    {
        var error = await Assert.ThrowsAsync<ArgumentException>(() => GetAsync(Murat, PeriodKind.LastMonth, "MARKET"));

        Assert.Equal("CategoryCode", error.ParamName);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Another_customers_account_is_not_found()
    {
        await using var db = Db();
        var service = new CategorySpendingService(db, Periods());

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.GetAsync(new SpendingQuery(Murat, PeriodKind.LastMonth, AccountId: AdaTryAccount), "GROCERY"));
    }
}
