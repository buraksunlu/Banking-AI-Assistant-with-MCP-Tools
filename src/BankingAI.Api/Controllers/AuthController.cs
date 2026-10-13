using System.ComponentModel.DataAnnotations;
using BankingAI.Api.Security;
using BankingAI.Data;
using BankingAI.Data.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BankingAI.Api.Controllers;

/// <summary>
/// Body of <c>POST /api/auth/login</c>. Both fields are required; a missing or blank one is a 400.
/// </summary>
public sealed record LoginRequest(
    [Required(ErrorMessage = "Zorunlu alan.")] string Username,
    [Required(ErrorMessage = "Zorunlu alan.")] string Password);

/// <summary>
/// A successful login: the bearer token and the moment it stops being accepted.
/// </summary>
public sealed record LoginResponse(string Token, DateTimeOffset ExpiresAt);

/// <summary>
/// <c>POST /api/auth/login</c>: exchanges a username and password for a bearer token.
/// </summary>
[ApiController]
[Route("api/auth")]
public sealed class AuthController(BankingDbContext db, TokenIssuer tokenIssuer) : ControllerBase
{
    /// <summary>Name of the rate-limit policy: 5 attempts per minute per client IP.</summary>
    public const string LoginRateLimitPolicy = "login";

    /// <summary>
    /// Returns 200 with a token for the right credentials. A wrong password and an unknown username get the
    /// same 401, so the response never reveals which usernames exist.
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(LoginRateLimitPolicy)]
    public async Task<ActionResult<LoginResponse>> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var customerId = await CredentialStore.VerifyAsync(db, request.Username, request.Password, cancellationToken);
        if (customerId is not { } id)
            return Unauthorized();

        var token = tokenIssuer.Issue(id);
        return Ok(new LoginResponse(token.Token, token.ExpiresAt));
    }
}
