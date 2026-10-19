using System.Text.Json;
using BankingAI.Application;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace BankingAI.McpServer.Tools;

/// <summary>
/// The tool error contract (MCP-a, decision K10-A): a failed call is an MCP tool error whose text is
/// <c>{"code": …, "message": …}</c>, so the AI service can tell "the model's arguments were wrong" from "sign in again"
/// from "something broke on our side". The only place exceptions become tool errors.
/// </summary>
public static class ToolErrors
{
    /// <summary>The model's arguments do not follow the tool's schema or a backend input rule.</summary>
    public const string InvalidArguments = "invalid_arguments";

    /// <summary>The token is genuine but its customer no longer exists.</summary>
    public const string SessionInvalid = "session_invalid";

    /// <summary>Anything else; the details stay in the server log.</summary>
    public const string InternalError = "internal_error";

    /// <summary>The message of <see cref="InternalError"/>, the same as the API's 500.</summary>
    public const string InternalErrorMessage = "Beklenmeyen bir hata oluştu.";

    /// <summary>
    /// Runs a tool body and turns its exceptions into the contract. The SDK would otherwise replace every exception with
    /// the same generic text, so the code would be lost.
    /// </summary>
    public static async Task<T> RunAsync<T>(Func<Task<T>> body, ILogger logger)
    {
        try
        {
            return await body();
        }
        catch (CustomerNotFoundException)
        {
            throw Error(SessionInvalid, "Oturum geçersiz; yeniden giriş yapın.");
        }
        // Service input rules are ArgumentExceptions whose messages are written for the client (as in the API).
        catch (ArgumentException error)
        {
            // .NET appends " (Parameter 'x')" to the message; the client never sees it, as in the API.
            var message = error.ParamName is null ? error.Message : error.Message.Replace($" (Parameter '{error.ParamName}')", "");
            throw Error(InvalidArguments, message);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            logger.LogError(error, "Tool failed unexpectedly");
            throw Error(InternalError, InternalErrorMessage);
        }
    }

    /// <summary>A tool error of the contract thrown from a tool body; <see cref="ToolCallFilter"/> turns it into the result.</summary>
    public static McpException Error(string code, string message) => new(Json(code, message));

    /// <summary>A tool error of the contract as a finished result, for the filter that answers without running a tool.</summary>
    public static CallToolResult Result(string code, string message) =>
        new() { IsError = true, Content = [new TextContentBlock { Text = Json(code, message) }] };

    private static string Json(string code, string message) => JsonSerializer.Serialize(new { code, message });
}
