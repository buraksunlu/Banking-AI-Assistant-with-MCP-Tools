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

    // ---- ANA-10: the closed merchant list and spending at a chosen group of merchants

    private static async Task<MerchantList> ListAsync(MerchantListQuery query)
    {
        await using var db = Db();
        return await new MerchantSpendingService(db, Periods()).ListAsync(query);
    }

    private static async Task<MerchantGroupSpending> GroupAsync(SpendingQuery query, params string[] merchants)
    {
        await using var db = Db();
        return await new MerchantSpendingService(db, Periods()).GetGroupSpendingAsync(query, merchants);
    }

    private static (string?, decimal, int, decimal)[] Rows(MerchantGroupSpending result) =>
        Assert.Single(result.Currencies).Merchants.Select(m => (m.Merchant, m.Amount, m.TransactionCount, m.Percentage)).ToArray();

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Merchant_list_is_every_merchant_the_customer_ever_bought_from()
    {
        var result = await ListAsync(new(Ada));

        // All time: the 2025 hobby shop purchase is still on the list (decision K3-A).
        Assert.Equal(new (string, string?, string, DateOnly, int)[]
        {
            ("Hobi Dükkânı", "OTHER", "Diğer", new(2025, 6, 22), 1),
            ("Kahve Durağı", "FOOD", "Yeme & İçme", new(2026, 10, 7), 3),
            ("Netflix", "SUBSCRIPTION", "Abonelik", new(2026, 9, 12), 9),
            ("Spotify", "SUBSCRIPTION", "Abonelik", new(2026, 9, 18), 9),
            ("Starbucks", "FOOD", "Yeme & İçme", new(2026, 10, 8), 9)
        }, result.Merchants.Select(m => (m.Merchant, m.CategoryCode, m.CategoryDisplayName, m.LastPurchaseDate, m.TransactionCount)).ToArray());
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Merchant_list_holds_only_the_customers_own_purchases()
    {
        var deniz = await ListAsync(new(Deniz));
        var dollars = await ListAsync(new(Ada, AdaUsdAccount));

        // Ordinal order puts Ç and Ş after the Latin letters. No Ada merchant, and not the shop's own sales.
        Assert.Equal(["Aile Marketi", "Akaryakıt İstasyonu", "Elektrik Faturası", "Çocuk Giyim", "Şehir Ulaşım"],
            deniz.Merchants.Select(m => m.Merchant));
        Assert.Empty(dollars.Merchants);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task A_concept_is_the_sum_of_the_chosen_merchants()
    {
        var result = await GroupAsync(new(Ada, PeriodKind.ThisMonth), "Starbucks", "Kahve Durağı");

        // "Kahveye ne kadar harcadım?": 375 + 215 = 590.
        Assert.Equal(["Starbucks", "Kahve Durağı"], result.Merchants);
        Assert.Equal(("TRY", 590m), (result.Currencies[0].CurrencyCode, result.Currencies[0].TotalAmount));
        Assert.Equal(new (string?, decimal, int, decimal)[] { ("Starbucks", 375m, 2, 63.56m), ("Kahve Durağı", 215m, 2, 36.44m) }, Rows(result));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task The_period_decides_which_purchases_count()
    {
        var result = await GroupAsync(new(Ada, PeriodKind.Last3Months), "Starbucks", "Kahve Durağı");

        Assert.Equal(1255m, result.Currencies[0].TotalAmount);
        Assert.Equal(new (string?, decimal, int, decimal)[] { ("Starbucks", 930m, 5, 74.10m), ("Kahve Durağı", 325m, 3, 25.90m) }, Rows(result));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task A_chosen_merchant_without_spending_is_included_but_has_no_row()
    {
        var result = await GroupAsync(new(Ada, PeriodKind.ThisMonth), "Netflix", "Starbucks");

        // Netflix's October payment has not come yet.
        Assert.Equal(["Netflix", "Starbucks"], result.Merchants);
        Assert.Equal(new (string?, decimal, int, decimal)[] { ("Starbucks", 375m, 2, 100m) }, Rows(result));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Other_spellings_are_named_canonically_and_repeats_count_once()
    {
        var spelled = await GroupAsync(new(Ada, PeriodKind.ThisMonth), "starbucks", "kahve duragi");
        var repeated = await GroupAsync(new(Ada, PeriodKind.ThisMonth), "Starbucks", "starbucks");

        Assert.Equal(["Starbucks", "Kahve Durağı"], spelled.Merchants);
        Assert.Equal(590m, spelled.Currencies[0].TotalAmount);
        Assert.Equal(["Starbucks"], repeated.Merchants);
        Assert.Equal(375m, repeated.Currencies[0].TotalAmount);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Names_resolve_what_a_single_merchant_text_could_not()
    {
        // "şehir" alone is ambiguous; the two names chosen explicitly are not.
        var result = await GroupAsync(new(Ege, PeriodKind.Last6Months), "Şehir Ulaşım", "Şehirlerarası Otobüs");

        Assert.Equal(6800m, result.Currencies[0].TotalAmount);
        Assert.Equal(new (string?, decimal, int, decimal)[] { ("Şehir Ulaşım", 5600m, 13, 82.35m), ("Şehirlerarası Otobüs", 1200m, 1, 17.65m) }, Rows(result));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task One_merchant_group_equals_the_single_merchant_spending()
    {
        var group = await GroupAsync(new(Ada, PeriodKind.Last3Months), "Starbucks");
        var single = await SpendingAsync(new(Ada, PeriodKind.Last3Months), "Starbucks");

        Assert.Equal(Assert.Single(single.Currencies).TotalAmount, Assert.Single(group.Currencies).TotalAmount);
    }

    [Theory]
    [Trait("Category", "PostgreSQL")]
    [InlineData("Starbuck", "'Starbuck' müşterinin merchant listesinde yok.")]         // no partial completion
    [InlineData("Aile Marketi", "'Aile Marketi' müşterinin merchant listesinde yok.")] // Deniz's merchant
    [InlineData("   ", "Metin null, boş veya yalnızca whitespace olamaz.")]
    public async Task A_name_that_is_not_on_the_list_is_an_error(string name, string message)
    {
        var error = await Assert.ThrowsAsync<ArgumentException>(() => GroupAsync(new(Ada, PeriodKind.ThisMonth), "Starbucks", name));

        Assert.Equal("Merchants", error.ParamName);
        Assert.StartsWith(message, error.Message);
    }

    [Theory]
    [Trait("Category", "PostgreSQL")]
    [InlineData(0)]
    [InlineData(21)]
    public async Task A_group_has_one_to_twenty_names(int count)
    {
        var names = Enumerable.Repeat("Starbucks", count).ToArray();

        var error = await Assert.ThrowsAsync<ArgumentException>(() => GroupAsync(new(Ada, PeriodKind.ThisMonth), names));

        Assert.Equal("Merchants", error.ParamName);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Scope_rules_match_the_other_spending_analyses()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() => ListAsync(new(Ada, DenizTryAccount)));
        await Assert.ThrowsAsync<ArgumentException>(() => ListAsync(new(Deniz, DenizShopAccount)));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => GroupAsync(new(Ada, PeriodKind.ThisMonth, AccountId: DenizTryAccount), "Starbucks"));
        await Assert.ThrowsAsync<ArgumentException>(() => GroupAsync(new(Deniz, PeriodKind.ThisMonth, AccountId: DenizShopAccount), "Aile Marketi"));
    }
}
