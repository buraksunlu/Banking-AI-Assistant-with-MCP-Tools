using BankingAI.Api.Security;
using BankingAI.Application;
using BankingAI.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BankingAI.Api.Controllers;

/// <summary>
/// <c>GET /api/me</c>: who the token belongs to. The internal customer id is deliberately left out; clients
/// never need it because every request is scoped by the token.
/// </summary>
public sealed record MeResponse(
    string Username, string CustomerNumber, string FirstName, string LastName, string PreferredCurrency, string TimeZone);

/// <summary>
/// <c>GET /api/me</c>: the first endpoint that requires a token (the API-wide default).
/// </summary>
[ApiController]
[Route("api/me")]
public sealed class MeController(BankingDbContext db) : ControllerBase
{
    /// <summary>
    /// Returns the logged-in customer. A valid token whose login no longer exists is a 401.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<MeResponse>> GetAsync(CancellationToken cancellationToken)
    {
        var customerId = User.GetCustomerId();
        var me = await (
                from customer in db.Customers.AsNoTracking()
                join credential in db.CustomerCredentials on customer.Id equals credential.CustomerId
                where customer.Id == customerId
                select new MeResponse(credential.Username, customer.CustomerNumber, customer.FirstName,
                    customer.LastName, customer.PreferredCurrency, customer.TimeZone))
            .SingleOrDefaultAsync(cancellationToken);

        // A genuine token whose customer (or login) is gone: the same "sign in again" answer as every other endpoint.
        return me is null ? throw new CustomerNotFoundException() : Ok(me);
    }
}
