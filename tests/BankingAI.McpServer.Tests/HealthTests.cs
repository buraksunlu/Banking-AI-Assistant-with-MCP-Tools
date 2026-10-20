using System.Net;
using System.Text.Json;
using BankingAI.Hosting;
using Xunit;

namespace BankingAI.McpServer.Tests;

// MCP-b K6-C: the AI service's readiness asks this, so "assistant available" also means "its tools can answer". Open
// without a token, like the API's /api/health; it reveals nothing about any customer.
public sealed class HealthTests
{
    private static async Task<(HttpStatusCode Status, string Body)> GetAsync(params (string Key, string Value)[] settings)
    {
        await using var server = McpTestServer.Create(settings: settings);
        using var http = server.CreateClient();
        var response = await http.GetAsync("/health");
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Health_is_200_without_a_token_when_the_database_answers()
    {
        var (status, body) = await GetAsync();

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("""{"status":"ok","database":true}""", JsonDocument.Parse(body).RootElement.GetRawText());
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Health_is_503_when_the_database_cannot_be_reached()
    {
        var (status, body) = await GetAsync(
            (BankingServiceCollectionExtensions.ConnectionStringKey, "Host=127.0.0.1;Port=1;Database=x;Username=x;Password=x;Timeout=1"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
        Assert.Equal("""{"status":"unavailable","database":false}""", JsonDocument.Parse(body).RootElement.GetRawText());
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Only_health_is_open_without_a_token()
    {
        await using var server = McpTestServer.Create();
        using var http = server.CreateClient();

        // Any other path, existing or not, is refused before routing reveals anything.
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/mcp")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/does-not-exist")).StatusCode);
    }
}
