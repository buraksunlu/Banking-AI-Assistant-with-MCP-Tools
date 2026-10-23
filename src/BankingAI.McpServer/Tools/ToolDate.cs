using System.Globalization;

namespace BankingAI.McpServer.Tools;

/// <summary>
/// Reads the <c>from</c>/<c>to</c> text of a tool call (CHAT-08). Only the exact <c>YYYY-MM-DD</c> form of a real day is
/// accepted; the Turkish ways of writing a date are the AI service's to read, never the server's to guess.
/// </summary>
internal static class ToolDate
{
    /// <summary>The day, or null when the argument was left out.</summary>
    /// <exception cref="ArgumentException">The text is not a <c>YYYY-MM-DD</c> day; the message is written for the model and the log.</exception>
    public static DateOnly? Read(string? text, string name)
    {
        if (text is null)
            return null;
        if (DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            return day;
        throw new ArgumentException($"{name} YYYY-MM-DD biçiminde geçerli bir gün olmalı.", name);
    }
}
