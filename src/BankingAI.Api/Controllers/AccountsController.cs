using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using BankingAI.Api.Security;
using BankingAI.Application.Accounts;
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
public sealed record CreateAccountRequest
{
    /// <summary>1–100 characters, not only whitespace (same rule as the database).</summary>
    [Required]
    [StringLength(100)]
    public string AccountName { get; init; } = "";

    /// <summary>Exactly one of the account currencies the database accepts; "try" is not corrected.</summary>
    [Required]
    [AllowedValues("TRY", "USD", "EUR", "GBP")]
    public string CurrencyCode { get; init; } = "";
}

/// <summary>
/// The logged-in customer's accounts. The customer always comes from the token; there is no customer
/// parameter to tamper with.
/// </summary>
[ApiController]
[Route("api/accounts")]
public sealed class AccountsController(IAccountReadService accounts) : ControllerBase
{
    /// <summary>
    /// <c>GET /api/accounts</c>: every account of the customer as a plain array, oldest first; <c>[]</c> when
    /// there are none. No paging: a customer has only a handful of accounts.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AccountDto>>> ListAsync(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await accounts.ListAsync(User.GetCustomerId(), cancellationToken));
        }
        catch (KeyNotFoundException)
        {
            // The token is genuine but its customer no longer exists: same answer as GET /api/me, so the
            // client sends the user back to the login screen. Moves to the shared error mapping in API-13.
            return Unauthorized();
        }
    }
}
