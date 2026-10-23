using BankingAI.Application.Analytics;
using BankingAI.Data;

namespace BankingAI.Application.Customers;

/// <summary>The customer's own calendar: today's date in their time zone, and the zone's id (CHAT-08 K9-C).</summary>
public sealed record CustomerDay(DateOnly Today, string TimeZone);

/// <summary>
/// Confirms that the customer of a request still exists, for endpoints that would otherwise not touch the
/// database (the assistant), and tells their day. Controllers depend on this contract rather than on the class.
/// </summary>
public interface ICustomerCheck
{
    /// <summary>
    /// Returns the customer's day when the customer exists; "today" is the business clock's, in the customer's time zone.
    /// </summary>
    /// <exception cref="ArgumentException">The customer id is empty.</exception>
    /// <exception cref="CustomerNotFoundException">The customer does not exist (a stale token).</exception>
    Task<CustomerDay> EnsureExistsAsync(Guid customerId, CancellationToken cancellationToken = default);
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
    public async Task<CustomerDay> EnsureExistsAsync(Guid customerId, CancellationToken cancellationToken = default)
    {
        var (timeZone, _) = await _customer.ResolveAccountsAsync(customerId, accountId: null, personalOnly: false, cancellationToken);
        return new CustomerDay(periodResolver.TodayIn(timeZone), timeZone);
    }
}
