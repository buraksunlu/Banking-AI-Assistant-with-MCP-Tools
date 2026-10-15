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
    SpendingSummaryService summaries, CategoryBreakdownService categories, MerchantSpendingService merchants)
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

    // The customer comes from the token only; the period is required (no fallback).
    private SpendingQuery Spending(string? period, DateOnly? from, DateOnly? to, Guid? accountId) =>
        new(User.GetCustomerId(), PeriodParameters.Resolve(period, from, to, fallback: null), from, to, accountId);
}
