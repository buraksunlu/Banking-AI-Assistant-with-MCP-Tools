namespace BankingAI.Api.Assistant;

/// <summary>
/// What kind of reply the assistant gave to one message.
/// </summary>
public enum AssistantReplyType
{
    /// <summary>The question was answered from backend figures.</summary>
    Answer,

    /// <summary>The assistant needs more detail and asks a question back (for example, which merchant).</summary>
    Clarification,

    /// <summary>The question is outside what the assistant can answer; the text says what it can do.</summary>
    OutOfScope
}

/// <summary>
/// A backend query the assistant ran for the answer, with the arguments it used, so the client can show how the
/// answer was found (AI-11) or link to the matching screen (CHAT-07).
/// </summary>
/// <param name="Tool">The tool name, for example <c>get_spending_summary</c>.</param>
/// <param name="Arguments">The checked arguments, for example <c>period = Yesterday</c>.</param>
public sealed record AssistantToolCall(string Tool, IReadOnlyDictionary<string, string> Arguments);

/// <summary>
/// One backend figure the answer is based on, already formatted for people ("Toplam harcama" / "145,00 TL").
/// </summary>
public sealed record AssistantFact(string Label, string Text);

/// <summary>
/// Reply of <c>POST /api/assistant/messages</c>. The figures in <c>answer</c> come from <c>facts</c>, which the
/// backend computed; the model only chose the tool and wrote the sentence.
/// </summary>
public sealed record AssistantReply(
    AssistantReplyType Type,
    string Answer,
    IReadOnlyList<AssistantToolCall> ToolCalls,
    IReadOnlyList<AssistantFact> Facts);

/// <summary>
/// Reply of <c>GET /api/assistant/status</c>: whether the chat screen can take questions right now.
/// </summary>
public sealed record AssistantStatus(bool Available);
