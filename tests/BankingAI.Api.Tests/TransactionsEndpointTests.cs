using System.Net;
using System.Text.Json.Nodes;
using Xunit;
using static BankingAI.Application.Tests.DemoData;

namespace BankingAI.Api.Tests;

// API-09. GET /api/accounts/{id}/transactions against the seeded demo database. The API uses the real clock,
// so every test that checks figures passes fixed dates (from/to); expected rows are Ada's TRY account between
// 1 and 10 October 2026 (docs/synthetic-seed.md), newest first.
public sealed class TransactionsEndpointTests
{
    private static Guid Tx(int ordinal) => Guid.Parse($"30000000-0000-0000-0000-{ordinal:D12}");

    private static readonly Guid[] AdaOctober = [Tx(604), Tx(718), Tx(603), Tx(715), Tx(602), Tx(601), Tx(711), Tx(709)];

    private static async Task<(HttpStatusCode Status, string Body)> GetAsync(string path, Guid? customerId = null)
    {
        await using var api = TestApi.Create();
        using var client = api.CreateClient();
        if (customerId is { } id)
            client.WithToken(TestTokens.Create(id));

        var response = await client.GetAsync(path);
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    private static string AdaTry(string query) => $"/api/accounts/{AdaTryAccount}/transactions{query}";

    private static Guid[] Ids(JsonObject page) => page["items"]!.AsArray().Select(i => i!["id"]!.GetValue<Guid>()).ToArray();

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Lists_the_accounts_transactions_newest_first()
    {
        var (status, body) = await GetAsync(AdaTry("?from=2026-10-01&to=2026-10-10"), Ada);

        Assert.Equal(HttpStatusCode.OK, status);
        var page = JsonNode.Parse(body)!.AsObject();
        Assert.Equal(AdaOctober, Ids(page));
        Assert.Equal((8, false), (page["totalCount"]!.GetValue<int>(), page["hasMore"]!.GetValue<bool>()));
        var period = page["period"]!.AsObject();
        Assert.Equal(("Custom", "2026-10-01", "2026-10-10"),
            (period["kind"]!.GetValue<string>(), period["startDate"]!.GetValue<string>(), period["endDate"]!.GetValue<string>()));
        Assert.Equal(["period", "totalCount", "hasMore", "items"], page.Select(p => p.Key));

        // Items are the shared TransactionItem: the outgoing transfer shows the masked counterparty.
        var transfer = page["items"]!.AsArray().Single(i => i!["id"]!.GetValue<Guid>() == Tx(711))!.AsObject();
        Assert.Equal(("Transfer", "Debit", "1500.00", "Deniz M***"),
            (transfer["kind"]!.GetValue<string>(), transfer["direction"]!.GetValue<string>(),
             transfer["amount"]!.ToJsonString(), transfer["counterparty"]!.GetValue<string>()));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Pages_with_limit_and_offset()
    {
        var first = JsonNode.Parse((await GetAsync(AdaTry("?from=2026-10-01&to=2026-10-10&limit=3"), Ada)).Body)!.AsObject();
        var last = JsonNode.Parse((await GetAsync(AdaTry("?from=2026-10-01&to=2026-10-10&limit=3&offset=6"), Ada)).Body)!.AsObject();

        Assert.Equal(AdaOctober[..3], Ids(first));
        Assert.Equal((8, true), (first["totalCount"]!.GetValue<int>(), first["hasMore"]!.GetValue<bool>()));
        Assert.Equal(AdaOctober[6..], Ids(last));
        Assert.Equal((8, false), (last["totalCount"]!.GetValue<int>(), last["hasMore"]!.GetValue<bool>()));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task An_account_without_transactions_gets_an_empty_page()
    {
        var (status, body) = await GetAsync($"/api/accounts/{AdaEmptyAccount}/transactions", Ada);

        Assert.Equal(HttpStatusCode.OK, status);
        var page = JsonNode.Parse(body)!.AsObject();
        Assert.Empty(page["items"]!.AsArray());
        Assert.Equal((0, false), (page["totalCount"]!.GetValue<int>(), page["hasMore"]!.GetValue<bool>()));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Without_dates_the_last_three_months_are_shown()
    {
        var (status, body) = await GetAsync(AdaTry(""), Ada);

        Assert.Equal(HttpStatusCode.OK, status);
        // Decision 1-B. Computed from the real clock in Ada's time zone, like the API does.
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul")).DateTime);
        var period = JsonNode.Parse(body)!["period"]!.AsObject();
        Assert.Equal(("Last3Months", today.AddMonths(-3).ToString("yyyy-MM-dd"), today.ToString("yyyy-MM-dd")),
            (period["kind"]!.GetValue<string>(), period["startDate"]!.GetValue<string>(), period["endDate"]!.GetValue<string>()));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task A_symbolic_period_can_be_chosen_by_name()
    {
        var (status, body) = await GetAsync(AdaTry("?period=LastMonth"), Ada);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("LastMonth", JsonNode.Parse(body)!["period"]!["kind"]!.GetValue<string>());
    }

    [Theory]
    [Trait("Category", "PostgreSQL")]
    [InlineData("ThisMonth")]
    [InlineData("LastMonth")]
    [InlineData("Last3Months")] // names with digits: only a purely numeric value like "2" is refused
    [InlineData("Last6Months")]
    public async Task Every_symbolic_period_is_accepted_by_its_name(string period)
    {
        var (status, body) = await GetAsync(AdaTry($"?period={period}"), Ada);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(period, JsonNode.Parse(body)!["period"]!["kind"]!.GetValue<string>());
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Listing_requires_a_token()
    {
        var (status, _) = await GetAsync(AdaTry(""));

        Assert.Equal(HttpStatusCode.Unauthorized, status);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Another_customers_account_gets_exactly_the_404_of_a_missing_one()
    {
        var foreign = await GetAsync($"/api/accounts/{DenizTryAccount}/transactions", Ada);
        var missing = await GetAsync($"/api/accounts/{Guid.NewGuid()}/transactions", Ada);

        Assert.Equal(HttpStatusCode.NotFound, foreign.Status);
        Assert.Equal(HttpStatusCode.NotFound, missing.Status);
        Assert.Equal(WithoutTraceId(missing.Body), WithoutTraceId(foreign.Body));
    }

    private static string WithoutTraceId(string body)
    {
        var json = JsonNode.Parse(body)!.AsObject();
        json.Remove("traceId");
        return json.ToJsonString();
    }

    [Theory]
    [Trait("Category", "PostgreSQL")]
    [InlineData("?limit=0")]
    [InlineData("?limit=51")]
    [InlineData("?offset=-1")]
    [InlineData("?offset=10001")]
    [InlineData("?from=2026-10-01")]                         // both dates or neither
    [InlineData("?to=2026-10-10")]
    [InlineData("?from=2026-10-10&to=2026-10-01")]           // from after to
    [InlineData("?from=2024-01-01&to=2026-10-10")]           // custom range above 24 months
    [InlineData("?period=LastMonth&from=2026-10-01&to=2026-10-10")]
    [InlineData("?period=Custom")]                           // custom only through from/to
    [InlineData("?period=2")]                                // names only, like enums in JSON
    [InlineData("?period=Foo")]
    [InlineData("?from=2026-13-01&to=2026-10-10")]
    public async Task Invalid_query_parameters_are_400(string query)
    {
        var (status, _) = await GetAsync(AdaTry(query), Ada);

        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task A_malformed_account_id_is_400()
    {
        var (status, _) = await GetAsync("/api/accounts/abc/transactions", Ada);

        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    // API-10. GET /api/transactions/{id}: one transaction of the token's customer, no period limit.

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task One_transaction_requires_a_token()
    {
        var (status, _) = await GetAsync($"/api/transactions/{Tx(602)}");

        Assert.Equal(HttpStatusCode.Unauthorized, status);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task One_transaction_is_the_plain_transaction_item()
    {
        var (status, body) = await GetAsync($"/api/transactions/{Tx(602)}", Ada);

        Assert.Equal(HttpStatusCode.OK, status);
        var json = JsonNode.Parse(body)!.AsObject();
        // No envelope and no internal fields (pairId, categorization source, customer id).
        Assert.Equal(
            ["id", "accountId", "accountName", "occurredAtUtc", "localDate", "kind", "direction", "amount", "currencyCode",
             "merchant", "categoryCode", "categoryDisplayName", "rawDescription", "counterparty", "counterpartyIsOwnAccount"],
            json.Select(p => p.Key));
        Assert.Equal((Tx(602), AdaTryAccount, "Purchase", "210.00", "2026-10-06", "Starbucks"),
            (json["id"]!.GetValue<Guid>(), json["accountId"]!.GetValue<Guid>(), json["kind"]!.GetValue<string>(),
             json["amount"]!.ToJsonString(), json["localDate"]!.GetValue<string>(), json["merchant"]!.GetValue<string>()));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task A_shop_owner_reads_a_sale_on_the_business_account()
    {
        var (status, body) = await GetAsync($"/api/transactions/{Tx(710)}", Deniz);

        Assert.Equal(HttpStatusCode.OK, status);
        var json = JsonNode.Parse(body)!;
        Assert.Equal(("Sale", "Credit", "Ada K***"),
            (json["kind"]!.GetValue<string>(), json["direction"]!.GetValue<string>(), json["counterparty"]!.GetValue<string>()));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Another_customers_transaction_gets_exactly_the_404_of_a_missing_one()
    {
        // 710 is the shop's leg of Ada's own purchase 709: related to her, but on Deniz's account.
        var foreign = await GetAsync($"/api/transactions/{Tx(710)}", Ada);
        var missing = await GetAsync($"/api/transactions/{Guid.NewGuid()}", Ada);

        Assert.Equal(HttpStatusCode.NotFound, foreign.Status);
        Assert.Equal(HttpStatusCode.NotFound, missing.Status);
        Assert.Equal(WithoutTraceId(missing.Body), WithoutTraceId(foreign.Body));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task The_all_zero_id_is_a_missing_transaction()
    {
        var (status, _) = await GetAsync($"/api/transactions/{Guid.Empty}", Ada);

        Assert.Equal(HttpStatusCode.NotFound, status);
    }

    [Theory]
    [Trait("Category", "PostgreSQL")]
    [InlineData("abc")]
    [InlineData("123")]
    public async Task A_malformed_transaction_id_is_400(string id)
    {
        var (status, _) = await GetAsync($"/api/transactions/{id}", Ada);

        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task One_transaction_for_a_customer_that_no_longer_exists_is_401()
    {
        // API-13: a missing customer is told apart from a missing transaction.
        var (status, _) = await GetAsync($"/api/transactions/{Tx(602)}", Guid.NewGuid());

        Assert.Equal(HttpStatusCode.Unauthorized, status);
    }
}
