using BankingAI.Application.Analytics;
using BankingAI.Data;
using BankingAI.Data.Entities;
using BankingAI.Data.Enums;
using Microsoft.EntityFrameworkCore;

namespace BankingAI.Application.Accounts;

/// <summary>
/// What a customer may choose when opening an account. Everything else is decided by the backend.
/// </summary>
public sealed record NewAccount(string AccountName, string CurrencyCode);

/// <summary>
/// Opens accounts for a customer. Controllers depend on this contract rather than on the class.
/// </summary>
public interface IAccountWriteService
{
    /// <summary>
    /// Opens a personal account with a zero balance and a new "ACC-n" number, and returns it exactly as
    /// <see cref="IAccountReadService.GetAsync"/> would.
    /// </summary>
    /// <exception cref="ArgumentException">Empty customer id, or a name or currency the account rules reject.</exception>
    /// <exception cref="KeyNotFoundException">The customer does not exist.</exception>
    Task<AccountDto> CreateAsync(Guid customerId, NewAccount account, CancellationToken cancellationToken = default);
}

/// <summary>
/// <see cref="IAccountWriteService"/> on PostgreSQL.
/// </summary>
public sealed class AccountWriteService(BankingDbContext db, PeriodResolver periodResolver, IAccountReadService reader)
    : IAccountWriteService
{
    // Customer lookup goes through the same place as every read, so "customer not found" means the same everywhere.
    private readonly CustomerScope _customer = new(db, periodResolver);

    /// <inheritdoc />
    public async Task<AccountDto> CreateAsync(Guid customerId, NewAccount account, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        await _customer.ResolveAccountsAsync(customerId, accountId: null, personalOnly: false, cancellationToken);

        // The sequence hands out each number exactly once, even to concurrent requests, so no retry or lock
        // is needed. Taking it before the insert keeps the entity complete for the C# validation.
        var number = await db.Database
            .SqlQueryRaw<long>($"SELECT nextval('\"{BankingDbContext.AccountNumberSequence}\"') AS \"Value\"")
            .SingleAsync(cancellationToken);

        // Type, balance and creation time are never taken from the caller: always Personal, always 0, and
        // CreatedAt is set to server time by SaveChanges. Name and currency are validated there as well.
        var entity = new Account
        {
            CustomerId = customerId,
            AccountName = account.AccountName,
            AccountNumber = $"ACC-{number}",
            CurrencyCode = account.CurrencyCode,
            AccountType = AccountType.Personal
        };
        db.Accounts.Add(entity);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (ArgumentException)
        {
            // Leave the context clean for the caller; the rejected account must not be saved by a later call.
            db.Entry(entity).State = EntityState.Detached;
            throw;
        }

        return await reader.GetAsync(customerId, entity.Id, cancellationToken);
    }
}
