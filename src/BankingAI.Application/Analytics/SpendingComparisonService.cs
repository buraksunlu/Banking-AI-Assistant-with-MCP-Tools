using BankingAI.Data;
using BankingAI.Data.Validation;
using Microsoft.EntityFrameworkCore;

namespace BankingAI.Application.Analytics;

// Default Kind (PreviousFullMonth) is the user's meaning of "geçen aya göre": the last complete
// month against the month before it. Both periods use the spending scope (personal, Purchase).
public sealed record SpendingComparisonQuery(
    Guid CustomerId, ComparisonKind Kind = ComparisonKind.PreviousFullMonth,
    DateOnly? CurrentFrom = null, DateOnly? CurrentTo = null, DateOnly? PreviousFrom = null, DateOnly? PreviousTo = null,
    Guid? AccountId = null);

// Change = CurrentAmount - PreviousAmount (signed).
public sealed record CategoryChange(
    string? CategoryCode, string DisplayName, decimal CurrentAmount, decimal PreviousAmount, decimal Change);

// PercentageChange is null when PreviousAmount is 0: a growth rate from zero is never invented.
public sealed record CurrencyComparison(
    string CurrencyCode, decimal CurrentAmount, int CurrentCount, decimal PreviousAmount, int PreviousCount,
    decimal Change, decimal? PercentageChange, IReadOnlyList<CategoryChange> Categories);

public sealed record SpendingComparison(ResolvedPeriod Current, ResolvedPeriod Previous, IReadOnlyList<CurrencyComparison> Currencies);

// CategoryCode or Merchant (not both) narrows the series; Months is the number of complete months.
public sealed record SpendingTrendQuery(
    Guid CustomerId, int Months = 6, string? CategoryCode = null, string? Merchant = null, Guid? AccountId = null);

public sealed record MonthlySpending(int Year, int Month, bool IsPartial, decimal Amount, int TransactionCount);

// Every window is present; a month without spending is a real 0, not an omitted month.
public sealed record CurrencyTrend(string CurrencyCode, IReadOnlyList<MonthlySpending> Months);

public sealed record SpendingTrend(IReadOnlyList<MonthWindow> Months, MerchantMatch? MerchantMatch, IReadOnlyList<CurrencyTrend> Currencies);

public sealed class SpendingComparisonService(BankingDbContext db, PeriodResolver periodResolver)
{
    private readonly PurchaseScope _scope = new(db, periodResolver);

    public async Task<SpendingComparison> CompareAsync(SpendingComparisonQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var (timeZone, purchases) = await _scope.ResolveAccountPurchasesAsync(query.CustomerId, query.AccountId, cancellationToken);
        var (current, previous) = periodResolver.ResolveComparison(
            query.Kind, timeZone, query.CurrentFrom, query.CurrentTo, query.PreviousFrom, query.PreviousTo);

        // Both periods in one aggregation: rows are filtered to either range and each sum picks its
        // own range with CASE WHEN. The resolver guarantees the ranges never overlap.
        var (cs, ce, ps, pe) = (current.StartUtc, current.EndUtc, previous.StartUtc, previous.EndUtc);
        var rows = await (
                from t in purchases.Where(t => (t.OccurredAt >= cs && t.OccurredAt < ce) || (t.OccurredAt >= ps && t.OccurredAt < pe))
                join c in db.TransactionCategories on t.CategoryId equals (short?)c.Id into categories
                from c in categories.DefaultIfEmpty()
                group t by new { t.CurrencyCode, Code = (string?)c!.Code, Name = (string?)c!.DisplayName } into g
                select new
                {
                    g.Key.CurrencyCode, g.Key.Code, g.Key.Name,
                    Current = g.Sum(t => t.OccurredAt >= cs && t.OccurredAt < ce ? t.Amount : 0m),
                    CurrentCount = g.Count(t => t.OccurredAt >= cs && t.OccurredAt < ce),
                    Previous = g.Sum(t => t.OccurredAt >= ps && t.OccurredAt < pe ? t.Amount : 0m),
                    PreviousCount = g.Count(t => t.OccurredAt >= ps && t.OccurredAt < pe)
                })
            .ToListAsync(cancellationToken);

        var currencies = rows
            .GroupBy(r => r.CurrencyCode)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g =>
            {
                var currentTotal = g.Sum(r => r.Current);
                var previousTotal = g.Sum(r => r.Previous);
                var change = currentTotal - previousTotal;
                var categories = g
                    .Select(r => new CategoryChange(r.Code, r.Name ?? AnalyticsLabels.Unclassified, r.Current, r.Previous, r.Current - r.Previous))
                    // The biggest driver first, whether it pushed spending up or down.
                    .OrderByDescending(c => Math.Abs(c.Change))
                    .ThenBy(c => c.CategoryCode is null)
                    .ThenBy(c => c.CategoryCode, StringComparer.Ordinal)
                    .ToList();
                return new CurrencyComparison(
                    g.Key, currentTotal, g.Sum(r => r.CurrentCount), previousTotal, g.Sum(r => r.PreviousCount), change,
                    previousTotal == 0m ? null : Math.Round(change / previousTotal * 100m, 2, MidpointRounding.AwayFromZero),
                    categories);
            })
            .ToList();

