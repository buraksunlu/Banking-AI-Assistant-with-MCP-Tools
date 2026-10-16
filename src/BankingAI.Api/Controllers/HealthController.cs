using BankingAI.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BankingAI.Api.Controllers;

/// <summary>
/// Liveness and database reachability of the API.
/// </summary>
/// <param name="Status"><c>ok</c> when the database is reachable, otherwise <c>unavailable</c>.</param>
/// <param name="Database">Whether a connection to PostgreSQL could be opened.</param>
public sealed record HealthResponse(string Status, bool Database);

/// <summary>
/// <c>GET /api/health</c>: proves the API is running and its injected DbContext can reach PostgreSQL.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/health")]
public sealed class HealthController(BankingDbContext db) : ControllerBase
{
    /// <summary>
    /// Returns 200 when the database is reachable and 503 when it is not.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<HealthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable, Description = "The database cannot be reached.")]
    public async Task<ActionResult<HealthResponse>> GetAsync(CancellationToken cancellationToken)
    {
        // CanConnectAsync returns false instead of throwing when the database is down.
        if (await db.Database.CanConnectAsync(cancellationToken))
            return Ok(new HealthResponse("ok", true));

        return StatusCode(StatusCodes.Status503ServiceUnavailable, new HealthResponse("unavailable", false));
    }
}
