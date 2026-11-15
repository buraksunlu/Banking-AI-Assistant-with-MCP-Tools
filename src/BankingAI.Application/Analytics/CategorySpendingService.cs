using BankingAI.Data;
using BankingAI.Data.Validation;
using Microsoft.EntityFrameworkCore;

namespace BankingAI.Application.Analytics;

/// <summary>The largest purchase of the period; <c>LocalDate</c> is the customer's calendar day.</summary>
public sealed record LargestPurchase(Guid TransactionId, decimal Amount, string? Merchant, DateOnly LocalDate);

/// <summary>
/// One currency's spending in one category (UX-01 PR3). Amounts are exact; averages and percentages are rounded to two
/// decimals. <c>PercentageChange</c> is null when the previous period is 0, <c>PercentageVsAverage</c> unless the period is
/// one of the complete months of <c>Months</c> and the average is above 0.
/// </summary>
public sealed record CurrencyCategoryDetail(
    string CurrencyCode, decimal Amount, int TransactionCount, decimal? AveragePerTransaction,
    decimal PreviousAmount, int PreviousCount, decimal Change, decimal? PercentageChange,
    decimal TotalSpending, decimal? ShareOfSpending,
    IReadOnlyList<MerchantSpending> Merchants, LargestPurchase? Largest,
    IReadOnlyList<MonthlySpending> Months, decimal MonthlyAverage, decimal? PercentageVsAverage);

/// <summary>
/// "Markete ne kadar gitti?": one category over a period, the period before it and the last six complete months plus the
/// running one. A currency is listed when the category has spending in any of those windows (a 0 is then a real 0); an
/// empty list means no spending in this category at all.
/// </summary>
public sealed record CategorySpendingDetail(
    ResolvedPeriod Period, ResolvedPeriod PreviousPeriod, string CategoryCode, string DisplayName,
    IReadOnlyList<MonthWindow> Months, IReadOnlyList<CurrencyCategoryDetail> Currencies);

/// <summary>The category detail analysis (UX-01 PR3, decision K4-A of the UX-01 design).</summary>
public sealed class CategorySpendingService(BankingDbContext db, PeriodResolver periodResolver)
{
    /// <summary>The complete months of the series; the running month is added after them.</summary>
    public const int SeriesMonths = 6;

    private readonly PurchaseScope _scope = new(db, periodResolver);

    /// <summary>Spending in the category with exactly this code, per currency.</summary>
    /// <exception cref="ArgumentException">The code is not a defined category (field <c>CategoryCode</c>) or the period is invalid.</exception>
    /// <exception cref="KeyNotFoundException">The account is not the customer's.</exception>
    public async Task<CategorySpendingDetail> GetAsync(SpendingQuery query, string categoryCode, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        // Reject invalid text before touching the database.
        InputRules.Text(categoryCode, 50, nameof(TransactionSearchQuery.CategoryCode));
        var scoped = await _scope.ResolveAsync(query, cancellationToken);
        var category = await CategoryLookup.FindAsync(db, categoryCode, cancellationToken);
        var period = scoped.Period;
        var previous = periodResolver.ResolvePrevious(period, scoped.TimeZone);
        var windows = periodResolver.ResolveMonthlyWindows(SeriesMonths, scoped.TimeZone);

        // K7-A: one read of the category's rows in the three windows (one customer, one category: a few hundred rows at
        // most), every figure below is computed from them. Rows between the windows of a long custom range are not read.
        var (ps, pe, qs, qe) = (period.StartUtc, period.EndUtc, previous.StartUtc, previous.EndUtc);
        var (ss, se) = (windows[0].Period.StartUtc, windows[^1].Period.EndUtc);
        var rows = await scoped.AccountPurchases
            .Where(t => t.CategoryId == category.Id
                && ((t.OccurredAt >= ps && t.OccurredAt < pe) || (t.OccurredAt >= qs && t.OccurredAt < qe)
                    || (t.OccurredAt >= ss && t.OccurredAt < se)))
            .Select(t => new Row(t.Id, t.CurrencyCode, t.OccurredAt, t.Amount, t.NormalizedMerchant))
            .ToListAsync(cancellationToken);

        // The share's base is all the period's spending, the unclassified part included.
        var totals = await scoped.Purchases
            .GroupBy(t => t.CurrencyCode)
            .Select(g => new { CurrencyCode = g.Key, Amount = g.Sum(t => t.Amount) })
            .ToDictionaryAsync(g => g.CurrencyCode, g => g.Amount, cancellationToken);

        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(scoped.TimeZone);
        var currencies = rows
            .GroupBy(r => r.CurrencyCode)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => Detail(g.Key, g.ToList(), totals.GetValueOrDefault(g.Key), period, previous, windows, timeZone))
            .ToList();

