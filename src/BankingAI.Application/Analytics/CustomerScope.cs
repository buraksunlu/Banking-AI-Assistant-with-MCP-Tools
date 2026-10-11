using BankingAI.Data;
using BankingAI.Data.Enums;
using Microsoft.EntityFrameworkCore;

namespace BankingAI.Application.Analytics;

// AccountIds is an unexecuted query of the accounts in scope; TimeZone is the customer's own.
internal sealed record ScopedAccounts(ResolvedPeriod Period, string TimeZone, IQueryable<Guid> AccountIds);

// The single place for customer lookup, account authorization and period resolution. Every
// analytics and search service goes through it, so an authorization rule cannot drift apart.
internal sealed class CustomerScope(BankingDbContext db, PeriodResolver periodResolver)
{
    public async Task<ScopedAccounts> ResolveAsync(
        Guid customerId, PeriodKind period, DateOnly? from, DateOnly? to, Guid? accountId,
        bool personalOnly, CancellationToken cancellationToken)
    {
        var (timeZone, accounts) = await ResolveAccountsAsync(customerId, accountId, personalOnly, cancellationToken);
        var resolved = periodResolver.Resolve(period, timeZone, from, to);
        return new ScopedAccounts(resolved, timeZone, accounts);
    }

    // Authorization without a period, for services that resolve their own periods
    // (comparisons, monthly windows). Same rules as ResolveAsync.
    public async Task<(string TimeZone, IQueryable<Guid> AccountIds)> ResolveAccountsAsync(
        Guid customerId, Guid? accountId, bool personalOnly, CancellationToken cancellationToken)
    {
        if (customerId == Guid.Empty)
            throw new ArgumentException("Müşteri kimliği boş olamaz.", nameof(customerId));

        // The customer's own time zone decides where "this month" starts.
        var timeZone = await db.Customers.AsNoTracking()
            .Where(c => c.Id == customerId)
            .Select(c => c.TimeZone)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException("Müşteri bulunamadı.");

        if (accountId is not null)
        {
            var account = await db.Accounts.AsNoTracking()
                .Where(a => a.Id == accountId && a.CustomerId == customerId)
                .Select(a => new { a.AccountType })
                .SingleOrDefaultAsync(cancellationToken);
            // Another customer's account looks exactly like a missing one, so its existence never leaks.
            if (account is null)
                throw new KeyNotFoundException("Hesap bulunamadı.");
            if (personalOnly && account.AccountType != AccountType.Personal)
                throw new ArgumentException("Harcama analizi yalnızca şahsi hesaplar içindir.", nameof(accountId));
        }

        var accounts = db.Accounts.Where(a => a.CustomerId == customerId);
        if (personalOnly)
            accounts = accounts.Where(a => a.AccountType == AccountType.Personal);
        if (accountId is not null)
            accounts = accounts.Where(a => a.Id == accountId);

        return (timeZone, accounts.Select(a => a.Id));
    }
}
