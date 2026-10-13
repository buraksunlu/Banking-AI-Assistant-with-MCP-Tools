using BankingAI.Application.Analytics;
using BankingAI.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static BankingAI.Application.Tests.DemoData;

namespace BankingAI.Application.Tests;

// Reads the seeded bankingai_foundation demo data. Expected values are hand-computed
// acceptance fixtures (docs/synthetic-seed.md), never derived from the seeders.
public sealed class SpendingSummaryServiceTests
{
    private static SpendingSummaryService Service(BankingDbContext db) =>
        new(db, new PeriodResolver(new FixedTimeProvider(DemoNowUtc)));

    private static async Task<SpendingSummary> GetAsync(SpendingQuery request)
    {
        Assert.False(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BANKINGAI_CONNECTION_STRING")),
            "PostgreSQL testi için önce .env ortam değişkenlerini yükleyin.");
        await using var db = new BankingDbContext();
        return await Service(db).GetAsync(request);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task This_month_counts_purchases_only_not_transfers_or_salary()
    {
        var summary = await GetAsync(new(Ada, PeriodKind.ThisMonth));

        // Starbucks 210 + 165, unknown 145, Kahve Durağı 120 + 95. Excludes the 1.500 sent,
        // the 300 received and the 45.000 salary; summing every Debit would give 2.235.
        var tl = Assert.Single(summary.Currencies);
        Assert.Equal(("TRY", 735m, 5), (tl.CurrencyCode, tl.TotalAmount, tl.TransactionCount));
        Assert.Equal((145m, 1), (tl.UncategorizedAmount, tl.UncategorizedCount));
        Assert.Equal((new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 10)), (summary.Period.StartDate, summary.Period.EndDate));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Shop_income_and_owner_transfer_are_not_personal_spending()
    {
        var summary = await GetAsync(new(Deniz, PeriodKind.ThisMonth));

        // Aile Marketi 9.500 + electricity 1.850. The shop's 355 sales and transfers stay out.
        var tl = Assert.Single(summary.Currencies);
        Assert.Equal(("TRY", 11350m, 2), (tl.CurrencyCode, tl.TotalAmount, tl.TransactionCount));
        Assert.Equal((0m, 0), (tl.UncategorizedAmount, tl.UncategorizedCount));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Each_customer_sees_only_their_own_spending()
    {
        var summary = await GetAsync(new(Ege, PeriodKind.ThisMonth));

        // Transport 700 + Kahve Durağı 140.
        var tl = Assert.Single(summary.Currencies);
        Assert.Equal(("TRY", 840m, 2), (tl.CurrencyCode, tl.TotalAmount, tl.TransactionCount));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Last_month_is_the_previous_full_month()
    {
        var summary = await GetAsync(new(Deniz, PeriodKind.LastMonth));

        // September: Aile Marketi 31.000; the 750 and 2.000 transfers are excluded.
        var tl = Assert.Single(summary.Currencies);
        Assert.Equal(("TRY", 31000m, 1), (tl.CurrencyCode, tl.TotalAmount, tl.TransactionCount));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Last_three_months_is_a_rolling_window()
    {
        var summary = await GetAsync(new(Ada, PeriodKind.Last3Months));

        // 10 Jul – 10 Oct: three full months of Netflix/Spotify/Starbucks (3 × 514,98),
        // Kahve Durağı 110 in September and October's 735.
        var tl = Assert.Single(summary.Currencies);
        Assert.Equal(("TRY", 2389.94m, 15), (tl.CurrencyCode, tl.TotalAmount, tl.TransactionCount));
        Assert.Equal((145m, 1), (tl.UncategorizedAmount, tl.UncategorizedCount));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Account_filter_limits_to_one_owned_account()
    {
        var summary = await GetAsync(new(Ada, PeriodKind.ThisMonth, AccountId: AdaTryAccount));

        Assert.Equal(735m, Assert.Single(summary.Currencies).TotalAmount);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Another_customers_account_is_reported_as_not_found()
    {
        // "Not found", never "not yours": the response must not reveal that the account exists.
        var error = await Assert.ThrowsAsync<KeyNotFoundException>(
            () => GetAsync(new(Ada, PeriodKind.ThisMonth, AccountId: DenizTryAccount)));
        Assert.DoesNotContain(DenizTryAccount.ToString(), error.Message);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Business_account_is_rejected_for_personal_spending()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => GetAsync(new(Deniz, PeriodKind.ThisMonth, AccountId: DenizShopAccount)));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Unknown_customer_is_reported_as_not_found()
    {
        await Assert.ThrowsAsync<CustomerNotFoundException>(() => GetAsync(new(Guid.NewGuid(), PeriodKind.ThisMonth)));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Period_without_purchases_returns_an_empty_list()
    {
        var summary = await GetAsync(new(Ada, PeriodKind.Custom, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31)));

        // No invented "0 TRY" row: the explanation layer says no spending was found.
        Assert.Empty(summary.Currencies);
        Assert.Equal(new DateOnly(2026, 1, 1), summary.Period.StartDate);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Currencies_are_reported_separately_never_summed()
    {
        Assert.False(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BANKINGAI_CONNECTION_STRING")),
            "PostgreSQL testi için önce .env ortam değişkenlerini yükleyin.");
        await using var db = new BankingDbContext();
        await using var scope = await db.Database.BeginTransactionAsync();
        // A temporary USD purchase visible only inside this transaction; rolled back below.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Transactions" ("Id", "AccountId", "Amount", "Direction", "Kind", "CurrencyCode", "RawDescription", "OccurredAt", "CreatedAt")
            VALUES ({Guid.NewGuid()}, {AdaUsdAccount}, 10.00, 1, 1, 'USD', 'ANA-02 USD test', '2026-10-05T10:00:00Z', now())
            """);

        var summary = await Service(db).GetAsync(new(Ada, PeriodKind.ThisMonth));
        await scope.RollbackAsync();

        Assert.Equal(new[] { ("TRY", 735m), ("USD", 10m) },
            summary.Currencies.Select(c => (c.CurrencyCode, c.TotalAmount)).ToArray());
    }
}
