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
}
