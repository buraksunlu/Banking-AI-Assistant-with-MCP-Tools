using System.Globalization;
using System.Text.RegularExpressions;
using BankingAI.Application.Analytics;
using BankingAI.Data;
using BankingAI.Data.Enums;
using Microsoft.EntityFrameworkCore;
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

    // API-10: one transaction by id. Expected values are the seeded rows (docs/synthetic-seed.md), Istanbul time.
    private static Guid Tx(int ordinal) => Guid.Parse($"30000000-0000-0000-0000-{ordinal:D12}");

    private static async Task<TransactionItem> GetAsync(Guid customerId, Guid transactionId, CancellationToken cancellationToken = default)
    {
        await using var db = new BankingDbContext();
        return await new TransactionSearchService(db, new PeriodResolver(new FixedTimeProvider(DemoNowUtc)))
            .GetAsync(customerId, transactionId, cancellationToken);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task One_transaction_is_returned_with_every_field()
    {
        var item = await GetAsync(Ada, Tx(602));

        Assert.Equal(new TransactionItem(
            Tx(602), AdaTryAccount, "Ada günlük TRY", new DateTimeOffset(2026, 10, 6, 9, 0, 0, TimeSpan.Zero), new DateOnly(2026, 10, 6),
            TransactionKind.Purchase, TransactionDirection.Debit, 210.00m, "TRY", "Starbucks", "FOOD", "Yeme & İçme",
            "POS 4412 STARBUCKS KADIKOY", null, false), item);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Counterparties_are_masked_or_named_like_in_the_statement()
    {
        var outgoing = await GetAsync(Ada, Tx(711));
        var incoming = await GetAsync(Ada, Tx(718));
        var shopPurchase = await GetAsync(Ada, Tx(709));
        var ownShopTransfer = await GetAsync(Deniz, Tx(707));

        Assert.Equal((TransactionKind.Transfer, TransactionDirection.Debit, 1500m, "Deniz M***", false, (string?)null),
            (outgoing.Kind, outgoing.Direction, outgoing.Amount, outgoing.Counterparty, outgoing.CounterpartyIsOwnAccount, outgoing.CategoryDisplayName));
        Assert.Equal((TransactionDirection.Credit, 300m, "Ege Y***"), (incoming.Direction, incoming.Amount, incoming.Counterparty));
        Assert.Equal((TransactionKind.Purchase, 120m, "Kahve Durağı – Kadıköy", false),
            (shopPurchase.Kind, shopPurchase.Amount, shopPurchase.Counterparty, shopPurchase.CounterpartyIsOwnAccount));
        Assert.Equal((2000m, "Kahve Durağı – Kadıköy", true, new DateOnly(2026, 9, 30)),
            (ownShopTransfer.Amount, ownShopTransfer.Counterparty, ownShopTransfer.CounterpartyIsOwnAccount, ownShopTransfer.LocalDate));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task A_business_account_transaction_is_readable_by_its_owner()
    {
        var sale = await GetAsync(Deniz, Tx(710));

        Assert.Equal((DenizShopAccount, "Kahve Durağı – Kadıköy", TransactionKind.Sale, TransactionDirection.Credit, 120m, "Ada K***", (string?)null),
            (sale.AccountId, sale.AccountName, sale.Kind, sale.Direction, sale.Amount, sale.Counterparty, sale.CategoryDisplayName));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task There_is_no_period_limit()
    {
        // Ada's USD opening funding from April 2025, far outside any default listing window.
        var funding = await GetAsync(Ada, Tx(1));

        Assert.Equal((AdaUsdAccount, "Ada USD", TransactionKind.OpeningFunding, TransactionDirection.Credit, 500.00m, "USD", new DateOnly(2025, 4, 1)),
            (funding.AccountId, funding.AccountName, funding.Kind, funding.Direction, funding.Amount, funding.CurrencyCode, funding.LocalDate));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Another_customers_transaction_is_reported_exactly_like_a_missing_one()
    {
        // 710 is the shop leg of Ada's own purchase 709 and 712 the receiving leg of her transfer 711: related
        // to Ada, but on Deniz's accounts.
        var errors = new[]
        {
            await Assert.ThrowsAsync<KeyNotFoundException>(() => GetAsync(Ada, Tx(710))),
            await Assert.ThrowsAsync<KeyNotFoundException>(() => GetAsync(Ada, Tx(712))),
            await Assert.ThrowsAsync<KeyNotFoundException>(() => GetAsync(Ada, Guid.NewGuid())),
            await Assert.ThrowsAsync<KeyNotFoundException>(() => GetAsync(Ada, Guid.Empty))
        };

        Assert.All(errors, e => Assert.Equal("İşlem bulunamadı.", e.Message));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task An_unknown_or_empty_customer_is_rejected()
    {
        await Assert.ThrowsAsync<CustomerNotFoundException>(() => GetAsync(Guid.NewGuid(), Tx(602)));
        await Assert.ThrowsAsync<ArgumentException>(() => GetAsync(Guid.Empty, Tx(602)));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task A_single_transaction_equals_its_statement_row()
    {
        var statement = await SearchAsync(new(Ada, PeriodKind.ThisMonth));

        Assert.Equal(8, statement.Items.Count);
        foreach (var row in statement.Items)
            Assert.Equal(row, await GetAsync(Ada, row.Id));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task A_cancelled_single_read_stops_the_query()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => GetAsync(Ada, Tx(602), cancelled.Token));
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

    // ---- ANA-11: amount range (inclusive) in one currency

    private static (DateOnly, decimal)[] DatedAmounts(TransactionSearchResult result) =>
        result.Items.Select(i => (i.LocalDate, i.Amount)).ToArray();

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Amount_range_includes_both_bounds()
    {
        var result = await SearchAsync(new(Ada, PeriodKind.ThisMonth, MinAmount: 145m, MaxAmount: 165m, CurrencyCode: "TRY"));

        // 145 and 165 sit exactly on the bounds; 120, 210 and the rest fall outside.
        Assert.Equal(new (DateOnly, decimal)[] { (new(2026, 10, 9), 145m), (new(2026, 10, 8), 165m) }, DatedAmounts(result));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Minimum_alone_keeps_every_kind_above_it()
    {
        var result = await SearchAsync(new(Ada, PeriodKind.ThisMonth, MinAmount: 150m, CurrencyCode: "TRY"));

        // A statement filter: the received transfer, the salary and the sent transfer count too.
        Assert.Equal(5, result.TotalCount);
        Assert.Equal(new (DateOnly, decimal)[]
        {
            (new(2026, 10, 8), 300m), (new(2026, 10, 8), 165m), (new(2026, 10, 6), 210m),
            (new(2026, 10, 5), 45000m), (new(2026, 10, 3), 1500m)
        }, DatedAmounts(result));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Maximum_alone_keeps_the_small_ones()
    {
        var result = await SearchAsync(new(Ada, PeriodKind.ThisMonth, MaxAmount: 150m, CurrencyCode: "TRY"));

        Assert.Equal(new[] { 145m, 95m, 120m }, result.Items.Select(i => i.Amount));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Amount_combines_with_category_for_large_grocery_purchases()
    {
        // "20.000 TL üstü market harcamalarım": Deniz's monthly market purchase grew from 12.000 to 31.000.
        var result = await SearchAsync(new(Deniz, PeriodKind.Last6Months, CategoryCode: "GROCERY", MinAmount: 20000m, CurrencyCode: "TRY"));

        Assert.Equal(new (int, decimal)[] { (9, 31000m), (8, 24000m) }, result.Items.Select(i => (i.LocalDate.Month, i.Amount)).ToArray());
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task A_threshold_never_compares_amounts_across_currencies()
    {
        Assert.False(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BANKINGAI_CONNECTION_STRING")),
            "PostgreSQL testi için önce .env ortam değişkenlerini yükleyin.");
        await using var db = new BankingDbContext();
        await using var scope = await db.Database.BeginTransactionAsync();
        // A temporary 300 USD purchase visible only inside this transaction; rolled back below.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Transactions" ("Id", "AccountId", "Amount", "Direction", "Kind", "CurrencyCode", "RawDescription", "OccurredAt", "CreatedAt")
            VALUES ({Guid.NewGuid()}, {AdaUsdAccount}, 300.00, 1, 1, 'USD', 'ANA-11 USD test', '2026-10-05T10:00:00Z', now())
            """);
        var service = new TransactionSearchService(db, new PeriodResolver(new FixedTimeProvider(DemoNowUtc)));

        var lira = await service.SearchAsync(new(Ada, PeriodKind.ThisMonth, MinAmount: 200m, CurrencyCode: "TRY"));
        var dollars = await service.SearchAsync(new(Ada, PeriodKind.ThisMonth, MinAmount: 200m, CurrencyCode: "USD"));
        var dollarsOnly = await service.SearchAsync(new(Ada, PeriodKind.ThisMonth, CurrencyCode: "USD"));
        await scope.RollbackAsync();

        // 300 USD is not "above 200 TL"; the TRY 300 transfer is.
        Assert.Equal(new[] { 300m, 210m, 45000m, 1500m }, lira.Items.Select(i => i.Amount));
        Assert.All(lira.Items, i => Assert.Equal("TRY", i.CurrencyCode));
        Assert.Equal(("USD", 300m), (Assert.Single(dollars.Items).CurrencyCode, dollars.Items[0].Amount));
        Assert.Equal("USD", Assert.Single(dollarsOnly.Items).CurrencyCode);
    }

    // Exact decimals from text: a double literal such as 1.005 would not survive the conversion unchanged.
    private static decimal? Amount(string? value) => value is null ? null : decimal.Parse(value, CultureInfo.InvariantCulture);

    [Theory]
    [Trait("Category", "PostgreSQL")]
    [InlineData("0", null, "TRY", "MinAmount")]         // below 0.01
    [InlineData(null, "1.005", "TRY", "MaxAmount")]     // three decimals
    [InlineData("200", "100", "TRY", "MinAmount")]      // minimum above maximum
    [InlineData("100", null, null, "CurrencyCode")]     // a threshold needs a currency
    [InlineData(null, "100", null, "CurrencyCode")]
    [InlineData(null, null, "JPY", "CurrencyCode")]     // not a supported currency
    [InlineData(null, null, "try", "CurrencyCode")]     // exact code only
    public async Task Invalid_amount_filters_name_their_field(string? min, string? max, string? currency, string field)
    {
        var error = await Assert.ThrowsAsync<ArgumentException>(() => SearchAsync(new(Ada, PeriodKind.ThisMonth,
            MinAmount: Amount(min), MaxAmount: Amount(max), CurrencyCode: currency)));

        Assert.Equal(field, error.ParamName);
    }
}
