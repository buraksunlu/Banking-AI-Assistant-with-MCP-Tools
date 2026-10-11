using BankingAI.Application.Analytics;
using BankingAI.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static BankingAI.Application.Tests.DemoData;

namespace BankingAI.Application.Tests;

// ANA-08: edge cases across every analytics service. Temporary rows are inserted inside the test's
// own DB transaction and rolled back, so the seeded demo data (116 transactions) never changes.
public sealed class AnalyticsEdgeCaseTests
{
    private static PeriodResolver Periods() => new(new FixedTimeProvider(DemoNowUtc));

    private static BankingDbContext Db()
    {
        Assert.False(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BANKINGAI_CONNECTION_STRING")),
            "PostgreSQL testi için önce .env ortam değişkenlerini yükleyin.");
        return new BankingDbContext();
    }

    private static Task InsertPurchaseAsync(BankingDbContext db, Guid accountId, string currency, decimal amount,
        string occurredAtUtc, string? merchant = null, short? categoryId = null) =>
        // Raw insert without a balance update: the row only lives until the rollback.
        db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Transactions" ("Id", "AccountId", "Amount", "Direction", "Kind", "CurrencyCode", "RawDescription",
                "NormalizedMerchant", "CategoryId", "CategorizationSource", "CategorizedAt", "OccurredAt", "CreatedAt")
            VALUES ({Guid.NewGuid()}, {accountId}, {amount}, 1, 1, {currency}, 'ANA-08 temporary row',
                {merchant}, {categoryId}, {(categoryId is null ? null : (short?)1)}, {(categoryId is null ? null : "now")}::timestamptz,
                {occurredAtUtc}::timestamptz, now())
            """);

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task A_customer_without_accounts_gets_empty_results_everywhere()
    {
        await using var db = Db();
        var spending = new SpendingQuery(EmptyCustomer, PeriodKind.Last6Months);

        Assert.Empty((await new SpendingSummaryService(db, Periods()).GetAsync(spending)).Currencies);
        Assert.Empty((await new CategoryBreakdownService(db, Periods()).GetAsync(spending)).Currencies);

        var merchants = new MerchantSpendingService(db, Periods());
        Assert.Empty((await merchants.GetBreakdownAsync(spending)).Currencies);
        var starbucks = await merchants.GetSpendingAsync(spending, "Starbucks");
        Assert.Equal(MerchantMatchStatus.NotFound, starbucks.Match.Status);
        Assert.Empty(starbucks.Currencies);

        var search = await new TransactionSearchService(db, Periods()).SearchAsync(new(EmptyCustomer, PeriodKind.Last6Months));
        Assert.Equal((0, false), (search.TotalCount, search.HasMore));
        Assert.Empty(search.Items);

        var highest = new HighestTransactionsService(db, Periods());
        Assert.Empty((await highest.GetAsync(new(EmptyCustomer, PeriodKind.Last6Months))).Currencies);
        Assert.Empty((await highest.GetAsync(new(EmptyCustomer, PeriodKind.Last6Months, IncludeAllTransactions: true))).Currencies);

        var comparisons = new SpendingComparisonService(db, Periods());
        Assert.Empty((await comparisons.CompareAsync(new(EmptyCustomer))).Currencies);
        var trend = await comparisons.GetMonthlyTrendAsync(new(EmptyCustomer));
        Assert.Equal(7, trend.Months.Count); // the months exist; there is simply no currency to report
        Assert.Empty(trend.Currencies);

        var recurring = await new RecurringPaymentService(db, Periods()).GetAsync(new(EmptyCustomer));
        Assert.Empty(recurring.Payments);
        Assert.Empty(recurring.MonthlyTotals);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Istanbul_midnight_decides_the_month_in_every_code_path()
    {
        await using var db = Db();
        await using var scope = await db.Database.BeginTransactionAsync();
        // Two Istanbul (UTC+3) midnights, all rows unclassified on purpose:
        // 31 Aug 23:59:59 / 1 Sep 00:00 is where the compared months (Sep vs Aug) touch;
        // 30 Sep 23:59:59 / 1 Oct 00:00 is where "last month" ends and "this month" starts.
        await InsertPurchaseAsync(db, AdaTryAccount, "TRY", 4.44m, "2026-08-31T20:59:59Z");
        await InsertPurchaseAsync(db, AdaTryAccount, "TRY", 3.33m, "2026-08-31T21:00:00Z");
        await InsertPurchaseAsync(db, AdaTryAccount, "TRY", 1.11m, "2026-09-30T20:59:59Z");
        await InsertPurchaseAsync(db, AdaTryAccount, "TRY", 2.22m, "2026-09-30T21:00:00Z");

        // 1) Summary: the shared scope's [StartUtc, EndUtc) filter.
        var summaries = new SpendingSummaryService(db, Periods());
        var october = Assert.Single((await summaries.GetAsync(new(Ada, PeriodKind.ThisMonth))).Currencies);
        var september = Assert.Single((await summaries.GetAsync(new(Ada, PeriodKind.LastMonth))).Currencies);
        Assert.Equal((737.22m, 147.22m), (october.TotalAmount, october.UncategorizedAmount));   // 735 + 2,22; 145 + 2,22
        Assert.Equal((629.42m, 4.44m), (september.TotalAmount, september.UncategorizedAmount)); // 624,98 + 1,11 + 3,33

        // 2) Comparison: the CASE WHEN split inside one query; the unclassified group is reported.
        var comparisons = new SpendingComparisonService(db, Periods());
        var previousFullMonth = Assert.Single((await comparisons.CompareAsync(new(Ada))).Currencies);
        // September 629,42 vs August 514,98 + 4,44 = 519,42: the 3,33 row belongs to September only.
        Assert.Equal((629.42m, 519.42m, 110m, (decimal?)21.18m),
            (previousFullMonth.CurrentAmount, previousFullMonth.PreviousAmount, previousFullMonth.Change, previousFullMonth.PercentageChange));
        Assert.Contains(previousFullMonth.Categories, c => c is { CategoryCode: null, CurrentAmount: 4.44m, PreviousAmount: 4.44m });
        var monthToDate = Assert.Single((await comparisons.CompareAsync(new(Ada, ComparisonKind.MonthToDate))).Currencies);
        Assert.Equal(737.22m, monthToDate.CurrentAmount);
        Assert.Contains(monthToDate.Categories, c => c is { CategoryCode: null, CurrentAmount: 147.22m, DisplayName: "Sınıflandırılmamış" });

        // 3) Monthly trend: rows bucketed in C# by the windows' UTC bounds.
        var trend = Assert.Single((await comparisons.GetMonthlyTrendAsync(new(Ada))).Currencies);
        Assert.Equal(new[] { 514.98m, 514.98m, 514.98m, 514.98m, 519.42m, 629.42m, 737.22m }, trend.Months.Select(m => m.Amount));

        // 4) Search: the row at 21:00 UTC is listed in October on local date 1 October; the other is not.
        var search = await new TransactionSearchService(db, Periods()).SearchAsync(new(Ada, PeriodKind.ThisMonth, Limit: 50));
        Assert.Equal(9, search.TotalCount);
        Assert.Contains(search.Items, i => i is { Amount: 2.22m } && i.LocalDate == new DateOnly(2026, 10, 1));
        Assert.DoesNotContain(search.Items, i => i.Amount == 1.11m);

        await scope.RollbackAsync();
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task A_second_currency_is_reported_separately_everywhere()
    {
        await using var db = Db();
        await using var scope = await db.Database.BeginTransactionAsync();
        // A monthly 9,99 USD subscription (5 Aug, 5 Sep, 5 Oct) and one unclassified 25 USD purchase.
        foreach (var day in new[] { "2026-08-05T10:00:00Z", "2026-09-05T10:00:00Z", "2026-10-05T10:00:00Z" })
            await InsertPurchaseAsync(db, AdaUsdAccount, "USD", 9.99m, day, "Cloud Storage", categoryId: 6);
        await InsertPurchaseAsync(db, AdaUsdAccount, "USD", 25m, "2026-10-06T10:00:00Z", "Duty Free");
        var thisMonth = new SpendingQuery(Ada, PeriodKind.ThisMonth);

        // Category breakdown: TRY untouched, USD in its own list.
        var categories = (await new CategoryBreakdownService(db, Periods()).GetAsync(thisMonth)).Currencies;
        Assert.Equal(new[] { ("TRY", 735m), ("USD", 34.99m) }, categories.Select(c => (c.CurrencyCode, c.TotalAmount)));
        Assert.Equal(new (string?, decimal, decimal)[] { (null, 25m, 71.45m), ("SUBSCRIPTION", 9.99m, 28.55m) },
            categories[1].Categories.Select(c => (c.CategoryCode, c.Amount, c.Percentage)).ToArray());

        // Merchant breakdown.
        var merchants = (await new MerchantSpendingService(db, Periods()).GetBreakdownAsync(thisMonth)).Currencies;
        Assert.Equal(new (string?, decimal)[] { ("Duty Free", 25m), ("Cloud Storage", 9.99m) },
            merchants.Single(c => c.CurrencyCode == "USD").Merchants.Select(m => (m.Merchant, m.Amount)).ToArray());
        Assert.Equal(735m, merchants.Single(c => c.CurrencyCode == "TRY").TotalAmount);

        // Comparison (September vs August): each currency compared with itself.
        var comparisons = new SpendingComparisonService(db, Periods());
        var compared = (await comparisons.CompareAsync(new(Ada))).Currencies;
        Assert.Equal(new[] { ("TRY", 624.98m, 514.98m), ("USD", 9.99m, 9.99m) },
            compared.Select(c => (c.CurrencyCode, c.CurrentAmount, c.PreviousAmount)));
        Assert.Equal((0m, (decimal?)0m), (compared[1].Change, compared[1].PercentageChange));

        // Monthly trend: one series per currency.
        var trend = (await comparisons.GetMonthlyTrendAsync(new(Ada))).Currencies;
        Assert.Equal(new[] { "TRY", "USD" }, trend.Select(t => t.CurrencyCode));
        Assert.Equal(new[] { 0m, 0m, 0m, 0m, 9.99m, 9.99m, 34.99m }, trend[1].Months.Select(m => m.Amount));
        Assert.Equal(735m, trend[0].Months[^1].Amount);

        // Recurring: the USD subscription is found on its own and totalled separately.
        var recurring = await new RecurringPaymentService(db, Periods()).GetAsync(new(Ada));
        Assert.Equal(new[] { ("Netflix", "TRY"), ("Spotify", "TRY"), ("Cloud Storage", "USD") },
            recurring.Payments.Select(p => (p.Merchant, p.CurrencyCode)));
        Assert.Equal(new[] { new CurrencyRecurringTotal("TRY", 329.98m), new CurrencyRecurringTotal("USD", 9.99m) },
            recurring.MonthlyTotals);

        await scope.RollbackAsync();
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Temporary_rows_never_reach_the_demo_data()
    {
        await using var db = Db();

        Assert.Equal(116, await db.Transactions.CountAsync());
        Assert.False(await db.Transactions.AnyAsync(t => t.RawDescription == "ANA-08 temporary row"));
    }
}
