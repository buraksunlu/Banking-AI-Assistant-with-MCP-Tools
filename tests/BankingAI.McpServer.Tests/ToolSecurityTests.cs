using System.Net;
using System.Net.Http.Headers;
using System.Text;
using BankingAI.Hosting;
using Xunit;
using static BankingAI.Application.Tests.DemoData;

namespace BankingAI.McpServer.Tests;

// The customer comes from the token only (K2-A). Without a valid token the HTTP layer answers 401 before any MCP
// message is read; a valid token of a deleted customer gets the session_invalid tool error (K10-A).
public sealed class ToolSecurityTests
{
    // A JSON-RPC tools/list message; the 401 comes before the body is even parsed.
    private const string ListTools = """{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}""";

    private static async Task<HttpStatusCode> PostAsync(string? token)
    {
        await using var server = McpTestServer.Create();
        using var http = server.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp") { Content = new StringContent(ListTools, Encoding.UTF8, "application/json") };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (await http.SendAsync(request)).StatusCode;
    }

    public static TheoryData<string, string?> RefusedTokens => new()
    {
        { "no token", null },
        { "wrong signature", TestTokens.Create(Ada, signingKey: new string('x', 48)) },
        { "expired", TestTokens.Create(Ada, expiresUtc: DateTime.UtcNow.AddMinutes(-1)) },
        { "not a token", "abc" },
    };

    [Theory]
    [Trait("Category", "PostgreSQL")]
    [MemberData(nameof(RefusedTokens))]
    public async Task Without_a_valid_token_nothing_is_answered(string reason, string? token)
    {
        Assert.True(await PostAsync(token) == HttpStatusCode.Unauthorized, reason);
    }

    [Theory]
    [Trait("Category", "PostgreSQL")]
    [InlineData("get_spending_summary", """{"period":"ThisMonth"}""")]
    [InlineData("get_category_breakdown", """{"period":"ThisMonth"}""")]
    [InlineData("get_merchant_spending", """{"merchant":"Starbucks","period":"ThisMonth"}""")]
    [InlineData("get_merchant_breakdown", """{"period":"ThisMonth"}""")]
    [InlineData("get_highest_transactions", """{"period":"ThisMonth"}""")]
    [InlineData("compare_spending", """{"comparison":"PreviousFullMonth"}""")]
    [InlineData("get_monthly_trend", """{"months":3}""")]
    [InlineData("get_recurring_payments", """{}""")]
    [InlineData("get_upcoming_payments", """{}""")]
    [InlineData("check_budget", """{"amount":1000,"currencyCode":"TRY"}""")]
    [InlineData("simulate_savings", """{"reductionPercent":50}""")]
    [InlineData("get_income_share", """{"period":"LastMonth"}""")]
    [InlineData("get_spending_advice", """{"period":"LastMonth"}""")]
    [InlineData("what_if_one_off", """{"period":"LastMonth"}""")]
    [InlineData("what_if_monthly", """{"fromAdvice":true,"months":12}""")]
    [InlineData("what_if_monthly", """{"amount":1000,"currencyCode":"TRY","months":12}""")]
    public async Task A_deleted_customer_is_told_to_sign_in_again(string tool, string arguments)
    {
        await using var server = McpTestServer.Create();
        await using var client = await McpTestServer.ConnectAsync(server, Guid.NewGuid());

        var (code, message) = McpTestServer.Error(await McpTestServer.CallAsync(client, tool, arguments));

        Assert.Equal(("session_invalid", "Oturum geçersiz; yeniden giriş yapın."), (code, message));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task An_unexpected_failure_is_reported_without_any_detail()
    {
        // A database that cannot be reached: the connection details must never reach the caller.
        await using var server = McpTestServer.Create(settings:
            (BankingServiceCollectionExtensions.ConnectionStringKey, "Host=127.0.0.1;Port=1;Database=x;Username=secretuser;Password=secretpass;Timeout=1"));
        await using var client = await McpTestServer.ConnectAsync(server, Ada);

        var (code, message) = McpTestServer.Error(await McpTestServer.CallAsync(client, "get_spending_summary", """{"period":"ThisMonth"}"""));

        Assert.Equal(("internal_error", "Beklenmeyen bir hata oluştu."), (code, message));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task The_token_is_never_logged()
    {
        var logs = new CapturedLogs();
        var token = TestTokens.Create(Ada);
        await using (var server = McpTestServer.Create(logs))
        {
            await using var client = await McpTestServer.ConnectAsync(server, token);
            McpTestServer.Result(await McpTestServer.CallAsync(client, "get_spending_summary", """{"period":"ThisMonth"}"""));
            McpTestServer.Error(await McpTestServer.CallAsync(client, "get_spending_summary", """{"period":"yesterday"}"""));
        }

        Assert.NotEmpty(logs.Lines);
        Assert.DoesNotContain(logs.Lines, line => line.Contains(token));
    }
}