        return new SpendingComparison(current, previous, currencies);
    }

    public async Task<SpendingTrend> GetMonthlyTrendAsync(SpendingTrendQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        InputRules.Require(query.Months is >= 1 and <= PeriodResolver.MaximumTrendMonths,
            nameof(query.Months), $"1 ile {PeriodResolver.MaximumTrendMonths} arasında olmalı.");
        InputRules.Require(query.CategoryCode is null || query.Merchant is null,
            nameof(query.Merchant), "Kategori ve merchant filtresi birlikte kullanılamaz.");
        if (query.CategoryCode is not null)
            InputRules.Text(query.CategoryCode, 50, nameof(query.CategoryCode));
        if (query.Merchant is not null)
            InputRules.Text(query.Merchant, MerchantMatcher.MaximumLength, nameof(query.Merchant));

        var (timeZone, purchases) = await _scope.ResolveAccountPurchasesAsync(query.CustomerId, query.AccountId, cancellationToken);
        var windows = periodResolver.ResolveMonthlyWindows(query.Months, timeZone);

        if (query.CategoryCode is { } code)
        {
            var categoryId = (await CategoryLookup.FindAsync(db, code, cancellationToken)).Id;
            purchases = purchases.Where(t => t.CategoryId == categoryId);
        }

        MerchantMatch? merchantMatch = null;
        if (query.Merchant is { } merchant)
        {
            var knownMerchants = await purchases
                .Where(t => t.NormalizedMerchant != null)
                .Select(t => t.NormalizedMerchant!)
                .Distinct()
                .ToListAsync(cancellationToken);
            merchantMatch = MerchantMatcher.Match(merchant, knownMerchants);
            if (merchantMatch.Status != MerchantMatchStatus.Matched)
                return new SpendingTrend(windows, merchantMatch, []);
            var canonical = merchantMatch.Merchant!;
            purchases = purchases.Where(t => t.NormalizedMerchant == canonical);
        }

        // Npgsql 10 cannot translate a DateTimeOffset time-zone conversion, so the local month of a
        // row cannot be grouped in SQL with LINQ. Only three columns of the already filtered rows
        // (one customer, personal accounts, at most 25 months) are read and bucketed here by the
        // windows' exact UTC bounds. Revisit with AT TIME ZONE raw SQL if PERF-02 shows a need.
        var (start, end) = (windows[0].Period.StartUtc, windows[^1].Period.EndUtc);
        var rows = await purchases
            .Where(t => t.OccurredAt >= start && t.OccurredAt < end)
            .Select(t => new { t.CurrencyCode, t.OccurredAt, t.Amount })
            .ToListAsync(cancellationToken);

        var currencies = rows
            .GroupBy(r => r.CurrencyCode)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new CurrencyTrend(g.Key, windows
                .Select(w =>
                {
                    var inWindow = g.Where(r => r.OccurredAt >= w.Period.StartUtc && r.OccurredAt < w.Period.EndUtc).ToList();
                    return new MonthlySpending(w.Year, w.Month, w.IsPartial, inWindow.Sum(r => r.Amount), inWindow.Count);
                })
                .ToList()))
            .ToList();

        return new SpendingTrend(windows, merchantMatch, currencies);
    }
}
