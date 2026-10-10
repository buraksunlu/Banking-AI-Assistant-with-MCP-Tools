using BankingAI.Application.Analytics;
using BankingAI.Data;
using Xunit;

namespace BankingAI.Application.Tests;

// Reads the seeded demo data. Expected values are hand-computed acceptance fixtures.
public sealed class CategoryBreakdownServiceTests
{
    private static readonly DateTimeOffset DemoNowUtc = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid Ada = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid Deniz = Guid.Parse("10000000-0000-0000-0000-000000000002");
    private static readonly Guid Ege = Guid.Parse("10000000-0000-0000-0000-000000000003");
    private static readonly Guid DenizTryAccount = Guid.Parse("20000000-0000-0000-0000-000000000003");
    private static readonly Guid DenizShopAccount = Guid.Parse("20000000-0000-0000-0000-000000000007");

    private static PeriodResolver Periods() => new(new FixedTimeProvider(DemoNowUtc));

    private static async Task<CategoryBreakdown> GetAsync(SpendingQuery query)
    {
        Assert.False(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BANKINGAI_CONNECTION_STRING")),
            "PostgreSQL testi için önce .env ortam değişkenlerini yükleyin.");
        await using var db = new BankingDbContext();
        return await new CategoryBreakdownService(db, Periods()).GetAsync(query);
    }

    private static (string? Code, decimal Amount, int Count, decimal Percentage)[] Rows(CurrencyCategoryBreakdown currency) =>
        currency.Categories.Select(c => (c.CategoryCode, c.Amount, c.TransactionCount, c.Percentage)).ToArray();

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task This_month_groups_purchases_and_keeps_unclassified_visible()
    {
        var breakdown = await GetAsync(new(Ada, PeriodKind.ThisMonth));

        // Starbucks 210 + 165 and Kahve Durağı 120 + 95 are FOOD; the 145 online payment is unclassified.
        var tl = Assert.Single(breakdown.Currencies);
        Assert.Equal(("TRY", 735m), (tl.CurrencyCode, tl.TotalAmount));
        Assert.Equal(new (string?, decimal, int, decimal)[] { ("FOOD", 590m, 4, 80.27m), (null, 145m, 1, 19.73m) }, Rows(tl));
        Assert.Equal("Yeme & İçme", tl.Categories[0].DisplayName);
        Assert.Equal("Sınıflandırılmamış", tl.Categories[1].DisplayName);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Largest_category_comes_first()
    {
        var breakdown = await GetAsync(new(Ada, PeriodKind.Last3Months));

        // FOOD: 3 × Starbucks 185 + Kahve Durağı 110 + October 590. SUBSCRIPTION: 3 × (229,99 + 99,99).
        var tl = Assert.Single(breakdown.Currencies);
        Assert.Equal(new (string?, decimal, int, decimal)[]
        {
            ("FOOD", 1255m, 8, 52.51m), ("SUBSCRIPTION", 989.94m, 6, 41.42m), (null, 145m, 1, 6.07m)
        }, Rows(tl));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Shop_income_never_appears_as_a_category()
    {
        var breakdown = await GetAsync(new(Deniz, PeriodKind.ThisMonth));

        var tl = Assert.Single(breakdown.Currencies);
        Assert.Equal(new (string?, decimal, int, decimal)[] { ("GROCERY", 9500m, 1, 83.70m), ("BILLS", 1850m, 1, 16.30m) }, Rows(tl));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Six_month_transport_profile()
    {
        var breakdown = await GetAsync(new(Ege, PeriodKind.Last6Months));

        // Transport Apr–Oct (600+650+700+650+900+1.400+700), one bus ticket, two Kahve Durağı purchases.
        var tl = Assert.Single(breakdown.Currencies);
        Assert.Equal(7070m, tl.TotalAmount);
        Assert.Equal(new (string?, decimal, int, decimal)[]
        {
            ("TRANSPORTATION", 5600m, 13, 79.21m), ("TRAVEL", 1200m, 1, 16.97m), ("FOOD", 270m, 2, 3.82m)
        }, Rows(tl));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Other_and_unclassified_stay_separate()
    {
        var breakdown = await GetAsync(new(Ada, PeriodKind.Custom, new DateOnly(2025, 6, 1), new DateOnly(2025, 6, 30)));

        // OTHER means "reviewed, fits no category"; unclassified means "not determined yet" (VAL-04).
        var tl = Assert.Single(breakdown.Currencies);
        Assert.Equal(new (string?, decimal, int, decimal)[] { ("OTHER", 780m, 1, 65.00m), (null, 420m, 1, 35.00m) }, Rows(tl));
        Assert.Equal("Diğer", tl.Categories[0].DisplayName);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Category_totals_always_match_the_spending_summary()
    {
        var queries = new SpendingQuery[]
        {
            new(Ada, PeriodKind.ThisMonth), new(Ada, PeriodKind.Last3Months), new(Ada, PeriodKind.Last6Months),
            new(Deniz, PeriodKind.ThisMonth), new(Deniz, PeriodKind.LastMonth), new(Deniz, PeriodKind.Last6Months),
            new(Ege, PeriodKind.ThisMonth), new(Ege, PeriodKind.Last6Months),
            new(Ada, PeriodKind.Custom, new DateOnly(2025, 4, 1), new DateOnly(2025, 9, 30))
        };

        await using var db = new BankingDbContext();
        var summaries = new SpendingSummaryService(db, Periods());
        var breakdowns = new CategoryBreakdownService(db, Periods());
        foreach (var query in queries)
        {
            var summary = await summaries.GetAsync(query);
            var breakdown = await breakdowns.GetAsync(query);

            Assert.Equal(summary.Currencies.Select(c => c.CurrencyCode), breakdown.Currencies.Select(c => c.CurrencyCode));
            foreach (var (total, split) in summary.Currencies.Zip(breakdown.Currencies))
            {
                Assert.Equal(total.TotalAmount, split.TotalAmount);
                Assert.Equal(total.TotalAmount, split.Categories.Sum(c => c.Amount));
                Assert.Equal(total.TransactionCount, split.Categories.Sum(c => c.TransactionCount));
                Assert.Equal(total.UncategorizedAmount, split.Categories.Where(c => c.CategoryCode is null).Sum(c => c.Amount));
                // Rounded percentages may miss 100 by a few hundredths; they are never forced to add up.
                Assert.InRange(split.Categories.Sum(c => c.Percentage), 99.95m, 100.05m);
            }
        }
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Empty_period_returns_an_empty_list()
    {
        var breakdown = await GetAsync(new(Ada, PeriodKind.Custom, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31)));

        Assert.Empty(breakdown.Currencies);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Shared_scope_rejects_foreign_and_business_accounts()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() => GetAsync(new(Ada, PeriodKind.ThisMonth, AccountId: DenizTryAccount)));
        await Assert.ThrowsAsync<ArgumentException>(() => GetAsync(new(Deniz, PeriodKind.ThisMonth, AccountId: DenizShopAccount)));
    }
}
