using System.Net;
using System.Text.Json.Nodes;
using BankingAI.Hosting.Time;
using Xunit;
using static BankingAI.Application.Tests.DemoData;

namespace BankingAI.Api.Tests;

// DEMO-02 over HTTP, with the business clock at the demo moment: Murat's September 2026 (hand-listed in the plan) as the breakdown and
// the cinema's insight. The figures are the service tests'; here the shape, the codes and the period rules.
public sealed class SpendingBreakdownEndpointTests
{
    private const string Cinema = "30000000-0000-0000-0000-000000012207";

    private static async Task<(HttpStatusCode Status, JsonNode? Body)> GetAsync(string path, Guid? customerId)
    {
        await using var api = TestApi.Create((BusinessClock.DemoNowKey, BusinessClockTests.DemoNow));
        using var client = api.CreateClient();
        if (customerId is { } id)
            client.WithToken(TestTokens.Create(id));
        var response = await client.GetAsync(path);
        var text = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, text.Length == 0 ? null : JsonNode.Parse(text));
    }

    // Data tests briefly commit a temporary active asset, so the real three are looked for, not counted.
    private static HashSet<string> Symbols(JsonNode assets) => assets.AsArray().Select(a => a!["symbol"]!.GetValue<string>()).ToHashSet();

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Last_month_is_broken_down_with_numbers_and_names()
    {
        var (status, body) = await GetAsync("/api/transactions/breakdown?period=LastMonth", Murat);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(["period", "periodTooLong", "merchantMatch", "currencies"], body!.AsObject().Select(p => p.Key));
        var lira = body["currencies"]![0]!;
        Assert.Equal(("51495.89", "1892.50"), (lira["routine"]!["total"]!.ToJsonString(), lira["nonRoutine"]!["total"]!.ToJsonString()));
        Assert.Contains(lira["routine"]!["items"]!.AsArray(), i => i!["reason"]!.GetValue<string>() == "Subscription");
        Assert.Superset(new HashSet<string> { "USDTRY", "EURTRY", "GRAM_GOLD_TRY" }, Symbols(lira["nonRoutine"]!["investment"]!["assets"]!));
        Assert.Equal("EachOnItsOwnDay", lira["nonRoutine"]!["investment"]!["method"]!.GetValue<string>());
    }

    [Theory]
    [Trait("Category", "PostgreSQL")]
    [InlineData("/api/transactions/breakdown", "period")]
    [InlineData("/api/transactions/breakdown?period=AllTime", "period")]
    [InlineData("/api/transactions/breakdown?period=LastMonth&categoryCode=NOPE", "categoryCode")]
    public async Task A_missing_or_wrong_period_or_category_is_a_400_on_that_field(string path, string field)
    {
        var (status, body) = await GetAsync(path, Murat);

        // The field proves the breakdown itself refused it, not "breakdown" read as a transaction id.
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.True(body!["errors"]?[field] is not null, body.ToJsonString());
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Three_months_are_not_broken_down()
    {
        var (status, body) = await GetAsync("/api/transactions/breakdown?period=Last3Months", Murat);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.True(body!["periodTooLong"]!.GetValue<bool>());
        Assert.Empty(body["currencies"]!.AsArray());
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task The_cinemas_insight_names_its_group()
    {
        var (status, body) = await GetAsync($"/api/transactions/{Cinema}/insight", Murat);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("Discretionary", body!["group"]!.GetValue<string>());
        Assert.Superset(new HashSet<string> { "USDTRY", "EURTRY", "GRAM_GOLD_TRY" }, Symbols(body["investment"]!["assets"]!));
    }

    [Theory]
    [Trait("Category", "PostgreSQL")]
    [InlineData("/api/transactions/breakdown?period=LastMonth")]
    [InlineData("/api/transactions/" + Cinema + "/insight")]
    public async Task Both_need_a_token(string path) =>
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetAsync(path, null)).Status);
}
