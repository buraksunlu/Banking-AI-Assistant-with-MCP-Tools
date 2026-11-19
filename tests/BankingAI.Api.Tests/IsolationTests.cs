using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static BankingAI.Application.Tests.DemoData;

namespace BankingAI.Api.Tests;

// SEC-02 (K1-B, K2-A, K3-A, K4-C). One place for "a customer cannot reach another customer's account or transaction by its id".
// Every endpoint that takes an account or transaction id is found in the API's real route table, so a new one fails here until it
// gets a sample. Reads use the demo customers (Ada asks for Deniz's ids); the write is aimed at a temporary TestUser's account, never at
// a demo account, so a broken check can only ever touch temporary rows (tests.md, API-11). The detailed per-endpoint tests stay where they are.
public sealed class IsolationTests
{
    // The parameters that name an account or a transaction, in the route or in the query.
    private static readonly string[] IdParameters = ["id", "accountId"];

    // Ada's transaction 709 at Deniz's shop has its shop leg 710 on Deniz's account: related to Ada, but not hers.
    private static readonly Guid DenizTransaction = Guid.Parse("30000000-0000-0000-0000-000000000710");

    // "METHOD route template" → a request with "{0}" where the foreign or the missing id goes. GETs only; the write is its own test.
    private static readonly Dictionary<string, string> Reads = new()
    {
        ["GET api/accounts/{id}"] = "/api/accounts/{0}",
        ["GET api/accounts/{accountId}/transactions"] = "/api/accounts/{0}/transactions",
        ["GET api/transactions"] = "/api/transactions?period=ThisMonth&accountId={0}",
        ["GET api/transactions/{id}"] = "/api/transactions/{0}",
        ["GET api/transactions/{id}/insight"] = "/api/transactions/{0}/insight",
        ["GET api/analytics/spending-summary"] = "/api/analytics/spending-summary?period=ThisMonth&accountId={0}",
        ["GET api/analytics/category-breakdown"] = "/api/analytics/category-breakdown?period=ThisMonth&accountId={0}",
        ["GET api/analytics/category-spending"] = "/api/analytics/category-spending?categoryCode=GROCERY&period=ThisMonth&accountId={0}",
        ["GET api/analytics/merchant-breakdown"] = "/api/analytics/merchant-breakdown?period=ThisMonth&accountId={0}",
        ["GET api/analytics/merchant-spending"] = "/api/analytics/merchant-spending?merchant=Starbucks&period=ThisMonth&accountId={0}",
        ["GET api/analytics/merchants"] = "/api/analytics/merchants?accountId={0}",
        ["GET api/analytics/merchant-group-spending"] = "/api/analytics/merchant-group-spending?merchants=Starbucks&period=ThisMonth&accountId={0}",
        ["GET api/analytics/period-comparison"] = "/api/analytics/period-comparison?comparison=PreviousFullMonth&accountId={0}",
        ["GET api/analytics/monthly-trend"] = "/api/analytics/monthly-trend?months=6&accountId={0}",
        ["GET api/analytics/highest-transactions"] = "/api/analytics/highest-transactions?period=ThisMonth&accountId={0}",
        ["GET api/analytics/recurring-payments"] = "/api/analytics/recurring-payments?accountId={0}",
        ["GET api/analytics/upcoming-payments"] = "/api/analytics/upcoming-payments?accountId={0}",
        ["GET api/analytics/budget-check"] = "/api/analytics/budget-check?amount=1000&currencyCode=TRY&accountId={0}",
        ["GET api/analytics/savings-simulation"] = "/api/analytics/savings-simulation?reductionPercent=10&accountId={0}",
    };

    private const string Write = "POST api/accounts/{accountId}/transactions";

    public static TheoryData<string> ReadEndpoints => new(Reads.Keys);

