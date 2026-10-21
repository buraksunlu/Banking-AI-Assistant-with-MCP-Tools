using BankingAI.Data.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BankingAI.Data.Tests;

// SYN-15 in the database: what `seed transactions realistic` wrote. Credits are hand-computed from the persona rules;
// debits and counts include the seeded background and are golden values, captured once from the definition and reviewed
// (docs/synthetic-seed.md "SYN-15"). A changed generator, seed or story turns this red on purpose.
[Collection(PostgreSqlCollection.Name)]
public sealed class RealisticDatasetTests
{
    private static readonly ExpectedAccount[] Accounts =
    [
        new("DUMMY-SELIN-TRY-001", 646, 145000m, 65227.37m),       // 5.000 + 13 × 8.000 burs + 12 × 3.000 from Hasan
        new("DUMMY-MURAT-TRY-001", 227, 1050000m, 405340.58m),     // 30.000 + 12 × 85.000
        new("DUMMY-ZEYNEP-TRY-001", 282, 270500m, 135077.50m),     // 15.000 + 255.500 project payments
        new("DUMMY-ZEYNEP-USD-001", 32, 12250m, 478.87m),          // 300 + 11.950; 13 × 22,99 + 12 × 15
        new("DUMMY-HASAN-TRY-001", 116, 272000m, 97819.50m),       // 8.000 + 12 × 22.000; debits include 12 × 3.000 to Selin
        new("DUMMY-ELIF-EUR-001", 152, 53400m, 24816.89m),         // 3.000 + 12 × 4.200
        new("DUMMY-ELIF-TRY-001", 11, 25000m, 7390m),              // trip: 860 + 1.240 + 420 + 210 + 600 + 340 + 1.150 + 120 + 1.800 + 650
        new("DUMMY-CAN-TRY-001", 222, 239000m, 166256.79m),        // 5.000 + 13 × 18.000 from the shop
        new("DUMMY-CAN-SHOP-001", 22, 253720m, 234000m)            // 250.000 + 3.720 sales; 13 × 18.000 to Can
    ];

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Every_realistic_account_has_its_fixture_totals_and_balance()
    {
        await using var db = new BankingDbContext();
        var numbers = Accounts.Select(a => a.Number).ToArray();
        var accounts = await db.Accounts.AsNoTracking().Where(a => numbers.Contains(a.AccountNumber))
            .ToDictionaryAsync(a => a.AccountNumber);
        Assert.Equal(Accounts.Length, accounts.Count);

        foreach (var expected in Accounts)
        {
            var account = accounts[expected.Number];
            var rows = await db.Transactions.AsNoTracking().Where(t => t.AccountId == account.Id)
                .Select(t => new { t.Direction, t.Amount }).ToListAsync();
            var credits = rows.Where(t => t.Direction == TransactionDirection.Credit).Sum(t => t.Amount);
            var debits = rows.Where(t => t.Direction == TransactionDirection.Debit).Sum(t => t.Amount);
            Assert.Equal((expected.Number, expected.Count, expected.Credits, expected.Debits),
                (expected.Number, rows.Count, credits, debits));
            Assert.Equal(credits - debits, account.Balance);
        }
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Kinds_pairs_and_customers_match_the_personas()
    {
        await using var db = new BankingDbContext();
        var realistic = Enumerable.Range(8, 9).Select(n => Guid.Parse($"20000000-0000-0000-0000-{n:D12}")).ToArray();
        var kinds = await db.Transactions.AsNoTracking().Where(t => realistic.Contains(t.AccountId))
            .GroupBy(t => new { t.Kind, t.Direction }).Select(g => new { g.Key.Kind, g.Key.Direction, Count = g.Count() }).ToListAsync();

        // 64 incomes (Selin 13, Murat 12, Zeynep 9 + 6 USD, Hasan 12, Elif 12); 9 opening balances; 12 + 13 transfers; 8 shop sales.
        var expected = new[]
        {
            (TransactionKind.Purchase, TransactionDirection.Debit, 1579), (TransactionKind.Salary, TransactionDirection.Credit, 64),
            (TransactionKind.OpeningFunding, TransactionDirection.Credit, 9), (TransactionKind.Sale, TransactionDirection.Credit, 8),
            (TransactionKind.Transfer, TransactionDirection.Debit, 25), (TransactionKind.Transfer, TransactionDirection.Credit, 25)
        };
        Assert.Equal(expected.Length, kinds.Count);
        foreach (var (kind, direction, count) in expected)
            Assert.Equal(count, kinds.Single(k => k.Kind == kind && k.Direction == direction).Count);
        Assert.Equal(33, await db.Transactions.Where(t => realistic.Contains(t.AccountId) && t.PairId != null).Select(t => t.PairId).Distinct().CountAsync());

        var elif = await db.Customers.AsNoTracking().SingleAsync(c => c.Id == Guid.Parse("10000000-0000-0000-0000-000000000008"));
        Assert.Equal(("Elif", "Europe/Berlin", "EUR"), (elif.FirstName, elif.TimeZone, elif.PreferredCurrency));
    }

    private sealed record ExpectedAccount(string Number, int Count, decimal Credits, decimal Debits);
}
