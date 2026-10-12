using BankingAI.Application.Analytics;
using BankingAI.Data;
using Microsoft.EntityFrameworkCore;

namespace BankingAI.Application.Accounts;

/// <summary>
/// Reads the accounts of one customer. Controllers depend on this contract rather than on the class.
/// </summary>
public interface IAccountReadService
{
    /// <summary>
    /// All accounts of the customer, oldest first; accounts opened at the same moment are ordered by account
    /// number. A customer without accounts gets an empty list.
    /// </summary>
    /// <exception cref="ArgumentException">The customer id is empty.</exception>
    /// <exception cref="KeyNotFoundException">The customer does not exist.</exception>
    Task<IReadOnlyList<AccountDto>> ListAsync(Guid customerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// One account of the customer.
    /// </summary>
    /// <exception cref="ArgumentException">The customer id is empty.</exception>
    /// <exception cref="KeyNotFoundException">
    /// The customer does not exist, or the account does not exist or belongs to someone else; the last two are
    /// indistinguishable so another customer's account never leaks its existence.
    /// </exception>
    Task<AccountDto> GetAsync(Guid customerId, Guid accountId, CancellationToken cancellationToken = default);
}

/// <summary>
/// <see cref="IAccountReadService"/> on PostgreSQL. Ownership is decided by <see cref="CustomerScope"/>, the
/// same rule every analytics service uses.
/// </summary>
public sealed class AccountReadService(BankingDbContext db, PeriodResolver periodResolver) : IAccountReadService
{
    // CustomerScope needs the period resolver for its analytics entry point; account reads only use its
    // ownership check, but sharing it keeps the authorization rule in one place.
    private readonly CustomerScope _customer = new(db, periodResolver);

    /// <inheritdoc />
    public async Task<IReadOnlyList<AccountDto>> ListAsync(Guid customerId, CancellationToken cancellationToken = default)
    {
        var (_, accountIds) = await _customer.ResolveAccountsAsync(customerId, accountId: null, personalOnly: false, cancellationToken);

        // Ordered before the projection: EF cannot sort on a record built through its constructor. The
        // account number breaks ties, because accounts seeded together share the same CreatedAt.
        return await db.Accounts
            .Where(a => accountIds.Contains(a.Id))
            .OrderBy(a => a.CreatedAt)
            .ThenBy(a => a.AccountNumber)
            .Select(AccountDto.Projection)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<AccountDto> GetAsync(Guid customerId, Guid accountId, CancellationToken cancellationToken = default)
    {
        // Throws KeyNotFoundException for a missing or foreign account before anything is read.
        var (_, accountIds) = await _customer.ResolveAccountsAsync(customerId, accountId, personalOnly: false, cancellationToken);

        return await db.Accounts
            .Where(a => accountIds.Contains(a.Id))
            .Select(AccountDto.Projection)
            .SingleAsync(cancellationToken);
    }
}
