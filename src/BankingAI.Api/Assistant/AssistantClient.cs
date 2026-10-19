using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BankingAI.Application;

namespace BankingAI.Api.Assistant;

/// <summary>
/// The API's connection to the AI service (AI-07). Controllers depend on this contract rather than on the class.
/// </summary>
public interface IAssistantClient
{
    /// <summary>
    /// Sends one chat message with the caller's bearer token and returns the assistant's reply.
    /// </summary>
    /// <exception cref="AssistantException">No reply could be produced.</exception>
    /// <exception cref="CustomerNotFoundException">The AI service was refused the token.</exception>
    Task<AssistantReply> SendAsync(string message, string bearerToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether the AI service and its model are ready. Never throws for an unreachable or slow service.
    /// </summary>
    Task<bool> IsReadyAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// <see cref="IAssistantClient"/> over HTTP to the FastAPI service (<c>POST /v1/chat</c>, <c>GET /health/ready</c>).
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
    public async Task<AssistantReply> SendAsync(string message, string bearerToken, CancellationToken cancellationToken = default)
    {
        // Our own deadline on top of the caller's: when the client leaves, the call is cancelled as well, and the two
        // causes stay apart because only this one is turned into a 504.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(settings.Timeout);
        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/chat")
        {
            Content = JsonContent.Create(new ChatRequest(message), options: Json)
        };
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

            var reply = Parse(body, out var model);
            logger.LogInformation(
                "Assistant reply {Type} via [{Tools}] by {Model} in {ElapsedMs} ms for a {Length}-character message",
                reply.Type, string.Join(", ", reply.ToolCalls.Select(c => c.Tool)), model, elapsedMs, message.Length);
            return reply;
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
        if (reply is { Type: not null, Answer: not null, ToolCalls: not null, Facts: not null }
            && ReplyTypes.TryGetValue(reply.Type, out var type)
            && reply.ToolCalls.All(c => c is { Tool: not null, Arguments: not null })
            && reply.Facts.All(f => f is { Label: not null, Text: not null }))
        {
            return new AssistantReply(
                type, reply.Answer,
                // Checked just above; the compiler cannot follow the checks inside All().
                reply.ToolCalls.Select(c => new AssistantToolCall(c!.Tool!, c.Arguments!)).ToList(),
                reply.Facts.Select(f => new AssistantFact(f!.Label!, f.Text!)).ToList());
        }

        logger.LogWarning("AI service reply does not match the chat contract");
        throw new AssistantException(AssistantFailure.Failed, "The AI service reply does not match the chat contract.");
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
    private sealed record ChatRequest(string Message);

    private sealed record ChatResponse(string? Type, string? Answer, List<ChatToolCall?>? ToolCalls, List<ChatFact?>? Facts, string? Model);

    private sealed record ChatToolCall(string? Tool, Dictionary<string, string>? Arguments);

    private sealed record ChatFact(string? Label, string? Text);
}
