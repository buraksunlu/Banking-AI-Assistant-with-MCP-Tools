namespace BankingAI.Api.Assistant;

/// <summary>
/// Why a message could not be answered, grouped by what the client should do next (decision AI-14b K5-A).
/// </summary>
public enum AssistantFailure
{
    /// <summary>The assistant is not running or its model is missing: show the ready-made screens instead.</summary>
    Unavailable,

    /// <summary>The assistant did not answer in time: trying again may work.</summary>
    Timeout,

    /// <summary>The assistant answered but no valid reply came out: asking differently may work.</summary>
    Failed
}

/// <summary>
/// The AI service could not produce a reply. The API answers it with 503, 504 or 502; the AI service's own error
/// code stays in the server log.
/// </summary>
public sealed class AssistantException(AssistantFailure failure, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    /// <summary>What went wrong, as the client sees it.</summary>
    public AssistantFailure Failure { get; } = failure;
}
