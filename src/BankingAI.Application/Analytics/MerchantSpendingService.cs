using BankingAI.Data;
using BankingAI.Data.Entities;
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

// ANA-10: the closed set a model chooses from. All-time purchases on the customer's personal accounts; the category is
// the latest purchase's (null: unclassified) and LastPurchaseDate is the customer's local date.
public sealed record MerchantListQuery(Guid CustomerId, Guid? AccountId = null);

public sealed record KnownMerchant(
    string Merchant, string? CategoryCode, string CategoryDisplayName, DateOnly LastPurchaseDate, int TransactionCount);

public sealed record MerchantList(IReadOnlyList<KnownMerchant> Merchants);

// Merchants: the canonical names included, in request order without repeats. A merchant without spending in the period
// is listed there but has no row in Currencies (no invented "0" row).
public sealed record MerchantGroupSpending(
    ResolvedPeriod Period, IReadOnlyList<string> Merchants, IReadOnlyList<CurrencyMerchantBreakdown> Currencies);

public sealed class MerchantSpendingService(BankingDbContext db, PeriodResolver periodResolver)
{
    public const int MaximumGroupSize = 20;

    private readonly PurchaseScope _scope = new(db, periodResolver);

    public async Task<MerchantBreakdown> GetBreakdownAsync(SpendingQuery query, CancellationToken cancellationToken = default)
    {
        var (period, purchases, _) = await _scope.ResolveAsync(query, cancellationToken);

        return new MerchantBreakdown(period, await BreakdownAsync(purchases, cancellationToken));
    }

    // One GROUP BY over the given purchases, presented like the category breakdown: exact amounts, display-only
    // percentages of the currency's total, largest first, the unclassified group last on ties.
    private static async Task<IReadOnlyList<CurrencyMerchantBreakdown>> BreakdownAsync(
        IQueryable<Transaction> purchases, CancellationToken cancellationToken)
    {
        var rows = await purchases
            .GroupBy(t => new { t.CurrencyCode, t.NormalizedMerchant })
            .Select(g => new { g.Key.CurrencyCode, g.Key.NormalizedMerchant, Amount = g.Sum(t => t.Amount), Count = g.Count() })
            .ToListAsync(cancellationToken);

        return rows
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
    }

    // The customer's own merchants of all time: the candidates of every merchant match, so another customer's
    // merchants never appear and a merchant without spending in the period is still known.
    private static Task<List<string>> KnownMerchantNamesAsync(IQueryable<Transaction> accountPurchases, CancellationToken cancellationToken) =>
        accountPurchases
            .Where(t => t.NormalizedMerchant != null)
            .Select(t => t.NormalizedMerchant!)
            .Distinct()
            .ToListAsync(cancellationToken);

    public async Task<MerchantSpendingResult> GetSpendingAsync(
        SpendingQuery query, string merchant, CancellationToken cancellationToken = default)
    {
        // Reject invalid text before touching the database.
        InputRules.Text(merchant, MerchantMatcher.MaximumLength, nameof(merchant));
        var (period, purchases, accountPurchases) = await _scope.ResolveAsync(query, cancellationToken);

        var match = MerchantMatcher.Match(merchant, await KnownMerchantNamesAsync(accountPurchases, cancellationToken));
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

    /// <summary>The customer's own merchants (ANA-10): the closed set a model may choose names from.</summary>
    public async Task<MerchantList> ListAsync(MerchantListQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var (timeZoneId, accountPurchases) = await _scope.ResolveAccountPurchasesAsync(query.CustomerId, query.AccountId, cancellationToken);

        // One customer's merchant purchases of all time, three columns each; the latest category of each merchant
        // needs the rows themselves, so they are grouped here rather than in SQL.
        var rows = await (
                from t in accountPurchases.Where(t => t.NormalizedMerchant != null)
                join c in db.TransactionCategories on t.CategoryId equals (short?)c.Id into categories
                from c in categories.DefaultIfEmpty()
                select new { Merchant = t.NormalizedMerchant!, t.OccurredAt, CategoryCode = (string?)c!.Code, CategoryName = (string?)c!.DisplayName })
            .ToListAsync(cancellationToken);

        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var merchants = rows
            .GroupBy(r => r.Merchant)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g =>
            {
                var latest = g.MaxBy(r => r.OccurredAt)!;
                return new KnownMerchant(g.Key, latest.CategoryCode, latest.CategoryName ?? AnalyticsLabels.Unclassified,
                    DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(latest.OccurredAt, timeZone).DateTime), g.Count());
            })
            .ToList();

        return new MerchantList(merchants);
    }

    /// <summary>
    /// Spending at several merchants chosen from <see cref="ListAsync"/> ("kahve" → Starbucks and Kahve Durağı). Every
    /// name must be another spelling of one of the customer's merchants; a name that is not on the list is an error,
    /// never completed or guessed.
    /// </summary>
    public async Task<MerchantGroupSpending> GetGroupSpendingAsync(
        SpendingQuery query, IReadOnlyList<string> merchants, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(merchants);
        const string field = nameof(MerchantGroupSpending.Merchants);
        InputRules.Require(merchants.Count is >= 1 and <= MaximumGroupSize, field, $"1 ile {MaximumGroupSize} arasında merchant verilmeli.");
        foreach (var name in merchants)
            InputRules.Text(name, MerchantMatcher.MaximumLength, field);

        var (period, purchases, accountPurchases) = await _scope.ResolveAsync(query, cancellationToken);
        var known = await KnownMerchantNamesAsync(accountPurchases, cancellationToken);

        // Only the exact (folded) step: the model copied these names from the list, so anything else is an error.
        var canonical = new List<string>();
        foreach (var name in merchants)
        {
            var match = MerchantMatcher.MatchExact(name, known);
            if (match.Status == MerchantMatchStatus.NotFound)
                throw new ArgumentException($"'{name}' müşterinin merchant listesinde yok.", field);
            if (match.Status == MerchantMatchStatus.Ambiguous)
                throw new ArgumentException($"'{name}' birden çok merchant'a karşılık geliyor.", field);
            // Another spelling of a name already chosen means the same merchant, not a second one.
            if (!canonical.Contains(match.Merchant!))
                canonical.Add(match.Merchant!);
        }

        // The canonical stored names reach SQL, never the model's text.
        var chosen = purchases.Where(t => t.NormalizedMerchant != null && canonical.Contains(t.NormalizedMerchant));
        return new MerchantGroupSpending(period, canonical, await BreakdownAsync(chosen, cancellationToken));
    }
}