        return new CategorySpendingDetail(period, previous, category.Code, category.DisplayName, windows, currencies);
    }

    private sealed record Row(Guid Id, string CurrencyCode, DateTimeOffset OccurredAt, decimal Amount, string? Merchant);

    private static CurrencyCategoryDetail Detail(
        string currency, List<Row> rows, decimal totalSpending, ResolvedPeriod period, ResolvedPeriod previous,
        IReadOnlyList<MonthWindow> windows, TimeZoneInfo timeZone)
    {
        var current = In(rows, period);
        var before = In(rows, previous);
        var (amount, previousAmount) = (current.Sum(r => r.Amount), before.Sum(r => r.Amount));
        var change = amount - previousAmount;

        var merchants = current
            .GroupBy(r => r.Merchant)
            .Select(m =>
            {
                var sum = m.Sum(r => r.Amount);
                return new MerchantSpending(m.Key, m.Key ?? AnalyticsLabels.Unclassified, sum, m.Count(), Percent(sum, amount)!.Value);
            })
            .OrderByDescending(m => m.Amount)
            .ThenBy(m => m.Merchant is null)
            .ThenBy(m => m.Merchant, StringComparer.Ordinal)
            .ToList();

        // K3-A: ties go to the newest, then the id, as in the highest transactions (TransactionItemReader.LargestFirst).
        var largest = current.OrderByDescending(r => r.Amount).ThenByDescending(r => r.OccurredAt).ThenByDescending(r => r.Id)
            .Select(r => new LargestPurchase(r.Id, r.Amount, r.Merchant,
                DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(r.OccurredAt, timeZone).DateTime)))
            .FirstOrDefault();

        var months = windows
            .Select(w =>
            {
                var inWindow = In(rows, w.Period);
                return new MonthlySpending(w.Year, w.Month, w.IsPartial, inWindow.Sum(r => r.Amount), inWindow.Count);
            })
            .ToList();
        // K2-A: the running month is not in the average, and only a period that is one of the complete months is compared
        // with it; the exact average is used for the percentage, the rounded one is shown.
        var average = months.Where(m => !m.IsPartial).Average(m => m.Amount);
        var isCompleteMonth = windows.Any(w => !w.IsPartial
            && w.Period.StartDate == period.StartDate && w.Period.EndDate == period.EndDate);

        return new CurrencyCategoryDetail(
            currency, amount, current.Count,
            current.Count == 0 ? null : Round(amount / current.Count),
            previousAmount, before.Count, change, Percent(change, previousAmount),
            totalSpending, Percent(amount, totalSpending),
            merchants, largest, months, Round(average),
            isCompleteMonth ? Percent(amount - average, average) : null);
    }

    private static List<Row> In(List<Row> rows, ResolvedPeriod period) =>
        rows.Where(r => r.OccurredAt >= period.StartUtc && r.OccurredAt < period.EndUtc).ToList();

    // A rate from zero is never invented.
    private static decimal? Percent(decimal part, decimal whole) => whole == 0m ? null : Round(part / whole * 100m);

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
