using BankingAI.Application.Accounts;
using BankingAI.Application.Analytics;
using BankingAI.Data;
using BankingAI.Data.Enums;
using Xunit;
using static BankingAI.Application.Tests.DemoData;

namespace BankingAI.Application.Tests;

// API-04. Expected values are the hand-computed fixtures of docs/synthetic-seed.md. Data tests running in
// parallel may open temporary accounts for Ada and Deniz, so exact lists are checked on Ege only.
public sealed class AccountReadServiceTests
{
    private static BankingDbContext Db()
    {
        Assert.False(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BANKINGAI_CONNECTION_STRING")),
            "PostgreSQL testi için önce .env ortam değişkenlerini yükleyin.");
        return new BankingDbContext();
    }

    // Tests use the contract, as the controllers will.
    private static IAccountReadService Service(BankingDbContext db) =>
        new AccountReadService(db, new PeriodResolver(new FixedTimeProvider(DemoNowUtc)));

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task List_returns_every_account_of_the_customer_oldest_first()
    {
        await using var db = Db();

        var accounts = await Service(db).ListAsync(Ege);

        Assert.Equal(
            [
                (EgeEurAccount, "DUMMY-EGE-EUR-001", "EUR", AccountType.Personal, 1000m),
                (EgeTryAccount, "DUMMY-EGE-TRY-001", "TRY", AccountType.Personal, 273380m)
            ],
            accounts.Select(a => (a.Id, a.AccountNumber, a.CurrencyCode, a.AccountType, a.Balance)));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task List_orders_by_opening_date_before_account_number()
    {
        await using var db = Db();

        var accounts = await Service(db).ListAsync(Deniz);

        // Opened 9 Oct (TRY) and 10 Oct (shop). By account number alone "SHOP" would come before "TRY".
        // Temporary accounts of parallel tests are filtered out; they are opened today and sort last anyway.
        Assert.Equal(["DUMMY-DENIZ-TRY-001", "DUMMY-DENIZ-SHOP-001"],
            accounts.Where(a => a.Id == DenizTryAccount || a.Id == DenizShopAccount).Select(a => a.AccountNumber));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task List_orders_accounts_opened_together_by_account_number()
    {
        await using var db = Db();
        Guid[] demo = [AdaTryAccount, AdaUsdAccount, AdaEmptyAccount];

        var accounts = await Service(db).ListAsync(Ada);

        // Ada's three demo accounts share one CreatedAt, so only the account number decides their order.
        Assert.Equal(["DUMMY-ADA-EMPTY-001", "DUMMY-ADA-TRY-001", "DUMMY-ADA-USD-001"],
            accounts.Where(a => demo.Contains(a.Id)).Select(a => a.AccountNumber));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task List_is_empty_for_a_customer_without_accounts()
    {
        await using var db = Db();

        Assert.Empty(await Service(db).ListAsync(EmptyCustomer));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Get_returns_one_account_of_the_customer()
    {
        await using var db = Db();

        var account = await Service(db).GetAsync(Ada, AdaTryAccount);

        Assert.Equal(("Ada günlük TRY", "DUMMY-ADA-TRY-001", "TRY", AccountType.Personal, 577490.18m),
            (account.AccountName, account.AccountNumber, account.CurrencyCode, account.AccountType, account.Balance));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Another_customers_account_looks_exactly_like_a_missing_one()
    {
        await using var db = Db();
        var service = Service(db);

        var foreign = await Assert.ThrowsAsync<KeyNotFoundException>(() => service.GetAsync(Ada, DenizTryAccount));
        var missing = await Assert.ThrowsAsync<KeyNotFoundException>(() => service.GetAsync(Ada, Guid.NewGuid()));

        Assert.Equal(missing.Message, foreign.Message);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Unknown_customer_is_not_found()
    {
        await using var db = Db();
        var service = Service(db);

        await Assert.ThrowsAsync<CustomerNotFoundException>(() => service.ListAsync(Guid.NewGuid()));
        await Assert.ThrowsAsync<CustomerNotFoundException>(() => service.GetAsync(Guid.NewGuid(), AdaTryAccount));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Empty_customer_id_is_rejected()
    {
        await using var db = Db();
        var service = Service(db);

        await Assert.ThrowsAsync<ArgumentException>(() => service.ListAsync(Guid.Empty));
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetAsync(Guid.Empty, AdaTryAccount));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task A_cancelled_request_stops_the_query()
    {
        await using var db = Db();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service(db).ListAsync(Ada, cancelled.Token));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Reading_tracks_no_entities()
    {
        await using var db = Db();
        var service = Service(db);

        await service.ListAsync(Ada);
        await service.GetAsync(Ada, AdaTryAccount);

        // DTO projections are never tracked, so a read leaves nothing for SaveChanges to inspect.
        Assert.Empty(db.ChangeTracker.Entries());
    }
}
