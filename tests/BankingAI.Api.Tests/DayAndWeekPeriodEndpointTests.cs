using System.Net;
using System.Text.Json.Nodes;
using BankingAI.Api.Time;
using Xunit;
using static BankingAI.Application.Tests.DemoData;

namespace BankingAI.Api.Tests;

// ANA-20. The new day and week periods reach every endpoint that reads "period", by their exact names only. The business
// clock is the demo moment (Saturday 10 October 2026); figures are the hand-computed ones of SpendingSummaryServiceTests.
public sealed class DayAndWeekPeriodEndpointTests
{
    private static async Task<(HttpStatusCode Status, JsonNode? Body)> GetAsync(string path)
    {
        await using var api = TestApi.Create((BusinessClock.DemoNowKey, BusinessClockTests.DemoNow));
        using var client = api.CreateClient();
        client.WithToken(TestTokens.Create(Ada));
        var response = await client.GetAsync(path);
        var text = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, text.Length == 0 ? null : JsonNode.Parse(text));
    }

    private static JsonNode Ok((HttpStatusCode Status, JsonNode? Body) response)
    {
        Assert.True(response.Status == HttpStatusCode.OK, response.Body?.ToJsonString());
        return response.Body!;
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Spending_summary_for_yesterday_is_the_ninth_of_october()
    {
        var body = Ok(await GetAsync("/api/analytics/spending-summary?period=Yesterday"));

        Assert.Equal(("Yesterday", "2026-10-09", "2026-10-09"),
            (body["period"]!["kind"]!.GetValue<string>(), body["period"]!["startDate"]!.GetValue<string>(),
             body["period"]!["endDate"]!.GetValue<string>()));
        var tryRow = Assert.Single(body["currencies"]!.AsArray())!;
        Assert.Equal(("145.00", 1), (tryRow["totalAmount"]!.ToJsonString(), tryRow["transactionCount"]!.GetValue<int>()));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task This_week_starts_on_monday()
    {
        var body = Ok(await GetAsync("/api/analytics/spending-summary?period=ThisWeek"));

        Assert.Equal(("2026-10-05", "2026-10-10"),
            (body["period"]!["startDate"]!.GetValue<string>(), body["period"]!["endDate"]!.GetValue<string>()));
        Assert.Equal("615.00", Assert.Single(body["currencies"]!.AsArray())!["totalAmount"]!.ToJsonString());
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Search_and_highest_transactions_accept_the_new_names_too()
    {
        // Yesterday Ada only had the unknown 145 TRY payment; last week only the 120 TRY Kahve Durağı purchase.
        var search = Ok(await GetAsync("/api/transactions?period=Yesterday"));
        Assert.Equal(1, search["totalCount"]!.GetValue<int>());
        Assert.Equal("145.00", search["items"]![0]!["amount"]!.ToJsonString());

        var highest = Ok(await GetAsync("/api/analytics/highest-transactions?period=LastWeek"));
        var item = Assert.Single(Assert.Single(highest["currencies"]!.AsArray())!["items"]!.AsArray())!;
        Assert.Equal("120.00", item["amount"]!.ToJsonString());
    }

    [Fact]
    public async Task A_name_in_other_casing_is_rejected_and_the_message_lists_every_name()
    {
        var (status, problem) = await GetAsync("/api/analytics/spending-summary?period=yesterday");

        Assert.Equal(HttpStatusCode.BadRequest, status);
        var message = Assert.Single(problem!["errors"]!["period"]!.AsArray())!.GetValue<string>();
        Assert.Equal(
            "ThisMonth, LastMonth, Last3Months, Last6Months, Today, Yesterday, ThisWeek, LastWeek, Last7Days, Last30Days veya ThisYear olmalı.",
            message);
    }
}
