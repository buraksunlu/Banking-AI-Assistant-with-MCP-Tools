using BankingAI.Data;
using BankingAI.Data.Entities;
using BankingAI.Data.Enums;
using Microsoft.EntityFrameworkCore;

namespace BankingAI.Application.Analytics;

// Shared input of every spending analysis. CustomerId comes from the trusted caller
// context (API session), never from a model argument.
public sealed record SpendingQuery(
    Guid CustomerId, PeriodKind Period, DateOnly? From = null, DateOnly? To = null, Guid? AccountId = null);

// Purchases: the period's Purchase rows. AccountPurchases: the same accounts' Purchase rows of all
// time, used to recognise names such as merchants the customer has ever used.
internal sealed record ScopedPurchases(
    ResolvedPeriod Period, IQueryable<Transaction> Purchases, IQueryable<Transaction> AccountPurchases)
{
    // Not positional, so existing three-part deconstruction stays valid.
    public required string TimeZone { get; init; }
}

// Spending analyses share CustomerScope's rules plus two of their own: personal accounts only
// and Kind = Purchase (VAL-05).
internal sealed class PurchaseScope(BankingDbContext db, PeriodResolver periodResolver)
{
    private readonly CustomerScope _customer = new(db, periodResolver);

    public async Task<ScopedPurchases> ResolveAsync(SpendingQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var (period, timeZone, accounts) = await _customer.ResolveAsync(
            query.CustomerId, query.Period, query.From, query.To, query.AccountId, personalOnly: true, cancellationToken);

        // Transfers, sales, salaries and opening funds are excluded. Queries are returned
        // unexecuted so each service composes its own single aggregation.
        var accountPurchases = db.Transactions.AsNoTracking()
            .Where(t => t.Kind == TransactionKind.Purchase && accounts.Contains(t.AccountId));
        var purchases = accountPurchases
            .Where(t => t.OccurredAt >= period.StartUtc && t.OccurredAt < period.EndUtc);

        return new ScopedPurchases(period, purchases, accountPurchases) { TimeZone = timeZone };
    }
}
