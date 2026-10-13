using System.Net;
using System.Text.Json.Nodes;
using Xunit;
using static BankingAI.Application.Tests.DemoData;

namespace BankingAI.Api.Tests;

// API-05. GET /api/accounts through the real HTTP pipeline against the seeded demo database. Tokens are
// signed for demo customers directly (TestTokens), so no demo password is needed. Data tests running in
// parallel may open temporary accounts for Ada and Deniz, so exact lists are checked on Ege only.
public sealed class AccountsEndpointTests
{
    private static async Task<(HttpStatusCode Status, string Body)> GetAsync(string path, Guid? customerId)
    {
        await using var api = TestApi.Create();
        using var client = api.CreateClient();
        if (customerId is { } id)
            client.WithToken(TestTokens.Create(id));

        var response = await client.GetAsync(path);
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    private static JsonArray Accounts(string body) => JsonNode.Parse(body)!.AsArray();

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task List_requires_a_token()
    {
        var (status, _) = await GetAsync("/api/accounts", customerId: null);

        Assert.Equal(HttpStatusCode.Unauthorized, status);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task List_returns_exactly_the_accounts_of_the_token_owner()
    {
        var (status, body) = await GetAsync("/api/accounts", Ege);

        Assert.Equal(HttpStatusCode.OK, status);
        var accounts = Accounts(body);
        Assert.Equal(
            [
                ("DUMMY-EGE-EUR-001", "EUR", "Personal", "1000.00"),
                ("DUMMY-EGE-TRY-001", "TRY", "Personal", "273380.00")
            ],
            accounts.Select(a => (
                a!["accountNumber"]!.GetValue<string>(),
                a["currencyCode"]!.GetValue<string>(),
                a["accountType"]!.GetValue<string>(),
                a["balance"]!.ToJsonString())));
        Assert.Equal([EgeEurAccount, EgeTryAccount], accounts.Select(a => a!["id"]!.GetValue<Guid>()));
        Assert.All(accounts, a => Assert.False(a!.AsObject().ContainsKey("customerId")));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Ada_sees_her_demo_accounts_in_order_and_nobody_elses()
    {
        var (status, body) = await GetAsync("/api/accounts", Ada);

        Assert.Equal(HttpStatusCode.OK, status);
        var ids = Accounts(body).Select(a => a!["id"]!.GetValue<Guid>()).ToList();
        Assert.Equal([AdaEmptyAccount, AdaTryAccount, AdaUsdAccount],
            ids.Where(id => id == AdaEmptyAccount || id == AdaTryAccount || id == AdaUsdAccount));
        Assert.DoesNotContain(DenizTryAccount, ids);
        Assert.DoesNotContain(DenizShopAccount, ids);
        Assert.DoesNotContain(EgeEurAccount, ids);
        Assert.DoesNotContain(EgeTryAccount, ids);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task A_customer_without_accounts_gets_an_empty_array()
    {
        var (status, body) = await GetAsync("/api/accounts", EmptyCustomer);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("[]", body);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task A_customer_id_in_the_query_string_is_ignored()
    {
        var (status, body) = await GetAsync($"/api/accounts?customerId={Deniz}", Ada);

        Assert.Equal(HttpStatusCode.OK, status);
        var ids = Accounts(body).Select(a => a!["id"]!.GetValue<Guid>()).ToList();
        Assert.Contains(AdaTryAccount, ids);
        Assert.DoesNotContain(DenizTryAccount, ids);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task A_valid_token_of_a_customer_that_no_longer_exists_is_401()
    {
        var (status, _) = await GetAsync("/api/accounts", Guid.NewGuid());

        Assert.Equal(HttpStatusCode.Unauthorized, status);
    }

    // API-06. GET /api/accounts/{id}.

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task One_account_requires_a_token()
    {
        var (status, _) = await GetAsync($"/api/accounts/{AdaTryAccount}", customerId: null);

        Assert.Equal(HttpStatusCode.Unauthorized, status);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task One_account_returns_the_customers_own_account()
    {
        var (status, body) = await GetAsync($"/api/accounts/{AdaTryAccount}", Ada);

        Assert.Equal(HttpStatusCode.OK, status);
        var account = JsonNode.Parse(body)!.AsObject();
        Assert.Equal(
            (AdaTryAccount, "Ada günlük TRY", "DUMMY-ADA-TRY-001", "TRY", "Personal", "577490.18"),
            (account["id"]!.GetValue<Guid>(), account["accountName"]!.GetValue<string>(), account["accountNumber"]!.GetValue<string>(),
             account["currencyCode"]!.GetValue<string>(), account["accountType"]!.GetValue<string>(), account["balance"]!.ToJsonString()));
        Assert.False(account.ContainsKey("customerId"));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task One_account_shows_a_business_account_to_its_owner()
    {
        var (status, body) = await GetAsync($"/api/accounts/{DenizShopAccount}", Deniz);

        Assert.Equal(HttpStatusCode.OK, status);
        var account = JsonNode.Parse(body)!.AsObject();
        Assert.Equal(("Business", "2595.00"), (account["accountType"]!.GetValue<string>(), account["balance"]!.ToJsonString()));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Another_customers_account_gets_exactly_the_404_of_a_missing_one()
    {
        var foreign = await GetAsync($"/api/accounts/{DenizTryAccount}", Ada);
        var missing = await GetAsync($"/api/accounts/{Guid.NewGuid()}", Ada);

        Assert.Equal(HttpStatusCode.NotFound, foreign.Status);
        Assert.Equal(HttpStatusCode.NotFound, missing.Status);
        // Only the per-request trace id may differ; nothing reveals that Deniz's account exists.
        Assert.Equal(WithoutTraceId(missing.Body), WithoutTraceId(foreign.Body));
        Assert.DoesNotContain("DUMMY-DENIZ", foreign.Body);
    }

    private static string WithoutTraceId(string body)
    {
        var json = JsonNode.Parse(body)!.AsObject();
        json.Remove("traceId");
        return json.ToJsonString();
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task The_all_zero_id_is_a_missing_account()
    {
        var (status, _) = await GetAsync($"/api/accounts/{Guid.Empty}", Ada);

        Assert.Equal(HttpStatusCode.NotFound, status);
    }

    [Theory]
    [Trait("Category", "PostgreSQL")]
    [InlineData("abc")]
    [InlineData("123")]
    public async Task A_malformed_id_is_400(string id)
    {
        var (status, _) = await GetAsync($"/api/accounts/{id}", Ada);

        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    // API-07. POST /api/accounts. Accounts are opened for a temporary test customer (TestUser), never for a
    // demo customer, and are deleted with it.

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string json) =>
        await client.PostAsync("/api/accounts", new StringContent(json, System.Text.Encoding.UTF8, "application/json"));

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Opening_an_account_requires_a_token()
    {
        await using var api = TestApi.Create();
        using var client = api.CreateClient();

        var response = await PostAsync(client, """{"accountName":"Tatil","currencyCode":"EUR"}""");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Opening_an_account_returns_201_with_its_location_and_the_new_account()
    {
        await using var user = await TestUser.CreateAsync();
        await using var api = TestApi.Create();
        using var client = api.CreateClient().WithToken(TestTokens.Create(user.CustomerId));

        var response = await PostAsync(client, """{"accountName":"Tatil birikimi","currencyCode":"EUR"}""");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var account = JsonNode.Parse(body)!.AsObject();
        Assert.Equal(("Tatil birikimi", "EUR", "Personal", "0.00"),
            (account["accountName"]!.GetValue<string>(), account["currencyCode"]!.GetValue<string>(),
             account["accountType"]!.GetValue<string>(), account["balance"]!.ToJsonString()));
        Assert.Matches("^ACC-[1-9][0-9]*$", account["accountNumber"]!.GetValue<string>());
        Assert.False(account.ContainsKey("customerId"));

        // The Location header points at the new account, which reads back exactly as it was returned.
        var id = account["id"]!.GetValue<Guid>();
        Assert.Equal($"/api/accounts/{id}", response.Headers.Location!.AbsolutePath);
        var read = await client.GetAsync(response.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(body, await read.Content.ReadAsStringAsync());

        // It also appears in the customer's list.
        var list = Accounts(await client.GetStringAsync("/api/accounts"));
        Assert.Equal([id], list.Select(a => a!["id"]!.GetValue<Guid>()));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Another_customer_cannot_see_a_new_account()
    {
        await using var user = await TestUser.CreateAsync();
        await using var api = TestApi.Create();
        using var owner = api.CreateClient().WithToken(TestTokens.Create(user.CustomerId));
        var created = JsonNode.Parse(await (await PostAsync(owner, """{"accountName":"Gizli","currencyCode":"TRY"}""")).Content.ReadAsStringAsync())!;
        using var ada = api.CreateClient().WithToken(TestTokens.Create(Ada));

        var response = await ada.GetAsync($"/api/accounts/{created["id"]!.GetValue<Guid>()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [Trait("Category", "PostgreSQL")]
    [InlineData("""{"accountName":"","currencyCode":"TRY"}""")]
    [InlineData("""{"accountName":"   ","currencyCode":"TRY"}""")]
    [InlineData("""{"accountName":"Tatil","currencyCode":"try"}""")]
    [InlineData("""{"accountName":"Tatil"}""")]
    [InlineData("""{"accountName":"Tatil","currencyCode":"EUR","balance":1000}""")]
    [InlineData("""{"accountName":"Tatil","currencyCode":"EUR","customerId":"10000000-0000-0000-0000-000000000001"}""")]
    [InlineData("""{"accountName":"Tatil\u0000","currencyCode":"EUR"}""")] // passes the attributes, fails the account rules
    [InlineData("")]
    public async Task Invalid_requests_are_400_and_open_nothing(string json)
    {
        await using var user = await TestUser.CreateAsync();
        await using var api = TestApi.Create();
        using var client = api.CreateClient().WithToken(TestTokens.Create(user.CustomerId));

        var response = await PostAsync(client, json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("[]", await client.GetStringAsync("/api/accounts"));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task An_account_name_of_101_characters_is_400()
    {
        await using var user = await TestUser.CreateAsync();
        await using var api = TestApi.Create();
        using var client = api.CreateClient().WithToken(TestTokens.Create(user.CustomerId));

        var response = await PostAsync(client, $$"""{"accountName":"{{new string('a', 101)}}","currencyCode":"TRY"}""");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Opening_an_account_for_a_customer_that_no_longer_exists_is_401()
    {
        await using var api = TestApi.Create();
        using var client = api.CreateClient().WithToken(TestTokens.Create(Guid.NewGuid()));

        var response = await PostAsync(client, """{"accountName":"Tatil","currencyCode":"EUR"}""");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task One_account_for_a_customer_that_no_longer_exists_is_401()
    {
        // API-13: a missing customer has its own exception now, so every endpoint sends the user back to sign in.
        var (status, _) = await GetAsync($"/api/accounts/{AdaTryAccount}", Guid.NewGuid());

        Assert.Equal(HttpStatusCode.Unauthorized, status);
    }
}
