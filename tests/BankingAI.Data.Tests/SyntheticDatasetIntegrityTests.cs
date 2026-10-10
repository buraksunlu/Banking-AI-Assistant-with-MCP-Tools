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
        new("DUMMY-ADA-TRY-001", 540000m, 5464.82m, 534535.18m),
        new("DUMMY-ADA-USD-001", 500m, 0m, 500m),
        new("DUMMY-DENIZ-TRY-001", 660000m, 179500m, 480500m),
        new("DUMMY-EGE-EUR-001", 1000m, 0m, 1000m),
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
        var expectedMonths = new[]
        {
            (Month: 4, Ada: 514.98m, Deniz: 12000m),
            (Month: 5, Ada: 514.98m, Deniz: 14000m),
            (Month: 6, Ada: 514.98m, Deniz: 16000m),
            (Month: 7, Ada: 514.98m, Deniz: 18000m),
            (Month: 8, Ada: 514.98m, Deniz: 24000m),
            (Month: 9, Ada: 514.98m, Deniz: 31000m)
        };

        await using var db = new BankingDbContext();
        var spending = await db.Transactions.AsNoTracking()
            .Where(t => t.OccurredAt >= start && t.OccurredAt < end && t.Direction == TransactionDirection.Debit)
            .Select(t => new { t.AccountId, t.Amount, t.OccurredAt, t.NormalizedMerchant })
            .ToListAsync();

        Assert.Equal(24, spending.Count);
        Assert.Equal(118089.88m, spending.Sum(t => t.Amount));
        foreach (var expected in expectedMonths)
        {
            var month = spending.Where(t => t.OccurredAt.Month == expected.Month).ToArray();
            Assert.Equal(4, month.Length);
            Assert.Equal(expected.Ada, month.Where(t => t.AccountId == adaId).Sum(t => t.Amount));
            Assert.Equal(expected.Deniz, month.Where(t => t.AccountId == denizId).Sum(t => t.Amount));
        }
        foreach (var merchant in new[] { "Starbucks", "Netflix", "Spotify", "Aile Marketi" })
            Assert.Equal(6, spending.Count(t => t.NormalizedMerchant == merchant));

        var yearStart = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var yearEnd = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        Assert.Equal(34, await db.Transactions.CountAsync(t => t.OccurredAt >= yearStart && t.OccurredAt < yearEnd));
    }

    private sealed record ExpectedAccount(
        string AccountNumber,
        decimal Credits,
        decimal Debits,
        decimal Balance);
}
