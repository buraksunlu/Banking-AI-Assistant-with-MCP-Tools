using BankingAI.Data;
using BankingAI.Data.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BankingAI.Data.Tests;

[Collection(PostgreSqlCollection.Name)]
public sealed class SyntheticDatasetIntegrityTests
{
    // Expected values are hand-maintained acceptance fixtures. Do not derive them
    // from SyntheticTransactionSeeder, which could repeat the same incorrect data.
    private static readonly ExpectedAccount[] ExpectedAccounts =
    [
        new("DUMMY-ADA-TRY-001", 585000m, 5984.82m, 579015.18m),
        new("DUMMY-ADA-USD-001", 500m, 0m, 500m),
        new("DUMMY-DENIZ-TRY-001", 715000m, 190850m, 524150m),
        new("DUMMY-EGE-EUR-001", 1000m, 0m, 1000m),
        new("DUMMY-EGE-TRY-001", 280000m, 6800m, 273200m),
        new("DUMMY-ADA-EMPTY-001", 0m, 0m, 0m)
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
            (Month: 9, Count: 6, Ada: 514.98m, Deniz: 31000m, Ege: 1400m),
            (Month: 10, Count: 6, Ada: 520m, Deniz: 11350m, Ege: 700m)
        };

        await using var db = new BankingDbContext();
        var spending = await db.Transactions.AsNoTracking()
            .Where(t => t.OccurredAt >= start && t.OccurredAt < end && t.Direction == TransactionDirection.Debit)
            .Select(t => new { t.AccountId, t.Amount, t.OccurredAt, t.NormalizedMerchant })
            .ToListAsync();

        Assert.Equal(43, spending.Count);
        Assert.Equal(136759.88m, spending.Sum(t => t.Amount));
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
                     ("Şehir Ulaşım", 13), ("Elektrik Faturası", 1), ("Şehirlerarası Otobüs", 1)
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

        // Each customer receives one salary; Credits must stay outside spending totals.
        var credits = october.Where(t => t.Direction == TransactionDirection.Credit).ToArray();
        Assert.Equal(3, credits.Length);
        Assert.Equal(140000m, credits.Sum(t => t.Amount));
        Assert.All(credits, t => Assert.Null(t.CategoryId));
        Assert.Equal(12570m, october.Where(t => t.Direction == TransactionDirection.Debit).Sum(t => t.Amount));

        // An ambiguous description stays unknown: no merchant, no category, not OTHER.
        var unknown = Assert.Single(october, t => t.Direction == TransactionDirection.Debit && t.CategoryId is null);
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
    public async Task Existing_rows_are_backfilled_with_kind_and_have_no_pairs()
    {
        await using var db = new BankingDbContext();
        var kinds = await db.Transactions.AsNoTracking()
            .GroupBy(t => new { t.Kind, t.Direction })
            .Select(g => new { g.Key.Kind, g.Key.Direction, Count = g.Count() })
            .ToListAsync();

        // 63 purchases, 33 salaries (2025: 12, 2026 recent: 12, SYN-14: 9), 2 opening fundings.
        Assert.Equal(3, kinds.Count);
        Assert.Equal(63, kinds.Single(k => k.Kind == TransactionKind.Purchase && k.Direction == TransactionDirection.Debit).Count);
        Assert.Equal(33, kinds.Single(k => k.Kind == TransactionKind.Salary && k.Direction == TransactionDirection.Credit).Count);
        Assert.Equal(2, kinds.Single(k => k.Kind == TransactionKind.OpeningFunding && k.Direction == TransactionDirection.Credit).Count);
        Assert.False(await db.Transactions.AnyAsync(t => t.PairId != null));
    }

    private sealed record ExpectedAccount(
        string AccountNumber,
        decimal Credits,
        decimal Debits,
        decimal Balance);
}
