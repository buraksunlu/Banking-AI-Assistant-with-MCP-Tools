using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

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
