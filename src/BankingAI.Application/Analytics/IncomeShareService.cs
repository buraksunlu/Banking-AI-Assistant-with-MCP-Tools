using BankingAI.Data;
using BankingAI.Data.Enums;
using Microsoft.EntityFrameworkCore;

namespace BankingAI.Application.Analytics;

/// <summary>
/// Input of <see cref="IncomeShareService"/> (INC-01): one complete calendar month, either <see cref="PeriodKind.LastMonth"/>
/// or a <see cref="PeriodKind.Custom"/> range from its first to its last day.
/// </summary>
public sealed record IncomeShareQuery(Guid CustomerId, PeriodKind Period, DateOnly? From = null, DateOnly? To = null);

/// <summary>One category's spending and its share of the income base; the share is null when there is no income.</summary>
public sealed record CategoryIncomeShare(
    string? CategoryCode, string DisplayName, decimal Amount, int TransactionCount, decimal? IncomeShare);

/// <summary>
/// One currency of the month. <c>IncomeBase</c> = <c>IncomeTotal</c> / <see cref="IncomeShareService.IncomeMonths"/>, months
/// without salary included; null when the window has no salary in this currency. Shares come from the exact total.
/// </summary>
public sealed record CurrencyIncomeShare(
    string CurrencyCode, decimal TotalSpent, decimal IncomeTotal, int IncomeCount, decimal? IncomeBase, decimal? SpentShare,
    IReadOnlyList<CategoryIncomeShare> Categories);

/// <summary>
/// Result of <see cref="IncomeShareService"/>: the month (<c>Period</c>), the income window (<c>IncomeFrom</c>–<c>IncomeTo</c>),
/// the assumption in <c>Method</c> and one row per currency with spending in the month (none: empty list).
/// </summary>
public sealed record IncomeShare(
    ResolvedPeriod Period, DateOnly IncomeFrom, DateOnly IncomeTo, int IncomeMonths, string Method,
    IReadOnlyList<CurrencyIncomeShare> Currencies);

/// <summary>
/// "How much of my income did I spend?" (INC-01): a month's spending and categories as a share of the average monthly salary
/// of the three complete months ending with it (K1-B, S2-A). Income is <c>Kind == Salary</c> on personal accounts; currencies
/// are never mixed. Category amounts come from <see cref="CategoryBreakdownService"/>, so they always equal the breakdown.
/// </summary>
public sealed class IncomeShareService(BankingDbContext db, PeriodResolver periodResolver, CategoryBreakdownService categories)
{
    /// <summary>Complete months averaged into the income base.</summary>
    public const int IncomeMonths = 3;

    /// <summary>The assumption written into every result.</summary>
    public const string Method = "ThreeFullMonthSalaryAverage";

    /// <summary>Spending and categories of one complete month as shares of the income base.</summary>
    /// <exception cref="ArgumentException">The period is not one complete calendar month.</exception>
    /// <exception cref="CustomerNotFoundException">The customer does not exist.</exception>
    public async Task<IncomeShare> GetAsync(IncomeShareQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        // Shape first, without touching the database: a share of "the last 30 days" or of a half month has no fixed income to
        // compare with (K2-A, S1-A).
        if (query.Period is not (PeriodKind.LastMonth or PeriodKind.Custom))
            throw new ArgumentException("Gelir payı yalnız tam bir ay için hesaplanır: LastMonth ya da Custom (ayın ilk–son günü).", "period");
        if (query.Period == PeriodKind.Custom && !IsWholeMonth(query.From, query.To))
            throw new ArgumentException("Custom dönem tek bir takvim ayının ilk gününden son gününe olmalı.", "from");

        var (period, timeZone, accounts) = await new CustomerScope(db, periodResolver).ResolveAsync(
            query.CustomerId, query.Period, query.From, query.To, accountId: null, personalOnly: true, cancellationToken);
        // The last day of the running month can be "today"; its spending and salary may still change.
        if (period.EndDate >= periodResolver.TodayIn(timeZone))
            throw new ArgumentException("Devam eden ay için gelir payı hesaplanmaz; tamamlanmış bir ay seçin.", "to");

        var breakdown = await categories.GetAsync(
            new SpendingQuery(query.CustomerId, query.Period, query.From, query.To), cancellationToken);

        var incomeFrom = period.StartDate.AddMonths(-(IncomeMonths - 1));
        var window = periodResolver.Resolve(PeriodKind.Custom, timeZone, incomeFrom, period.EndDate);
        // One aggregation: salaries on personal accounts in the window. Business income (Sale) is not personal income.
        var income = await db.Transactions.AsNoTracking()
            .Where(t => t.Kind == TransactionKind.Salary && accounts.Contains(t.AccountId)
                && t.OccurredAt >= window.StartUtc && t.OccurredAt < window.EndUtc)
            .GroupBy(t => t.CurrencyCode)
            .Select(g => new { CurrencyCode = g.Key, Total = g.Sum(t => t.Amount), Count = g.Count() })
            .ToDictionaryAsync(r => r.CurrencyCode, r => (r.Total, r.Count), StringComparer.Ordinal, cancellationToken);

        var currencies = breakdown.Currencies
            .Select(c =>
            {
                var (total, count) = income.GetValueOrDefault(c.CurrencyCode);
                return new CurrencyIncomeShare(
                    c.CurrencyCode, c.TotalAmount, total, count,
                    total > 0m ? Round(total / IncomeMonths) : null,
                    Share(c.TotalAmount, total),
                    c.Categories
                        .Select(k => new CategoryIncomeShare(k.CategoryCode, k.DisplayName, k.Amount, k.TransactionCount, Share(k.Amount, total)))
                        .ToList());
            })
            .ToList();

        return new IncomeShare(period, window.StartDate, window.EndDate, IncomeMonths, Method, currencies);
    }

    /// <summary>
    /// Share of <paramref name="amount"/> in the income base, in percent with 2 decimals; null without income. Computed from
    /// the exact <paramref name="incomeTotal"/> of the window, not from the rounded base.
    /// </summary>
    public static decimal? Share(decimal amount, decimal incomeTotal) =>
        incomeTotal > 0m ? Round(amount * IncomeMonths * 100m / incomeTotal) : null;

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static bool IsWholeMonth(DateOnly? from, DateOnly? to) =>
        from is { Day: 1 } start && to is { } end && end == start.AddMonths(1).AddDays(-1);
}