    private static async Task<string[]> EndpointsTakingAnIdAsync()
    {
        await using var api = TestApi.Create();
        using var client = api.CreateClient(); // builds the host, so the route table is complete
        return api.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<ControllerActionDescriptor>()?.Parameters.Any(p => IdParameters.Contains(p.Name)) is true)
            .SelectMany(e => (e.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? ["*"])
                .Select(method => $"{method} {e.RoutePattern.RawText!.TrimStart('/')}"))
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static async Task<(HttpStatusCode Status, string Body)> GetAsync(string path, Guid customer)
    {
        await using var api = TestApi.Create();
        using var client = api.CreateClient().WithToken(TestTokens.Create(customer));
        var response = await client.GetAsync(path);
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    // Only the per-request trace id may differ between two 404s.
    private static string WithoutTraceId(string body)
    {
        var json = JsonNode.Parse(body)!.AsObject();
        json.Remove("traceId");
        return json.ToJsonString();
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Every_endpoint_that_takes_an_account_or_transaction_id_has_an_isolation_sample()
    {
        // A new endpoint with an id fails here until it gets a sample, and with it the sweep below.
        Assert.Equal(Reads.Keys.Append(Write).Order(StringComparer.Ordinal), await EndpointsTakingAnIdAsync());
    }

    [Theory]
    [Trait("Category", "PostgreSQL")]
    [MemberData(nameof(ReadEndpoints))]
    public async Task Another_customers_id_gets_exactly_the_404_of_a_missing_one(string endpoint)
    {
        var foreignId = endpoint.StartsWith("GET api/transactions/{id}", StringComparison.Ordinal) ? DenizTransaction : DenizTryAccount;

        var foreign = await GetAsync(string.Format(Reads[endpoint], foreignId), Ada);
        var missing = await GetAsync(string.Format(Reads[endpoint], Guid.NewGuid()), Ada);

        Assert.True(foreign.Status == HttpStatusCode.NotFound, $"{endpoint}: {(int)foreign.Status} {foreign.Body}");
        Assert.Equal(HttpStatusCode.NotFound, missing.Status);
        // Nothing tells Ada that Deniz's account or transaction exists, nor shows any of it.
        Assert.Equal(WithoutTraceId(missing.Body), WithoutTraceId(foreign.Body));
        Assert.DoesNotContain("DUMMY-DENIZ", foreign.Body);
        Assert.DoesNotContain(foreignId.ToString(), foreign.Body);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Another_customers_account_cannot_be_written_to_and_stays_as_it_was()
    {
        await using var owner = await TestUser.CreateAsync();
        await using var api = TestApi.Create();
        using var ownerClient = api.CreateClient().WithToken(TestTokens.Create(owner.CustomerId));
        var opened = await ownerClient.PostAsync("/api/accounts",
            new StringContent("""{"accountName":"SEC-02 test","currencyCode":"TRY"}""", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Created, opened.StatusCode);
        var accountId = JsonNode.Parse(await opened.Content.ReadAsStringAsync())!["id"]!.GetValue<Guid>();
        var before = await ownerClient.GetStringAsync($"/api/accounts/{accountId}/transactions");

        using var adaClient = api.CreateClient().WithToken(TestTokens.Create(Ada));
        var foreign = await PostSalaryAsync(adaClient, accountId);
        var missing = await PostSalaryAsync(adaClient, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(WithoutTraceId(await missing.Content.ReadAsStringAsync()), WithoutTraceId(await foreign.Content.ReadAsStringAsync()));
        // The refusal wrote nothing: the owner's balance and list are what they were.
        Assert.Equal("0.00", JsonNode.Parse(await ownerClient.GetStringAsync($"/api/accounts/{accountId}"))!["balance"]!.ToJsonString());
        Assert.Equal(before, await ownerClient.GetStringAsync($"/api/accounts/{accountId}/transactions"));
    }

    private static Task<HttpResponseMessage> PostSalaryAsync(HttpClient client, Guid accountId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/accounts/{accountId}/transactions")
        {
            Content = new StringContent("""{"kind":"Salary","amount":10,"description":"SEC-02 deneme"}""", Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        return client.SendAsync(request);
    }
}
