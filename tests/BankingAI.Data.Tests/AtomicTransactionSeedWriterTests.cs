using BankingAI.Data;
using BankingAI.Data.Entities;
using BankingAI.Data.Enums;
using BankingAI.Data.Seeding;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace BankingAI.Data.Tests;

[Collection(PostgreSqlCollection.Name)]
public sealed class AtomicTransactionSeedWriterTests
{
    private const decimal StartingBalance = 100m;

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Concurrent_credit_and_debit_preserve_the_sum_of_both_deltas()
    {
        EnsurePostgreSqlConfigured();
        var accountId = await CreateTestAccountAsync();
        try
        {
            var initialBalance = await GetBalanceAsync(accountId);
            Assert.Equal(0m, initialBalance);
            await WriteAsync(accountId, 100m, TransactionDirection.Credit);
            var before = await GetBalanceAsync(accountId);
            Assert.Equal(StartingBalance, before);

            var credit = NewTransaction(accountId, 25m, TransactionDirection.Credit);
            var debit = NewTransaction(accountId, 40m, TransactionDirection.Debit);
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            async Task WriteAfterRelease(Transaction transaction)
            {
                await start.Task;
                await using var db = new BankingDbContext();
                await TransactionSeedWriter.AddAsync(db, transaction);
            }

            var creditTask = Task.Run(() => WriteAfterRelease(credit));
            var debitTask = Task.Run(() => WriteAfterRelease(debit));
            start.SetResult();
            await Task.WhenAll(creditTask, debitTask);

            await using var verifyDb = new BankingDbContext();
            var finalBalance = await verifyDb.Accounts.Where(a => a.Id == accountId)
                .Select(a => a.Balance).SingleAsync();
            var transactions = await verifyDb.Transactions.AsNoTracking()
                .Where(t => t.AccountId == accountId).ToListAsync();
            var expected = initialBalance + transactions
                .Where(t => t.Direction == TransactionDirection.Credit).Sum(t => t.Amount)
                - transactions.Where(t => t.Direction == TransactionDirection.Debit).Sum(t => t.Amount);

            Assert.Equal(3, transactions.Count);
            Assert.Equal(85m, expected);
            Assert.Equal(expected, finalBalance);
        }
        finally
        {
            await DeleteTestAccountAsync(accountId);
        }
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Failed_transaction_insert_and_failed_balance_update_leave_no_partial_write()
    {
        EnsurePostgreSqlConfigured();
        var accountId = await CreateTestAccountAsync();
        try
        {
            await WriteAsync(accountId, StartingBalance, TransactionDirection.Credit);
            var before = await GetBalanceAsync(accountId);

            // Valid categorization shape with a missing lookup ID makes PostgreSQL reject
            // the transaction INSERT after the writer has started its database transaction.
            var missingCategory = short.MaxValue;
            await using (var checkDb = new BankingDbContext())
                Assert.False(await checkDb.TransactionCategories.AnyAsync(c => c.Id == missingCategory));
            var rejectedInsert = NewTransaction(accountId, 15m, TransactionDirection.Debit);
            rejectedInsert.CategoryId = missingCategory;
            rejectedInsert.CategorizationSource = CategorizationSource.Rule;
            rejectedInsert.CategorizedAt = DateTimeOffset.UtcNow;

            await using (var db = new BankingDbContext())
            {
                var error = await Assert.ThrowsAsync<DbUpdateException>(
                    () => TransactionSeedWriter.AddAsync(db, rejectedInsert));
                Assert.Equal("23503", Assert.IsType<PostgresException>(error.InnerException).SqlState);
            }

            Assert.Equal(before, await GetBalanceAsync(accountId));
            Assert.False(await TransactionExistsAsync(rejectedInsert.Id));

            // Here INSERT succeeds first, but the atomic Debit update is rejected as
            // insufficient. The transaction insert must roll back with the balance step.
            var rejectedDebit = NewTransaction(accountId, before + 0.01m, TransactionDirection.Debit);
            await using (var db = new BankingDbContext())
            {
                var error = await Assert.ThrowsAsync<InvalidOperationException>(
                    () => TransactionSeedWriter.AddAsync(db, rejectedDebit));
                Assert.Contains("Yetersiz bakiye", error.Message);
            }

            Assert.Equal(before, await GetBalanceAsync(accountId));
            Assert.False(await TransactionExistsAsync(rejectedDebit.Id));
        }
        finally
        {
            await DeleteTestAccountAsync(accountId);
        }
    }

    private static void EnsurePostgreSqlConfigured() =>
        Assert.False(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BANKINGAI_CONNECTION_STRING")),
            "PostgreSQL testleri için önce .env ortam değişkenlerini yükleyin.");

    private static async Task<Guid> CreateTestAccountAsync()
    {
        await using var db = new BankingDbContext();
        var customerId = await db.Customers.AsNoTracking().Select(c => c.Id).FirstAsync();
        var id = Guid.NewGuid();
        db.Accounts.Add(new Account
        {
            Id = id,
            CustomerId = customerId,
            AccountName = "BAL integration test",
            AccountNumber = $"DUMMY-BALTEST-{Guid.NewGuid():N}",
            CurrencyCode = "TRY"
        });
        await db.SaveChangesAsync();
        return id;
    }

    private static async Task WriteAsync(Guid accountId, decimal amount, TransactionDirection direction)
    {
        await using var db = new BankingDbContext();
        await TransactionSeedWriter.AddAsync(db, NewTransaction(accountId, amount, direction));
    }

    private static Transaction NewTransaction(Guid accountId, decimal amount, TransactionDirection direction)
    {
        var now = DateTimeOffset.UtcNow;
        return new Transaction
        {
            AccountId = accountId,
            Amount = amount,
            Direction = direction,
            CurrencyCode = "TRY",
            RawDescription = "BAL integration test",
            OccurredAt = now.AddSeconds(-1)
        };
    }

    private static async Task<decimal> GetBalanceAsync(Guid accountId)
    {
        await using var db = new BankingDbContext();
        return await db.Accounts.AsNoTracking().Where(a => a.Id == accountId)
            .Select(a => a.Balance).SingleAsync();
    }

    private static async Task<bool> TransactionExistsAsync(Guid transactionId)
    {
        await using var db = new BankingDbContext();
        return await db.Transactions.AsNoTracking().AnyAsync(t => t.Id == transactionId);
    }

    private static async Task DeleteTestAccountAsync(Guid accountId)
    {
        await using var db = new BankingDbContext();
        await db.Transactions.Where(t => t.AccountId == accountId).ExecuteDeleteAsync();
        await db.Accounts.Where(a => a.Id == accountId).ExecuteDeleteAsync();
    }
}
