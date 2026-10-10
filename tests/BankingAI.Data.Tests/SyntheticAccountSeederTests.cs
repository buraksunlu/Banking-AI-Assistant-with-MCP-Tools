using BankingAI.Data;
using BankingAI.Data.Seeding;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace BankingAI.Data.Tests;

[Collection(PostgreSqlCollection.Name)]
public sealed class SyntheticAccountSeederTests
{
    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Accounts_have_valid_ownership_repeat_safely_and_reject_conflicts()
    {
        await using var db = new BankingDbContext();
        await using var transaction = await db.Database.BeginTransactionAsync();
        // These fixtures depend on the explicit SYN-03 customer seed.
        var adaId = Guid.Parse("10000000-0000-0000-0000-000000000001");
        var denizId = Guid.Parse("10000000-0000-0000-0000-000000000002");
        var egeId = Guid.Parse("10000000-0000-0000-0000-000000000003");
        var expected = new[]
        {
            (Number: "DUMMY-ADA-TRY-001", Customer: adaId, Currency: "TRY"),
            (Number: "DUMMY-ADA-USD-001", Customer: adaId, Currency: "USD"),
            (Number: "DUMMY-DENIZ-TRY-001", Customer: denizId, Currency: "TRY"),
            (Number: "DUMMY-EGE-EUR-001", Customer: egeId, Currency: "EUR"),
            (Number: "DUMMY-EGE-TRY-001", Customer: egeId, Currency: "TRY"),
            (Number: "DUMMY-ADA-EMPTY-001", Customer: adaId, Currency: "TRY")
        };
        var numbers = expected.Select(a => a.Number).ToArray();
        var originalIds = await db.Accounts.Select(a => a.Id).ToListAsync();
        var added = await SyntheticAccountSeeder.SeedAsync(db);
        var accounts = await db.Accounts.AsNoTracking().Where(a => numbers.Contains(a.AccountNumber)).ToListAsync();
        Assert.Equal(6, accounts.Count);
        Assert.Equal(accounts.Count(a => !originalIds.Contains(a.Id)), added);
        Assert.Equal(6, accounts.Select(a => a.AccountNumber).Distinct().Count());
        foreach (var item in expected)
        {
            var account = Assert.Single(accounts, a => a.AccountNumber == item.Number);
            Assert.Equal(item.Customer, account.CustomerId);
            Assert.Equal(item.Currency, account.CurrencyCode);
            Assert.Contains(account.CurrencyCode, new[] { "TRY", "USD", "EUR", "GBP" });
            Assert.True(await db.Customers.AnyAsync(c => c.Id == account.CustomerId));
            if (!originalIds.Contains(account.Id)) Assert.Equal(0m, account.Balance);
        }
        var empty = Assert.Single(accounts, a => a.AccountNumber == "DUMMY-ADA-EMPTY-001");
        Assert.Equal(0m, empty.Balance);
        Assert.False(await db.Transactions.AnyAsync(t => t.AccountId == empty.Id));
        Assert.Equal(0, await SyntheticAccountSeeder.SeedAsync(db));

        // Even when a balance has changed, rerunning seed must preserve it and CreatedAt.
        var target = Assert.Single(accounts, a => a.AccountNumber == "DUMMY-ADA-TRY-001");
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Accounts\" SET \"Balance\" = 123.45 WHERE \"Id\" = {target.Id}");
        Assert.Equal(0, await SyntheticAccountSeeder.SeedAsync(db));
        var persisted = await db.Accounts.AsNoTracking().SingleAsync(a => a.Id == target.Id);
        Assert.Equal(123.45m, persisted.Balance);
        Assert.Equal(target.CreatedAt, persisted.CreatedAt);

        // Changed ownership is an error, not silently accepted or overwritten.
        await transaction.CreateSavepointAsync("conflict");
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Accounts\" SET \"CustomerId\" = {denizId} WHERE \"Id\" = {target.Id}");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => SyntheticAccountSeeder.SeedAsync(db));
        Assert.Contains("çelişiyor", error.Message);
        Assert.Equal(denizId, await db.Accounts.Where(a => a.Id == target.Id).Select(a => a.CustomerId).SingleAsync());
        await transaction.RollbackToSavepointAsync("conflict");

        // The empty fixture has no transactions, so its key can be temporarily moved.
        await transaction.CreateSavepointAsync("number_conflict");
        var otherId = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Accounts\" SET \"Id\" = {otherId} WHERE \"Id\" = {empty.Id}");
        var numberError = await Assert.ThrowsAsync<InvalidOperationException>(() => SyntheticAccountSeeder.SeedAsync(db));
        Assert.Contains("farklı bir Id", numberError.Message);
        await transaction.RollbackToSavepointAsync("number_conflict");

        async Task RejectInsert(string number, Guid owner, string currency, string sqlState)
        {
            await transaction.CreateSavepointAsync("invalid");
            var id = Guid.NewGuid();
            var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Accounts" ("Id", "CustomerId", "AccountName", "AccountNumber", "CurrencyCode", "AccountType", "Balance", "CreatedAt")
                VALUES ({id}, {owner}, 'Seed validation', {number}, {currency}, 1, 0, CURRENT_TIMESTAMP)
                """));
            Assert.Equal(sqlState, error.SqlState);
            await transaction.RollbackToSavepointAsync("invalid");
        }
        await RejectInsert(target.AccountNumber, adaId, "TRY", "23505"); // unique
        await RejectInsert("DUMMY-TEST-MISSING", Guid.NewGuid(), "TRY", "23503"); // FK
        await RejectInsert("DUMMY-TEST-CURRENCY", adaId, "JPY", "23514"); // CHECK

        await transaction.RollbackAsync();
        db.ChangeTracker.Clear();
        Assert.Equal(originalIds.Order(), (await db.Accounts.Select(a => a.Id).ToListAsync()).Order());
    }
}
