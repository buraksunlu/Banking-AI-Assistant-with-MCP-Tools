using BankingAI.Data;
using BankingAI.Data.Validation;

namespace BankingAI.Application.Analytics;

/// <summary>
/// Input of <see cref="SavingsSimulationService"/> (ANA-14): cut spending by <c>ReductionPercent</c> (1–100) based on the
/// last <c>Months</c> complete months (default 6, at most 24). Without <c>CategoryCode</c> and <c>Merchant</c> the whole
/// spending is simulated; the two cannot be combined.
/// </summary>
public sealed record SavingsSimulationQuery(
    Guid CustomerId, int ReductionPercent, string? CategoryCode = null, string? Merchant = null,
    int Months = SavingsSimulationService.DefaultMonths, Guid? AccountId = null);

/// <summary>
/// One currency's simulation. <c>MonthlyAverage</c> = <c>TotalSpent</c> / months, months without spending included;
/// the savings are computed from the exact total, not from the rounded average. <c>MonthlyAmounts</c> is the evidence:
/// every complete month used, in order.
/// </summary>
public sealed record CurrencySavings(
    string CurrencyCode, decimal TotalSpent, decimal MonthlyAverage, decimal MonthlySaving, decimal AnnualSaving,
    IReadOnlyList<MonthlySpending> MonthlyAmounts);

/// <summary>
/// Result of <see cref="SavingsSimulationService"/>: the complete months used (<c>From</c>–<c>To</c>), the assumption in
/// <c>Method</c> and one row per currency with spending in those months (none: empty list). A merchant text that does not
/// match exactly one of the customer's merchants gives no rows; <c>MerchantMatch</c> says why. <c>CategoryDisplayName</c>
/// is the category's Turkish name (MCP-18), as in <see cref="BudgetCheck"/>.
/// </summary>
public sealed record SavingsSimulation(
    DateOnly From, DateOnly To, int Months, int ReductionPercent, string? CategoryCode, string? CategoryDisplayName,
    MerchantMatch? MerchantMatch, string Method, IReadOnlyList<CurrencySavings> Currencies);

/// <summary>
/// "What if I spent less?" (ANA-14): the past monthly average times a reduction rate, per month and per year. Not
/// investment advice and no return, interest or inflation; only the assumption that the past average continues. The
/// monthly amounts come from <see cref="SpendingComparisonService.GetMonthlyTrendAsync"/>, so they always equal the
/// monthly trend; this service only adds the savings arithmetic.
/// </summary>
public sealed class SavingsSimulationService(BankingDbContext db, SpendingComparisonService comparisons)
{
    public const int DefaultMonths = 6;
    public const int MaximumReductionPercent = 100;

    /// <summary>The assumption written into every result.</summary>
    public const string Method = "FullMonthAverageTimesReduction";

    /// <summary>Simulates the saving for the complete months before the running month.</summary>
    /// <exception cref="ArgumentException">Invalid rate, months, category, merchant text, both filters, or a non-personal account.</exception>
    /// <exception cref="KeyNotFoundException">The customer, or the given account for this customer, does not exist.</exception>
    public async Task<SavingsSimulation> SimulateAsync(SavingsSimulationQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        InputRules.Require(query.ReductionPercent is >= 1 and <= MaximumReductionPercent,
            nameof(query.ReductionPercent), $"1 ile {MaximumReductionPercent} arasında olmalı.");

        // Months, category, merchant matching and the account scope are the monthly trend's rules.
        var trend = await comparisons.GetMonthlyTrendAsync(
            new SpendingTrendQuery(query.CustomerId, query.Months, query.CategoryCode, query.Merchant, query.AccountId), cancellationToken);
        // The running month is never averaged: half a month would pull the average down.
        var full = trend.Months.Where(w => !w.IsPartial).ToList();
        var (from, to, months) = (full[0].Period.StartDate, full[^1].Period.EndDate, full.Count);

        var currencies = trend.Currencies
            .Select(c =>
            {
                var used = c.Months.Where(m => !m.IsPartial).ToList();
                var total = used.Sum(m => m.Amount);
                // From the exact total: rounding the monthly saving first and multiplying by 12 would carry its error.
                return new CurrencySavings(c.CurrencyCode, total, Round(total / months),
                    Round(total * query.ReductionPercent / (100m * months)),
                    Round(total * query.ReductionPercent * 12m / (100m * months)), used);
            })
            // A currency that appears only in the running month has nothing to average; no invented "0" row.
            .Where(c => c.TotalSpent > 0m)
            .ToList();

        // The trend has already refused an unknown code, so this only reads the name of a known one.
        var categoryName = query.CategoryCode is { } code ? (await CategoryLookup.FindAsync(db, code, cancellationToken)).DisplayName : null;
        return new SavingsSimulation(
            from, to, months, query.ReductionPercent, query.CategoryCode, categoryName, trend.MerchantMatch, Method, currencies);
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
