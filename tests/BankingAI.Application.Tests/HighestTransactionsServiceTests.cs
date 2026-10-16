using System.Globalization;
using BankingAI.Application.Analytics;
using BankingAI.Data;
using BankingAI.Data.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static BankingAI.Application.Tests.DemoData;

namespace BankingAI.Application.Tests;

// Reads the seeded demo data. Expected rows are hand-computed acceptance fixtures.
public sealed class HighestTransactionsServiceTests
{
    private static HighestTransactionsService Service(BankingDbContext db) =>
        new(db, new PeriodResolver(new FixedTimeProvider(DemoNowUtc)));

    private static async Task<HighestTransactions> GetAsync(HighestTransactionsQuery query)
    {
        Assert.False(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BANKINGAI_CONNECTION_STRING")),
            "PostgreSQL testi için önce .env ortam değişkenlerini yükleyin.");
        await using var db = new BankingDbContext();
        return await Service(db).GetAsync(query);
    }

    private static (DateOnly, decimal, string?)[] Rows(CurrencyHighestTransactions currency) =>
        currency.Items.Select(i => (i.LocalDate, i.Amount, i.Merchant)).ToArray();

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Default_lists_the_five_largest_purchases_only()
    {
        var result = await GetAsync(new(Ada, PeriodKind.ThisMonth));

        // The 1.500 sent transfer and the 45.000 salary are not spending.
        var tl = Assert.Single(result.Currencies);
        Assert.Equal(new (DateOnly, decimal, string?)[]
        {
            (new(2026, 10, 6), 210m, "Starbucks"), (new(2026, 10, 8), 165m, "Starbucks"), (new(2026, 10, 9), 145m, null),
            (new(2026, 10, 2), 120m, "Kahve Durağı"), (new(2026, 10, 7), 95m, "Kahve Durağı")
        }, Rows(tl));
        Assert.All(tl.Items, i => Assert.Equal(TransactionKind.Purchase, i.Kind));
        Assert.Equal("Kahve Durağı – Kadıköy", tl.Items[3].Counterparty);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Equal_amounts_list_the_newest_first()
    {
        var result = await GetAsync(new(Ada, PeriodKind.Last3Months));

        Assert.Equal(new (DateOnly, decimal, string?)[]
        {
            (new(2026, 9, 12), 229.99m, "Netflix"), (new(2026, 8, 12), 229.99m, "Netflix"), (new(2026, 7, 12), 229.99m, "Netflix"),
            (new(2026, 10, 6), 210m, "Starbucks"), (new(2026, 9, 20), 185m, "Starbucks")
        }, Rows(Assert.Single(result.Currencies)));
    }

    [Theory]
    [Trait("Category", "PostgreSQL")]
    [InlineData("Deniz", 3, new[] { 31000, 24000, 18000 })]
    [InlineData("Ege", 3, new[] { 1200, 700, 700 })]
    public async Task Requested_count_is_returned(string customer, int limit, int[] expected)
    {
        var id = customer == "Deniz" ? Deniz : Ege;
        var result = await GetAsync(new(id, PeriodKind.Last6Months, Limit: limit));

        Assert.Equal(expected.Select(e => (decimal)e), Assert.Single(result.Currencies).Items.Select(i => i.Amount));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Ties_in_ege_transport_are_ordered_newest_first()
    {
        var result = await GetAsync(new(Ege, PeriodKind.Last6Months, Limit: 3));

        Assert.Equal(new[] { new DateOnly(2026, 8, 14), new DateOnly(2026, 10, 3), new DateOnly(2026, 9, 25) },
            Assert.Single(result.Currencies).Items.Select(i => i.LocalDate));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task All_transactions_on_request_include_salary_and_transfers()
    {
        var result = await GetAsync(new(Ada, PeriodKind.ThisMonth, IncludeAllTransactions: true));

        var tl = Assert.Single(result.Currencies);
        Assert.Equal(new (TransactionKind, TransactionDirection, decimal)[]
        {
            (TransactionKind.Salary, TransactionDirection.Credit, 45000m),
            (TransactionKind.Transfer, TransactionDirection.Debit, 1500m),
            (TransactionKind.Transfer, TransactionDirection.Credit, 300m),
            (TransactionKind.Purchase, TransactionDirection.Debit, 210m),
            (TransactionKind.Purchase, TransactionDirection.Debit, 165m)
        }, tl.Items.Select(i => (i.Kind, i.Direction, i.Amount)).ToArray());
        Assert.Equal("Deniz M***", tl.Items[1].Counterparty);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task All_transactions_include_the_shop_account()
    {
        var all = await GetAsync(new(Deniz, PeriodKind.ThisMonth, IncludeAllTransactions: true, Limit: 10));
        var shop = await GetAsync(new(Deniz, PeriodKind.ThisMonth, AccountId: DenizShopAccount, IncludeAllTransactions: true));

        // October: salary 55.000, market 9.500, electricity 1.850, Ada's transfer 1.500, shop sales 140/120/95.
        Assert.Equal(new[] { 55000m, 9500m, 1850m, 1500m, 140m, 120m, 95m },
            Assert.Single(all.Currencies).Items.Select(i => i.Amount));
        Assert.Equal(new[] { 140m, 120m, 95m }, Assert.Single(shop.Currencies).Items.Select(i => i.Amount));
        Assert.All(shop.Currencies[0].Items, i => Assert.Equal(TransactionKind.Sale, i.Kind));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Currencies_are_ranked_separately()
    {
        Assert.False(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BANKINGAI_CONNECTION_STRING")),
            "PostgreSQL testi için önce .env ortam değişkenlerini yükleyin.");
        await using var db = new BankingDbContext();
        await using var scope = await db.Database.BeginTransactionAsync();
        // A temporary USD purchase visible only inside this transaction; rolled back below.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Transactions" ("Id", "AccountId", "Amount", "Direction", "Kind", "CurrencyCode", "RawDescription", "OccurredAt", "CreatedAt")
            VALUES ({Guid.NewGuid()}, {AdaUsdAccount}, 10.00, 1, 1, 'USD', 'ANA-06 USD test', '2026-10-05T10:00:00Z', now())
            """);

        var result = await Service(db).GetAsync(new(Ada, PeriodKind.ThisMonth, Limit: 2));
        await scope.RollbackAsync();

        // 10 USD is never compared with 210 TRY; each currency has its own top list.
        Assert.Equal(new[] { "TRY", "USD" }, result.Currencies.Select(c => c.CurrencyCode));
        Assert.Equal(new[] { 210m, 165m }, result.Currencies[0].Items.Select(i => i.Amount));
        Assert.Equal(new[] { 10m }, result.Currencies[1].Items.Select(i => i.Amount));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Period_without_transactions_returns_an_empty_list()
    {
        var result = await GetAsync(new(Ada, PeriodKind.Custom, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31)));

        Assert.Empty(result.Currencies);
    }

    [Theory]
    [Trait("Category", "PostgreSQL")]
    [InlineData(0)]
    [InlineData(101)]
    public async Task Limit_must_be_between_one_and_one_hundred(int limit)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => GetAsync(new(Ada, PeriodKind.ThisMonth, Limit: limit)));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task One_hundred_is_accepted()
    {
        var result = await GetAsync(new(Ada, PeriodKind.Last6Months, Limit: 100, IncludeAllTransactions: true));

        Assert.InRange(Assert.Single(result.Currencies).Items.Count, 1, 100);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Scope_rules_match_the_other_services()
    {
        // Default spending scope rejects a Business account; another customer's account is "not found".
        await Assert.ThrowsAsync<ArgumentException>(() => GetAsync(new(Deniz, PeriodKind.ThisMonth, AccountId: DenizShopAccount)));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => GetAsync(new(Ada, PeriodKind.ThisMonth, AccountId: DenizTryAccount)));
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => GetAsync(new(Ada, PeriodKind.ThisMonth, AccountId: DenizTryAccount, IncludeAllTransactions: true)));
    }

    // ---- ANA-11: amount range (inclusive) in one currency

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Largest_purchases_below_a_maximum()
    {
        var result = await GetAsync(new(Ada, PeriodKind.ThisMonth, MaxAmount: 150m, CurrencyCode: "TRY"));

        Assert.Equal(new[] { 145m, 120m, 95m }, Assert.Single(result.Currencies).Items.Select(i => i.Amount));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task All_transactions_above_a_minimum()
    {
        var result = await GetAsync(new(Ada, PeriodKind.ThisMonth, IncludeAllTransactions: true, MinAmount: 1000m, CurrencyCode: "TRY"));

        Assert.Equal(new (TransactionKind, decimal)[] { (TransactionKind.Salary, 45000m), (TransactionKind.Transfer, 1500m) },
            Assert.Single(result.Currencies).Items.Select(i => (i.Kind, i.Amount)).ToArray());
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Limit_applies_after_the_amount_range()
    {
        // 24.000 and 31.000 are above the maximum, so the next three monthly market purchases are the largest.
        var result = await GetAsync(new(Deniz, PeriodKind.Last6Months, Limit: 3, MaxAmount: 20000m, CurrencyCode: "TRY"));

        Assert.Equal(new[] { 18000m, 16000m, 14000m }, Assert.Single(result.Currencies).Items.Select(i => i.Amount));
    }

    // Exact decimals from text: a double literal such as 1.005 would not survive the conversion unchanged.
    private static decimal? Amount(string? value) => value is null ? null : decimal.Parse(value, CultureInfo.InvariantCulture);

    [Theory]
    [Trait("Category", "PostgreSQL")]
    [InlineData("200", "100", "TRY", "MinAmount")]
    [InlineData("100", null, null, "CurrencyCode")]
    [InlineData(null, null, "JPY", "CurrencyCode")]
    public async Task Amount_filters_follow_the_search_rules(string? min, string? max, string? currency, string field)
    {
        var error = await Assert.ThrowsAsync<ArgumentException>(() => GetAsync(new(Ada, PeriodKind.ThisMonth,
            MinAmount: Amount(min), MaxAmount: Amount(max), CurrencyCode: currency)));

        Assert.Equal(field, error.ParamName);
    }
}
