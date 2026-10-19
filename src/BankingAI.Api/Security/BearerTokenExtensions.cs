namespace BankingAI.Api.Security;

/// <summary>
/// Reads the caller's own token so it can be passed on to the AI service (docs/security.md): the AI service calls
/// the API back with it, and the token never goes into a prompt or a log.
/// </summary>
public static class BearerTokenExtensions
{
    /// <summary>The token of an authenticated request's <c>Authorization: Bearer</c> header.</summary>
    /// <exception cref="InvalidOperationException">The request has no bearer token.</exception>
    public static string BearerToken(this HttpRequest request)
    {
        // Only called after JWT validation, which accepted this very header.
        const string scheme = "Bearer ";
        var header = request.Headers.Authorization.ToString();
        return header.StartsWith(scheme, StringComparison.OrdinalIgnoreCase) && header.Length > scheme.Length
            ? header[scheme.Length..].Trim()
            : throw new InvalidOperationException("The request has no bearer token.");
    }
}
