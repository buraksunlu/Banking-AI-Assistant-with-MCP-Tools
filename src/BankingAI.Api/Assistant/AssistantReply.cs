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
/// One option of a question back, shown as a tappable row under it (CHAT-10 K4-A). Tapping it sends <c>label</c> as the
/// next message, exactly as written; the AI service matches it in code, so typing the same words works too (K6-A).
/// </summary>
/// <param name="Label">What the row says and what a tap sends, for example <c>geçen ay</c> or <c>1.000 TL</c>.</param>
/// <param name="Detail">What the option is, for a choice between two calculations; empty when the label says it alone.</param>
public sealed record AssistantChoice(string Label, string Detail);

/// <summary>
/// Reply of <c>POST /api/assistant/messages</c>. The figures in <c>answer</c> come from <c>facts</c>, which the
/// backend computed; the model only chose the tool and wrote the sentence. <c>suggestions</c> are up to three questions
/// to offer under the reply, from a fixed table (CHAT-06); sent back as written, one runs the call it stands for.
/// <c>links</c> are the buttons under an answer that open the list of transactions behind its figure (CHAT-07); the
/// backend builds them from the checked tool calls and the AI service never sees them. <c>choices</c> are a question
/// back's options as tappable rows (CHAT-10), empty in any other reply; the <c>answer</c> of a question then holds only
/// the question, not the options. <c>inputHint</c> is a last row inviting an answer of the user's own ("Başka bir tutar
/// yazın"), which opens the keyboard instead of sending anything; empty when the options are all there is (CHAT-10 PR3b).
/// </summary>
public sealed record AssistantReply(
    AssistantReplyType Type,
    string Answer,
    IReadOnlyList<AssistantToolCall> ToolCalls,
    IReadOnlyList<AssistantFact> Facts,
    IReadOnlyList<string> Suggestions,
    IReadOnlyList<AssistantChoice> Choices,
    string InputHint,
    IReadOnlyList<AssistantLink> Links);

/// <summary>
/// Reply of <c>GET /api/assistant/suggestions</c>: up to three questions to show when the chat opens, chosen from the
/// customer's own data without a model (CHAT-06).
/// </summary>
public sealed record AssistantSuggestions(IReadOnlyList<string> Suggestions);

/// <summary>
/// Reply of <c>GET /api/assistant/status</c>: whether the chat screen can take questions right now.
/// </summary>
public sealed record AssistantStatus(bool Available);
