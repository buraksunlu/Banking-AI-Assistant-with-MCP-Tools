using BankingAI.Application.Analytics;
using BankingAI.Data;
using Xunit;
using static BankingAI.Application.Tests.DemoData;

namespace BankingAI.Application.Tests;

// Reads the seeded demo data. Expected values are hand-computed acceptance fixtures.
public sealed class MerchantSpendingServiceTests
{
    private static PeriodResolver Periods() => new(new FixedTimeProvider(DemoNowUtc));

    private static BankingDbContext Db()
    {
        Assert.False(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BANKINGAI_CONNECTION_STRING")),
            "PostgreSQL testi için önce .env ortam değişkenlerini yükleyin.");
        return new BankingDbContext();
    }

    private static async Task<MerchantSpendingResult> SpendingAsync(SpendingQuery query, string merchant)
    {
        await using var db = Db();
        return await new MerchantSpendingService(db, Periods()).GetSpendingAsync(query, merchant);
    }

    private static async Task<MerchantBreakdown> BreakdownAsync(SpendingQuery query)
    {
        await using var db = Db();
        return await new MerchantSpendingService(db, Periods()).GetBreakdownAsync(query);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Starbucks_last_three_months_excludes_other_coffee_shops()
    {
        var result = await SpendingAsync(new(Ada, PeriodKind.Last3Months), "starbucks");

        // 20 Jul, 20 Aug, 20 Sep (185 each) + 6 Oct 210 + 8 Oct 165. Kahve Durağı is not Starbucks.
        Assert.Equal((MerchantMatchStatus.Matched, "Starbucks", false), (result.Match.Status, result.Match.Merchant, result.Match.IsPartialMatch));
        var tl = Assert.Single(result.Currencies);
        Assert.Equal(("TRY", 930m, 5), (tl.CurrencyCode, tl.TotalAmount, tl.TransactionCount));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Partial_match_is_flagged()
    {
        var result = await SpendingAsync(new(Ada, PeriodKind.Last3Months), "starbuck");

        Assert.Equal((MerchantMatchStatus.Matched, "Starbucks", true), (result.Match.Status, result.Match.Merchant, result.Match.IsPartialMatch));
        Assert.Equal(930m, Assert.Single(result.Currencies).TotalAmount);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Known_merchant_without_spending_in_period_is_matched_with_no_totals()
    {
        // Hobi Dükkânı exists in June 2025 history but has no purchase this month.
        var result = await SpendingAsync(new(Ada, PeriodKind.ThisMonth), "hobi dükkanı");

        Assert.Equal((MerchantMatchStatus.Matched, "Hobi Dükkânı"), (result.Match.Status, result.Match.Merchant));
        Assert.Empty(result.Currencies);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Ambiguous_text_returns_candidates_and_no_amounts()
    {
        var result = await SpendingAsync(new(Ege, PeriodKind.Last6Months), "şehir");

        Assert.Equal(MerchantMatchStatus.Ambiguous, result.Match.Status);
        Assert.Equal(new[] { "Şehir Ulaşım", "Şehirlerarası Otobüs" }, result.Match.Candidates);
        Assert.Empty(result.Currencies);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Another_customers_merchant_is_never_a_candidate()
    {
        // "Aile Marketi" is Deniz's merchant; for Ada it simply does not exist.
        var result = await SpendingAsync(new(Ada, PeriodKind.Last6Months), "Aile Marketi");

        Assert.Equal(MerchantMatchStatus.NotFound, result.Match.Status);
        Assert.Empty(result.Match.Candidates);
        Assert.Empty(result.Currencies);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Invalid_merchant_text_is_rejected()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => SpendingAsync(new(Ada, PeriodKind.ThisMonth), "   "));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Breakdown_lists_merchants_largest_first_with_unclassified_group()
    {
        var breakdown = await BreakdownAsync(new(Ada, PeriodKind.ThisMonth));

        var tl = Assert.Single(breakdown.Currencies);
        Assert.Equal(new (string?, string, decimal, int, decimal)[]
        {
            ("Starbucks", "Starbucks", 375m, 2, 51.02m),
            ("Kahve Durağı", "Kahve Durağı", 215m, 2, 29.25m),
            (null, "Sınıflandırılmamış", 145m, 1, 19.73m)
        }, tl.Merchants.Select(m => (m.Merchant, m.DisplayName, m.Amount, m.TransactionCount, m.Percentage)).ToArray());
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Breakdown_six_month_transport_profile()
    {
        var breakdown = await BreakdownAsync(new(Ege, PeriodKind.Last6Months));

        var tl = Assert.Single(breakdown.Currencies);
        Assert.Equal(new (string?, decimal, int, decimal)[]
        {
            ("Şehir Ulaşım", 5600m, 13, 79.21m), ("Şehirlerarası Otobüs", 1200m, 1, 16.97m), ("Kahve Durağı", 270m, 2, 3.82m)
        }, tl.Merchants.Select(m => (m.Merchant, m.Amount, m.TransactionCount, m.Percentage)).ToArray());
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Merchant_totals_always_match_the_spending_summary()
    {
        var queries = new SpendingQuery[]
        {
            new(Ada, PeriodKind.ThisMonth), new(Ada, PeriodKind.Last3Months), new(Ada, PeriodKind.Last6Months),
            new(Deniz, PeriodKind.ThisMonth), new(Deniz, PeriodKind.LastMonth), new(Deniz, PeriodKind.Last6Months),
            new(Ege, PeriodKind.ThisMonth), new(Ege, PeriodKind.Last6Months),
            new(Ada, PeriodKind.Custom, new DateOnly(2025, 4, 1), new DateOnly(2025, 9, 30))
        };

        await using var db = Db();
        var summaries = new SpendingSummaryService(db, Periods());
        var merchants = new MerchantSpendingService(db, Periods());
        foreach (var query in queries)
        {
            var summary = await summaries.GetAsync(query);
            var breakdown = await merchants.GetBreakdownAsync(query);

            Assert.Equal(summary.Currencies.Select(c => c.CurrencyCode), breakdown.Currencies.Select(c => c.CurrencyCode));
            foreach (var (total, split) in summary.Currencies.Zip(breakdown.Currencies))
            {
                Assert.Equal(total.TotalAmount, split.TotalAmount);
                Assert.Equal(total.TotalAmount, split.Merchants.Sum(m => m.Amount));
                Assert.Equal(total.TransactionCount, split.Merchants.Sum(m => m.TransactionCount));
                Assert.InRange(split.Merchants.Sum(m => m.Percentage), 99.95m, 100.05m);
            }
        }
    }
}
