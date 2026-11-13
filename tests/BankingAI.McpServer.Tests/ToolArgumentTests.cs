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
        // The budget (MCP-17): a JSON number, never text or a flag; the currency and category by their exact names.
        { "check_budget", """{"amount":"1000","currencyCode":"TRY"}""" },
        { "check_budget", """{"amount":true,"currencyCode":"TRY"}""" },
        { "check_budget", """{"amount":null,"currencyCode":"TRY"}""" },
        { "check_budget", """{"amount":1e40,"currencyCode":"TRY"}""" },
        { "check_budget", """{"currencyCode":"TRY"}""" },
        { "check_budget", """{"amount":1000}""" },
        { "check_budget", """{"amount":1000,"currencyCode":"TL"}""" },
        { "check_budget", """{"amount":1000,"currencyCode":"try"}""" },
        { "check_budget", """{"amount":1000,"currencyCode":null}""" },
        { "check_budget", """{"amount":1000,"currencyCode":"TRY","categoryCode":"food"}""" },
        { "check_budget", """{"amount":1000,"currencyCode":"TRY","categoryCode":"MARKET"}""" },
        { "check_budget", """{"amount":1000,"currencyCode":"TRY","merchant":""}""" },
        { "check_budget", """{"amount":1000,"currencyCode":"TRY","merchant":5}""" },
        { "check_budget", """{"amount":1000,"currencyCode":"TRY","accountId":"20000000-0000-0000-0000-000000000001"}""" },
        // The backend's own rules (InputRules.Money, one target only), reported the same way.
        { "check_budget", """{"amount":0,"currencyCode":"TRY"}""" },
        { "check_budget", """{"amount":-5,"currencyCode":"TRY"}""" },
        { "check_budget", """{"amount":1000.555,"currencyCode":"TRY"}""" },
        { "check_budget", """{"amount":1000,"currencyCode":"TRY","categoryCode":"FOOD","merchant":"Starbucks"}""" },
        // The savings simulation (MCP-18): a whole percent of 1-100 and at most twelve months.
        { "simulate_savings", """{"reductionPercent":0}""" },
        { "simulate_savings", """{"reductionPercent":101}""" },
        { "simulate_savings", """{"reductionPercent":"50"}""" },
        { "simulate_savings", """{"reductionPercent":50.5}""" },
        { "simulate_savings", """{}""" },
        { "simulate_savings", """{"reductionPercent":50,"months":0}""" },
        { "simulate_savings", """{"reductionPercent":50,"months":13}""" },
        { "simulate_savings", """{"reductionPercent":50,"categoryCode":"Yeme"}""" },
        { "simulate_savings", """{"reductionPercent":50,"categoryCode":"FOOD","merchant":"Starbucks"}""" },
        // INC-03: income and advice take a complete month only; the service refuses a range that is not one calendar month.
        { "get_income_share", """{"period":"ThisMonth"}""" },
        { "get_income_share", """{}""" },
        { "get_income_share", """{"period":"Custom","from":"2026-09-02","to":"2026-09-30"}""" },
        { "get_spending_advice", """{"period":"Last3Months"}""" },
        { "get_spending_advice", """{"period":"LastMonth","categoryCode":"FOOD"}""" },
        { "what_if_one_off", """{"period":"LastMonth","categoryCode":"food"}""" },
        { "what_if_one_off", """{"period":"LastMonth","merchant":"Starbucks"}""" },
        { "what_if_one_off", """{"categoryCode":"FOOD"}""" },
        // The monthly saving: one source (K13-B), lira only (K15-B), the money rule and the 1.000.000 limit (K14-A).
        { "what_if_monthly", """{"months":12}""" },
        { "what_if_monthly", """{"fromAdvice":false,"months":12}""" },
        { "what_if_monthly", """{"amount":1000,"currencyCode":"TRY","fromAdvice":true,"months":12}""" },
        { "what_if_monthly", """{"amount":1000,"months":12}""" },
        { "what_if_monthly", """{"amount":1000,"currencyCode":"USD","months":12}""" },
        { "what_if_monthly", """{"amount":"1000","currencyCode":"TRY","months":12}""" },
        { "what_if_monthly", """{"amount":0,"currencyCode":"TRY","months":12}""" },
        { "what_if_monthly", """{"amount":1000000.01,"currencyCode":"TRY","months":12}""" },
        { "what_if_monthly", """{"fromAdvice":"true","months":12}""" },
        { "what_if_monthly", """{"fromAdvice":true,"currencyCode":"TRY","months":12}""" },
        // INV-03: the months are required, a whole number of 2-22 (K1-A, K3-A).
        { "what_if_monthly", """{"fromAdvice":true}""" },
        { "what_if_monthly", """{"amount":1000,"currencyCode":"TRY"}""" },
        { "what_if_monthly", """{"fromAdvice":true,"months":1}""" },
        { "what_if_monthly", """{"fromAdvice":true,"months":23}""" },
        { "what_if_monthly", """{"fromAdvice":true,"months":"6"}""" },
        { "what_if_monthly", """{"fromAdvice":true,"months":6.5}""" },
        { "what_if_monthly", """{"fromAdvice":true,"months":null}""" },
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
    [InlineData("what_if_monthly", """{"amount":1000,"currencyCode":"TRY","months":2}""")]
    [InlineData("what_if_monthly", """{"fromAdvice":true,"months":22}""")]
    [InlineData("get_monthly_trend", """{"months":1}""")]
    [InlineData("get_monthly_trend", """{"months":12}""")]
    [InlineData("get_upcoming_payments", """{"days":1}""")]
    [InlineData("get_upcoming_payments", """{"days":31}""")]
    [InlineData("get_upcoming_payments", """{}""")]
    [InlineData("get_recurring_payments", """{}""")]
    [InlineData("check_budget", """{"amount":1000.50,"currencyCode":"TRY"}""")]
    [InlineData("check_budget", """{"amount":1000,"currencyCode":"TRY","categoryCode":null,"merchant":null}""")]
    [InlineData("simulate_savings", """{"reductionPercent":1,"months":1}""")]
    [InlineData("simulate_savings", """{"reductionPercent":100,"months":12}""")]
    [InlineData("simulate_savings", """{"reductionPercent":50,"categoryCode":null}""")]
    [InlineData("what_if_monthly", """{"amount":1000000,"currencyCode":"TRY","months":12}""")]
    [InlineData("what_if_monthly", """{"amount":null,"currencyCode":null,"fromAdvice":true,"months":12}""")]
    [InlineData("what_if_one_off", """{"period":"LastMonth","categoryCode":null}""")]
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
