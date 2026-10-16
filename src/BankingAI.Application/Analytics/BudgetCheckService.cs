using BankingAI.Data;
using BankingAI.Data.Validation;

namespace BankingAI.Application.Analytics;

/// <summary>
/// Input of <see cref="BudgetCheckService"/> (ANA-13). The budget comes from the user's question as an argument and is
/// never stored. Without <c>CategoryCode</c> and <c>Merchant</c> the budget covers the month's total spending; the two
/// cannot be combined. <c>CurrencyCode</c> is required: a budget is never compared across currencies.
/// </summary>
public sealed record BudgetCheckQuery(
    Guid CustomerId, decimal Amount, string? CurrencyCode, string? CategoryCode = null, string? Merchant = null,
    Guid? AccountId = null);

/// <summary>
/// The budget arithmetic for the running month. <c>DailyAverage</c> = spent / elapsed days (today included);
/// <c>ProjectedMonthEnd</c> = spent × days in month / elapsed days (<see cref="BudgetCheckService.ProjectionMethod"/>),
/// computed from the exact ratio, not the rounded average. All amounts are rounded to two decimals, away from zero.
/// </summary>
public sealed record BudgetFigures(
    decimal SpentAmount, int TransactionCount, decimal RemainingAmount, decimal OverAmount, bool IsOverBudget,
    int ElapsedDays, int DaysInMonth, decimal DailyAverage, decimal ProjectedMonthEnd, decimal ProjectedOverAmount,
    bool WillExceed, string ProjectionMethod);

/// <summary>
/// Result of <see cref="BudgetCheckService"/>. <c>Figures</c> is null only when a merchant text did not match exactly one
/// of the customer's merchants; <c>MerchantMatch</c> then says why. No spending is a real 0, not a missing answer.
/// </summary>
public sealed record BudgetCheck(
    ResolvedPeriod Period, string CurrencyCode, decimal BudgetAmount, string? CategoryCode, string? CategoryDisplayName,
    MerchantMatch? MerchantMatch, BudgetFigures? Figures);

/// <summary>
/// Checks a monthly budget against this month's spending (ANA-13). The spent amount comes from the existing analyses
/// (<see cref="SpendingSummaryService"/>, <see cref="CategoryBreakdownService"/>, <see cref="MerchantSpendingService"/>),
/// so it always equals what they report; this service only adds the budget arithmetic.
/// </summary>
public sealed class BudgetCheckService(
    BankingDbContext db, SpendingSummaryService summaries, CategoryBreakdownService categories, MerchantSpendingService merchants)
{
    /// <summary>The projection written into every result: the month so far, extended linearly to its last day.</summary>
    public const string ProjectionMethod = "LinearDailyAverage";

    /// <summary>Checks the budget for the running month in the customer's time zone.</summary>
    /// <exception cref="ArgumentException">Invalid amount, currency, category or merchant text, both filters, or a non-personal account.</exception>
    /// <exception cref="KeyNotFoundException">The customer, or the given account for this customer, does not exist.</exception>
    public async Task<BudgetCheck> CheckAsync(BudgetCheckQuery query, CancellationToken cancellationToken = default)
    {
        Validate(query);
        var currency = query.CurrencyCode!;
        // Decision K2-A: always the running month; the other analyses resolve it in the customer's time zone.
        var month = new SpendingQuery(query.CustomerId, PeriodKind.ThisMonth, AccountId: query.AccountId);

        if (query.Merchant is { } merchant)
        {
            var found = await merchants.GetSpendingAsync(month, merchant, cancellationToken);
            if (found.Match.Status != MerchantMatchStatus.Matched)
                return new BudgetCheck(found.Period, currency, query.Amount, null, null, found.Match, null);
            var row = found.Currencies.SingleOrDefault(c => c.CurrencyCode == currency);
            return Result(found.Period, null, null, found.Match, row?.TotalAmount ?? 0m, row?.TransactionCount ?? 0);
        }

        if (query.CategoryCode is { } code)
        {
            // An unknown code is an error before any spending is read; a known one with no spending is a real 0.
            var category = await CategoryLookup.FindAsync(db, code, cancellationToken);
            var breakdown = await categories.GetAsync(month, cancellationToken);
            var row = breakdown.Currencies.SingleOrDefault(c => c.CurrencyCode == currency)?
                .Categories.SingleOrDefault(c => c.CategoryCode == category.Code);
            return Result(breakdown.Period, category.Code, category.DisplayName, null, row?.Amount ?? 0m, row?.TransactionCount ?? 0);
        }

        var summary = await summaries.GetAsync(month, cancellationToken);
        var total = summary.Currencies.SingleOrDefault(c => c.CurrencyCode == currency);
        return Result(summary.Period, null, null, null, total?.TotalAmount ?? 0m, total?.TransactionCount ?? 0);

        BudgetCheck Result(ResolvedPeriod period, string? categoryCode, string? categoryName, MerchantMatch? match, decimal spent, int count) =>
            new(period, currency, query.Amount, categoryCode, categoryName, match, Figures(period, query.Amount, spent, count));
    }

    // Decision K3-A: elapsed days include today, so the first day of the month already divides by 1, never by 0.
    private static BudgetFigures Figures(ResolvedPeriod month, decimal budget, decimal spent, int count)
    {
        var today = month.EndDate;
        var elapsed = today.Day;
        var daysInMonth = DateTime.DaysInMonth(today.Year, today.Month);
        // From the exact ratio: rounding the daily average first and multiplying would carry its error 31 times.
        var projected = Round(spent * daysInMonth / elapsed);
        return new BudgetFigures(
            spent, count, Math.Max(0m, budget - spent), Math.Max(0m, spent - budget), spent > budget,
            elapsed, daysInMonth, Round(spent / elapsed), projected, Math.Max(0m, projected - budget), projected > budget,
            ProjectionMethod);
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    // Every input check runs before the database is touched; the field names are the query's.
    private static void Validate(BudgetCheckQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        InputRules.Money(query.Amount, nameof(query.Amount), positive: true);
        InputRules.Require(query.CurrencyCode is not null, nameof(query.CurrencyCode), "Bütçenin para birimi (currencyCode) verilmeli.");
        InputRules.Currency(query.CurrencyCode, nameof(query.CurrencyCode));
        InputRules.Require(query.CategoryCode is null || query.Merchant is null,
            nameof(query.Merchant), "Kategori ve merchant filtresi birlikte kullanılamaz.");
        if (query.CategoryCode is not null)
            InputRules.Text(query.CategoryCode, 50, nameof(query.CategoryCode));
        if (query.Merchant is not null)
            InputRules.Text(query.Merchant, MerchantMatcher.MaximumLength, nameof(query.Merchant));
    }
}
