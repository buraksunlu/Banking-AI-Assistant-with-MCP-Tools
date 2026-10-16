namespace BankingAI.Api.Queries;

/// <summary>
/// A query value the request must give. Bound as nullable so a missing value is answered with the endpoint's own
/// message instead of being taken as 0 or a default.
/// </summary>
public static class RequiredParameter
{
    /// <summary>The value, when present.</summary>
    /// <exception cref="ArgumentException">The value is missing; the parameter name is the client's field name.</exception>
    public static T Value<T>(T? value, string field, string missingMessage) where T : struct =>
        value ?? throw new ArgumentException(missingMessage, field);
}
