using System.Net;
using System.Text.Json.Nodes;
using BankingAI.Api.Time;
using Xunit;
using static BankingAI.Application.Tests.DemoData;

namespace BankingAI.Api.Tests;

// ANA-09 part 3. GET /api/transactions: the statement-like search over all of the customer's accounts, with the
// filters of TransactionSearchService. Read-only against the seeded demo data, with the business clock fixed to the
// demo moment, so symbolic periods give the hand-computed rows of TransactionSearchServiceTests.
public sealed class TransactionSearchEndpointTests
{
    private static async Task<(HttpStatusCode Status, JsonNode? Body)> GetAsync(string query, Guid? customerId)
    {
        await using var api = TestApi.Create((BusinessClock.DemoNowKey, BusinessClockTests.DemoNow));
        using var client = api.CreateClient();
        if (customerId is { } id)
            client.WithToken(TestTokens.Create(id));
        var response = await client.GetAsync($"/api/transactions{query}");
        var text = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, text.Length == 0 ? null : JsonNode.Parse(text));
    }

    private static JsonNode Ok((HttpStatusCode Status, JsonNode? Body) response)
    {
        Assert.True(response.Status == HttpStatusCode.OK, response.Body?.ToJsonString());
        return response.Body!;
    }

    private static JsonArray Items(JsonNode body) => body["items"]!.AsArray();

    private static decimal Amount(JsonNode? value) => value!.GetValue<decimal>();

    private static (int TotalCount, bool HasMore) Counts(JsonNode body) =>
        (body["totalCount"]!.GetValue<int>(), body["hasMore"]!.GetValue<bool>());

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Without_a_period_the_last_three_months_are_searched()
    {
        var body = Ok(await GetAsync("", Ada));

        // Decision K1-B: a list, like the account's list (API-09), not a figure; the answer names its period.
        Assert.Equal(("Last3Months", "2026-07-10", "2026-10-10"),
            (body["period"]!["kind"]!.GetValue<string>(), body["period"]!["startDate"]!.GetValue<string>(),
             body["period"]!["endDate"]!.GetValue<string>()));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Statement_lists_every_kind_newest_first_with_masked_counterparties()
    {
        var body = Ok(await GetAsync("?period=ThisMonth", Ada));

        Assert.Equal(["period", "merchantMatch", "totalCount", "hasMore", "items"], body.AsObject().Select(p => p.Key));
        Assert.Null(body["merchantMatch"]);
        Assert.Equal((8, false), Counts(body));
        Assert.Equal(
            [("2026-10-09", "Purchase", "Debit", 145m, null), ("2026-10-08", "Transfer", "Credit", 300m, "Ege Y***"),
             ("2026-10-08", "Purchase", "Debit", 165m, null), ("2026-10-07", "Purchase", "Debit", 95m, "Kahve Durağı – Kadıköy"),
             ("2026-10-06", "Purchase", "Debit", 210m, null), ("2026-10-05", "Salary", "Credit", 45000m, null),
             ("2026-10-03", "Transfer", "Debit", 1500m, "Deniz M***"), ("2026-10-02", "Purchase", "Debit", 120m, "Kahve Durağı – Kadıköy")],
            Items(body).Select(i => (i!["localDate"]!.GetValue<string>(), i["kind"]!.GetValue<string>(), i["direction"]!.GetValue<string>(),
                Amount(i["amount"]), i["counterparty"]?.GetValue<string>())));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task All_accounts_are_searched_including_the_shop()
    {
        var body = Ok(await GetAsync("?period=ThisMonth", Deniz));

        // Salary, market, electricity, Ada's transfer and the shop's three sales: a statement, not spending.
        Assert.Equal((7, false), Counts(body));
        Assert.Equal(
            [("Purchase", 1850m), ("Purchase", 9500m), ("Salary", 55000m), ("Sale", 95m), ("Sale", 120m), ("Sale", 140m), ("Transfer", 1500m)],
            Items(body).Select(i => (i!["kind"]!.GetValue<string>(), Amount(i["amount"])))
                .OrderBy(i => i.Item1, StringComparer.Ordinal).ThenBy(i => i.Item2).ToArray());
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Kind_and_direction_find_sent_transfers()
    {
        var ada = Ok(await GetAsync("?period=ThisMonth&kind=Transfer&direction=Debit", Ada));
        var deniz = Ok(await GetAsync("?period=LastMonth&kind=Transfer&direction=Debit", Deniz));

        var sent = Assert.Single(Items(ada))!;
        Assert.Equal((1500m, "Deniz M***"), (Amount(sent["amount"]), sent["counterparty"]!.GetValue<string>()));
        // A transfer to her own shop names the account; one to a person is masked.
        Assert.Equal([(2000m, "Kahve Durağı – Kadıköy", true), (750m, "Ege Y***", false)],
            Items(deniz).Select(i => (Amount(i!["amount"]), i["counterparty"]!.GetValue<string>(), i["counterpartyIsOwnAccount"]!.GetValue<bool>())));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Category_filters_find_classified_and_unclassified_purchases()
    {
        var unclassified = Ok(await GetAsync("?period=ThisMonth&onlyUnclassified=true", Ada));
        var food = Ok(await GetAsync("?period=ThisMonth&categoryCode=FOOD", Ada));

        var item = Assert.Single(Items(unclassified))!;
        Assert.Equal(("POS 6604 ONLINE ODEME 4471", 145m, null),
            (item["rawDescription"]!.GetValue<string>(), Amount(item["amount"]), item["merchant"]?.GetValue<string>()));
        Assert.Equal([165m, 95m, 210m, 120m], Items(food).Select(i => Amount(i!["amount"])));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Merchant_filter_never_guesses_between_merchants()
    {
        var starbucks = Ok(await GetAsync("?period=Last3Months&merchant=starbuck", Ada));
        var ambiguous = Ok(await GetAsync("?period=Last6Months&merchant=şehir", Ege));

        Assert.Equal(("Matched", "Starbucks", true),
            (starbucks["merchantMatch"]!["status"]!.GetValue<string>(), starbucks["merchantMatch"]!["merchant"]!.GetValue<string>(),
             starbucks["merchantMatch"]!["isPartialMatch"]!.GetValue<bool>()));
        Assert.Equal(5, Counts(starbucks).TotalCount);
        Assert.All(Items(starbucks), i => Assert.Equal("Starbucks", i!["merchant"]!.GetValue<string>()));
        // Two candidates: no rows, and the answer says why instead of picking one.
        Assert.Equal("Ambiguous", ambiguous["merchantMatch"]!["status"]!.GetValue<string>());
        Assert.Equal((0, false), Counts(ambiguous));
        Assert.Empty(Items(ambiguous));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Pages_follow_the_full_list()
    {
        var all = Items(Ok(await GetAsync("?period=ThisMonth", Ada))).Select(i => i!["id"]!.GetValue<string>()).ToArray();
        var first = Ok(await GetAsync("?period=ThisMonth&limit=3", Ada));
        var last = Ok(await GetAsync("?period=ThisMonth&limit=3&offset=6", Ada));

        Assert.Equal((8, true), Counts(first));
        Assert.Equal(all.Take(3), Items(first).Select(i => i!["id"]!.GetValue<string>()));
        Assert.Equal((8, false), Counts(last));
        Assert.Equal(all.Skip(6), Items(last).Select(i => i!["id"]!.GetValue<string>()));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task One_own_account_can_be_searched_even_a_business_one()
    {
        var shop = Ok(await GetAsync($"?period=ThisMonth&accountId={DenizShopAccount}", Deniz));

        Assert.Equal([("Sale", 95m, "Ada K***"), ("Sale", 140m, "Ege Y***"), ("Sale", 120m, "Ada K***")],
            Items(shop).Select(i => (i!["kind"]!.GetValue<string>(), Amount(i["amount"]), i["counterparty"]!.GetValue<string>())));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Search_needs_a_token_and_an_own_account()
    {
        var noToken = await GetAsync("?period=ThisMonth", null);
        var foreign = await GetAsync($"?period=ThisMonth&accountId={DenizTryAccount}", Ada);

        Assert.Equal(HttpStatusCode.Unauthorized, noToken.Status);
        // Another customer's account is answered exactly like an account that does not exist.
        Assert.Equal(HttpStatusCode.NotFound, foreign.Status);
    }

    [Theory]
    [Trait("Category", "PostgreSQL")]
    [InlineData("kind=purchase", "kind", "Purchase, Sale, Transfer, Salary veya OpeningFunding olmalı.")]
    [InlineData("kind=1", "kind", "Purchase, Sale, Transfer, Salary veya OpeningFunding olmalı.")]
    [InlineData("direction=debit", "direction", "Debit veya Credit olmalı.")]
    [InlineData("direction=2", "direction", "Debit veya Credit olmalı.")]
    [InlineData("categoryCode=YEMEK", "categoryCode", "Tanımlı bir kategori kodu olmalı.")]
    [InlineData("categoryCode=FOOD&onlyUnclassified=true", "categoryCode",
        "Kategori kodu ile yalnızca sınıflandırılmamış filtresi birlikte kullanılamaz.")]
    [InlineData("kind=Transfer&categoryCode=FOOD", "kind", "Kategori filtreleri yalnızca Purchase işlemlerine uygulanır.")]
    [InlineData("limit=0", "limit", "1 ile 50 arasında olmalı.")]
    [InlineData("limit=51", "limit", "1 ile 50 arasında olmalı.")]
    [InlineData("offset=-1", "offset", "0 ile 10000 arasında olmalı.")]
    [InlineData("offset=10001", "offset", "0 ile 10000 arasında olmalı.")]
    [InlineData("onlyUnclassified=evet", "onlyUnclassified", "'evet' geçerli bir değer değil.")]
    [InlineData("from=2026-10-01&to=2026-10-10", "period", "Dönem adı ile tarih aralığı birlikte verilemez.")]
    public async Task Search_parameters_are_checked(string query, string field, string message)
    {
        var response = await GetAsync($"?period=ThisMonth&{query}", Ada);

        Assert.True(response.Status == HttpStatusCode.BadRequest, response.Body?.ToJsonString());
        Assert.Equal([message], response.Body!["errors"]![field]!.AsArray().Select(m => m!.GetValue<string>()));
    }
}
