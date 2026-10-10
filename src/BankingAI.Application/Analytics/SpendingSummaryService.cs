using BankingAI.Data;
using Microsoft.EntityFrameworkCore;

namespace BankingAI.Application.Analytics;

// One row per currency: TRY and USD are never added together (VAL-05).
public sealed record CurrencySpending(
    string CurrencyCode, decimal TotalAmount, int TransactionCount, decimal UncategorizedAmount, int UncategorizedCount);

// An empty Currencies list means no spending in the period; no "0" row is invented.
public sealed record SpendingSummary(ResolvedPeriod Period, IReadOnlyList<CurrencySpending> Currencies);

public sealed class SpendingSummaryService(BankingDbContext db, PeriodResolver periodResolver)
{
    private readonly PurchaseScope _scope = new(db, periodResolver);

    public async Task<SpendingSummary> GetAsync(SpendingQuery query, CancellationToken cancellationToken = default)
    {
        var (period, purchases, _) = await _scope.ResolveAsync(query, cancellationToken);

        // Aggregation runs in PostgreSQL as a single GROUP BY; rows are never loaded into memory.
        var currencies = await purchases
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
