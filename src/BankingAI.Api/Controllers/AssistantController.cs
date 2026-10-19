using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using BankingAI.Api.Assistant;
using BankingAI.Api.Errors;
using BankingAI.Api.Security;
using BankingAI.Application.Customers;
using BankingAI.Data.Validation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BankingAI.Api.Controllers;

/// <summary>
/// Body of <c>POST /api/assistant/messages</c>: only the user's text. The customer comes from the token; any other
/// field (for example <c>customerId</c>) is a 400.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AssistantMessageRequest : IValidatableObject
{
    /// <summary>1–500 characters, not only whitespace; the same limit as the AI service.</summary>
    [Required(ErrorMessage = "Zorunlu alan.")]
    public string Message { get; init; } = "";

    /// <summary>The text rule of the database layer, reused rather than copied (counts characters, rejects NUL).</summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        // A missing or blank message is already reported by [Required].
        string.IsNullOrWhiteSpace(Message)
            ? []
            : ApiValidation.Rule(() => InputRules.Text(Message, 500, nameof(Message)), nameof(Message));
}

/// <summary>
/// The chat assistant (AI-14b). The API is the only door to it: the token, the customer and the request are
/// checked here, then the message goes to the AI service with the same token.
/// </summary>
[ApiController]
[Route("api/assistant")]
public sealed class AssistantController(ICustomerCheck customers, IAssistantClient assistant) : ControllerBase
{
    /// <summary>Name of the rate-limit policy: 10 messages per minute per customer (SEC-06).</summary>
    public const string RateLimitPolicy = "assistant";

    /// <summary>
    /// <c>POST /api/assistant/messages</c>: answers one question about the customer's own spending.
    /// </summary>
    [HttpPost("messages")]
    [EnableRateLimiting(RateLimitPolicy)]
    [ProducesResponseType<AssistantReply>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests, Description = "More than ten messages a minute from one customer.")]
    [ProducesResponseType(StatusCodes.Status502BadGateway, Description = "No valid reply came out (assistant_failed); asking differently may work.")]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable, Description = "The assistant is not running (assistant_unavailable).")]
    [ProducesResponseType(StatusCodes.Status504GatewayTimeout, Description = "The assistant did not answer in time (assistant_timeout).")]
    public async Task<ActionResult<AssistantReply>> SendAsync(AssistantMessageRequest request, CancellationToken cancellationToken)
    {
        // Checked before the model runs: a deleted customer must not keep the single local model busy.
        await customers.EnsureExistsAsync(User.GetCustomerId(), cancellationToken);
        return Ok(await assistant.SendAsync(request.Message, Request.BearerToken(), cancellationToken));
    }

    /// <summary>
    /// <c>GET /api/assistant/status</c>: whether the chat screen can take questions right now. Always 200; an
    /// unavailable assistant is a state, not an error (decision AI-14b K8-A).
    /// </summary>
    [HttpGet("status")]
    [ProducesResponseType<AssistantStatus>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AssistantStatus>> GetStatusAsync(CancellationToken cancellationToken)
    {
        await customers.EnsureExistsAsync(User.GetCustomerId(), cancellationToken);
        return Ok(new AssistantStatus(await assistant.IsReadyAsync(cancellationToken)));
    }
}
