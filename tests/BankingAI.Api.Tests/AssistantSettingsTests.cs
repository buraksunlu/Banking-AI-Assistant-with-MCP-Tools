using BankingAI.Api.Assistant;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace BankingAI.Api.Tests;

// AI-07: where the AI service is and how long a chat turn may take. Missing values take defaults (decision K7-C), so the
// banking API starts without the AI service; a value that is set but wrong stops the API at startup.
public sealed class AssistantSettingsTests
{
    private static AssistantSettings Read(string? url = null, string? timeout = null) =>
        AssistantSettings.FromConfiguration(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [AssistantSettings.UrlKey] = url,
                [AssistantSettings.TimeoutKey] = timeout
            })
            .Build());

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_missing_address_is_the_local_ai_service(string? url)
    {
        Assert.Equal(new Uri("http://127.0.0.1:8000/"), Read(url: url).BaseAddress);
    }

    [Theory]
    [InlineData("http://localhost:9000", "http://localhost:9000/")]
    [InlineData("http://localhost:9000/", "http://localhost:9000/")]
    // Without the trailing slash, "v1/chat" would replace "ai" instead of being added after it.
    [InlineData("https://ai.example.test/ai", "https://ai.example.test/ai/")]
    public void The_address_always_ends_in_a_slash(string url, string expected)
    {
        Assert.Equal(new Uri(expected), Read(url: url).BaseAddress);
    }

    [Theory]
    [InlineData("localhost:8000")]
    [InlineData("not an address")]
    [InlineData("ftp://127.0.0.1:8000")]
    [InlineData("/v1")]
    public void An_address_that_is_not_http_stops_startup(string url)
    {
        var error = Assert.Throws<InvalidOperationException>(() => Read(url: url));
        Assert.Contains(AssistantSettings.UrlKey, error.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void A_missing_timeout_is_35_seconds(string? timeout)
    {
        Assert.Equal(TimeSpan.FromSeconds(35), Read(timeout: timeout).Timeout);
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData("60", 60)]
    public void The_timeout_is_read_in_whole_seconds(string timeout, int seconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(seconds), Read(timeout: timeout).Timeout);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("abc")]
    [InlineData("1.5")]
    [InlineData("1,5")]
    [InlineData("601")]
    public void A_timeout_that_is_not_1_to_600_whole_seconds_stops_startup(string timeout)
    {
        var error = Assert.Throws<InvalidOperationException>(() => Read(timeout: timeout));
        Assert.Contains(AssistantSettings.TimeoutKey, error.Message);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task A_wrong_timeout_stops_the_api_itself_at_startup()
    {
        await using var api = TestApi.Create((AssistantSettings.TimeoutKey, "0"));

        var error = Assert.Throws<InvalidOperationException>(() => api.CreateClient());
        Assert.Contains(AssistantSettings.TimeoutKey, error.Message);
    }
}
