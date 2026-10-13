using BankingAI.Data;
using BankingAI.Data.Enums;
using BankingAI.TestSupport;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BankingAI.Data.Tests;

[Collection(PostgreSqlCollection.Name)]
public sealed class SyntheticDatasetIntegrityTests : IAsyncLifetime
{
    // Several checks count rows across all customers, so temporary rows committed by tests in other projects
    // (API-11 writes real transactions) must not exist while they run: exclusive against every shared holder.
    private CommittedRowsLock? rowsLock;

    public async Task InitializeAsync() => rowsLock = await CommittedRowsLock.ExclusiveAsync();

    public async Task DisposeAsync() => await rowsLock!.DisposeAsync();

    // Expected values are hand-maintained acceptance fixtures. Do not derive them
    // from SyntheticTransactionSeeder, which could repeat the same incorrect data.
    private static readonly ExpectedAccount[] ExpectedAccounts =
    [
        // KIND-04b: Ada −325 shop purchases, −1.500 sent, +300 received.
        new("DUMMY-ADA-TRY-001", 585300m, 7809.82m, 577490.18m),
        new("DUMMY-ADA-USD-001", 500m, 0m, 500m),
        // Deniz −750 to Ege, −2.000 to own shop, +1.500 from Ada.
        new("DUMMY-DENIZ-TRY-001", 716500m, 193600m, 522900m),
        new("DUMMY-EGE-EUR-001", 1000m, 0m, 1000m),
        // Ege −270 shop purchases, +750 from Deniz, −300 to Ada.
        new("DUMMY-EGE-TRY-001", 280750m, 7370m, 273380m),
        new("DUMMY-ADA-EMPTY-001", 0m, 0m, 0m),
        // Kahve Durağı: 5 sales (595) + 2.000 owner transfer.
        new("DUMMY-DENIZ-SHOP-001", 2595m, 0m, 2595m)
    ];

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Seeded_account_balances_match_independent_credit_and_debit_fixtures()
    {
        Assert.False(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BANKINGAI_CONNECTION_STRING")),
            "PostgreSQL testi için önce .env ortam değişkenlerini yükleyin.");

        var accountNumbers = ExpectedAccounts.Select(a => a.AccountNumber).ToArray();
        await using var db = new BankingDbContext();
        var accounts = await db.Accounts.AsNoTracking()
            .Where(a => accountNumbers.Contains(a.AccountNumber))
            .Select(a => new { a.Id, a.AccountNumber, a.Balance })
            .ToDictionaryAsync(a => a.AccountNumber);

        Assert.Equal(ExpectedAccounts.Length, accounts.Count);

        var accountIds = accounts.Values.Select(a => a.Id).ToArray();
        var transactions = await db.Transactions.AsNoTracking()
            .Where(t => accountIds.Contains(t.AccountId))
            .Select(t => new { t.AccountId, t.Amount, t.Direction })
            .ToListAsync();

        foreach (var expected in ExpectedAccounts)
        {
            var account = accounts[expected.AccountNumber];
            var accountTransactions = transactions.Where(t => t.AccountId == account.Id).ToArray();
            var credits = accountTransactions
                .Where(t => t.Direction == TransactionDirection.Credit)
                .Sum(t => t.Amount);
            var debits = accountTransactions
                .Where(t => t.Direction == TransactionDirection.Debit)
                .Sum(t => t.Amount);

            Assert.Equal(expected.Credits, credits);
            Assert.Equal(expected.Debits, debits);
            Assert.Equal(expected.Balance, account.Balance);
            Assert.Equal(credits - debits, account.Balance);
        }
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Spending_covers_six_demo_months_and_preserves_2025_examples()
    {
        // This acceptance window is fixed to the 10 October 2026 demo date,
        // so advancing the real clock does not silently change test expectations.
        var start = new DateTimeOffset(2026, 4, 10, 0, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2026, 10, 11, 0, 0, 0, TimeSpan.Zero);
        var adaId = Guid.Parse("20000000-0000-0000-0000-000000000001");
        var denizId = Guid.Parse("20000000-0000-0000-0000-000000000003");
        var egeTryId = Guid.Parse("20000000-0000-0000-0000-000000000006");
        var expectedMonths = new[]
        {
            (Month: 4, Count: 6, Ada: 514.98m, Deniz: 12000m, Ege: 600m),
            (Month: 5, Count: 6, Ada: 514.98m, Deniz: 14000m, Ege: 650m),
            (Month: 6, Count: 6, Ada: 514.98m, Deniz: 16000m, Ege: 700m),
            (Month: 7, Count: 6, Ada: 514.98m, Deniz: 18000m, Ege: 650m),
            (Month: 8, Count: 7, Ada: 514.98m, Deniz: 24000m, Ege: 2100m),
            (Month: 9, Count: 8, Ada: 624.98m, Deniz: 31000m, Ege: 1530m),
            (Month: 10, Count: 9, Ada: 735m, Deniz: 11350m, Ege: 840m)
        };

        await using var db = new BankingDbContext();
        // Spending is Kind = Purchase (KIND-01); transfers are Debits but not spending.
        var spending = await db.Transactions.AsNoTracking()
            .Where(t => t.OccurredAt >= start && t.OccurredAt < end && t.Kind == TransactionKind.Purchase)
            .Select(t => new { t.AccountId, t.Amount, t.OccurredAt, t.NormalizedMerchant })
            .ToListAsync();

        Assert.Equal(48, spending.Count);
        Assert.Equal(137354.88m, spending.Sum(t => t.Amount));
        foreach (var expected in expectedMonths)
        {
            var month = spending.Where(t => t.OccurredAt.Month == expected.Month).ToArray();
            Assert.Equal(expected.Count, month.Length);
            Assert.Equal(expected.Ada, month.Where(t => t.AccountId == adaId).Sum(t => t.Amount));
            Assert.Equal(expected.Deniz, month.Where(t => t.AccountId == denizId).Sum(t => t.Amount));
            Assert.Equal(expected.Ege, month.Where(t => t.AccountId == egeTryId).Sum(t => t.Amount));
        }
        foreach (var (merchant, count) in new[]
                 {
                     ("Starbucks", 8), ("Netflix", 6), ("Spotify", 6), ("Aile Marketi", 7),
                     ("Şehir Ulaşım", 13), ("Elektrik Faturası", 1), ("Şehirlerarası Otobüs", 1), ("Kahve Durağı", 5)
                 })
            Assert.Equal(count, spending.Count(t => t.NormalizedMerchant == merchant));

        var yearStart = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var yearEnd = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        Assert.Equal(34, await db.Transactions.CountAsync(t => t.OccurredAt >= yearStart && t.OccurredAt < yearEnd));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task October_2026_demo_data_separates_salary_unknown_and_transport_patterns()
    {
        // 1–10 Ekim 2026 Istanbul günleri; demo "bu ay" penceresi.
        var octoberStart = new DateTimeOffset(2026, 9, 30, 21, 0, 0, TimeSpan.Zero);
        var octoberEnd = new DateTimeOffset(2026, 10, 10, 21, 0, 0, TimeSpan.Zero);
        var egeTryId = Guid.Parse("20000000-0000-0000-0000-000000000006");

        await using var db = new BankingDbContext();
        var october = await db.Transactions.AsNoTracking()
            .Where(t => t.OccurredAt >= octoberStart && t.OccurredAt < octoberEnd)
            .ToListAsync();

        // Each customer receives one salary; salaries stay outside spending totals.
        var salaries = october.Where(t => t.Kind == TransactionKind.Salary).ToArray();
        Assert.Equal(3, salaries.Length);
        Assert.Equal(140000m, salaries.Sum(t => t.Amount));
        Assert.All(salaries, t => Assert.Null(t.CategoryId));
        Assert.Equal(12925m, october.Where(t => t.Kind == TransactionKind.Purchase).Sum(t => t.Amount));
        // Counting every Debit would wrongly add October transfers (1.500 + 300) to spending.
        Assert.Equal(14725m, october.Where(t => t.Direction == TransactionDirection.Debit).Sum(t => t.Amount));

        // An ambiguous description stays unknown: no merchant, no category, not OTHER.
        var unknown = Assert.Single(october, t => t.Kind == TransactionKind.Purchase && t.CategoryId is null);
        Assert.Equal("POS 6604 ONLINE ODEME 4471", unknown.RawDescription);
        Assert.Null(unknown.NormalizedMerchant);
        Assert.Null(unknown.CategorizationSource);
        Assert.Null(unknown.CategoryConfidence);
        Assert.Null(unknown.CategorizedAt);

        // Ege TRY transport series used by the six-month comparison question.
        var transport = await db.Transactions.AsNoTracking()
            .Where(t => t.AccountId == egeTryId && t.CategoryId == 4)
            .Select(t => new { t.OccurredAt, t.Amount })
            .ToListAsync();
        var monthly = transport.GroupBy(t => t.OccurredAt.Month)
            .ToDictionary(g => g.Key, g => g.Sum(t => t.Amount));
        Assert.Equal(new Dictionary<int, decimal>
        {
            [4] = 600m, [5] = 650m, [6] = 700m, [7] = 650m, [8] = 900m, [9] = 1400m, [10] = 700m
        }, monthly);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Transaction_kinds_and_pairs_match_independent_fixture()
    {
        await using var db = new BankingDbContext();
        var kinds = await db.Transactions.AsNoTracking()
            .GroupBy(t => new { t.Kind, t.Direction })
            .Select(g => new { g.Key.Kind, g.Key.Direction, Count = g.Count() })
            .ToListAsync();

        // 63 backfilled purchases + 5 shop purchases; 33 salaries; 2 opening fundings;
        // 5 shop sales; 4 transfers, each with a Debit and a Credit leg.
        var expected = new[]
        {
            (TransactionKind.Purchase, TransactionDirection.Debit, 68),
            (TransactionKind.Salary, TransactionDirection.Credit, 33),
            (TransactionKind.OpeningFunding, TransactionDirection.Credit, 2),
            (TransactionKind.Sale, TransactionDirection.Credit, 5),
            (TransactionKind.Transfer, TransactionDirection.Debit, 4),
            (TransactionKind.Transfer, TransactionDirection.Credit, 4)
        };
        Assert.Equal(expected.Length, kinds.Count);
        foreach (var (kind, direction, count) in expected)
            Assert.Equal(count, kinds.Single(k => k.Kind == kind && k.Direction == direction).Count);

        // Nine pairs, each exactly one Debit + one Credit with the same amount.
        var pairs = await db.Transactions.AsNoTracking()
            .Where(t => t.PairId != null)
            .GroupBy(t => t.PairId)
            .Select(g => new
            {
                Legs = g.Count(),
                Debits = g.Count(t => t.Direction == TransactionDirection.Debit),
                Amounts = g.Select(t => t.Amount).Distinct().Count()
            })
            .ToListAsync();
        Assert.Equal(9, pairs.Count);
        Assert.All(pairs, p => Assert.Equal((2, 1, 1), (p.Legs, p.Debits, p.Amounts)));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Seed_accounts_have_expected_types()
    {
        var numbers = ExpectedAccounts.Select(a => a.AccountNumber).ToArray();
        await using var db = new BankingDbContext();
        var types = await db.Accounts.AsNoTracking()
            .Where(a => numbers.Contains(a.AccountNumber))
            .ToDictionaryAsync(a => a.AccountNumber, a => a.AccountType);

        Assert.Equal(numbers.Length, types.Count);
        Assert.Equal(AccountType.Business, types["DUMMY-DENIZ-SHOP-001"]);
        Assert.All(types.Where(t => t.Key != "DUMMY-DENIZ-SHOP-001"), t => Assert.Equal(AccountType.Personal, t.Value));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Internal_payments_conserve_money_and_report_shop_sales()
    {
        await using var db = new BankingDbContext();
        // Transfers and shop purchases only move TRY between seed accounts; the total before KIND-04b was 1.376.365,18.
        var tryNumbers = new[] { "DUMMY-ADA-TRY-001", "DUMMY-DENIZ-TRY-001", "DUMMY-EGE-TRY-001", "DUMMY-ADA-EMPTY-001", "DUMMY-DENIZ-SHOP-001" };
        Assert.Equal(1376365.18m, await db.Accounts.Where(a => tryNumbers.Contains(a.AccountNumber)).SumAsync(a => a.Balance));

        var shopId = Guid.Parse("20000000-0000-0000-0000-000000000007");
        var sales = await db.Transactions.AsNoTracking()
            .Where(t => t.AccountId == shopId && t.Kind == TransactionKind.Sale)
            .Select(t => new { t.OccurredAt, t.Amount })
            .ToListAsync();
        Assert.Equal((2, 240m), (sales.Count(s => s.OccurredAt.Month == 9), sales.Where(s => s.OccurredAt.Month == 9).Sum(s => s.Amount)));
        Assert.Equal((3, 355m), (sales.Count(s => s.OccurredAt.Month == 10), sales.Where(s => s.OccurredAt.Month == 10).Sum(s => s.Amount)));

        // Ada's October transfer is found through the pair's other leg, not a copied name.
        var adaId = Guid.Parse("20000000-0000-0000-0000-000000000001");
        var denizId = Guid.Parse("20000000-0000-0000-0000-000000000003");
        var sent = await db.Transactions.AsNoTracking()
            .Where(t => t.AccountId == adaId && t.Kind == TransactionKind.Transfer && t.Direction == TransactionDirection.Debit)
            .Join(db.Transactions, d => d.PairId, c => c.PairId, (d, c) => new { d.Amount, c.AccountId, c.Direction })
            .Where(x => x.Direction == TransactionDirection.Credit)
            .ToListAsync();
        var transfer = Assert.Single(sent);
        Assert.Equal((1500m, denizId), (transfer.Amount, transfer.AccountId));
    }

    private sealed record ExpectedAccount(
        string AccountNumber,
        decimal Credits,
        decimal Debits,
        decimal Balance);
}
