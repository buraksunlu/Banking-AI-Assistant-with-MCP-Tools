using System.Net;
using System.Text.Json.Nodes;
using BankingAI.Hosting.Time;
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

        Assert.Equal(["period", "merchantMatch", "totalCount", "hasMore", "nextCursor", "items"], body.AsObject().Select(p => p.Key));
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
        var all = Ids(Ok(await GetAsync("?period=ThisMonth", Ada)));
        var first = Ok(await GetAsync("?period=ThisMonth&limit=3", Ada));
        var second = Ok(await GetAsync($"?period=ThisMonth&limit=3&cursor={first["nextCursor"]}", Ada));
        var last = Ok(await GetAsync($"?period=ThisMonth&limit=3&cursor={second["nextCursor"]}", Ada));

        Assert.Equal((8, true), Counts(first));
        Assert.Equal(all.Take(3), Ids(first));
        Assert.Equal(all.Skip(3).Take(3), Ids(second));
        Assert.Equal((8, false), Counts(last));
        Assert.Equal(all.Skip(6), Ids(last));
        Assert.Null(last["nextCursor"]);
    }

    private static string[] Ids(JsonNode body) => Items(body).Select(i => i!["id"]!.GetValue<string>()).ToArray();

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task All_time_pages_through_the_whole_history()
    {
        // API-17: what the iPhone's "Tüm işlemler" asks for. The last three months held 20 of Ada's 49 transactions.
        var pages = new List<JsonNode> { Ok(await GetAsync("?period=AllTime", Ada)) };
        // A cursor that does not move would loop forever; ten pages is far more than 49 rows need.
        while (pages[^1]["nextCursor"] is { } cursor && pages.Count < 10)
            pages.Add(Ok(await GetAsync($"?period=AllTime&cursor={cursor}", Ada)));

        Assert.Equal([20, 20, 9], pages.Select(p => Items(p).Count));
        Assert.All(pages, p => Assert.Equal(49, p["totalCount"]!.GetValue<int>()));
        Assert.Equal(49, pages.SelectMany(Ids).Distinct().Count());
        Assert.Equal(("AllTime", "2025-04-01", "2026-10-10"),
            (pages[0]["period"]!["kind"]!.GetValue<string>(), pages[0]["period"]!["startDate"]!.GetValue<string>(),
             pages[0]["period"]!["endDate"]!.GetValue<string>()));
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
    public async Task Amount_range_is_inclusive_and_in_one_currency()
    {
        var body = Ok(await GetAsync("?period=ThisMonth&currencyCode=TRY&minAmount=145&maxAmount=165.00", Ada));

        // ANA-11: both bounds included, the decimal point is the query string's separator.
        Assert.Equal([("2026-10-09", 145m), ("2026-10-08", 165m)],
            Items(body).Select(i => (i!["localDate"]!.GetValue<string>(), Amount(i["amount"]))));
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
    [InlineData("cursor=abc", "cursor", "Geçersiz sayfa imleci.")]
    [InlineData("cursor=AgAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", "cursor", "Geçersiz sayfa imleci.")]
    [InlineData("onlyUnclassified=evet", "onlyUnclassified", "'evet' geçerli bir değer değil.")]
    [InlineData("from=2026-10-01&to=2026-10-10", "period", "Dönem adı ile tarih aralığı birlikte verilemez.")]
    [InlineData("minAmount=0&currencyCode=TRY", "minAmount", "Tutar 0.01 ile parasal üst sınır arasında olmalı.")]
    [InlineData("maxAmount=1.005&currencyCode=TRY", "maxAmount", "En fazla iki anlamlı ondalık basamak kullanılabilir.")]
    [InlineData("minAmount=500,50&currencyCode=TRY", "minAmount", "'500,50' geçerli bir değer değil.")] // not 50050
    [InlineData("minAmount=45,000&currencyCode=TRY", "minAmount", "'45,000' geçerli bir değer değil.")] // no thousands separator
    [InlineData("maxAmount=-5&currencyCode=TRY", "maxAmount", "'-5' geçerli bir değer değil.")]
    [InlineData("minAmount=200&maxAmount=100&currencyCode=TRY", "minAmount", "En az tutar en çok tutardan büyük olamaz.")]
    [InlineData("minAmount=100", "currencyCode", "Tutar filtresi için para birimi (currencyCode) verilmeli.")]
    [InlineData("accountType=personal", "accountType", "Personal veya Business olmalı.")]
    [InlineData("accountType=1", "accountType", "Personal veya Business olmalı.")]
    [InlineData("currencyCode=JPY", "currencyCode", "TRY/USD/EUR/GBP olmalı.")]
    [InlineData("currencyCode=try", "currencyCode", "TRY/USD/EUR/GBP olmalı.")]
    public async Task Search_parameters_are_checked(string query, string field, string message)
    {
        // Every case adds a valid period, so only the parameter under test is wrong.
        var response = await GetAsync($"?period=ThisMonth&{query}", Ada);

        Assert.True(response.Status == HttpStatusCode.BadRequest, response.Body?.ToJsonString());
        Assert.Equal([message], response.Body!["errors"]![field]!.AsArray().Select(m => m!.GetValue<string>()));
    }

    [Theory]
    [Trait("Category", "PostgreSQL")]
    [InlineData("period=AllTime&from=2026-10-01&to=2026-10-10", "Dönem adı ile tarih aralığı birlikte verilemez.")]
    // A custom range is sent as days only; "Custom" is not a name to ask for (the phone sent both until IOS-05 was fixed).
    [InlineData("period=Custom&from=2026-09-01&to=2026-09-05", "Dönem adı ile tarih aralığı birlikte verilemez.")]
    [InlineData("period=Custom",
        "ThisMonth, LastMonth, Last3Months, Last6Months, Today, Yesterday, ThisWeek, LastWeek, Last7Days, Last30Days, ThisYear veya AllTime olmalı.")]
    // A list names AllTime among its periods; the analyses' message does not (DayAndWeekPeriodEndpointTests).
    [InlineData("period=Foo",
        "ThisMonth, LastMonth, Last3Months, Last6Months, Today, Yesterday, ThisWeek, LastWeek, Last7Days, Last30Days, ThisYear veya AllTime olmalı.")]
    public async Task Period_parameters_are_checked(string query, string message)
    {
        var response = await GetAsync($"?{query}", Ada);

        Assert.True(response.Status == HttpStatusCode.BadRequest, response.Body?.ToJsonString());
        Assert.Equal([message], response.Body!["errors"]!["period"]!.AsArray().Select(m => m!.GetValue<string>()));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Account_type_narrows_the_search_like_a_chat_link_does()
    {
        // CHAT-07: Deniz in October 2026 has 4 rows on personal accounts and 3 on the shop account (hand-counted).
        var personal = Ok(await GetAsync("?period=ThisMonth&accountType=Personal", Deniz));
        var business = Ok(await GetAsync("?period=ThisMonth&accountType=Business", Deniz));

        Assert.Equal((4, 3), (Counts(personal).TotalCount, Counts(business).TotalCount));
        Assert.All(Items(business), i => Assert.Equal(DenizShopAccount.ToString(), i!["accountId"]!.GetValue<string>()));
    }
}
