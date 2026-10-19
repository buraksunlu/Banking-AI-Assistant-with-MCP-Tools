using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace BankingAI.Hosting.Security;

/// <summary>
/// Token settings of the API: fixed issuer/audience/lifetime and the signing key from configuration.
/// </summary>
public sealed class JwtSettings
{
    public const string SigningKeyKey = "BANKINGAI_JWT_SIGNING_KEY";
    public const string Issuer = "BankingAI.Api";
    public const string Audience = "BankingAI";
    public const int MinimumKeyBytes = 32; // HS256 needs a key of at least 256 bits.
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(60);

    private JwtSettings(SymmetricSecurityKey signingKey) => SigningKey = signingKey;

    /// <summary>The HS256 key that signs and validates every token.</summary>
    public SymmetricSecurityKey SigningKey { get; }

    /// <summary>
    /// Reads the signing key from <see cref="SigningKeyKey"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">The key is missing or shorter than 32 bytes.</exception>
    public static JwtSettings FromConfiguration(IConfiguration configuration)
    {
        var key = configuration[SigningKeyKey];
        if (string.IsNullOrWhiteSpace(key) || Encoding.UTF8.GetByteCount(key) < MinimumKeyBytes)
            throw new InvalidOperationException($"Set {SigningKeyKey} to a random value of at least {MinimumKeyBytes} bytes before starting the API.");
        return new JwtSettings(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)));
    }

    /// <summary>
    /// Rules every incoming token must pass: our signature with HS256 only, our issuer and audience, not expired.
    /// </summary>
    public TokenValidationParameters ValidationParameters() => new()
    {
        ValidIssuer = Issuer,
        ValidAudience = Audience,
        IssuerSigningKey = SigningKey,
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
        // The default 5-minute tolerance would make a 60-minute token live 65 minutes; issuer and validator
        // are the same server, so there is no clock difference to tolerate.
        ClockSkew = TimeSpan.Zero
    };
}
