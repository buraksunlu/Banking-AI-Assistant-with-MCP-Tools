namespace BankingAI.Api.Queries;

/// <summary>
/// Reads an enum from the query string by its exact name, like enums in JSON (decision API-13 4-A). ASP.NET's own
/// binding would also accept a number ("1") or another casing ("purchase").
/// </summary>
public static class ExactEnum
{
    /// <summary>
    /// The member named exactly <paramref name="value"/>, or <c>null</c> when the query does not give one.
    /// </summary>
    /// <param name="value">The query string value.</param>
    /// <param name="field">The client's field name, used in the error.</param>
    /// <param name="message">The error for a value that is not an accepted name.</param>
    /// <param name="excluded">Members that exist but may not be asked for by name here (e.g. <c>Custom</c>).</param>
    /// <exception cref="ArgumentException">The value is not an accepted name.</exception>
    public static TEnum? Parse<TEnum>(string? value, string field, string message, params TEnum[] excluded)
        where TEnum : struct, Enum
    {
        if (value is null)
            return null;
        if (!Enum.GetNames<TEnum>().Contains(value) || excluded.Any(e => e.ToString() == value))
            throw new ArgumentException(message, field);
        return Enum.Parse<TEnum>(value);
    }
}
