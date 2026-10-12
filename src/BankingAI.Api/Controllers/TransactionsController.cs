using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using BankingAI.Data.Enums;
using BankingAI.Data.Validation;

namespace BankingAI.Api.Controllers;

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
