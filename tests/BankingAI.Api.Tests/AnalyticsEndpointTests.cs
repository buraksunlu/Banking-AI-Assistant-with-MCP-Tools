using System.Net;
using System.Text.Json.Nodes;
using BankingAI.Api.Time;
using Xunit;
using static BankingAI.Application.Tests.DemoData;

namespace BankingAI.Api.Tests;

// ANA-09. The analytics services over HTTP. Read-only against the seeded demo data, with the business clock fixed to
// the demo moment (decision 2-A), so symbolic periods give the hand-computed figures of docs/analytics.md.
public sealed class AnalyticsEndpointTests
{
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

    private static JsonNode Ok((HttpStatusCode Status, JsonNode? Body) response)
    {
        Assert.True(response.Status == HttpStatusCode.OK, response.Body?.ToJsonString());
        return response.Body!;
    }

    private static string[] Errors(JsonNode problem, string field) =>
        problem["errors"]![field]!.AsArray().Select(m => m!.GetValue<string>()).ToArray();

    private static string Money(JsonNode? value) => value!.ToJsonString();

    // ---- spending-summary

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Spending_summary_counts_only_purchases_of_the_period()
    {
        var body = Ok(await GetAsync("/api/analytics/spending-summary?period=ThisMonth", Ada));

        Assert.Equal(["period", "currencies"], body.AsObject().Select(p => p.Key));
        Assert.Equal(("ThisMonth", "2026-10-01", "2026-10-10"),
            (body["period"]!["kind"]!.GetValue<string>(), body["period"]!["startDate"]!.GetValue<string>(),
             body["period"]!["endDate"]!.GetValue<string>()));
        // 735, not 2.235: the 1.500 transfer to Deniz is a Debit but not spending.
        var tryRow = Assert.Single(body["currencies"]!.AsArray())!;
        Assert.Equal(("TRY", "735.00", 5, "145.00", 1),
            (tryRow["currencyCode"]!.GetValue<string>(), Money(tryRow["totalAmount"]), tryRow["transactionCount"]!.GetValue<int>(),
             Money(tryRow["uncategorizedAmount"]), tryRow["uncategorizedCount"]!.GetValue<int>()));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Spending_summary_follows_the_named_period_and_dates()
    {
        var lastMonth = Ok(await GetAsync("/api/analytics/spending-summary?period=LastMonth", Deniz));
        var january = Ok(await GetAsync("/api/analytics/spending-summary?from=2026-01-01&to=2026-01-31", Ada));

        Assert.Equal(("31000.00", 1), (Money(lastMonth["currencies"]![0]!["totalAmount"]), lastMonth["currencies"]![0]!["transactionCount"]!.GetValue<int>()));
        // No spending is an empty list, never an invented "0" row.
        Assert.Empty(january["currencies"]!.AsArray());
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Spending_summary_can_be_limited_to_one_own_personal_account()
    {
        var own = Ok(await GetAsync($"/api/analytics/spending-summary?period=ThisMonth&accountId={AdaTryAccount}", Ada));
        var foreign = await GetAsync($"/api/analytics/spending-summary?period=ThisMonth&accountId={DenizTryAccount}", Ada);
        var shop = await GetAsync($"/api/analytics/spending-summary?period=ThisMonth&accountId={DenizShopAccount}", Deniz);

        Assert.Equal("735.00", Money(own["currencies"]![0]!["totalAmount"]));
        Assert.Equal(HttpStatusCode.NotFound, foreign.Status);
        // Spending analysis is for personal accounts; the shop's income is not spending.
        Assert.Equal(HttpStatusCode.BadRequest, shop.Status);
        Assert.Equal(["Harcama analizi yalnızca şahsi hesaplar içindir."], Errors(shop.Body!, "accountId"));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Analytics_need_a_token_and_a_period()
    {
        var noToken = await GetAsync("/api/analytics/spending-summary?period=ThisMonth", null);
        var noPeriod = await GetAsync("/api/analytics/spending-summary", Ada);
        var both = await GetAsync("/api/analytics/category-breakdown?period=ThisMonth&from=2026-10-01&to=2026-10-10", Ada);

        Assert.Equal(HttpStatusCode.Unauthorized, noToken.Status);
        Assert.Equal(HttpStatusCode.BadRequest, noPeriod.Status);
        Assert.Equal(["Dönem zorunlu: period ya da from ile to verilmeli."], Errors(noPeriod.Body!, "period"));
        Assert.Equal(HttpStatusCode.BadRequest, both.Status);
    }

    // ---- category-breakdown

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Category_breakdown_keeps_unclassified_apart()
    {
        var body = Ok(await GetAsync("/api/analytics/category-breakdown?period=ThisMonth", Ada));

        var currency = Assert.Single(body["currencies"]!.AsArray())!;
        Assert.Equal("735.00", Money(currency["totalAmount"]));
        Assert.Equal(new (string?, string, string, int, string)[]
            {
                ("FOOD", "Yeme & İçme", "590.00", 4, "80.27"),
                (null, "Sınıflandırılmamış", "145.00", 1, "19.73")
            },
            currency["categories"]!.AsArray().Select(c => (c!["categoryCode"]?.GetValue<string>(), c["displayName"]!.GetValue<string>(),
                Money(c["amount"]), c["transactionCount"]!.GetValue<int>(), Money(c["percentage"]))).ToArray());
    }

    // ---- merchant-breakdown

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Merchant_breakdown_lists_merchants_largest_first()
    {
        var body = Ok(await GetAsync("/api/analytics/merchant-breakdown?period=ThisMonth", Ada));

        Assert.Equal(new (string?, string, string)[]
            {
                ("Starbucks", "375.00", "51.02"),
                ("Kahve Durağı", "215.00", "29.25"),
                (null, "145.00", "19.73")
            },
            body["currencies"]![0]!["merchants"]!.AsArray()
                .Select(m => (m!["merchant"]?.GetValue<string>(), Money(m["amount"]), Money(m["percentage"]))).ToArray());
    }

    // ---- merchant-spending

    [Theory]
    [Trait("Category", "PostgreSQL")]
    [InlineData("starbucks", false)]
    [InlineData("starbuck", true)] // a single containing name is accepted, and the answer says so
    public async Task Merchant_spending_matches_the_customers_own_merchant(string merchant, bool partial)
    {
        var body = Ok(await GetAsync($"/api/analytics/merchant-spending?merchant={merchant}&period=Last3Months", Ada));

        Assert.Equal(("Matched", "Starbucks", partial),
            (body["match"]!["status"]!.GetValue<string>(), body["match"]!["merchant"]!.GetValue<string>(),
             body["match"]!["isPartialMatch"]!.GetValue<bool>()));
        // Kahve Durağı is not Starbucks: 5 Starbucks purchases between 10 July and 10 October.
        Assert.Equal(("TRY", "930.00", 5),
            (body["currencies"]![0]!["currencyCode"]!.GetValue<string>(), Money(body["currencies"]![0]!["totalAmount"]),
             body["currencies"]![0]!["transactionCount"]!.GetValue<int>()));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Merchant_spending_never_guesses_between_merchants()
    {
        var ambiguous = Ok(await GetAsync("/api/analytics/merchant-spending?merchant=şehir&period=Last6Months", Ege));
        var foreign = Ok(await GetAsync("/api/analytics/merchant-spending?merchant=Aile%20Marketi&period=ThisMonth", Ada));

        Assert.Equal("Ambiguous", ambiguous["match"]!["status"]!.GetValue<string>());
        Assert.Equal(["Şehir Ulaşım", "Şehirlerarası Otobüs"], ambiguous["match"]!["candidates"]!.AsArray().Select(c => c!.GetValue<string>()));
        Assert.Empty(ambiguous["currencies"]!.AsArray());
        // Deniz's market is not one of Ada's merchants.
        Assert.Equal("NotFound", foreign["match"]!["status"]!.GetValue<string>());
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Merchant_spending_needs_a_merchant()
    {
        var missing = await GetAsync("/api/analytics/merchant-spending?period=ThisMonth", Ada);

        Assert.Equal(HttpStatusCode.BadRequest, missing.Status);
        Assert.Equal(["Metin null, boş veya yalnızca whitespace olamaz."], Errors(missing.Body!, "merchant"));
    }
}
