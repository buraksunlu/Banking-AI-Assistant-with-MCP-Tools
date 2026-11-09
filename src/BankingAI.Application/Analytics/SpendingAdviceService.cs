using BankingAI.Data;
using Microsoft.EntityFrameworkCore;

namespace BankingAI.Application.Analytics;

/// <summary>Input of <see cref="SpendingAdviceService"/> (INC-02): one complete month, as in <see cref="IncomeShareQuery"/>.</summary>
public sealed record SpendingAdviceQuery(Guid CustomerId, PeriodKind Period, DateOnly? From = null, DateOnly? To = null);

/// <summary>
/// Result of <see cref="SpendingAdviceService"/>: the month, the income window of INC-01, the usual months before the month
/// (<c>UsualFrom</c>–<c>UsualTo</c>), the savings window ending with the month, the thresholds and one row per currency with
/// spending in the month.
/// </summary>
public sealed record SpendingAdvice(
    ResolvedPeriod Period, DateOnly IncomeFrom, DateOnly IncomeTo, DateOnly UsualFrom, DateOnly UsualTo, DateOnly SavingsFrom,
    DateOnly SavingsTo, AdviceThresholds Thresholds, string Method, IReadOnlyList<CurrencyAdvice> Currencies);

/// <summary>
/// "Where could I save?" (INC-02): a complete month split into essential, discretionary and unknown spending, with at most two
/// pieces of rule-based advice. Income figures and categories come from <see cref="IncomeShareService"/>; the rules are
/// <see cref="SpendingAdviceRules"/>. Not investment advice; computed on request and never stored (K9-A).
/// </summary>
public sealed class SpendingAdviceService(BankingDbContext db, PeriodResolver periodResolver, IncomeShareService incomeShares)
{
    /// <summary>The assumption written into every result.</summary>
    public const string Method = "RuleBasedAdvice";

    /// <summary>Groups and advice of one complete month.</summary>
    /// <exception cref="ArgumentException">The period is not one complete calendar month.</exception>
    /// <exception cref="CustomerNotFoundException">The customer does not exist.</exception>
    public async Task<SpendingAdvice> GetAsync(SpendingAdviceQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        // The month rules (one complete month, not the running one) and the income are INC-01's.
        var share = await incomeShares.GetAsync(new IncomeShareQuery(query.CustomerId, query.Period, query.From, query.To), cancellationToken);
        var month = share.Period;
        // K4-A: the usual months end before the month, so a spike cannot raise its own average.
        var usualFrom = month.StartDate.AddMonths(-SpendingAdviceRules.UsualMonths);
        var usualTo = month.StartDate.AddDays(-1);
        // The savings window ends with the month; for "last month" it is exactly ANA-14's last six complete months.
        var savingsFrom = month.StartDate.AddMonths(-(SpendingAdviceRules.SavingsMonths - 1));

        var (timeZone, purchases) = await new PurchaseScope(db, periodResolver)
            .ResolveAccountPurchasesAsync(query.CustomerId, accountId: null, cancellationToken);
        var window = periodResolver.Resolve(PeriodKind.Custom, timeZone, usualFrom, month.EndDate);
        var savingsStart = periodResolver.Resolve(PeriodKind.Custom, timeZone, savingsFrom, month.EndDate).StartUtc;
        var monthStart = month.StartUtc;

        // One aggregation over both windows: the usual total before the month and the savings total ending with it.
        var rows = await (
                from t in purchases.Where(t => t.OccurredAt >= window.StartUtc && t.OccurredAt < window.EndUtc)
                join c in db.TransactionCategories on t.CategoryId equals (short?)c.Id into categories
                from c in categories.DefaultIfEmpty()
                group t by new { t.CurrencyCode, Code = (string?)c!.Code } into g
                select new
                {
                    g.Key.CurrencyCode,
                    g.Key.Code,
                    Usual = g.Sum(t => t.OccurredAt < monthStart ? t.Amount : 0m),
                    Savings = g.Sum(t => t.OccurredAt >= savingsStart ? t.Amount : 0m),
                })
            .ToListAsync(cancellationToken);
        var totals = rows.ToDictionary(r => (r.CurrencyCode, (string?)r.Code), r => (r.Usual, r.Savings));

        var currencies = share.Currencies
            .Select(c => SpendingAdviceRules.Evaluate(c.CurrencyCode, month.StartDate, c.IncomeTotal,
                c.Categories
                    .Select(k =>
                    {
                        var (usual, savings) = totals.GetValueOrDefault((c.CurrencyCode, k.CategoryCode));
                        return new AdviceCategoryInput(k.CategoryCode, k.DisplayName, k.Amount, usual, savings);
                    })
                    .ToList()))
            .ToList();

        return new SpendingAdvice(month, share.IncomeFrom, share.IncomeTo, usualFrom, usualTo, savingsFrom, month.EndDate,
            SpendingAdviceRules.Thresholds, Method, currencies);
    }
}
