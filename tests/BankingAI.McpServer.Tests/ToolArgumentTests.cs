using ModelContextProtocol;
using Xunit;
using static BankingAI.Application.Tests.DemoData;

namespace BankingAI.McpServer.Tests;

// Model arguments are untrusted (the SDK publishes the schema but does not enforce it): the server checks them as
// strictly as the AI service did in AI-14a. Exact names only, no extra fields, no silent correction (K10-A:
// invalid_arguments, and nothing is computed).
public sealed class ToolArgumentTests
{
    public static TheoryData<string, string> InvalidArguments => new()
    {
        { "get_spending_summary", """{"period":"yesterday"}""" },
        { "get_spending_summary", """{"period":"Custom"}""" },
        { "get_spending_summary", """{"period":"Dün"}""" },
        { "get_spending_summary", """{"period":3}""" },
        { "get_spending_summary", """{}""" },
        { "get_spending_summary", """{"period":"ThisMonth","customerId":"10000000-0000-0000-0000-000000000002"}""" },
        { "get_category_breakdown", """{"period":"ThisMonth","accountId":"20000000-0000-0000-0000-000000000003"}""" },
        { "get_category_breakdown", """{"period":"ThisMonth","from":"2026-01-01"}""" },
        { "get_merchant_spending", """{"merchant":"","period":"ThisMonth"}""" },
        { "get_merchant_spending", """{"merchant":"   ","period":"ThisMonth"}""" },
        { "get_merchant_spending", $$"""{"merchant":"{{new string('a', 151)}}","period":"ThisMonth"}""" },
        { "get_merchant_spending", """{"period":"ThisMonth"}""" },
        { "get_merchant_spending", """{"merchant":null,"period":"ThisMonth"}""" },
        // Counts (MCP-16, MCP-24): whole JSON numbers inside the published limits; text, fractions and nulls are refused.
        { "get_monthly_trend", """{"months":0}""" },
        { "get_monthly_trend", """{"months":13}""" },
        { "get_monthly_trend", """{"months":"3"}""" },
        { "get_monthly_trend", """{"months":3.5}""" },
        { "get_monthly_trend", """{"months":null}""" },
        { "get_monthly_trend", """{}""" },
        { "get_upcoming_payments", """{"days":0}""" },
        { "get_upcoming_payments", """{"days":32}""" },
        { "get_upcoming_payments", """{"days":-1}""" },
        { "get_upcoming_payments", """{"days":"7"}""" },
        { "get_upcoming_payments", """{"days":7,"accountId":"20000000-0000-0000-0000-000000000001"}""" },
        // The comparison (MCP-09): two exact names; custom ranges would let the model choose dates.
        { "compare_spending", """{"comparison":"Custom"}""" },
        { "compare_spending", """{"comparison":"previousFullMonth"}""" },
        { "compare_spending", """{"comparison":"PreviousFullMonth","currentFrom":"2026-09-01"}""" },
        { "compare_spending", """{}""" },
        { "get_highest_transactions", """{"period":"LastMonth","limit":3}""" },
        { "get_highest_transactions", """{"period":"LastMonth","includeAllTransactions":true}""" },
        { "get_recurring_payments", """{"months":6}""" },
    };

    [Theory]
    [Trait("Category", "PostgreSQL")]
    [MemberData(nameof(InvalidArguments))]
    public async Task Arguments_outside_the_schema_are_refused(string tool, string arguments)
    {
        await using var server = McpTestServer.Create();
        await using var client = await McpTestServer.ConnectAsync(server, Ada);

        var (code, message) = McpTestServer.Error(await McpTestServer.CallAsync(client, tool, arguments));

        Assert.Equal("invalid_arguments", code);
        Assert.False(string.IsNullOrWhiteSpace(message));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task A_merchant_of_exactly_150_characters_is_accepted()
    {
        await using var server = McpTestServer.Create();
        await using var client = await McpTestServer.ConnectAsync(server, Ada);

        var result = McpTestServer.Result(await McpTestServer.CallAsync(client, "get_merchant_spending",
            $$"""{"merchant":"{{new string('ı', 150)}}","period":"ThisMonth"}"""));

        Assert.Equal("NotFound", result.GetProperty("match").GetProperty("status").GetString());
    }

    [Theory]
    [Trait("Category", "PostgreSQL")]
    [InlineData("get_monthly_trend", """{"months":1}""")]
    [InlineData("get_monthly_trend", """{"months":12}""")]
    [InlineData("get_upcoming_payments", """{"days":1}""")]
    [InlineData("get_upcoming_payments", """{"days":31}""")]
    [InlineData("get_upcoming_payments", """{}""")]
    [InlineData("get_recurring_payments", """{}""")]
    public async Task Counts_on_their_limits_and_left_out_optional_ones_are_accepted(string tool, string arguments)
    {
        await using var server = McpTestServer.Create();
        await using var client = await McpTestServer.ConnectAsync(server, Ada);

        var result = McpTestServer.Result(await McpTestServer.CallAsync(client, tool, arguments));

        Assert.Equal(System.Text.Json.JsonValueKind.Object, result.ValueKind);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task A_tool_that_is_not_published_does_not_exist()
    {
        await using var server = McpTestServer.Create();
        await using var client = await McpTestServer.ConnectAsync(server, Ada);

        // Writing tools are not hidden, they are absent: there is nothing to call.
        await Assert.ThrowsAnyAsync<McpException>(() => McpTestServer.CallAsync(client, "create_transaction", """{"amount":100}"""));
    }
}
