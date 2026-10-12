using System.Text.RegularExpressions;
using BankingAI.Application.Accounts;
using BankingAI.Application.Analytics;
using BankingAI.Data;
using BankingAI.Data.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static BankingAI.Application.Tests.DemoData;

namespace BankingAI.Application.Tests;

// API-07. Every test runs in a transaction that is rolled back, so the demo database keeps its accounts.
// Only the sequence moves on (PostgreSQL never rolls a sequence back), which leaves harmless gaps in "ACC-n".
public sealed class AccountWriteServiceTests
{
    private static BankingDbContext Db()
    {
        Assert.False(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BANKINGAI_CONNECTION_STRING")),
            "PostgreSQL testi için önce .env ortam değişkenlerini yükleyin.");
        return new BankingDbContext();
    }

    private static IAccountWriteService Service(BankingDbContext db)
    {
        var periods = new PeriodResolver(new FixedTimeProvider(DemoNowUtc));
        return new AccountWriteService(db, periods, new AccountReadService(db, periods));
    }

    private static long NumberOf(AccountDto account)
    {
        var match = Regex.Match(account.AccountNumber, "^ACC-([1-9][0-9]*)$");
        Assert.True(match.Success, $"Unexpected account number '{account.AccountNumber}'.");
        return long.Parse(match.Groups[1].Value);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Opens_a_personal_zero_balance_account_for_the_customer()
    {
        await using var db = Db();
        await using var scope = await db.Database.BeginTransactionAsync();

        var created = await Service(db).CreateAsync(EmptyCustomer, new NewAccount("Tatil birikimi", "EUR"));

        Assert.Equal(("Tatil birikimi", "EUR", AccountType.Personal, 0m),
            (created.AccountName, created.CurrencyCode, created.AccountType, created.Balance));
        NumberOf(created);
        // Stored for the right owner, and returned exactly as a read returns it.
        Assert.Equal(EmptyCustomer, await db.Accounts.Where(a => a.Id == created.Id).Select(a => a.CustomerId).SingleAsync());
        Assert.Equal(created, await new AccountReadService(db, new PeriodResolver(TimeProvider.System)).GetAsync(EmptyCustomer, created.Id));

        await scope.RollbackAsync();
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Each_account_gets_a_new_higher_number()
    {
        await using var db = Db();
        await using var scope = await db.Database.BeginTransactionAsync();
        var service = Service(db);

        var first = await service.CreateAsync(EmptyCustomer, new NewAccount("Bir", "TRY"));
        var second = await service.CreateAsync(EmptyCustomer, new NewAccount("Bir", "TRY")); // same name is allowed

        Assert.True(NumberOf(second) > NumberOf(first));
        Assert.NotEqual(first.Id, second.Id);

        await scope.RollbackAsync();
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Unknown_or_empty_customer_is_rejected()
    {
        await using var db = Db();
        var service = Service(db);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.CreateAsync(Guid.NewGuid(), new NewAccount("Tatil", "EUR")));
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(Guid.Empty, new NewAccount("Tatil", "EUR")));
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Theory]
    [Trait("Category", "PostgreSQL")]
    [InlineData("", "TRY")]
    [InlineData("Tatil\0", "TRY")] // NUL passes the HTTP attributes but not the account rules
    [InlineData("Tatil", "JPY")]
    public async Task Invalid_input_is_rejected_by_the_account_rules(string name, string currency)
    {
        await using var db = Db();
        await using var scope = await db.Database.BeginTransactionAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => Service(db).CreateAsync(EmptyCustomer, new NewAccount(name, currency)));
        Assert.False(await db.Accounts.AnyAsync(a => a.CustomerId == EmptyCustomer));

        await scope.RollbackAsync();
    }
}
