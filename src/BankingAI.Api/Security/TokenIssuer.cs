using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace BankingAI.Api.Security;

/// <summary>
/// A signed bearer token and the moment it expires.
/// </summary>
public sealed record IssuedToken(string Token, DateTimeOffset ExpiresAt);

/// <summary>
/// Creates the HS256 token returned by a successful login. The customer id travels only as the
/// <c>sub</c> claim; nothing else about the customer is put into the token.
/// </summary>
public sealed class TokenIssuer(JwtSettings settings, TimeProvider timeProvider)
{
    private readonly JsonWebTokenHandler handler = new();

    /// <summary>Issues a token for <paramref name="customerId"/> that is valid for <see cref="JwtSettings.Lifetime"/>.</summary>
    public IssuedToken Issue(Guid customerId)
    {
        // Token times are stored in whole seconds; truncating here keeps ExpiresAt equal to the token's own expiry.
        var now = timeProvider.GetUtcNow();
        var issuedAt = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerSecond));
        var expiresAt = issuedAt + JwtSettings.Lifetime;

        var token = handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = JwtSettings.Issuer,
            Audience = JwtSettings.Audience,
            Subject = new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Sub, customerId.ToString())]),
            IssuedAt = issuedAt.UtcDateTime,
            NotBefore = issuedAt.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = new SigningCredentials(settings.SigningKey, SecurityAlgorithms.HmacSha256)
        });
        return new IssuedToken(token, expiresAt);
    }
}
