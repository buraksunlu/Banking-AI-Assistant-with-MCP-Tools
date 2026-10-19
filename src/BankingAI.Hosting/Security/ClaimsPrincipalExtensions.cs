using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;

namespace BankingAI.Hosting.Security;

/// <summary>
/// The only way controllers learn the current customer: from the validated token, never from the request.
/// </summary>
public static class ClaimsPrincipalExtensions
{
    /// <summary>Customer id from the token's <c>sub</c> claim.</summary>
    /// <exception cref="InvalidOperationException">The user is not authenticated or the claim is not a customer id.</exception>
    public static Guid GetCustomerId(this ClaimsPrincipal user)
    {
        // Every token we sign carries a GUID subject, so a missing one is a server bug, not a client error.
        if (user.Identity?.IsAuthenticated == true
            && Guid.TryParse(user.FindFirstValue(JwtRegisteredClaimNames.Sub), out var customerId))
            return customerId;
        throw new InvalidOperationException("The request has no authenticated customer.");
    }
}
