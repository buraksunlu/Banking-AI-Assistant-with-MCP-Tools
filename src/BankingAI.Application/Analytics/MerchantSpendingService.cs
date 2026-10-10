using BankingAI.Data;
using BankingAI.Data.Validation;
using Microsoft.EntityFrameworkCore;

namespace BankingAI.Application.Analytics;

// Merchant is null for purchases without a NormalizedMerchant; DisplayName is then "Sınıflandırılmamış".
public sealed record MerchantSpending(
    string? Merchant, string DisplayName, decimal Amount, int TransactionCount, decimal Percentage);

public sealed record CurrencyMerchantBreakdown(
    string CurrencyCode, decimal TotalAmount, IReadOnlyList<MerchantSpending> Merchants);

public sealed record MerchantBreakdown(ResolvedPeriod Period, IReadOnlyList<CurrencyMerchantBreakdown> Currencies);

public sealed record MerchantCurrencyTotal(string CurrencyCode, decimal TotalAmount, int TransactionCount);

// Currencies is empty when the match is Ambiguous/NotFound, or when a matched merchant has no
// purchase in the period ("known merchant, no spending" differs from "no such merchant").
public sealed record MerchantSpendingResult(
    ResolvedPeriod Period, MerchantMatch Match, IReadOnlyList<MerchantCurrencyTotal> Currencies);

public sealed class MerchantSpendingService(BankingDbContext db, PeriodResolver periodResolver)
{
    private readonly PurchaseScope _scope = new(db, periodResolver);

    public async Task<MerchantBreakdown> GetBreakdownAsync(SpendingQuery query, CancellationToken cancellationToken = default)
    {
        var (period, purchases, _) = await _scope.ResolveAsync(query, cancellationToken);

        var rows = await purchases
            .GroupBy(t => new { t.CurrencyCode, t.NormalizedMerchant })
            .Select(g => new { g.Key.CurrencyCode, g.Key.NormalizedMerchant, Amount = g.Sum(t => t.Amount), Count = g.Count() })
            .ToListAsync(cancellationToken);

        // Same presentation rules as the category breakdown: exact amounts, display-only percentages,
        // largest first, the unclassified group last on ties.
        var currencies = rows
            .GroupBy(r => r.CurrencyCode)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g =>
            {
                var total = g.Sum(r => r.Amount);
                var merchants = g
                    .Select(r => new MerchantSpending(
                        r.NormalizedMerchant, r.NormalizedMerchant ?? AnalyticsLabels.Unclassified, r.Amount, r.Count,
                        Math.Round(r.Amount / total * 100m, 2, MidpointRounding.AwayFromZero)))
                    .OrderByDescending(m => m.Amount)
                    .ThenBy(m => m.Merchant is null)
                    .ThenBy(m => m.Merchant, StringComparer.Ordinal)
                    .ToList();
                return new CurrencyMerchantBreakdown(g.Key, total, merchants);
            })
            .ToList();

        return new MerchantBreakdown(period, currencies);
    }

    public async Task<MerchantSpendingResult> GetSpendingAsync(
        SpendingQuery query, string merchant, CancellationToken cancellationToken = default)
    {
        // Reject invalid text before touching the database.
        InputRules.Text(merchant, MerchantMatcher.MaximumLength, nameof(merchant));
        var (period, purchases, accountPurchases) = await _scope.ResolveAsync(query, cancellationToken);

        // Candidates come only from this customer's own purchase history (all time), so another
        // customer's merchants never appear and a merchant without spending in the period is still known.
        var knownMerchants = await accountPurchases
            .Where(t => t.NormalizedMerchant != null)
            .Select(t => t.NormalizedMerchant!)
            .Distinct()
            .ToListAsync(cancellationToken);
        var match = MerchantMatcher.Match(merchant, knownMerchants);
        if (match.Status != MerchantMatchStatus.Matched)
            return new MerchantSpendingResult(period, match, []);

        // The canonical stored name is used for filtering; the model's raw text never reaches SQL.
        var canonical = match.Merchant!;
        var currencies = await purchases
            .Where(t => t.NormalizedMerchant == canonical)
            .GroupBy(t => t.CurrencyCode)
            .OrderBy(g => g.Key)
            .Select(g => new MerchantCurrencyTotal(g.Key, g.Sum(t => t.Amount), g.Count()))
            .ToListAsync(cancellationToken);

        return new MerchantSpendingResult(period, match, currencies);
    }
}
