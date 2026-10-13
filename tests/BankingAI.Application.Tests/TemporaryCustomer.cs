using BankingAI.Data;
using BankingAI.Data.Entities;
using BankingAI.Data.Enums;
using BankingAI.TestSupport;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BankingAI.Application.Tests;

/// <summary>
/// A throwaway customer with its own accounts, committed so writes that manage their own DB transaction can be
/// tested, and deleted with everything it owns on dispose. Demo customers are never written to: a check broken
/// on purpose (or by a bug) can then only ever touch this customer's rows.
/// </summary>
internal sealed class TemporaryCustomer : IAsyncDisposable
{
    private const string FirstName = "Test";
    private const string LastName = "Application";

    private readonly CommittedRowsLock rowsLock;

    private TemporaryCustomer(CommittedRowsLock rowsLock, Guid customerId)
    {
        this.rowsLock = rowsLock;
        CustomerId = customerId;
    }

    public Guid CustomerId { get; }

    public static async Task<TemporaryCustomer> CreateAsync()
    {
        Assert.False(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BANKINGAI_CONNECTION_STRING")),
            "PostgreSQL testi için önce .env ortam değişkenlerini yükleyin.");
        // Held from before the insert until after the delete, so whole-table counts elsewhere never see these rows.
        var rowsLock = await CommittedRowsLock.SharedAsync();
        await using var db = new BankingDbContext();
        var customer = new Customer { FirstName = FirstName, LastName = LastName };
        try
        {
            await RemoveLeftoversAsync(db);
            db.Customers.Add(customer);
            await db.SaveChangesAsync();
            return new TemporaryCustomer(rowsLock, customer.Id);
        }
        catch
        {
            await DeleteAsync(db, [customer.Id]);
            await rowsLock.DisposeAsync();
            throw;
        }
    }

    /// <summary>Opens a zero-balance account for this customer and returns its id.</summary>
    public async Task<Guid> AddAccountAsync(string currencyCode = "TRY", AccountType type = AccountType.Personal)
    {
        await using var db = new BankingDbContext();
        var account = new Account
        {
            CustomerId = CustomerId,
            AccountName = $"{type} {currencyCode} test",
            AccountNumber = $"DUMMY-APPTEST-{Guid.NewGuid():N}",
            CurrencyCode = currencyCode,
            AccountType = type
        };
        db.Accounts.Add(account);
        await db.SaveChangesAsync();
        return account.Id;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await using var db = new BankingDbContext();
            await DeleteAsync(db, [CustomerId]);
        }
        finally
        {
            await rowsLock.DisposeAsync();
        }
    }

    // A run killed before dispose leaves a customer behind; only old ones are removed, so a test running in
    // parallel never loses the customer it just created.
    private static async Task RemoveLeftoversAsync(BankingDbContext db)
    {
        var staleBefore = DateTimeOffset.UtcNow.AddMinutes(-10);
        var stale = await db.Customers
            .Where(c => c.FirstName == FirstName && c.LastName == LastName && c.CreatedAt < staleBefore)
            .Select(c => c.Id)
            .ToListAsync();
        await DeleteAsync(db, stale);
    }

    private static async Task DeleteAsync(BankingDbContext db, List<Guid> customerIds)
    {
        if (customerIds.Count == 0)
            return;
        // Every reference is ON DELETE RESTRICT, so transactions go first, then accounts, then the customer. The
        // name check keeps this from ever touching a demo customer's rows.
        var owned = db.Customers.Where(c => customerIds.Contains(c.Id) && c.FirstName == FirstName && c.LastName == LastName)
            .Select(c => c.Id);
        await db.Transactions.Where(t => db.Accounts.Any(a => a.Id == t.AccountId && owned.Contains(a.CustomerId)))
            .ExecuteDeleteAsync();
        await db.Accounts.Where(a => owned.Contains(a.CustomerId)).ExecuteDeleteAsync();
        await db.Customers.Where(c => owned.Contains(c.Id)).ExecuteDeleteAsync();
    }
}
