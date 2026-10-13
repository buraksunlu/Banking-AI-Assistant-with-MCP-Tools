using BankingAI.Data;
using BankingAI.Data.Entities;
using BankingAI.Data.Enums;
using BankingAI.Data.Writing;
using BankingAI.TestSupport;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BankingAI.Data.Tests;

[Collection(PostgreSqlCollection.Name)]
public sealed class PairedTransactionWriterTests : IAsyncLifetime
{
    // These tests commit temporary rows and delete them again. The shared lock keeps whole-table counts in
    // other test projects (exclusive holders) from seeing those rows come and go.
    private CommittedRowsLock? rowsLock;

    public async Task InitializeAsync() => rowsLock = await CommittedRowsLock.SharedAsync();

    public async Task DisposeAsync() => await rowsLock!.DisposeAsync();

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Transfer_moves_money_between_customers_atomically()
    {
        var (a, b) = await CreateTestAccountsAsync();
        try
        {
            await FundAsync(a, 100m);
            var (debit, credit) = Pair(a, b, 40m, TransactionKind.Transfer, TransactionKind.Transfer);

            await using (var db = new BankingDbContext())
                await PairedTransactionWriter.AddAsync(db, debit, credit);

            Assert.Equal(60m, await BalanceAsync(a));
            Assert.Equal(40m, await BalanceAsync(b));
            var legs = await LegsAsync(debit.PairId!.Value);
            Assert.Equal(2, legs.Count);
            Assert.All(legs, leg => Assert.Equal(TransactionKind.Transfer, leg.Kind));
            Assert.Equal(a, legs.Single(l => l.Direction == TransactionDirection.Debit).AccountId);
            Assert.Equal(b, legs.Single(l => l.Direction == TransactionDirection.Credit).AccountId);
        }
        finally { await DeleteTestAccountsAsync(a, b); }
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Purchase_and_sale_pair_keeps_category_on_buyer_only()
    {
        var (buyer, shop) = await CreateTestAccountsAsync(AccountType.Business);
        try
        {
            await FundAsync(buyer, 100m);
            short foodId;
            await using (var lookup = new BankingDbContext())
                foodId = await lookup.TransactionCategories.Where(c => c.Code == "FOOD").Select(c => c.Id).SingleAsync();
            var (purchase, sale) = Pair(buyer, shop, 85m, TransactionKind.Purchase, TransactionKind.Sale);
            purchase.SetCategorization(foodId, CategorizationSource.Rule);

            await using (var db = new BankingDbContext())
                await PairedTransactionWriter.AddAsync(db, purchase, sale);

            var legs = await LegsAsync(purchase.PairId!.Value);
            var storedPurchase = legs.Single(l => l.Kind == TransactionKind.Purchase);
            var storedSale = legs.Single(l => l.Kind == TransactionKind.Sale);
            Assert.Equal(foodId, storedPurchase.CategoryId);
            Assert.Null(storedSale.CategoryId);
            Assert.Equal(15m, await BalanceAsync(buyer));
            Assert.Equal(85m, await BalanceAsync(shop));
        }
        finally { await DeleteTestAccountsAsync(buyer, shop); }
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Sale_into_personal_account_is_rejected_without_side_effects()
    {
        var (buyer, personal) = await CreateTestAccountsAsync();
        try
        {
            await FundAsync(buyer, 100m);
            var (purchase, sale) = Pair(buyer, personal, 50m, TransactionKind.Purchase, TransactionKind.Sale);

            await using (var db = new BankingDbContext())
            {
                var error = await Assert.ThrowsAsync<InvalidOperationException>(
                    () => PairedTransactionWriter.AddAsync(db, purchase, sale));
                Assert.Contains("Business", error.Message);
            }

            Assert.Equal(100m, await BalanceAsync(buyer));
            Assert.Equal(0m, await BalanceAsync(personal));
            Assert.Empty(await LegsAsync(purchase.PairId!.Value));
        }
        finally { await DeleteTestAccountsAsync(buyer, personal); }
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Insufficient_balance_rolls_back_both_accounts()
    {
        var (a, b) = await CreateTestAccountsAsync();
        try
        {
            await FundAsync(a, 100m);
            var (debit, credit) = Pair(a, b, 100.01m, TransactionKind.Transfer, TransactionKind.Transfer);

            await using (var db = new BankingDbContext())
            {
                var error = await Assert.ThrowsAsync<BalanceLimitException>(
                    () => PairedTransactionWriter.AddAsync(db, debit, credit));
                Assert.Contains("Yetersiz bakiye", error.Message);
            }

            Assert.Equal(100m, await BalanceAsync(a));
            Assert.Equal(0m, await BalanceAsync(b));
            Assert.Empty(await LegsAsync(debit.PairId!.Value));
        }
        finally { await DeleteTestAccountsAsync(a, b); }
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Failed_second_balance_update_rolls_back_the_first()
    {
        var (a, b) = await CreateTestAccountsAsync();
        try
        {
            await FundAsync(a, 100m);
            // The missing account sorts after a, so a's balance is updated first and must be undone.
            Guid missing;
            do missing = Guid.NewGuid(); while (missing.CompareTo(a) <= 0);
            var (debit, credit) = Pair(a, missing, 30m, TransactionKind.Transfer, TransactionKind.Transfer);

            await using (var db = new BankingDbContext())
            {
                var error = await Assert.ThrowsAsync<InvalidOperationException>(
                    () => PairedTransactionWriter.AddAsync(db, debit, credit));
                Assert.Contains("Hesap bulunamadı", error.Message);
            }

            Assert.Equal(100m, await BalanceAsync(a));
            Assert.Empty(await LegsAsync(debit.PairId!.Value));
        }
        finally { await DeleteTestAccountsAsync(a, b); }
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Inconsistent_pairs_are_rejected_before_touching_the_database()
    {
        var (a, b) = await CreateTestAccountsAsync();
        try
        {
            await FundAsync(a, 100m);
            var occurredAt = DateTimeOffset.UtcNow.AddSeconds(-1);
            var pairId = Guid.NewGuid();
            Transaction Leg(Guid account, TransactionDirection direction, TransactionKind kind,
                decimal amount = 10m, string currency = "TRY", Guid? pair = null, DateTimeOffset? at = null) => new()
            {
                AccountId = account, Amount = amount, Direction = direction, Kind = kind,
                PairId = pair ?? pairId, CurrencyCode = currency, RawDescription = "Inconsistent pair",
                OccurredAt = at ?? occurredAt
            };
            var debit = TransactionDirection.Debit;
            var credit = TransactionDirection.Credit;
            var transfer = TransactionKind.Transfer;
            var invalidPairs = new (Transaction Debit, Transaction Credit)[]
            {
                (Leg(a, debit, transfer), Leg(b, credit, transfer, amount: 11m)),            // farklı tutar
                (Leg(a, debit, transfer), Leg(b, credit, transfer, currency: "USD")),        // farklı para birimi
                (Leg(a, debit, transfer), Leg(a, credit, transfer)),                         // aynı hesap
                (Leg(a, debit, transfer), Leg(b, credit, transfer, pair: Guid.NewGuid())),   // farklı PairId
                (Leg(a, debit, transfer), Leg(b, credit, transfer, at: occurredAt.AddSeconds(-1))), // farklı zaman
                (Leg(a, debit, TransactionKind.Purchase), Leg(b, credit, transfer)),          // Purchase → Transfer
                (Leg(a, debit, transfer), Leg(b, credit, TransactionKind.Sale)),              // Transfer → Sale
                (Leg(b, credit, transfer), Leg(a, debit, transfer))                           // bacaklar ters verilmiş
            };

            foreach (var (debitLeg, creditLeg) in invalidPairs)
            {
                await using var db = new BankingDbContext();
                await Assert.ThrowsAsync<ArgumentException>(() => PairedTransactionWriter.AddAsync(db, debitLeg, creditLeg));
            }

            var sameId = Leg(a, debit, transfer);
            await using (var db = new BankingDbContext())
                await Assert.ThrowsAsync<ArgumentException>(() => PairedTransactionWriter.AddAsync(db, sameId,
                    new Transaction
                    {
                        Id = sameId.Id, AccountId = b, Amount = 10m, Direction = credit, Kind = transfer, PairId = pairId,
                        CurrencyCode = "TRY", RawDescription = "Same id", OccurredAt = occurredAt
                    }));

            Assert.Equal(100m, await BalanceAsync(a));
            Assert.Equal(0m, await BalanceAsync(b));
            Assert.Empty(await LegsAsync(pairId));
        }
        finally { await DeleteTestAccountsAsync(a, b); }
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Repeated_pair_id_is_rejected_and_balances_change_once()
    {
        var (a, b) = await CreateTestAccountsAsync();
        try
        {
            await FundAsync(a, 100m);
            var (debit, credit) = Pair(a, b, 40m, TransactionKind.Transfer, TransactionKind.Transfer);
            await using (var db = new BankingDbContext())
                await PairedTransactionWriter.AddAsync(db, debit, credit);

            // Same PairId, new transaction ids: the retry must not move money again.
            var (retryDebit, retryCredit) = Pair(a, b, 40m, TransactionKind.Transfer, TransactionKind.Transfer, debit.PairId);
            await using (var db = new BankingDbContext())
            {
                var error = await Assert.ThrowsAsync<InvalidOperationException>(
                    () => PairedTransactionWriter.AddAsync(db, retryDebit, retryCredit));
                Assert.Contains("zaten mevcut", error.Message);
            }

            Assert.Equal(60m, await BalanceAsync(a));
            Assert.Equal(40m, await BalanceAsync(b));
            Assert.Equal(2, (await LegsAsync(debit.PairId!.Value)).Count);
        }
        finally { await DeleteTestAccountsAsync(a, b); }
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Opposite_concurrent_transfers_do_not_deadlock_and_conserve_money()
    {
        var (a, b) = await CreateTestAccountsAsync();
        try
        {
            await FundAsync(a, 100m);
            await FundAsync(b, 100m);

            async Task TransferAsync(Guid from, Guid to)
            {
                var (debit, credit) = Pair(from, to, 1m, TransactionKind.Transfer, TransactionKind.Transfer);
                await using var db = new BankingDbContext();
                await PairedTransactionWriter.AddAsync(db, debit, credit);
            }

            // Without a fixed lock order, a→b and b→a lock rows in opposite order and PostgreSQL aborts one (40P01).
            var work = Enumerable.Range(0, 20).SelectMany(_ => new[] { TransferAsync(a, b), TransferAsync(b, a) });
            await Task.WhenAll(work);

            Assert.Equal(100m, await BalanceAsync(a));
            Assert.Equal(100m, await BalanceAsync(b));
            await using var check = new BankingDbContext();
            Assert.Equal(80, await check.Transactions.CountAsync(t =>
                (t.AccountId == a || t.AccountId == b) && t.Kind == TransactionKind.Transfer));
        }
        finally { await DeleteTestAccountsAsync(a, b); }
    }

    private static (Transaction Debit, Transaction Credit) Pair(
        Guid from, Guid to, decimal amount, TransactionKind debitKind, TransactionKind creditKind, Guid? pairId = null)
    {
        var pair = pairId ?? Guid.NewGuid();
        var occurredAt = DateTimeOffset.UtcNow.AddSeconds(-1);
        return (
            new Transaction
            {
                AccountId = from, Amount = amount, Direction = TransactionDirection.Debit, Kind = debitKind,
                PairId = pair, CurrencyCode = "TRY", RawDescription = "KIND-03 debit leg", OccurredAt = occurredAt
            },
            new Transaction
            {
                AccountId = to, Amount = amount, Direction = TransactionDirection.Credit, Kind = creditKind,
                PairId = pair, CurrencyCode = "TRY", RawDescription = "KIND-03 credit leg", OccurredAt = occurredAt
            });
    }

    private static async Task<(Guid A, Guid B)> CreateTestAccountsAsync(AccountType secondType = AccountType.Personal)
    {
        Assert.False(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BANKINGAI_CONNECTION_STRING")),
            "PostgreSQL testleri için önce .env ortam değişkenlerini yükleyin.");
        await using var db = new BankingDbContext();
        // Two different customers: the default case is a transfer between people. Fixed demo customers (Ada,
        // Deniz), because a temporary customer from another test project could otherwise sort first.
        Guid[] customers = [Guid.Parse("10000000-0000-0000-0000-000000000001"), Guid.Parse("10000000-0000-0000-0000-000000000002")];
        var accounts = customers.Select((customerId, index) => new Account
        {
            CustomerId = customerId, AccountName = "KIND-03 test", CurrencyCode = "TRY",
            AccountNumber = $"DUMMY-PAIRTEST-{Guid.NewGuid():N}",
            AccountType = index == 1 ? secondType : AccountType.Personal
        }).ToArray();
        db.Accounts.AddRange(accounts);
        await db.SaveChangesAsync();
        return (accounts[0].Id, accounts[1].Id);
    }

    private static async Task FundAsync(Guid accountId, decimal amount)
    {
        await using var db = new BankingDbContext();
        await TransactionWriter.AddAsync(db, new Transaction
        {
            AccountId = accountId, Amount = amount, Direction = TransactionDirection.Credit,
            Kind = TransactionKind.OpeningFunding, CurrencyCode = "TRY", RawDescription = "KIND-03 funding",
            OccurredAt = DateTimeOffset.UtcNow.AddSeconds(-1)
        });
    }

    private static async Task<decimal> BalanceAsync(Guid accountId)
    {
        await using var db = new BankingDbContext();
        return await db.Accounts.AsNoTracking().Where(a => a.Id == accountId).Select(a => a.Balance).SingleAsync();
    }

    private static async Task<List<Transaction>> LegsAsync(Guid pairId)
    {
        await using var db = new BankingDbContext();
        return await db.Transactions.AsNoTracking().Where(t => t.PairId == pairId).ToListAsync();
    }

    private static async Task DeleteTestAccountsAsync(params Guid[] accountIds)
    {
        await using var db = new BankingDbContext();
        await db.Transactions.Where(t => accountIds.Contains(t.AccountId)).ExecuteDeleteAsync();
        await db.Accounts.Where(a => accountIds.Contains(a.Id)).ExecuteDeleteAsync();
    }
}
