using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using BankingAI.Hosting.Time;
using Xunit;
using static BankingAI.Application.Tests.DemoData;

namespace BankingAI.Api.Tests;

// CHAT-07. The reason for the feature: the list a link opens must add up to the figure of the answer. The link is taken
// from a chat reply and sent to the search endpoint exactly as the phone will send it; the rows are then added up here
// (tests may add, the phone never does). Expected values are hand-checked SQL sums of Murat's seeded year.
public sealed class AssistantLinkEndToEndTests
{
    private static string Reply(string arguments, string tool = "get_spending_summary") => $$"""
        {"type":"answer","answer":"x","toolCalls":[{"tool":"{{tool}}","arguments":{{arguments}}}],
         "facts":[],"suggestions":[],"choices":[],"inputHint":"","model":"m"}
        """;

    private static async Task<(int Count, decimal Sum, string Url)> OpenLinkAsync(string reply, Guid? customer = null)
    {
        await using var api = FakeAssistantService.Api(FakeAssistantService.Answering(reply), settings: (BusinessClock.DemoNowKey, BusinessClockTests.DemoNow));
        using var client = api.CreateClient().WithToken(TestTokens.Create(customer ?? Murat));

        var chat = await client.PostAsJsonAsync("/api/assistant/messages", new { message = "Ne kadar harcadım?" });
        var link = JsonNode.Parse(await chat.Content.ReadAsStringAsync())!["links"]![0]!["query"]!;

        // The same query string the phone builds from the link.
        // The search takes a named period or both days, never both (PeriodParameters): Custom is sent as days only.
        var parts = new List<string> { $"kind={link["kind"]}", $"accountType={link["accountType"]}" };
        if (link["period"]!.GetValue<string>() == "Custom")
            parts.AddRange([$"from={link["from"]}", $"to={link["to"]}"]);
        else
            parts.Add($"period={link["period"]}");
        // ACC-01b PR2: the filters of a transfer and of a search, sent only when the link has them (the phone does the same).
        foreach (var name in new[] { "direction", "counterparty", "minAmount", "maxAmount", "currencyCode", "merchant", "categoryCode" })
            if (link[name] is { } value)
                parts.Add($"{name}={Uri.EscapeDataString(value.ToString())}");
        var url = "/api/transactions?" + string.Join("&", parts) + "&limit=50";
        var list = await client.GetAsync(url);
        Assert.True(list.StatusCode == HttpStatusCode.OK, await list.Content.ReadAsStringAsync());

        var body = JsonNode.Parse(await list.Content.ReadAsStringAsync())!;
        var items = body["items"]!.AsArray();
        Assert.Equal(body["totalCount"]!.GetValue<int>(), items.Count);
        return (items.Count, items.Sum(i => i!["amount"]!.GetValue<decimal>()), url);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task This_months_link_lists_the_four_purchases_behind_the_figure()
    {
        var (count, sum, _) = await OpenLinkAsync(Reply("""{"period":"ThisMonth"}"""));

        Assert.Equal((4, 8241.40m), (count, sum));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task A_date_range_link_lists_the_two_purchases_behind_the_range_figure()
    {
        var (count, sum, _) = await OpenLinkAsync(Reply("""{"period":"Custom","from":"2026-09-01","to":"2026-09-05"}"""));

        Assert.Equal((2, 5116.50m), (count, sum));
    }

    // ACC-01b PR2: the three list tools of ACC-01a. Figures are the ones their answers show (MCP ToolCallTests, SQL-checked).

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task A_search_link_lists_the_seven_purchases_over_the_limit()
    {
        var (count, sum, url) = await OpenLinkAsync(Reply("""{"period":"LastMonth","minAmount":"2000","currencyCode":"TRY"}""", "search_transactions"));

        Assert.Equal((7, 41363.50m), (count, sum));
        Assert.Contains("minAmount=2000", url);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task An_income_link_lists_the_salary()
    {
        var (count, sum, _) = await OpenLinkAsync(Reply("""{"period":"LastMonth"}""", "get_income"));

        Assert.Equal((1, 85000.00m), (count, sum));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task A_transfer_link_lists_the_money_sent_to_that_person_only()
    {
        var (count, sum, url) = await OpenLinkAsync(
            Reply("""{"period":"Last3Months","direction":"Sent","counterparty":"Selin"}""", "get_transfers"), Hasan);

        Assert.Equal((3, 9000.00m), (count, sum));
        Assert.Contains("direction=Debit", url);
    }
}
