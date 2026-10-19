using System.Text.Json;
using Xunit;
using static BankingAI.Application.Tests.DemoData;

namespace BankingAI.McpServer.Tests;

// MCP-05, MCP-07, MCP-25: the same figures as the API, in the API's own JSON (K9-A). Expected values are the
// hand-computed fixtures of docs/synthetic-seed.md at the demo moment (10 October 2026), the same ones the API tests use.
public sealed class ToolCallTests
{
    private static async Task<JsonElement> CallAsync(Guid customerId, string tool, string arguments)
    {
        await using var server = McpTestServer.Create();
        await using var client = await McpTestServer.ConnectAsync(server, customerId);
        return McpTestServer.Result(await McpTestServer.CallAsync(client, tool, arguments));
    }

    // Raw JSON text: money must stay a number with its two decimals, exactly as the API writes it.
    private static string Money(JsonElement value) => value.GetRawText();

    [Theory]
    [Trait("Category", "PostgreSQL")]
    [InlineData("ThisMonth", "735.00", 5, "145.00", 1)] // the 1.500 TL transfer is not spending
    [InlineData("Yesterday", "145.00", 1, "145.00", 1)]
    [InlineData("ThisWeek", "615.00", 4, "145.00", 1)]
    public async Task Spending_summary_has_the_apis_figures(string period, string total, int count, string unclassified, int unclassifiedCount)
    {
        var result = await CallAsync(Ada, "get_spending_summary", $$"""{"period":"{{period}}"}""");

        Assert.Equal(period, result.GetProperty("period").GetProperty("kind").GetString());
        var tl = Assert.Single(result.GetProperty("currencies").EnumerateArray());
        Assert.Equal(("TRY", total, count, unclassified, unclassifiedCount),
            (tl.GetProperty("currencyCode").GetString(), Money(tl.GetProperty("totalAmount")), tl.GetProperty("transactionCount").GetInt32(),
             Money(tl.GetProperty("uncategorizedAmount")), tl.GetProperty("uncategorizedCount").GetInt32()));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Spending_summary_of_this_month_covers_1_to_10_october()
    {
        var period = (await CallAsync(Ada, "get_spending_summary", """{"period":"ThisMonth"}""")).GetProperty("period");

        Assert.Equal(("2026-10-01", "2026-10-10"), (period.GetProperty("startDate").GetString(), period.GetProperty("endDate").GetString()));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Category_breakdown_keeps_the_unclassified_group_apart()
    {
        var result = await CallAsync(Ada, "get_category_breakdown", """{"period":"ThisMonth"}""");

        var tl = Assert.Single(result.GetProperty("currencies").EnumerateArray());
        Assert.Equal(
            [("FOOD", "590.00", "80.27"), (null, "145.00", "19.73")],
            tl.GetProperty("categories").EnumerateArray()
                .Select(c => (c.GetProperty("categoryCode").GetString(), Money(c.GetProperty("amount")), Money(c.GetProperty("percentage")))));
    }

    [Theory]
    [Trait("Category", "PostgreSQL")]
    [InlineData("Starbucks", false)]
    [InlineData("starbuck", true)] // a single containing name is accepted, and the result says so
    public async Task Merchant_spending_matches_the_customers_own_merchant(string merchant, bool partial)
    {
        var result = await CallAsync(Ada, "get_merchant_spending", $$"""{"merchant":"{{merchant}}","period":"Last3Months"}""");

        var match = result.GetProperty("match");
        Assert.Equal(("Matched", "Starbucks", partial),
            (match.GetProperty("status").GetString(), match.GetProperty("merchant").GetString(), match.GetProperty("isPartialMatch").GetBoolean()));
        var tl = Assert.Single(result.GetProperty("currencies").EnumerateArray());
        Assert.Equal(("TRY", "930.00", 5),
            (tl.GetProperty("currencyCode").GetString(), Money(tl.GetProperty("totalAmount")), tl.GetProperty("transactionCount").GetInt32()));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Merchant_spending_never_guesses_between_merchants()
    {
        var ambiguous = await CallAsync(Ege, "get_merchant_spending", """{"merchant":"şehir","period":"Last6Months"}""");
        var foreign = await CallAsync(Ada, "get_merchant_spending", """{"merchant":"Aile Marketi","period":"ThisMonth"}""");

        Assert.Equal("Ambiguous", ambiguous.GetProperty("match").GetProperty("status").GetString());
        Assert.Equal(["Şehir Ulaşım", "Şehirlerarası Otobüs"],
            ambiguous.GetProperty("match").GetProperty("candidates").EnumerateArray().Select(c => c.GetString()));
        Assert.Empty(ambiguous.GetProperty("currencies").EnumerateArray());
        // Deniz's market is not one of Ada's merchants.
        Assert.Equal("NotFound", foreign.GetProperty("match").GetProperty("status").GetString());
        Assert.Empty(foreign.GetProperty("currencies").EnumerateArray());
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Each_customer_sees_only_their_own_spending()
    {
        // Same tool, same period, another token: Deniz's own last month (one purchase of 31.000 TL).
        var result = await CallAsync(Deniz, "get_spending_summary", """{"period":"LastMonth"}""");

        var tl = Assert.Single(result.GetProperty("currencies").EnumerateArray());
        Assert.Equal(("31000.00", 1), (Money(tl.GetProperty("totalAmount")), tl.GetProperty("transactionCount").GetInt32()));
    }
}
