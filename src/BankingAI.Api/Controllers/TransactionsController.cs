using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using BankingAI.Api.Errors;
using BankingAI.Api.Queries;
using BankingAI.Api.Security;
using BankingAI.Application.Analytics;
using BankingAI.Application.Transactions;
using BankingAI.Data.Enums;
using BankingAI.Data.Validation;
using Microsoft.AspNetCore.Mvc;

namespace BankingAI.Api.Controllers;

/// <summary>
/// One page of an account's transactions: the resolved period, how many transactions it holds, whether more
/// pages follow, and the items (the shared <see cref="TransactionItem"/>, newest first).
/// </summary>
public sealed record TransactionPage(ResolvedPeriod Period, int TotalCount, bool HasMore, IReadOnlyList<TransactionItem> Items);

/// <summary>
/// The logged-in customer's transactions: an account's list, single transactions and adding a synthetic one.
/// Listing, filtering and ownership are done by <see cref="TransactionSearchService"/>, writing by
/// <see cref="ITransactionWriteService"/>; this controller only translates HTTP.
/// </summary>
/// <remarks>
/// Each action carries its full route because the two live under different roots: a list belongs to an
/// account, while a single transaction is addressed by its own id.
/// </remarks>
[ApiController]
public sealed class TransactionsController(TransactionSearchService search, ITransactionWriteService writer) : ControllerBase
{
    /// <summary>Period used when the client gives neither a period nor dates (decision 1-B).</summary>
    public const PeriodKind DefaultPeriod = PeriodKind.Last3Months;

    /// <summary>
    /// <c>GET /api/accounts/{accountId}/transactions</c>: a page of the account's transactions, newest first.
    /// Either a symbolic <paramref name="period"/> or both <paramref name="from"/> and <paramref name="to"/>
    /// (local dates, inclusive); neither means the last three months. Another customer's account is a 404.
    /// </summary>
    [HttpGet("api/accounts/{accountId}/transactions")]
    public async Task<ActionResult<TransactionPage>> ListAsync(
        Guid accountId,
        [FromQuery] string? period,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] int limit = TransactionSearchService.DefaultLimit,
        [FromQuery] int offset = 0,
        CancellationToken cancellationToken = default)
    {
        var kind = PeriodParameters.Resolve(period, from, to, DefaultPeriod);

        // Limit/offset bounds and date rules are the service's and the period resolver's; their errors, like a
        // missing or foreign account, are answered by ApiExceptionHandler.
        var result = await search.SearchAsync(
            new TransactionSearchQuery(User.GetCustomerId(), kind, from, to, accountId, Limit: limit, Offset: offset),
            cancellationToken);
        return Ok(new TransactionPage(result.Period, result.TotalCount, result.HasMore, result.Items));
    }

    /// <summary>
    /// <c>GET /api/transactions/{id}</c>: one transaction of the customer, of any kind and on any of their accounts,
    /// without a period limit. Another customer's transaction gets exactly the 404 of a missing one; an id that is
    /// not a GUID is a 400.
    /// </summary>
    [HttpGet("api/transactions/{id}")]
    public async Task<ActionResult<TransactionItem>> GetAsync(Guid id, CancellationToken cancellationToken) =>
        Ok(await search.GetAsync(User.GetCustomerId(), id, cancellationToken));

    /// <summary>
    /// <c>POST /api/accounts/{accountId}/transactions</c>: adds a synthetic Purchase or Salary to one of the
    /// customer's accounts and answers 201 with its <c>Location</c> and the transaction exactly as
    /// <c>GET /api/transactions/{id}</c> returns it. Needs an <c>Idempotency-Key</c>: the same key again returns
    /// the first answer. A balance that would leave its range, or a key reused for another request, is a 409;
    /// another customer's account is a 404.
    /// </summary>
    [HttpPost("api/accounts/{accountId}/transactions")]
    public async Task<ActionResult<TransactionItem>> CreateAsync(
        Guid accountId, CreateTransactionRequest request,
        [FromHeader(Name = ApiProblems.IdempotencyKeyHeader)] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        // Required and validated by the request rules before this runs, so the values are present. The key is
        // checked by the service (required for money), so its error names the field like every other rule.
        var result = await writer.CreateAsync(User.GetCustomerId(), accountId,
            new NewTransaction(request.Kind!.Value, request.Amount!.Value, request.Description, request.Merchant),
            idempotencyKey, cancellationToken);
        // A retry gets the first answer unchanged (201, same body and Location); the header only says so.
        if (result.Replayed)
            Response.Headers[ApiProblems.ReplayedHeader] = "true";
        return CreatedAtAction(nameof(GetAsync), new { id = result.Transaction.Id }, result.Transaction);
    }
}

/// <summary>
/// Body of <c>POST /api/accounts/{accountId}/transactions</c> (API-11): only the event itself. The backend
/// derives direction from the kind and currency from the account, takes the account from the URL and the time
/// from its own clock, and owns every category field; any of those in the JSON is a 400.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateTransactionRequest : IValidatableObject
{
    /// <summary>
    /// <c>Purchase</c> (money out) or <c>Salary</c> (money in, so new zero-balance accounts can be used).
    /// Two-legged kinds (Transfer, Sale) and OpeningFunding are not accepted.
    /// </summary>
    [Required(ErrorMessage = "Zorunlu alan.")]
    [AllowedValues(TransactionKind.Purchase, TransactionKind.Salary, ErrorMessage = "Purchase veya Salary olmalı.")]
    public TransactionKind? Kind { get; init; }

    /// <summary>Positive, at most two decimals, within the money ceiling; never rounded.</summary>
    [Required(ErrorMessage = "Zorunlu alan.")]
    public decimal? Amount { get; init; }

    /// <summary>The raw statement text, 1–500 characters, not only whitespace.</summary>
    [Required(ErrorMessage = "Zorunlu alan.")]
    public string Description { get; init; } = "";

    /// <summary>
    /// Optional shop name, part of the event like on a card statement (1–150 characters). The category, the
    /// bank's interpretation, is never taken from the client.
    /// </summary>
    public string? Merchant { get; init; }

    /// <summary>
    /// The money and text rules the database layer enforces (reused, not copied, so the API cannot drift from them
    /// and counts characters like the database), plus the rule that spans fields.
    /// </summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Amount is { } amount)
            foreach (var error in ApiValidation.Rule(() => InputRules.Money(amount, nameof(Amount), positive: true), nameof(Amount)))
                yield return error;
        // A missing or blank description is already reported by [Required].
        if (!string.IsNullOrWhiteSpace(Description))
            foreach (var error in ApiValidation.Rule(() => InputRules.Text(Description, 500, nameof(Description)), nameof(Description)))
                yield return error;
        foreach (var error in ApiValidation.Rule(() => InputRules.Text(Merchant, 150, nameof(Merchant), optional: true), nameof(Merchant)))
            yield return error;

        if (Kind == TransactionKind.Salary && Merchant is not null)
            yield return new ValidationResult("Maaş işleminde mağaza adı olmaz.", [nameof(Merchant)]);
    }
}
