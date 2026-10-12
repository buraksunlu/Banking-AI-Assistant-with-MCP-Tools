using System.Text.RegularExpressions;
using BankingAI.Application.Analytics;
using BankingAI.Data;
using BankingAI.Data.Enums;
using Xunit;
using static BankingAI.Application.Tests.DemoData;

namespace BankingAI.Application.Tests;

// Reads the seeded demo data. Expected rows are hand-computed acceptance fixtures.
public sealed class TransactionSearchServiceTests
{
    private static async Task<TransactionSearchResult> SearchAsync(TransactionSearchQuery query)
    {
        Assert.False(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BANKINGAI_CONNECTION_STRING")),
            "PostgreSQL testi için önce .env ortam değişkenlerini yükleyin.");
        await using var db = new BankingDbContext();
        return await new TransactionSearchService(db, new PeriodResolver(new FixedTimeProvider(DemoNowUtc))).SearchAsync(query);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Items_carry_their_own_account_id()
    {
        // API-08: the API returns TransactionItem as its transaction DTO, so each item names its account.
        var ada = await SearchAsync(new(Ada, PeriodKind.ThisMonth));
        var shop = await SearchAsync(new(Deniz, PeriodKind.ThisMonth, AccountId: DenizShopAccount));

        var starbucks = ada.Items.Single(i => i.Id == Guid.Parse("30000000-0000-0000-0000-000000000602"));
        var transfer = ada.Items.Single(i => i.Id == Guid.Parse("30000000-0000-0000-0000-000000000711"));
        Assert.Equal(AdaTryAccount, starbucks.AccountId);
        // Adding the field leaves the masking untouched.
        Assert.Equal((AdaTryAccount, "Deniz M***", false), (transfer.AccountId, transfer.Counterparty, transfer.CounterpartyIsOwnAccount));
        Assert.NotEmpty(shop.Items);
        Assert.All(shop.Items, i => Assert.Equal(DenizShopAccount, i.AccountId));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Statement_lists_every_kind_newest_first_with_masked_counterparties()
    {
        var result = await SearchAsync(new(Ada, PeriodKind.ThisMonth));

        Assert.Equal((8, false), (result.TotalCount, result.HasMore));
        Assert.Equal(new (DateOnly, TransactionKind, TransactionDirection, decimal, string?, string?)[]
        {
            (new(2026, 10, 9), TransactionKind.Purchase, TransactionDirection.Debit, 145m, null, null),
            (new(2026, 10, 8), TransactionKind.Transfer, TransactionDirection.Credit, 300m, null, "Ege Y***"),
            (new(2026, 10, 8), TransactionKind.Purchase, TransactionDirection.Debit, 165m, "Starbucks", null),
            (new(2026, 10, 7), TransactionKind.Purchase, TransactionDirection.Debit, 95m, "Kahve Durağı", "Kahve Durağı – Kadıköy"),
            (new(2026, 10, 6), TransactionKind.Purchase, TransactionDirection.Debit, 210m, "Starbucks", null),
            (new(2026, 10, 5), TransactionKind.Salary, TransactionDirection.Credit, 45000m, null, null),
            (new(2026, 10, 3), TransactionKind.Transfer, TransactionDirection.Debit, 1500m, null, "Deniz M***"),
            (new(2026, 10, 2), TransactionKind.Purchase, TransactionDirection.Debit, 120m, "Kahve Durağı", "Kahve Durağı – Kadıköy")
        }, result.Items.Select(i => (i.LocalDate, i.Kind, i.Direction, i.Amount, i.Merchant, i.Counterparty)).ToArray());

        Assert.All(result.Items, i => Assert.Equal("Ada günlük TRY", i.AccountName));
        Assert.All(result.Items, i => Assert.False(i.CounterpartyIsOwnAccount));
        Assert.Equal(("FOOD", "Yeme & İçme"), (result.Items[2].CategoryCode, result.Items[2].CategoryDisplayName));
        Assert.Equal((null, "Sınıflandırılmamış"), (result.Items[0].CategoryCode, result.Items[0].CategoryDisplayName));
        // Non-purchases carry no category label at all.
        Assert.Null(result.Items[5].CategoryDisplayName);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Pages_are_stable_and_report_whether_more_rows_exist()
    {
        var first = await SearchAsync(new(Ada, PeriodKind.ThisMonth, Limit: 3));
        var last = await SearchAsync(new(Ada, PeriodKind.ThisMonth, Limit: 3, Offset: 6));
        var all = await SearchAsync(new(Ada, PeriodKind.ThisMonth));

        Assert.Equal((8, true), (first.TotalCount, first.HasMore));
        Assert.Equal(all.Items.Take(3).Select(i => i.Id), first.Items.Select(i => i.Id));
        Assert.Equal((8, false), (last.TotalCount, last.HasMore));
        Assert.Equal(all.Items.Skip(6).Select(i => i.Id), last.Items.Select(i => i.Id));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Sent_transfers_are_filtered_by_kind_and_direction()
    {
        var result = await SearchAsync(new(Ada, PeriodKind.ThisMonth, Kind: TransactionKind.Transfer, Direction: TransactionDirection.Debit));

        var item = Assert.Single(result.Items);
        Assert.Equal((1500m, "Deniz M***"), (item.Amount, item.Counterparty));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Unclassified_filter_finds_the_purchase_to_explain()
    {
        var result = await SearchAsync(new(Ada, PeriodKind.ThisMonth, OnlyUnclassified: true));

        var item = Assert.Single(result.Items);
        Assert.Equal(("POS 6604 ONLINE ODEME 4471", 145m, (string?)null), (item.RawDescription, item.Amount, item.Merchant));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Category_filter_uses_existing_codes_only()
    {
        var food = await SearchAsync(new(Ada, PeriodKind.ThisMonth, CategoryCode: "FOOD"));

        Assert.Equal(new[] { 165m, 95m, 210m, 120m }, food.Items.Select(i => i.Amount));
        // "No such category" must differ from "no transactions in this category".
        await Assert.ThrowsAsync<ArgumentException>(() => SearchAsync(new(Ada, PeriodKind.ThisMonth, CategoryCode: "YEMEK")));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Merchant_filter_reuses_the_matcher()
    {
        var starbucks = await SearchAsync(new(Ada, PeriodKind.Last3Months, Merchant: "starbuck"));
        var ambiguous = await SearchAsync(new(Ege, PeriodKind.Last6Months, Merchant: "şehir"));

        Assert.Equal((MerchantMatchStatus.Matched, true, 5), (starbucks.MerchantMatch!.Status, starbucks.MerchantMatch.IsPartialMatch, starbucks.TotalCount));
        Assert.All(starbucks.Items, i => Assert.Equal("Starbucks", i.Merchant));
        Assert.Equal(MerchantMatchStatus.Ambiguous, ambiguous.MerchantMatch!.Status);
        Assert.Equal((0, false), (ambiguous.TotalCount, ambiguous.HasMore));
        Assert.Empty(ambiguous.Items);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Shop_statement_shows_masked_buyers()
    {
        var result = await SearchAsync(new(Deniz, PeriodKind.ThisMonth, AccountId: DenizShopAccount));

        Assert.All(result.Items, i => Assert.Equal((TransactionKind.Sale, "Kahve Durağı – Kadıköy"), (i.Kind, i.AccountName)));
        Assert.Equal(new (decimal, string?)[] { (95m, "Ada K***"), (140m, "Ege Y***"), (120m, "Ada K***") },
            result.Items.Select(i => (i.Amount, i.Counterparty)).ToArray());
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Own_account_transfer_shows_the_account_name()
    {
        var result = await SearchAsync(new(Deniz, PeriodKind.LastMonth, Kind: TransactionKind.Transfer, Direction: TransactionDirection.Debit));

        Assert.Equal(new (decimal, string?, bool)[] { (2000m, "Kahve Durağı – Kadıköy", true), (750m, "Ege Y***", false) },
            result.Items.Select(i => (i.Amount, i.Counterparty, i.CounterpartyIsOwnAccount)).ToArray());
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Full_counterparty_names_never_leave_the_service()
    {
        var results = new[]
        {
            await SearchAsync(new(Ada, PeriodKind.Last6Months, Limit: 50)),
            await SearchAsync(new(Deniz, PeriodKind.Last6Months, Limit: 50)),
            await SearchAsync(new(Ege, PeriodKind.Last6Months, Limit: 50))
        };
        var masked = new Regex(@"^\p{L}+ \p{L}\*\*\*$");

        foreach (var item in results.SelectMany(r => r.Items).Where(i => i.Counterparty is not null))
        {
            Assert.DoesNotContain(item.Counterparty!, new[] { "Ada Kahve", "Deniz Market", "Ege Yolcu" });
            // A person is always masked; only account names (own account or a shop) appear in clear.
            Assert.True(masked.IsMatch(item.Counterparty!) || item.Counterparty!.Contains("Kahve Durağı") || item.CounterpartyIsOwnAccount,
                $"Maskelenmemiş karşı taraf: {item.Counterparty}");
        }
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Another_customers_account_is_reported_as_not_found()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() => SearchAsync(new(Ada, PeriodKind.ThisMonth, AccountId: DenizTryAccount)));
    }

    [Theory]
    [Trait("Category", "PostgreSQL")]
    [InlineData(0, 0)]
    [InlineData(51, 0)]
    [InlineData(20, -1)]
    [InlineData(20, 10001)]
    public async Task Paging_limits_are_enforced(int limit, int offset)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => SearchAsync(new(Ada, PeriodKind.ThisMonth, Limit: limit, Offset: offset)));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Contradictory_filters_are_rejected()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => SearchAsync(new(Ada, PeriodKind.ThisMonth, CategoryCode: "FOOD", OnlyUnclassified: true)));
        // Only purchases carry categories; a category filter on transfers is a contradiction, not an empty result.
        await Assert.ThrowsAsync<ArgumentException>(() => SearchAsync(new(Ada, PeriodKind.ThisMonth, Kind: TransactionKind.Transfer, CategoryCode: "FOOD")));
        await Assert.ThrowsAsync<ArgumentException>(() => SearchAsync(new(Ada, PeriodKind.ThisMonth, Merchant: "   ")));
    }
}
