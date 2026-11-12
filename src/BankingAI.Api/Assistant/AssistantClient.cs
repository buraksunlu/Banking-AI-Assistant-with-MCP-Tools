using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BankingAI.Application;
using BankingAI.Application.Customers;

namespace BankingAI.Api.Assistant;

/// <summary>
/// The API's connection to the AI service (AI-07). Controllers depend on this contract rather than on the class.
/// </summary>
public interface IAssistantClient
{
    /// <summary>
    /// Sends one chat message with the caller's bearer token and returns the assistant's reply. <paramref name="day"/> is the
    /// customer's own today and time zone: the AI service reads dates against it and keeps no clock of its own (CHAT-08 K9-C).
    /// </summary>
    /// <exception cref="AssistantException">No reply could be produced.</exception>
    /// <exception cref="CustomerNotFoundException">The AI service was refused the token.</exception>
    Task<AssistantReply> SendAsync(string message, CustomerDay day, string bearerToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// The questions to show when the chat opens, for the customer of the bearer token (CHAT-06).
    /// </summary>
    /// <exception cref="AssistantException">No questions could be produced.</exception>
    /// <exception cref="CustomerNotFoundException">The AI service was refused the token.</exception>
    Task<AssistantSuggestions> GetSuggestionsAsync(string bearerToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether the AI service and its model are ready. Never throws for an unreachable or slow service.
    /// </summary>
    Task<bool> IsReadyAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// <see cref="IAssistantClient"/> over HTTP to the FastAPI service (<c>POST /v1/chat</c>, <c>GET /v1/suggestions</c>,
/// <c>GET /health/ready</c>).
/// The message and the token are never logged (decision AI-14b K9-A); the log tells a turn's type, tools, model, time
/// and error code instead.
/// </summary>
public sealed class AssistantClient(HttpClient http, AssistantSettings settings, ILogger<AssistantClient> logger) : IAssistantClient
{
    /// <summary>
    /// The AI service's own readiness check gives up after 3 seconds; a little more covers the connection itself.
    /// </summary>
    public static readonly TimeSpan ReadinessTimeout = TimeSpan.FromSeconds(5);

    private static readonly JsonSerializerOptions Json = JsonSerializerOptions.Web;

    private static readonly Dictionary<string, AssistantReplyType> ReplyTypes = new(StringComparer.Ordinal)
    {
        ["answer"] = AssistantReplyType.Answer,
        ["clarification"] = AssistantReplyType.Clarification,
        ["outOfScope"] = AssistantReplyType.OutOfScope
    };

    /// <inheritdoc />
    public async Task<AssistantReply> SendAsync(string message, CustomerDay day, string bearerToken, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/chat")
        {
            Content = JsonContent.Create(new ChatRequest(message, day.Today, day.TimeZone), options: Json)
        };
        var (body, elapsedMs) = await ExchangeAsync(request, bearerToken, cancellationToken);

        var reply = Parse(body, out var model);
        logger.LogInformation(
            "Assistant reply {Type} via [{Tools}] by {Model} in {ElapsedMs} ms for a {Length}-character message",
            reply.Type, string.Join(", ", reply.ToolCalls.Select(c => c.Tool)), model, elapsedMs, message.Length);
        return reply;
    }

    /// <inheritdoc />
    public async Task<AssistantSuggestions> GetSuggestionsAsync(string bearerToken, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "v1/suggestions");
        var (body, elapsedMs) = await ExchangeAsync(request, bearerToken, cancellationToken);

        var suggestions = ParseSuggestions(body);
        logger.LogInformation("Assistant starters: {Count} in {ElapsedMs} ms", suggestions.Suggestions.Count, elapsedMs);
        return suggestions;
    }

    // One call to the AI service with the caller's token; a reply other than 200 becomes the error the client acts on.
    private async Task<(string Body, long ElapsedMs)> ExchangeAsync(
        HttpRequestMessage request, string bearerToken, CancellationToken cancellationToken)
    {
        // Our own deadline on top of the caller's: when the client leaves, the call is cancelled as well, and the two
        // causes stay apart because only this one is turned into a 504.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(settings.Timeout);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        var started = Stopwatch.GetTimestamp();

        HttpResponseMessage response;
        string body;
        try
        {
            response = await http.SendAsync(request, deadline.Token);
            body = await response.Content.ReadAsStringAsync(deadline.Token);
        }
        catch (OperationCanceledException error) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("AI service gave no reply within {TimeoutSeconds} s", settings.Timeout.TotalSeconds);
            throw new AssistantException(AssistantFailure.Timeout, "The AI service did not answer in time.", error);
        }
        catch (HttpRequestException error)
        {
            logger.LogWarning("AI service cannot be reached: {Error}", error.Message);
            throw new AssistantException(AssistantFailure.Unavailable, "The AI service cannot be reached.", error);
        }

        using (response)
        {
            var elapsedMs = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            if (response.StatusCode != HttpStatusCode.OK)
                throw Failure(response.StatusCode, body, elapsedMs);
            return (body, elapsedMs);
        }
    }

    /// <inheritdoc />
    public async Task<bool> IsReadyAsync(CancellationToken cancellationToken = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(ReadinessTimeout);
        try
        {
            // No token: a health check needs no customer, so the token stays with the API.
            using var response = await http.GetAsync("health/ready", deadline.Token);
            if (response.StatusCode == HttpStatusCode.OK)
                return true;
            logger.LogInformation("AI service is not ready: {Status} {Code}", (int)response.StatusCode,
                ProblemCode(await response.Content.ReadAsStringAsync(deadline.Token)));
            return false;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation("AI service readiness gave no answer within {TimeoutSeconds} s", ReadinessTimeout.TotalSeconds);
            return false;
        }
        catch (HttpRequestException error)
        {
            logger.LogInformation("AI service cannot be reached: {Error}", error.Message);
            return false;
        }
    }

    // What the client should do decides the code (K5-A): wait (503), try again (504) or ask differently (502). Anything
    // the contract does not expect from the AI service is our mismatch, never the client's fault, so it is a 502 too.
    private Exception Failure(HttpStatusCode status, string body, long elapsedMs)
    {
        var code = ProblemCode(body);
        logger.LogWarning("AI service answered {Status} {Code} after {ElapsedMs} ms", (int)status, code, elapsedMs);
        return status switch
        {
            // The API refused the token when the AI service called it back: the customer is gone, so "sign in again".
            HttpStatusCode.Unauthorized => new CustomerNotFoundException(),
            HttpStatusCode.ServiceUnavailable => new AssistantException(AssistantFailure.Unavailable, $"The AI service is unavailable ({code})."),
            HttpStatusCode.GatewayTimeout => new AssistantException(AssistantFailure.Timeout, $"The AI service timed out ({code})."),
            _ => new AssistantException(AssistantFailure.Failed, $"The AI service answered {(int)status} ({code}).")
        };
    }

    private AssistantReply Parse(string body, out string? model)
    {
        ChatResponse? reply;
        try
        {
            reply = JsonSerializer.Deserialize<ChatResponse>(body, Json);
        }
        catch (JsonException)
        {
            reply = null;
        }

        model = reply?.Model;
        if (reply is { Type: not null, Answer: not null, ToolCalls: not null, Facts: not null, Suggestions: not null, Choices: not null }
            && ReplyTypes.TryGetValue(reply.Type, out var type)
            && reply.ToolCalls.All(c => c is { Tool: not null, Arguments: not null })
            && reply.Facts.All(f => f is { Label: not null, Text: not null })
            && reply.Suggestions.All(s => s is not null)
            && reply.Choices.All(c => c is { Label: not null, Detail: not null }))
        {
            return new AssistantReply(
                type, reply.Answer,
                // Checked just above; the compiler cannot follow the checks inside All().
                reply.ToolCalls.Select(c => new AssistantToolCall(c!.Tool!, c.Arguments!)).ToList(),
                reply.Facts.Select(f => new AssistantFact(f!.Label!, f.Text!)).ToList(),
                reply.Suggestions.Select(s => s!).ToList(),
                reply.Choices.Select(c => new AssistantChoice(c!.Label!, c.Detail!)).ToList(),
                // Built by the controller from the checked tool calls (CHAT-07), never read from the AI service.
                []);
        }

        logger.LogWarning("AI service reply does not match the chat contract");
        throw new AssistantException(AssistantFailure.Failed, "The AI service reply does not match the chat contract.");
    }

    private AssistantSuggestions ParseSuggestions(string body)
    {
        SuggestionsResponse? reply;
        try
        {
            reply = JsonSerializer.Deserialize<SuggestionsResponse>(body, Json);
        }
        catch (JsonException)
        {
            reply = null;
        }

        if (reply is { Suggestions: not null } && reply.Suggestions.All(s => s is not null))
            return new AssistantSuggestions(reply.Suggestions.Select(s => s!).ToList());

        logger.LogWarning("AI service starters do not match the contract");
        throw new AssistantException(AssistantFailure.Failed, "The AI service starters do not match the contract.");
    }

    // The AI service's problem bodies carry only a code and a Turkish title, never customer data, so the code may be logged.
    private static string ProblemCode(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.ValueKind == JsonValueKind.Object
                   && document.RootElement.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.String
                ? code.GetString()!
                : "no code";
        }
        catch (JsonException)
        {
            return "no code";
        }
    }

    // The AI service's wire format (ai/service/contracts.py). Every field is nullable so a missing one is caught above
    // as a contract mismatch instead of becoming a silent default.
    private sealed record ChatRequest(string Message, DateOnly Today, string TimeZone);

    private sealed record ChatResponse(
        string? Type, string? Answer, List<ChatToolCall?>? ToolCalls, List<ChatFact?>? Facts, List<string?>? Suggestions, List<ChatChoice?>? Choices,
        string? Model);

    private sealed record SuggestionsResponse(List<string?>? Suggestions);

    private sealed record ChatToolCall(string? Tool, Dictionary<string, string>? Arguments);

    private sealed record ChatFact(string? Label, string? Text);

    private sealed record ChatChoice(string? Label, string? Detail);
}
