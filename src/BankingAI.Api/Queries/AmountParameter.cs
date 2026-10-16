using System.Globalization;

namespace BankingAI.Api.Queries;

/// <summary>
/// Reads a money amount from the query string strictly: digits and an optional decimal point only (ANA-11).
/// </summary>
/// <remarks>
/// ASP.NET's own decimal binding reads the query with the invariant culture and allows thousands separators, so
/// "500,50" (the Turkish way of writing 500.50) would silently become 50050. Here any comma, sign, space or exponent
/// is rejected instead; the amount rules themselves (range, two decimals) stay with the service.
/// </remarks>
public static class AmountParameter
{
    /// <summary>The amount, or <c>null</c> when the query does not give one.</summary>
    /// <exception cref="ArgumentException">The value is not a plain decimal number such as <c>500</c> or <c>500.50</c>.</exception>
    public static decimal? Parse(string? value, string field)
    {
        if (value is null)
            return null;
        if (!decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount))
            throw new ArgumentException($"'{value}' geçerli bir değer değil.", field);
        return amount;
    }
}
