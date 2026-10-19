using System.Globalization;
using System.Text.Json;
using BankingAI.Hosting;
using BankingAI.Hosting.Security;
using BankingAI.Hosting.Time;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;
using static BankingAI.Application.Tests.DemoData;

namespace BankingAI.McpServer.Tests;

/// <summary>
/// The MCP server in memory with the demo moment fixed, and a real MCP client connected to it over HTTP (K11-A).
/// </summary>
internal static class McpTestServer
{
    public static WebApplicationFactory<Program> Create(CapturedLogs? logs = null, params (string Key, string Value)[] settings)
    {
        Assert.False(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(BankingServiceCollectionExtensions.ConnectionStringKey)),
            "MCP testleri için önce .env ortam değişkenlerini yükleyin.");
        Assert.False(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(JwtSettings.SigningKeyKey)),
            "MCP testleri için önce .env ortam değişkenlerini yükleyin.");
        return new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            // Every expected figure is hand-computed for the demo moment (docs/synthetic-seed.md).
            host.UseSetting(BusinessClock.DemoNowKey, DemoNowUtc.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
            foreach (var (key, value) in settings)
                host.UseSetting(key, value);
            if (logs is not null)
                host.ConfigureTestServices(services => services.AddSingleton<ILoggerProvider>(logs));
        });
    }

    /// <summary>An MCP client for the customer of <paramref name="customerId"/>, with a freshly signed token.</summary>
    public static Task<McpClient> ConnectAsync(WebApplicationFactory<Program> server, Guid customerId) =>
        ConnectAsync(server, TestTokens.Create(customerId));

    public static async Task<McpClient> ConnectAsync(WebApplicationFactory<Program> server, string token)
    {
        var http = server.CreateClient();
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri(http.BaseAddress!, "mcp"),
                AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" }
            },
            http, ownsHttpClient: true);
        return await McpClient.CreateAsync(transport);
    }

    /// <summary>Calls a tool with JSON arguments exactly as written, so tests can send what a model might.</summary>
    public static async Task<CallToolResult> CallAsync(McpClient client, string tool, string argumentsJson)
    {
        var arguments = JsonSerializer.Deserialize<Dictionary<string, object?>>(argumentsJson)!;
        return await client.CallToolAsync(tool, arguments);
    }

    /// <summary>The <c>{code, message}</c> of a tool error (K10-A).</summary>
    public static (string Code, string Message) Error(CallToolResult result)
    {
        Assert.True(result.IsError, "expected a tool error");
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        using var json = JsonDocument.Parse(text);
        return (json.RootElement.GetProperty("code").GetString()!, json.RootElement.GetProperty("message").GetString()!);
    }

    /// <summary>The structured result of a successful call (K9-A: the API's own JSON).</summary>
    public static JsonElement Result(CallToolResult result)
    {
        Assert.False(result.IsError is true, result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text);
        return Assert.IsType<JsonElement>(result.StructuredContent);
    }
}
