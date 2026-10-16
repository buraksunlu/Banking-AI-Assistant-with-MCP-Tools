using BankingAI.Api.Queries;
using BankingAI.Api.Security;
using BankingAI.Application.Analytics;
using Microsoft.AspNetCore.Mvc;

namespace BankingAI.Api.Controllers;

/// <summary>
/// The deterministic analytics of the logged-in customer (ANA-09). Every figure is computed by the analytics
/// services; this controller only reads the query string and passes the token's customer on. A period is required
/// (decision 3-A): either a symbolic <c>period</c> or both <c>from</c> and <c>to</c>.
/// </summary>
[ApiController]
[Route("api/analytics")]
public sealed class AnalyticsController(
    SpendingSummaryService summaries, CategoryBreakdownService categories, MerchantSpendingService merchants,
    SpendingComparisonService comparisons, HighestTransactionsService highest, RecurringPaymentService recurring)
    : ControllerBase
{
    /// <summary>
    /// <c>GET /api/analytics/spending-summary</c>: spending (purchases only) per currency, with the unclassified part.
    /// </summary>
    [HttpGet("spending-summary")]
    public async Task<ActionResult<SpendingSummary>> SpendingSummaryAsync(
        [FromQuery] string? period, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] Guid? accountId,
        CancellationToken cancellationToken) =>
        Ok(await summaries.GetAsync(Spending(period, from, to, accountId), cancellationToken));

    /// <summary><c>GET /api/analytics/category-breakdown</c>: spending per category and currency, largest first.</summary>
    [HttpGet("category-breakdown")]
    public async Task<ActionResult<CategoryBreakdown>> CategoryBreakdownAsync(
        [FromQuery] string? period, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] Guid? accountId,
        CancellationToken cancellationToken) =>
        Ok(await categories.GetAsync(Spending(period, from, to, accountId), cancellationToken));

    /// <summary><c>GET /api/analytics/merchant-breakdown</c>: spending per merchant and currency, largest first.</summary>
    [HttpGet("merchant-breakdown")]
    public async Task<ActionResult<MerchantBreakdown>> MerchantBreakdownAsync(
        [FromQuery] string? period, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] Guid? accountId,
        CancellationToken cancellationToken) =>
        Ok(await merchants.GetBreakdownAsync(Spending(period, from, to, accountId), cancellationToken));

    /// <summary>
    /// <c>GET /api/analytics/merchant-spending</c>: spending at one merchant. The text is matched against the
    /// customer's own merchants; an ambiguous or unknown name returns the match without amounts, never a guess.
    /// </summary>
    [HttpGet("merchant-spending")]
    public async Task<ActionResult<MerchantSpendingResult>> MerchantSpendingAsync(
        [FromQuery] string? merchant,
        [FromQuery] string? period, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] Guid? accountId,
        CancellationToken cancellationToken) =>
        // A missing merchant reaches the service's own text rule, which names the field.
        Ok(await merchants.GetSpendingAsync(Spending(period, from, to, accountId), merchant!, cancellationToken));

    /// <summary>
    /// <c>GET /api/analytics/period-comparison</c>: spending of two periods per currency, with the change and the
    /// categories that caused it (largest first). The comparison is required: <c>PreviousFullMonth</c> (last complete
    /// month against the month before), <c>MonthToDate</c>, or two explicit ranges given by four dates.
    /// </summary>
    [HttpGet("period-comparison")]
    public async Task<ActionResult<SpendingComparison>> PeriodComparisonAsync(
        [FromQuery] string? comparison,
        [FromQuery] DateOnly? currentFrom, [FromQuery] DateOnly? currentTo,
        [FromQuery] DateOnly? previousFrom, [FromQuery] DateOnly? previousTo,
        [FromQuery] Guid? accountId, CancellationToken cancellationToken) =>
        Ok(await comparisons.CompareAsync(
            new SpendingComparisonQuery(User.GetCustomerId(),
                PeriodParameters.ResolveComparison(comparison, currentFrom, currentTo, previousFrom, previousTo),
                currentFrom, currentTo, previousFrom, previousTo, accountId),
            cancellationToken));

    /// <summary>
    /// <c>GET /api/analytics/monthly-trend</c>: spending per calendar month for the last <c>months</c> complete months
    /// plus the running month; a month without spending is 0. Optionally narrowed to one category or one merchant.
    /// </summary>
    [HttpGet("monthly-trend")]
    public async Task<ActionResult<SpendingTrend>> MonthlyTrendAsync(
        [FromQuery] int? months, [FromQuery] string? categoryCode, [FromQuery] string? merchant, [FromQuery] Guid? accountId,
        CancellationToken cancellationToken) =>
        Ok(await comparisons.GetMonthlyTrendAsync(
            new SpendingTrendQuery(User.GetCustomerId(), PeriodParameters.TrendMonths(months), categoryCode, merchant, accountId),
            cancellationToken));

    /// <summary>
    /// <c>GET /api/analytics/highest-transactions</c>: the largest purchases per currency (5 by default, at most 100).
    /// <c>includeAllTransactions=true</c> widens it to every kind on every account of the customer.
    /// </summary>
    [HttpGet("highest-transactions")]
    public async Task<ActionResult<HighestTransactions>> HighestTransactionsAsync(
        [FromQuery] string? period, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] Guid? accountId,
        [FromQuery] int? limit, [FromQuery] bool? includeAllTransactions, CancellationToken cancellationToken) =>
        Ok(await highest.GetAsync(
            new HighestTransactionsQuery(User.GetCustomerId(), PeriodParameters.Resolve(period, from, to, fallback: null),
                from, to, accountId, limit ?? HighestTransactionsService.DefaultLimit, includeAllTransactions ?? false),
            cancellationToken));

    /// <summary>
    /// <c>GET /api/analytics/recurring-payments</c>: the monthly payments found in the last six months and what they
    /// add up to per month. The six months are the rule itself, so this endpoint takes no period (decision K3-A).
    /// </summary>
    [HttpGet("recurring-payments")]
    public async Task<ActionResult<RecurringPayments>> RecurringPaymentsAsync(
        [FromQuery] Guid? accountId, CancellationToken cancellationToken) =>
        Ok(await recurring.GetAsync(new RecurringPaymentQuery(User.GetCustomerId(), accountId), cancellationToken));

    // The customer comes from the token only; the period is required (no fallback).
    private SpendingQuery Spending(string? period, DateOnly? from, DateOnly? to, Guid? accountId) =>
        new(User.GetCustomerId(), PeriodParameters.Resolve(period, from, to, fallback: null), from, to, accountId);
}
