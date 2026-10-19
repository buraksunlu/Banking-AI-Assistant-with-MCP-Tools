using System.Security.Claims;
using System.Text;
using BankingAI.Hosting.Security;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace BankingAI.TestSupport;

/// <summary>
/// Signs tokens independently of the API's own issuer, so tests can forge expired or wrongly signed ones.
/// </summary>
internal static class TestTokens
{
    public static string Create(Guid customerId, string? signingKey = null, DateTime? expiresUtc = null)
    {
        var key = signingKey ?? Environment.GetEnvironmentVariable(JwtSettings.SigningKeyKey)!;
        var expires = expiresUtc ?? DateTime.UtcNow.AddMinutes(10);
        var issued = expires.AddMinutes(-60);
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = JwtSettings.Issuer,
            Audience = JwtSettings.Audience,
            Subject = new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Sub, customerId.ToString())]),
            IssuedAt = issued,
            NotBefore = issued,
            Expires = expires,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256)
        });
    }
}
