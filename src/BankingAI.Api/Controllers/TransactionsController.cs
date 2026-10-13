using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using BankingAI.Api.Security;
using BankingAI.Application.Analytics;
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
/// The logged-in customer's transactions: an account's list and single transactions. Listing, filtering and
/// ownership are done by <see cref="TransactionSearchService"/>; this controller only translates HTTP.
/// </summary>
/// <remarks>
/// Each action carries its full route because the two live under different roots: a list belongs to an
/// account, while a single transaction is addressed by its own id.
/// </remarks>
[ApiController]
public sealed class TransactionsController(TransactionSearchService search) : ControllerBase
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
        if ((from is null) != (to is null))
            return Invalid(nameof(from), "Başlangıç ve bitiş tarihi birlikte verilmeli.");

        PeriodKind kind;
        if (from is not null)
        {
            if (period is not null)
                return Invalid(nameof(period), "Dönem adı ile tarih aralığı birlikte verilemez.");
            kind = PeriodKind.Custom;
        }
        else if (period is null)
        {
            kind = DefaultPeriod;
        }
        // Names only, like enums in JSON: Enum.TryParse would also accept "2". A custom range comes from dates.
        else if (period.Any(char.IsDigit) || !Enum.TryParse(period, ignoreCase: true, out kind)
                 || !Enum.IsDefined(kind) || kind == PeriodKind.Custom)
        {
            return Invalid(nameof(period), "ThisMonth, LastMonth, Last3Months veya Last6Months olmalı.");
        }

        try
        {
            var result = await search.SearchAsync(
                new TransactionSearchQuery(User.GetCustomerId(), kind, from, to, accountId, Limit: limit, Offset: offset),
                cancellationToken);
            return Ok(new TransactionPage(result.Period, result.TotalCount, result.HasMore, result.Items));
        }
        catch (KeyNotFoundException)
        {
            // A missing or foreign account (and, until API-13, a customer that no longer exists): the same 404.
            return NotFound();
        }
        catch (ArgumentException error)
        {
            // Limit/offset bounds and date rules are enforced by the service and the period resolver.
            return Invalid(error.ParamName ?? nameof(period), error.Message);
        }
    }

    /// <summary>
    /// <c>GET /api/transactions/{id}</c>: one transaction of the customer, of any kind and on any of their accounts,
    /// without a period limit. Another customer's transaction gets exactly the 404 of a missing one; an id that is
    /// not a GUID is a 400.
    /// </summary>
    [HttpGet("api/transactions/{id}")]
    public async Task<ActionResult<TransactionItem>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await search.GetAsync(User.GetCustomerId(), id, cancellationToken));
        }
        catch (KeyNotFoundException)
        {
            // Also reached when the token's customer no longer exists: the service reports both with the same
            // exception, so until API-13 splits them this endpoint answers 404 there as well (decision 4-A).
            return NotFound();
        }
    }

    private ActionResult Invalid(string field, string message)
    {
        ModelState.AddModelError(field, message);
        return ValidationProblem(ModelState);
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
    [Required]
    [AllowedValues(TransactionKind.Purchase, TransactionKind.Salary)]
    public TransactionKind? Kind { get; init; }

    /// <summary>Positive, at most two decimals, within the money ceiling; never rounded.</summary>
    [Required]
    public decimal? Amount { get; init; }

    /// <summary>The raw statement text, 1–500 characters, not only whitespace.</summary>
    [Required]
    [StringLength(500)]
    public string Description { get; init; } = "";

    /// <summary>
    /// Optional shop name, part of the event like on a card statement (1–150 characters). The category, the
    /// bank's interpretation, is never taken from the client.
    /// </summary>
    [StringLength(150)]
    public string? Merchant { get; init; }

    /// <summary>Rules that span fields or reuse the account-wide money rule.</summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // The same money rule the database layer enforces, so the API cannot drift from it.
        string? amountError = null;
        if (Amount is { } amount)
        {
            try
            {
                InputRules.Money(amount, nameof(Amount), positive: true);
            }
            catch (ArgumentException error)
            {
                amountError = error.Message;
            }
        }
        if (amountError is not null)
            yield return new ValidationResult(amountError, [nameof(Amount)]);

        if (Merchant is not null && string.IsNullOrWhiteSpace(Merchant))
            yield return new ValidationResult("Mağaza adı verildiyse boş olamaz.", [nameof(Merchant)]);

        if (Kind == TransactionKind.Salary && Merchant is not null)
            yield return new ValidationResult("Maaş işleminde mağaza adı olmaz.", [nameof(Merchant)]);
    }
}
