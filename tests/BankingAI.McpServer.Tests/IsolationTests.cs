using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;
using static BankingAI.Application.Tests.DemoData;

namespace BankingAI.McpServer.Tests;

// SEC-02 (K1-B, K2-A, K5-B). No tool takes an account or customer id: the customer is the token's. Every tool the server publishes is
// called here with each of two tokens, and the answer must carry none of the other customer's marks. The tool list is read from the
// server, so a new tool fails here until it gets a sample.
public sealed class IsolationTests
{
    // A valid call of every tool, for a wide period so that each customer's own data is in the answer.
    private static readonly Dictionary<string, string> Samples = new()
    {
        ["get_spending_summary"] = """{"period":"Last6Months"}""",
        ["get_category_breakdown"] = """{"period":"Last6Months"}""",
        ["get_category_spending"] = """{"categoryCode":"GROCERY","period":"Last6Months"}""",
        ["get_merchant_spending"] = """{"merchant":"Starbucks","period":"Last6Months"}""",
        ["get_merchant_breakdown"] = """{"period":"Last6Months"}""",
        ["get_highest_transactions"] = """{"period":"Last6Months"}""",
        ["compare_spending"] = """{"comparison":"PreviousFullMonth"}""",
        ["get_monthly_trend"] = """{"months":12}""",
        ["get_recurring_payments"] = "{}",
        ["get_upcoming_payments"] = "{}",
        ["check_budget"] = """{"amount":1000,"currencyCode":"TRY"}""",
        ["simulate_savings"] = """{"reductionPercent":10}""",
        ["get_income_share"] = """{"period":"LastMonth"}""",
        ["get_spending_advice"] = """{"period":"LastMonth"}""",
        ["what_if_one_off"] = """{"period":"LastMonth"}""",
        ["what_if_monthly"] = """{"months":12,"fromAdvice":true}""", // the amount is the customer's own advice
        ["get_accounts"] = "{}",
        ["search_transactions"] = """{"period":"Last6Months"}""",
        ["get_income"] = """{"period":"Last6Months"}""",
        ["get_transfers"] = """{"period":"Last6Months"}""",
    };

    // What only the other customer's data contains. A counterparty is shown masked ("Deniz M***"), so the full name is a mark of a leak;
    // Aile Marketi is Deniz's monthly market and Netflix Ada's subscription, neither ever on the other's statement.
    private static readonly string[] DenizMarks =
        ["Deniz Market", "DUMMY-DENIZ", "Aile Marketi", DenizTryAccount.ToString(), DenizShopAccount.ToString(), Deniz.ToString()];
    private static readonly string[] AdaMarks =
        ["Ada Kahve", "DUMMY-ADA", "Netflix", AdaTryAccount.ToString(), AdaUsdAccount.ToString(), Ada.ToString()];

    public static TheoryData<string> Tools => new(Samples.Keys);

    // The whole answer as text: the structured result and every text block.
    private static async Task<string> AnswerAsync(McpClient client, string tool)
    {
        var result = await McpTestServer.CallAsync(client, tool, Samples[tool]);
        Assert.False(result.IsError is true, $"{tool}: {result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text}");
        var texts = result.Content.OfType<TextContentBlock>().Select(t => t.Text);
        return string.Join("\n", texts.Append(result.StructuredContent is JsonElement json ? json.GetRawText() : ""));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Every_tool_of_the_server_has_an_isolation_sample()
    {
        await using var server = McpTestServer.Create();
        await using var client = await McpTestServer.ConnectAsync(server, Ada);

        var tools = (await client.ListToolsAsync()).Select(t => t.Name).Order(StringComparer.Ordinal);

        Assert.Equal(Samples.Keys.Order(StringComparer.Ordinal), tools);
    }

    [Theory]
    [Trait("Category", "PostgreSQL")]
    [MemberData(nameof(Tools))]
    public async Task Each_token_gets_only_its_own_customers_data(string tool)
    {
        await using var server = McpTestServer.Create();
        await using var ada = await McpTestServer.ConnectAsync(server, Ada);
        await using var deniz = await McpTestServer.ConnectAsync(server, Deniz);

        var adasAnswer = await AnswerAsync(ada, tool);
        var denizsAnswer = await AnswerAsync(deniz, tool);

        Assert.All(DenizMarks, mark => Assert.False(adasAnswer.Contains(mark, StringComparison.Ordinal), $"{tool}: Ada sees \"{mark}\""));
        Assert.All(AdaMarks, mark => Assert.False(denizsAnswer.Contains(mark, StringComparison.Ordinal), $"{tool}: Deniz sees \"{mark}\""));
    }

    [Theory]
    [Trait("Category", "PostgreSQL")]
    [MemberData(nameof(Tools))]
    public async Task Two_customers_never_get_the_same_answer(string tool)
    {
        // A tool that answers only with figures carries no mark, so a token read as the wrong customer would pass the test above.
        // Two personas with a year of data and an income (Ada and Deniz have no salary, so no advice) differ in every tool.
        await using var server = McpTestServer.Create();
        await using var murat = await McpTestServer.ConnectAsync(server, Murat);
        await using var selin = await McpTestServer.ConnectAsync(server, Selin);

        Assert.NotEqual(await AnswerAsync(murat, tool), await AnswerAsync(selin, tool));
    }
}
