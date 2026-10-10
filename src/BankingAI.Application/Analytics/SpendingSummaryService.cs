using BankingAI.Data;
using BankingAI.Data.Enums;
using Microsoft.EntityFrameworkCore;

namespace BankingAI.Application.Analytics;

// CustomerId comes from the trusted caller context (API session), never from a model argument.
public sealed record SpendingSummaryRequest(
    Guid CustomerId, PeriodKind Period, DateOnly? From = null, DateOnly? To = null, Guid? AccountId = null);

// One row per currency: TRY and USD are never added together (VAL-05).
public sealed record CurrencySpending(
    string CurrencyCode, decimal TotalAmount, int TransactionCount, decimal UncategorizedAmount, int UncategorizedCount);

// An empty Currencies list means no spending in the period; no "0" row is invented.
public sealed record SpendingSummary(ResolvedPeriod Period, IReadOnlyList<CurrencySpending> Currencies);

public sealed class SpendingSummaryService(BankingDbContext db, PeriodResolver periodResolver)
{
    public async Task<SpendingSummary> GetAsync(SpendingSummaryRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.CustomerId == Guid.Empty)
            throw new ArgumentException("Müşteri kimliği boş olamaz.", nameof(request));

        // The customer's own time zone decides where "this month" starts.
        var timeZone = await db.Customers.AsNoTracking()
            .Where(c => c.Id == request.CustomerId)
            .Select(c => c.TimeZone)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException("Müşteri bulunamadı.");

        var accountId = request.AccountId;
        if (accountId is not null)
        {
            var account = await db.Accounts.AsNoTracking()
                .Where(a => a.Id == accountId && a.CustomerId == request.CustomerId)
                .Select(a => new { a.AccountType })
                .SingleOrDefaultAsync(cancellationToken);
            // Another customer's account looks exactly like a missing one, so its existence never leaks.
            if (account is null)
                throw new KeyNotFoundException("Hesap bulunamadı.");
            if (account.AccountType != AccountType.Personal)
                throw new ArgumentException("Harcama özeti yalnızca şahsi hesaplar içindir.", nameof(request));
        }

        var period = periodResolver.Resolve(request.Period, timeZone, request.From, request.To);

        var accounts = db.Accounts
            .Where(a => a.CustomerId == request.CustomerId && a.AccountType == AccountType.Personal)
            .Where(a => accountId == null || a.Id == accountId)
            .Select(a => a.Id);

        // Spending is Kind = Purchase (VAL-05): transfers, sales, salaries and opening funds are excluded.
        // Aggregation runs in PostgreSQL as a single GROUP BY; rows are never loaded into memory.
        var currencies = await db.Transactions.AsNoTracking()
            .Where(t => t.Kind == TransactionKind.Purchase
                && t.OccurredAt >= period.StartUtc && t.OccurredAt < period.EndUtc
                && accounts.Contains(t.AccountId))
            .GroupBy(t => t.CurrencyCode)
            // Order by the group key before projecting: EF cannot translate a sort on a
            // property of a record built through its constructor.
            .OrderBy(g => g.Key)
            .Select(g => new CurrencySpending(
                g.Key,
                g.Sum(t => t.Amount),
                g.Count(),
                g.Sum(t => t.CategoryId == null ? t.Amount : 0m),
                g.Count(t => t.CategoryId == null)))
            .ToListAsync(cancellationToken);

        return new SpendingSummary(period, currencies);
    }
}
