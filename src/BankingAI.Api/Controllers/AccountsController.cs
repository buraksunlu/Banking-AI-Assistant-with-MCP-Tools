using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using BankingAI.Api.Errors;
using BankingAI.Api.Security;
using BankingAI.Application.Accounts;
using BankingAI.Data.Validation;
using Microsoft.AspNetCore.Mvc;

namespace BankingAI.Api.Controllers;

/// <summary>
/// Body of <c>POST /api/accounts</c> (API-07). Holds only what the client may choose; owner, number, type
/// (always Personal), balance (always 0), id and creation time are set by the backend. Any other field in
/// the JSON is a 400 instead of being silently ignored.
/// </summary>
/// <remarks>
/// Properties rather than a positional record: <see cref="Validator"/> reads validation attributes from
/// properties, while on a positional record they would sit on the constructor parameters.
/// </remarks>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateAccountRequest : IValidatableObject
{
    /// <summary>1–100 characters, not only whitespace (same rule as the database).</summary>
    [Required(ErrorMessage = "Zorunlu alan.")]
    public string AccountName { get; init; } = "";

    /// <summary>Exactly one of the account currencies the database accepts; "try" is not corrected.</summary>
    [Required(ErrorMessage = "Zorunlu alan.")]
    [AllowedValues("TRY", "USD", "EUR", "GBP", ErrorMessage = "TRY, USD, EUR veya GBP olmalı.")]
    public string CurrencyCode { get; init; } = "";

    /// <summary>
    /// The account-name rule of the database layer, reused rather than copied: it also counts characters the
    /// way the database does and rejects NUL.
    /// </summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        // A missing or blank name is already reported by [Required].
        string.IsNullOrWhiteSpace(AccountName)
            ? []
            : ApiValidation.Rule(() => InputRules.Text(AccountName, 100, nameof(AccountName)), nameof(AccountName));
}

/// <summary>
/// The logged-in customer's accounts. The customer always comes from the token; there is no customer
/// parameter to tamper with.
/// </summary>
[ApiController]
[Route("api/accounts")]
public sealed class AccountsController(IAccountReadService accounts, IAccountWriteService accountWriter) : ControllerBase
{
    /// <summary>
    /// <c>GET /api/accounts</c>: every account of the customer as a plain array, oldest first; <c>[]</c> when
    /// there are none. No paging: a customer has only a handful of accounts.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AccountDto>>> ListAsync(CancellationToken cancellationToken) =>
        Ok(await accounts.ListAsync(User.GetCustomerId(), cancellationToken));

    /// <summary>
    /// <c>GET /api/accounts/{id}</c>: one account of the customer. Another customer's account gets exactly the
    /// 404 of a missing one, so guessing ids reveals nothing. An id that is not a GUID is a 400.
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<AccountDto>> GetAsync(Guid id, CancellationToken cancellationToken) =>
        Ok(await accounts.GetAsync(User.GetCustomerId(), id, cancellationToken));

    /// <summary>
    /// <c>POST /api/accounts</c>: opens a personal, zero-balance account for the customer and answers 201 with
    /// its <c>Location</c> and the account exactly as <c>GET /api/accounts/{id}</c> returns it.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<AccountDto>> CreateAsync(CreateAccountRequest request,
        [FromHeader(Name = ApiProblems.IdempotencyKeyHeader)] string? idempotencyKey, CancellationToken cancellationToken)
    {
        // The key is optional here (decision 2-A): a duplicate empty account is harmless, a duplicate payment is not.
        var result = await accountWriter.CreateAsync(
            User.GetCustomerId(), new NewAccount(request.AccountName, request.CurrencyCode), idempotencyKey, cancellationToken);
        if (result.Replayed)
            Response.Headers[ApiProblems.ReplayedHeader] = "true";
        return CreatedAtAction(nameof(GetAsync), new { id = result.Account.Id }, result.Account);
    }
}
