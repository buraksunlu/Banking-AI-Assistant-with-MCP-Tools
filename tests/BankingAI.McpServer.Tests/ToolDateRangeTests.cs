using System.Text.Json;
using Xunit;
using static BankingAI.Application.Tests.DemoData;

namespace BankingAI.McpServer.Tests;

// CHAT-08 K2-B: the five period tools take an optional from/to (YYYY-MM-DD) with the period "Custom". The AI service hides
// these fields from the model and fills them from dates its own code read; the server checks them anyway, because every
// argument is untrusted. The range rules (not in the future, at most 24 months, start before end) are the backend's own.
public sealed class ToolDateRangeTests
{
    private static readonly string[] PeriodTools =
        ["get_spending_summary", "get_category_breakdown", "get_merchant_spending", "get_merchant_breakdown", "get_highest_transactions"];

    private static string Arguments(string tool, string dates) =>
        tool == "get_merchant_spending" ? $$"""{"merchant":"Starbucks",{{dates}}}""" : "{" + dates + "}";

    public static TheoryData<string> Tools => [.. PeriodTools];

    [Theory]
    [Trait("Category", "PostgreSQL")]
    [MemberData(nameof(Tools))]
    public async Task A_custom_range_is_resolved_and_echoed_by_every_period_tool(string tool)
    {
        await using var server = McpTestServer.Create();
        await using var client = await McpTestServer.ConnectAsync(server, Ada);

        var result = McpTestServer.Result(await McpTestServer.CallAsync(client, tool,
            Arguments(tool, Custom("2026-10-05", "2026-10-09"))));

        var period = result.GetProperty("period");
        Assert.Equal(("Custom", "2026-10-05", "2026-10-09"),
            (period.GetProperty("kind").GetString(), period.GetProperty("startDate").GetString(), period.GetProperty("endDate").GetString()));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task A_single_day_range_has_the_figures_of_that_day()
    {
        await using var server = McpTestServer.Create();
        await using var client = await McpTestServer.ConnectAsync(server, Ada);

        // Ada's only purchase on 9 October is the 145,00 TL one (the same figure as "Yesterday").
        var result = McpTestServer.Result(await McpTestServer.CallAsync(client, "get_spending_summary",
            """{"period":"Custom","from":"2026-10-09","to":"2026-10-09"}"""));

        var tl = Assert.Single(result.GetProperty("currencies").EnumerateArray());
        Assert.Equal(("145.00", 1), (tl.GetProperty("totalAmount").GetRawText(), tl.GetProperty("transactionCount").GetInt32()));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task The_whole_month_so_far_equals_this_month()
    {
        await using var server = McpTestServer.Create();
        await using var client = await McpTestServer.ConnectAsync(server, Ada);

        var result = McpTestServer.Result(await McpTestServer.CallAsync(client, "get_spending_summary",
            """{"period":"Custom","from":"2026-10-01","to":"2026-10-10"}"""));

        Assert.Equal("735.00", Assert.Single(result.GetProperty("currencies").EnumerateArray()).GetProperty("totalAmount").GetRawText());
    }

    public static TheoryData<string> BrokenDates =>
    [
        Custom("2026-9-1", "2026-09-05"),                                    // not zero padded
        Custom("01.09.2026", "05.09.2026"),                                  // the Turkish way is the AI service's to read
        Custom("2026-02-31", "2026-03-01"),                                  // no such day
        Custom("2026-09-01T00:00:00", "2026-09-05"),                         // a moment, not a day
        "\"period\":\"Custom\",\"from\":20260901,\"to\":\"2026-09-05\"", // a number
        "\"period\":\"Custom\",\"from\":\"2026-09-01\"",                    // one end only
        "\"period\":\"Custom\",\"to\":\"2026-09-05\"",
        Custom("2026-09-05", "2026-09-01"),                                  // ends before it starts
        Custom("2026-10-05", "2026-10-11"),                                  // the future (demo moment: 10 October)
        Custom("2024-01-01", "2026-10-05"),                                  // more than 24 months
        "\"period\":\"Custom\"",                                            // Custom without dates
        "\"period\":\"ThisMonth\",\"from\":\"2026-09-01\",\"to\":\"2026-09-05\"", // dates only belong to Custom
    ];

    private static string Custom(string from, string to) => $"\"period\":\"Custom\",\"from\":\"{from}\",\"to\":\"{to}\"";

    [Theory]
    [Trait("Category", "PostgreSQL")]
    [MemberData(nameof(BrokenDates))]
    public async Task Broken_or_impossible_ranges_are_refused_by_every_period_tool(string dates)
    {
        await using var server = McpTestServer.Create();
        await using var client = await McpTestServer.ConnectAsync(server, Ada);

        foreach (var tool in PeriodTools)
        {
            var (code, message) = McpTestServer.Error(await McpTestServer.CallAsync(client, tool, Arguments(tool, dates)));
            Assert.Equal("invalid_arguments", code);
            Assert.False(string.IsNullOrWhiteSpace(message));
        }
    }

    [Theory]
    [Trait("Category", "PostgreSQL")]
    [MemberData(nameof(Tools))]
    public async Task Left_out_or_null_dates_keep_a_named_period_working(string tool)
    {
        await using var server = McpTestServer.Create();
        await using var client = await McpTestServer.ConnectAsync(server, Ada);

        var left = McpTestServer.Result(await McpTestServer.CallAsync(client, tool, Arguments(tool, "\"period\":\"ThisMonth\"")));
        var nulls = McpTestServer.Result(await McpTestServer.CallAsync(client, tool,
            Arguments(tool, "\"period\":\"ThisMonth\",\"from\":null,\"to\":null")));

        Assert.Equal(JsonValueKind.Object, left.ValueKind);
        Assert.Equal(left.GetProperty("period").GetRawText(), nulls.GetProperty("period").GetRawText());
    }
}
