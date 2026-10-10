using BankingAI.Data;
using Microsoft.EntityFrameworkCore;

namespace BankingAI.Application.Analytics;

// CategoryCode is null for the unclassified group (CategoryId null), which is distinct from OTHER.
public sealed record CategorySpending(
    string? CategoryCode, string DisplayName, decimal Amount, int TransactionCount, decimal Percentage);

public sealed record CurrencyCategoryBreakdown(
    string CurrencyCode, decimal TotalAmount, IReadOnlyList<CategorySpending> Categories);

// An empty Currencies list means no spending in the period.
public sealed record CategoryBreakdown(ResolvedPeriod Period, IReadOnlyList<CurrencyCategoryBreakdown> Currencies);

public sealed class CategoryBreakdownService(BankingDbContext db, PeriodResolver periodResolver)
{
    private readonly PurchaseScope _scope = new(db, periodResolver);

    public async Task<CategoryBreakdown> GetAsync(SpendingQuery query, CancellationToken cancellationToken = default)
    {
        var (period, purchases, _) = await _scope.ResolveAsync(query, cancellationToken);

        // One aggregation query. LEFT JOIN keeps unclassified purchases, whose category columns are NULL.
        var rows = await (
                from t in purchases
                join c in db.TransactionCategories on t.CategoryId equals (short?)c.Id into categories
                from c in categories.DefaultIfEmpty()
                group t by new { t.CurrencyCode, Code = (string?)c!.Code, Name = (string?)c!.DisplayName } into g
                select new { g.Key.CurrencyCode, g.Key.Code, g.Key.Name, Amount = g.Sum(t => t.Amount), Count = g.Count() })
            .ToListAsync(cancellationToken);

        // Percentages and ordering run on the few aggregated rows. Amounts stay exact; only the
        // percentage is rounded for display and is never adjusted to force a 100 total.
        var currencies = rows
            .GroupBy(r => r.CurrencyCode)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g =>
            {
                var total = g.Sum(r => r.Amount);
                var categories = g
                    .Select(r => new CategorySpending(
                        r.Code, r.Name ?? AnalyticsLabels.Unclassified, r.Amount, r.Count,
                        Math.Round(r.Amount / total * 100m, 2, MidpointRounding.AwayFromZero)))
                    // Largest first; ties are ordered by code with the unclassified group last.
                    .OrderByDescending(c => c.Amount)
                    .ThenBy(c => c.CategoryCode is null)
                    .ThenBy(c => c.CategoryCode, StringComparer.Ordinal)
                    .ToList();
                return new CurrencyCategoryBreakdown(g.Key, total, categories);
            })
            .ToList();

        return new CategoryBreakdown(period, currencies);
    }
}
