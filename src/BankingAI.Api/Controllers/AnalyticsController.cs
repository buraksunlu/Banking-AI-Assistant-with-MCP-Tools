using BankingAI.Api.Queries;
using BankingAI.Api.Security;
using BankingAI.Application.Analytics;
using BankingAI.Hosting.Security;
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
    SpendingComparisonService comparisons, HighestTransactionsService highest, RecurringPaymentService recurring,
    UpcomingPaymentService upcoming, BudgetCheckService budgets, SavingsSimulationService savings, IncomeShareService incomeShares)
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
    /// <c>includeAllTransactions=true</c> widens it to every kind on every account of the customer. An optional inclusive
    /// amount range needs <c>currencyCode</c> (ANA-11).
    /// </summary>
    [HttpGet("highest-transactions")]
    public async Task<ActionResult<HighestTransactions>> HighestTransactionsAsync(
        [FromQuery] string? period, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] Guid? accountId,
        [FromQuery] int? limit, [FromQuery] bool? includeAllTransactions,
        [FromQuery] string? minAmount, [FromQuery] string? maxAmount, [FromQuery] string? currencyCode,
        CancellationToken cancellationToken) =>
        Ok(await highest.GetAsync(
            new HighestTransactionsQuery(User.GetCustomerId(), PeriodParameters.Resolve(period, from, to, fallback: null),
                from, to, accountId, limit ?? HighestTransactionsService.DefaultLimit, includeAllTransactions ?? false,
                AmountParameter.Parse(minAmount, nameof(minAmount)), AmountParameter.Parse(maxAmount, nameof(maxAmount)),
                currencyCode),
            cancellationToken));

    /// <summary>
    /// <c>GET /api/analytics/recurring-payments</c>: the monthly payments found in the last six months and what they
    /// add up to per month. The six months are the rule itself, so this endpoint takes no period (decision K3-A).
    /// </summary>
    [HttpGet("recurring-payments")]
    public async Task<ActionResult<RecurringPayments>> RecurringPaymentsAsync(
        [FromQuery] Guid? accountId, CancellationToken cancellationToken) =>
        Ok(await recurring.GetAsync(new RecurringPaymentQuery(User.GetCustomerId(), accountId), cancellationToken));

    /// <summary>
    /// <c>GET /api/analytics/upcoming-payments</c>: the recurring payments expected in the next <c>days</c> days (1–31,
    /// default 30; decision K3-A), earliest first, with overdue ones flagged and an estimated total per currency. Dates
    /// and amounts are estimates from the recurring rule, never promises (ANA-12).
    /// </summary>
    [HttpGet("upcoming-payments")]
    public async Task<ActionResult<UpcomingPayments>> UpcomingPaymentsAsync(
        [FromQuery] int? days, [FromQuery] Guid? accountId, CancellationToken cancellationToken) =>
        Ok(await upcoming.GetAsync(
            new UpcomingPaymentQuery(User.GetCustomerId(), days ?? UpcomingPaymentService.DefaultDays, accountId), cancellationToken));

    /// <summary>
    /// <c>GET /api/analytics/budget-check</c>: this month's spending against a budget given by the user (never stored):
    /// spent, remaining or over, daily average and a linear month-end projection whose method is named in the answer.
    /// Without <c>categoryCode</c> and <c>merchant</c> the budget covers total spending; <c>currencyCode</c> is required
    /// (ANA-13).
    /// </summary>
    [HttpGet("budget-check")]
    public async Task<ActionResult<BudgetCheck>> BudgetCheckAsync(
        [FromQuery] string? amount, [FromQuery] string? currencyCode, [FromQuery] string? categoryCode, [FromQuery] string? merchant,
        [FromQuery] Guid? accountId, CancellationToken cancellationToken) =>
        Ok(await budgets.CheckAsync(
            new BudgetCheckQuery(User.GetCustomerId(), AmountParameter.Required(amount, nameof(amount), "Bütçe tutarı zorunlu."),
                currencyCode, categoryCode, merchant, accountId),
            cancellationToken));

    /// <summary>
    /// <c>GET /api/analytics/savings-simulation</c>: "what if I cut it by N %?". The average of the last <c>months</c>
    /// complete months (default 6, months without spending included) times the rate, per month and per year, one row per
    /// currency; the assumption is named in <c>method</c>. Not investment advice: no return, interest or inflation (ANA-14).
    /// </summary>
    [HttpGet("savings-simulation")]
    public async Task<ActionResult<SavingsSimulation>> SavingsSimulationAsync(
        [FromQuery] int? reductionPercent, [FromQuery] string? categoryCode, [FromQuery] string? merchant, [FromQuery] int? months,
        [FromQuery] Guid? accountId, CancellationToken cancellationToken) =>
        Ok(await savings.SimulateAsync(
            new SavingsSimulationQuery(User.GetCustomerId(),
                RequiredParameter.Value(reductionPercent, nameof(reductionPercent), "Azaltma oranı zorunlu."),
                categoryCode, merchant, months ?? SavingsSimulationService.DefaultMonths, accountId),
            cancellationToken));

    /// <summary>
    /// <c>GET /api/analytics/income-share</c>: one complete month's spending and categories as a share of the average monthly
    /// salary of the three complete months ending with it (INC-01). Only <c>period=LastMonth</c> or <c>from</c>/<c>to</c>
    /// covering one whole calendar month; shares are null in a currency without salary.
    /// </summary>
    [HttpGet("income-share")]
    public async Task<ActionResult<IncomeShare>> IncomeShareAsync(
        [FromQuery] string? period, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken cancellationToken) =>
        Ok(await incomeShares.GetAsync(
            new IncomeShareQuery(User.GetCustomerId(), PeriodParameters.Resolve(period, from, to, fallback: null), from, to),
            cancellationToken));

    /// <summary>
    /// <c>GET /api/analytics/merchants</c>: every merchant the customer has bought from on personal accounts, all time,
    /// with the latest category and purchase date (ANA-10). This is the closed set a model chooses names from.
    /// </summary>
    [HttpGet("merchants")]
    public async Task<ActionResult<MerchantList>> MerchantsAsync([FromQuery] Guid? accountId, CancellationToken cancellationToken) =>
        Ok(await merchants.ListAsync(new MerchantListQuery(User.GetCustomerId(), accountId), cancellationToken));

    /// <summary>
    /// <c>GET /api/analytics/merchant-group-spending?merchants=…&amp;merchants=…</c>: spending at 1–20 merchants chosen from
    /// <c>/merchants</c> ("kahve" → Starbucks and Kahve Durağı), per currency and per merchant (ANA-10). Each name must be
    /// another spelling of one of the customer's merchants; anything else is a 400, never completed or guessed.
    /// </summary>
    [HttpGet("merchant-group-spending")]
    public async Task<ActionResult<MerchantGroupSpending>> MerchantGroupSpendingAsync(
        [FromQuery(Name = "merchants")] string[]? names,
        [FromQuery] string? period, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] Guid? accountId,
        CancellationToken cancellationToken) =>
        Ok(await merchants.GetGroupSpendingAsync(Spending(period, from, to, accountId), names ?? [], cancellationToken));

    // The customer comes from the token only; the period is required (no fallback).
    private SpendingQuery Spending(string? period, DateOnly? from, DateOnly? to, Guid? accountId) =>
        new(User.GetCustomerId(), PeriodParameters.Resolve(period, from, to, fallback: null), from, to, accountId);
}
