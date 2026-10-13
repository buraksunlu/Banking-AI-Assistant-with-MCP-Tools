using BankingAI.Application.Analytics;
using BankingAI.Application.Transactions;
using BankingAI.Data;
using BankingAI.Data.Enums;
using BankingAI.Data.Writing;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static BankingAI.Application.Tests.DemoData;

namespace BankingAI.Application.Tests;

// API-11. The writer commits its own DB transaction, so these tests cannot roll back: every write goes to a
// TemporaryCustomer that is deleted afterwards. Demo accounts are never the target, not even of a test that
// expects a refusal, so a check broken on purpose can only ever touch temporary rows.
public sealed class TransactionWriteServiceTests
{
    // One minute in the past (never "future" for the validation) and whole seconds, so the stored OccurredAt
    // (PostgreSQL keeps microseconds) compares exactly.
    private static readonly DateTimeOffset Now =
        DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds());

    private static readonly DateOnly Today =
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(Now, TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul")).DateTime);

    private static TransactionSearchService Reader(BankingDbContext db) =>
        new(db, new PeriodResolver(new FixedTimeProvider(Now)));

    // Each call is a new event (a fresh key) unless a test repeats a key on purpose.
    private static async Task<TransactionItem> AddAsync(Guid customerId, Guid accountId, TransactionKind kind, decimal amount,
        string description = "API-11 test", string? merchant = null, CancellationToken cancellationToken = default) =>
        (await WriteAsync(customerId, accountId, kind, amount, Guid.NewGuid().ToString(), description, merchant, cancellationToken)).Transaction;

    private static async Task<TransactionWriteResult> WriteAsync(Guid customerId, Guid accountId, TransactionKind kind, decimal amount,
        string? key, string description = "API-11 test", string? merchant = null, CancellationToken cancellationToken = default)
    {
        await using var db = new BankingDbContext();
        var service = new TransactionWriteService(db, new PeriodResolver(new FixedTimeProvider(Now)), new FixedTimeProvider(Now), Reader(db));
        return await service.CreateAsync(customerId, accountId, new NewTransaction(kind, amount, description, merchant), key, cancellationToken);
    }

    private static async Task<(decimal Balance, int Transactions)> StateAsync(Guid accountId)
    {
        await using var db = new BankingDbContext();
        var balance = await db.Accounts.Where(a => a.Id == accountId).Select(a => a.Balance).SingleAsync();
        return (balance, await db.Transactions.CountAsync(t => t.AccountId == accountId));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Salary_and_purchase_move_the_balance_and_read_back_like_a_statement_row()
    {
        await using var customer = await TemporaryCustomer.CreateAsync();
        var account = await customer.AddAccountAsync("TRY");

        var salary = await AddAsync(customer.CustomerId, account, TransactionKind.Salary, 1000m, "Maaş ödemesi");

        Assert.Equal((account, TransactionKind.Salary, TransactionDirection.Credit, 1000m, "TRY", Now, Today),
            (salary.AccountId, salary.Kind, salary.Direction, salary.Amount, salary.CurrencyCode, salary.OccurredAtUtc, salary.LocalDate));
        Assert.Equal(((string?)null, (string?)null, (string?)null, (string?)null, false),
            (salary.Merchant, salary.CategoryCode, salary.CategoryDisplayName, salary.Counterparty, salary.CounterpartyIsOwnAccount));
        Assert.Equal((1000m, 1), await StateAsync(account));

        var purchase = await AddAsync(customer.CustomerId, account, TransactionKind.Purchase, 250.75m,
            "POS 4412 STARBUCKS KADIKOY", "Starbucks");

        // Direction comes from the kind; a new purchase starts unclassified.
        Assert.Equal((TransactionDirection.Debit, 250.75m, "Starbucks", (string?)null, "Sınıflandırılmamış", "POS 4412 STARBUCKS KADIKOY"),
            (purchase.Direction, purchase.Amount, purchase.Merchant, purchase.CategoryCode, purchase.CategoryDisplayName, purchase.RawDescription));
        Assert.Equal((749.25m, 2), await StateAsync(account));

        // Returned exactly as a single read returns it, and stored with backend-owned fields only.
        await using var db = new BankingDbContext();
        Assert.Equal(purchase, await Reader(db).GetAsync(customer.CustomerId, purchase.Id));
        var row = await db.Transactions.AsNoTracking().SingleAsync(t => t.Id == purchase.Id);
        Assert.True(row.CreatedAt >= row.OccurredAt);
        Assert.Equal(((short?)null, (CategorizationSource?)null, (decimal?)null, (DateTimeOffset?)null, (Guid?)null),
            (row.CategoryId, row.CategorizationSource, row.CategoryConfidence, row.CategorizedAt, row.PairId));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task A_purchase_above_the_balance_writes_nothing_but_the_whole_balance_can_be_spent()
    {
        await using var customer = await TemporaryCustomer.CreateAsync();
        var account = await customer.AddAccountAsync("TRY");
        await AddAsync(customer.CustomerId, account, TransactionKind.Salary, 100m);

        var error = await Assert.ThrowsAsync<BalanceLimitException>(
            () => AddAsync(customer.CustomerId, account, TransactionKind.Purchase, 100.01m));

        Assert.Contains("Yetersiz bakiye", error.Message);
        Assert.Equal((100m, 1), await StateAsync(account));

        await AddAsync(customer.CustomerId, account, TransactionKind.Purchase, 100m);
        Assert.Equal((0m, 2), await StateAsync(account));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task The_currency_comes_from_the_account()
    {
        await using var customer = await TemporaryCustomer.CreateAsync();
        var account = await customer.AddAccountAsync("USD");

        var salary = await AddAsync(customer.CustomerId, account, TransactionKind.Salary, 10m);

        Assert.Equal("USD", salary.CurrencyCode);
        Assert.Equal((10m, 1), await StateAsync(account));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task A_business_account_accepts_transactions_too()
    {
        // Decision 3-B: allowed. Spending analytics still only look at personal accounts.
        await using var customer = await TemporaryCustomer.CreateAsync();
        var shop = await customer.AddAccountAsync("TRY", AccountType.Business);

        await AddAsync(customer.CustomerId, shop, TransactionKind.Salary, 50m);
        var purchase = await AddAsync(customer.CustomerId, shop, TransactionKind.Purchase, 20m);

        Assert.Equal(shop, purchase.AccountId);
        Assert.Equal((30m, 2), await StateAsync(shop));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Another_customers_or_a_missing_account_is_reported_as_not_found()
    {
        // Ada writes into the temporary customer's account, never the other way round (see the class comment).
        await using var customer = await TemporaryCustomer.CreateAsync();
        var account = await customer.AddAccountAsync("TRY");

        await Assert.ThrowsAsync<KeyNotFoundException>(() => AddAsync(Ada, account, TransactionKind.Salary, 10m));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => AddAsync(customer.CustomerId, Guid.NewGuid(), TransactionKind.Salary, 10m));

        Assert.Equal((0m, 0), await StateAsync(account));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task An_unknown_or_empty_customer_is_rejected()
    {
        await using var customer = await TemporaryCustomer.CreateAsync();
        var account = await customer.AddAccountAsync("TRY");

        await Assert.ThrowsAsync<CustomerNotFoundException>(() => AddAsync(Guid.NewGuid(), account, TransactionKind.Salary, 10m));
        await Assert.ThrowsAsync<ArgumentException>(() => AddAsync(Guid.Empty, account, TransactionKind.Salary, 10m));
        Assert.Equal((0m, 0), await StateAsync(account));
    }

    [Theory]
    [Trait("Category", "PostgreSQL")]
    [InlineData(TransactionKind.Transfer)]
    [InlineData(TransactionKind.Sale)]
    [InlineData(TransactionKind.OpeningFunding)]
    public async Task Only_purchase_and_salary_are_accepted(TransactionKind kind)
    {
        await using var customer = await TemporaryCustomer.CreateAsync();
        var account = await customer.AddAccountAsync("TRY");

        await Assert.ThrowsAsync<ArgumentException>(() => AddAsync(customer.CustomerId, account, kind, 10m));
        Assert.Equal((0m, 0), await StateAsync(account));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Invalid_values_write_nothing()
    {
        await using var customer = await TemporaryCustomer.CreateAsync();
        var account = await customer.AddAccountAsync("TRY");

        // A salary has no shop; three decimals are never rounded; NUL is not text.
        await Assert.ThrowsAsync<ArgumentException>(() => AddAsync(customer.CustomerId, account, TransactionKind.Salary, 10m, merchant: "Starbucks"));
        await Assert.ThrowsAsync<ArgumentException>(() => AddAsync(customer.CustomerId, account, TransactionKind.Salary, 12.345m));
        await Assert.ThrowsAsync<ArgumentException>(() => AddAsync(customer.CustomerId, account, TransactionKind.Salary, 10m, "POS\0X"));
        Assert.Equal((0m, 0), await StateAsync(account));
    }

    // API-12: the Idempotency-Key.

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task The_same_key_returns_the_first_transaction_and_moves_the_balance_once()
    {
        await using var customer = await TemporaryCustomer.CreateAsync();
        var account = await customer.AddAccountAsync("TRY");

        var first = await WriteAsync(customer.CustomerId, account, TransactionKind.Salary, 100m, "maas-ekim");
        var retry = await WriteAsync(customer.CustomerId, account, TransactionKind.Salary, 100m, "maas-ekim");

        Assert.Equal((false, true), (first.Replayed, retry.Replayed));
        Assert.Equal(first.Transaction, retry.Transaction);
        Assert.Equal(IdempotentId.For(IdempotentId.Transaction, customer.CustomerId, "maas-ekim"), first.Transaction.Id);
        Assert.Equal((100m, 1), await StateAsync(account));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task The_same_key_for_a_different_event_or_account_is_a_conflict()
    {
        await using var customer = await TemporaryCustomer.CreateAsync();
        var account = await customer.AddAccountAsync("TRY");
        var other = await customer.AddAccountAsync("TRY");
        await WriteAsync(customer.CustomerId, account, TransactionKind.Salary, 100m, "maas-ekim");

        await Assert.ThrowsAsync<IdempotencyConflictException>(
            () => WriteAsync(customer.CustomerId, account, TransactionKind.Salary, 200m, "maas-ekim"));
        await Assert.ThrowsAsync<IdempotencyConflictException>(
            () => WriteAsync(customer.CustomerId, other, TransactionKind.Salary, 100m, "maas-ekim"));

        Assert.Equal((100m, 1), await StateAsync(account));
        Assert.Equal((0m, 0), await StateAsync(other));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task A_failed_attempt_is_not_remembered_and_can_be_retried_with_the_same_key()
    {
        // Decision 5-A: only a successful write binds the key.
        await using var customer = await TemporaryCustomer.CreateAsync();
        var account = await customer.AddAccountAsync("TRY");

        await Assert.ThrowsAsync<BalanceLimitException>(
            () => WriteAsync(customer.CustomerId, account, TransactionKind.Purchase, 50m, "kahve-1"));
        await AddAsync(customer.CustomerId, account, TransactionKind.Salary, 100m);
        var retry = await WriteAsync(customer.CustomerId, account, TransactionKind.Purchase, 50m, "kahve-1");

        Assert.False(retry.Replayed);
        Assert.Equal((50m, 2), await StateAsync(account));
    }

    [Theory]
    [Trait("Category", "PostgreSQL")]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task A_missing_or_invalid_key_writes_nothing(string? key)
    {
        await using var customer = await TemporaryCustomer.CreateAsync();
        var account = await customer.AddAccountAsync("TRY");

        var error = await Assert.ThrowsAsync<ArgumentException>(
            () => WriteAsync(customer.CustomerId, account, TransactionKind.Salary, 10m, key));

        Assert.Equal("idempotencyKey", error.ParamName);
        Assert.Equal((0m, 0), await StateAsync(account));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task A_cancelled_request_writes_nothing()
    {
        await using var customer = await TemporaryCustomer.CreateAsync();
        var account = await customer.AddAccountAsync("TRY");
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => AddAsync(customer.CustomerId, account, TransactionKind.Salary, 10m, cancellationToken: cancelled.Token));
        Assert.Equal((0m, 0), await StateAsync(account));
    }
}
