using BankingAI.Data;
using BankingAI.Data.Entities;
using BankingAI.Data.Enums;
using Microsoft.EntityFrameworkCore;

namespace BankingAI.Application.Analytics;

// Shared input of every spending analysis. CustomerId comes from the trusted caller
// context (API session), never from a model argument.
public sealed record SpendingQuery(
    Guid CustomerId, PeriodKind Period, DateOnly? From = null, DateOnly? To = null, Guid? AccountId = null);

// One place for the rules every spending analysis must share: customer lookup, account
// authorization, period resolution and the Purchase-only base query. Keeping them here means
// an authorization fix can never land in one service and be forgotten in another.
internal sealed class PurchaseScope(BankingDbContext db, PeriodResolver periodResolver)
{
    public async Task<(ResolvedPeriod Period, IQueryable<Transaction> Purchases)> ResolveAsync(
        SpendingQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.CustomerId == Guid.Empty)
            throw new ArgumentException("Müşteri kimliği boş olamaz.", nameof(query));

        // The customer's own time zone decides where "this month" starts.
        var timeZone = await db.Customers.AsNoTracking()
            .Where(c => c.Id == query.CustomerId)
            .Select(c => c.TimeZone)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException("Müşteri bulunamadı.");

        var accountId = query.AccountId;
        if (accountId is not null)
        {
            var account = await db.Accounts.AsNoTracking()
                .Where(a => a.Id == accountId && a.CustomerId == query.CustomerId)
                .Select(a => new { a.AccountType })
                .SingleOrDefaultAsync(cancellationToken);
            // Another customer's account looks exactly like a missing one, so its existence never leaks.
            if (account is null)
                throw new KeyNotFoundException("Hesap bulunamadı.");
            if (account.AccountType != AccountType.Personal)
                throw new ArgumentException("Harcama analizi yalnızca şahsi hesaplar içindir.", nameof(query));
        }

        var period = periodResolver.Resolve(query.Period, timeZone, query.From, query.To);

        var accounts = db.Accounts
            .Where(a => a.CustomerId == query.CustomerId && a.AccountType == AccountType.Personal)
            .Where(a => accountId == null || a.Id == accountId)
            .Select(a => a.Id);

        // Spending is Kind = Purchase (VAL-05): transfers, sales, salaries and opening funds are excluded.
        // The query is returned unexecuted so each service composes its own single aggregation.
        var purchases = db.Transactions.AsNoTracking()
            .Where(t => t.Kind == TransactionKind.Purchase
                && t.OccurredAt >= period.StartUtc && t.OccurredAt < period.EndUtc
                && accounts.Contains(t.AccountId));

        return (period, purchases);
    }
}
