using System.Globalization;

namespace BankingAI.Api.Assistant;

/// <summary>
/// Where the AI service listens and how long one chat turn may take (AI-07).
/// </summary>
/// <param name="BaseAddress">The AI service root, always ending in a slash.</param>
/// <param name="Timeout">How long the API waits for one chat reply.</param>
public sealed record AssistantSettings(Uri BaseAddress, TimeSpan Timeout)
{
    /// <summary>Configuration key of the AI service address.</summary>
    public const string UrlKey = "BANKINGAI_AI_URL";

    /// <summary>Configuration key of the chat timeout in whole seconds.</summary>
    public const string TimeoutKey = "BANKINGAI_AI_TIMEOUT_SECONDS";

    /// <summary>Where the AI service is started locally (decision AI-14b K7-C).</summary>
    public static readonly Uri DefaultBaseAddress = new("http://127.0.0.1:8000/");

    /// <summary>
    /// A little more than the AI service's own 30-second limit, so its more specific error arrives first
    /// (decision AI-14b K4-B).
    /// </summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(35);

    /// <summary>
    /// Reads the settings; a missing value takes its default.
    /// </summary>
    /// <exception cref="InvalidOperationException">A value is set but invalid.</exception>
    public static AssistantSettings FromConfiguration(IConfiguration configuration) =>
        new(ReadBaseAddress(configuration[UrlKey]), ReadTimeout(configuration[TimeoutKey]));

    // A missing address is the local default, so the banking API starts without the AI service; a wrong one is a
    // configuration mistake and stops the API instead of turning every chat into a 503.
    private static Uri ReadBaseAddress(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return DefaultBaseAddress;
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var address) || address.Scheme is not ("http" or "https"))
            throw new InvalidOperationException($"{UrlKey} must be an absolute http or https address.");
        // Relative paths ("v1/chat") are appended only after a trailing slash.
        return address.AbsoluteUri.EndsWith('/') ? address : new Uri(address.AbsoluteUri + "/");
    }

    private static TimeSpan ReadTimeout(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return DefaultTimeout;
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) || seconds is < 1 or > 600)
            throw new InvalidOperationException($"{TimeoutKey} must be a whole number of seconds from 1 to 600.");
        return TimeSpan.FromSeconds(seconds);
    }
}
