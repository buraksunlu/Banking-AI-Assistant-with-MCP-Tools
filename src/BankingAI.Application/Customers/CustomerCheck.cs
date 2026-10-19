using BankingAI.Application.Analytics;
using BankingAI.Data;

namespace BankingAI.Application.Customers;

/// <summary>
/// Confirms that the customer of a request still exists, for endpoints that would otherwise not touch the
/// database (the assistant). Controllers depend on this contract rather than on the class.
/// </summary>
public interface ICustomerCheck
{
    /// <summary>
    /// Returns when the customer exists.
    /// </summary>
    /// <exception cref="ArgumentException">The customer id is empty.</exception>
    /// <exception cref="CustomerNotFoundException">The customer does not exist (a stale token).</exception>
    Task EnsureExistsAsync(Guid customerId, CancellationToken cancellationToken = default);
}

/// <summary>
/// <see cref="ICustomerCheck"/> on PostgreSQL.
/// </summary>
public sealed class CustomerCheck(BankingDbContext db, PeriodResolver periodResolver) : ICustomerCheck
{
    // Only CustomerScope's customer lookup is used, but going through it keeps the "missing customer" rule in the
    // one place every analytics and account service shares.
    private readonly CustomerScope _customer = new(db, periodResolver);

    /// <inheritdoc />
    public async Task EnsureExistsAsync(Guid customerId, CancellationToken cancellationToken = default) =>
        await _customer.ResolveAccountsAsync(customerId, accountId: null, personalOnly: false, cancellationToken);
}
