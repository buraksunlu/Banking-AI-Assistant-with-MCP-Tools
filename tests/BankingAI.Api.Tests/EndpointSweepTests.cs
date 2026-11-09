using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BankingAI.Api.Tests;

// API-14. Rules every endpoint must follow, checked against the API's real route table so that a new endpoint cannot
// be forgotten: each protected endpoint has a valid sample request here, refuses a request without a token, and tells a
// customer that no longer exists to sign in again. Only GET samples read demo data; every write is sent for a customer
// that does not exist, so nothing is ever written.
public sealed class EndpointSweepTests
{
    // "METHOD route template" → a valid request for an existing customer (path with query, optional JSON body).
    private static readonly Dictionary<string, (string Path, string? Body)> Protected = new()
    {
        ["GET api/me"] = ("/api/me", null),
        ["GET api/accounts"] = ("/api/accounts", null),
        ["GET api/accounts/{id}"] = ("/api/accounts/20000000-0000-0000-0000-000000000001", null),
        ["POST api/accounts"] = ("/api/accounts", """{"accountName":"Tarama","currencyCode":"TRY"}"""),
        ["GET api/accounts/{accountId}/transactions"] = ("/api/accounts/20000000-0000-0000-0000-000000000001/transactions", null),
        ["POST api/accounts/{accountId}/transactions"] =
            ("/api/accounts/20000000-0000-0000-0000-000000000001/transactions", """{"kind":"Salary","amount":10,"description":"Tarama"}"""),
        ["GET api/transactions"] = ("/api/transactions?period=ThisMonth", null),
        ["GET api/transactions/{id}"] = ("/api/transactions/30000000-0000-0000-0000-000000000602", null),
        ["GET api/analytics/spending-summary"] = ("/api/analytics/spending-summary?period=ThisMonth", null),
        ["GET api/analytics/category-breakdown"] = ("/api/analytics/category-breakdown?period=ThisMonth", null),
        ["GET api/analytics/merchant-breakdown"] = ("/api/analytics/merchant-breakdown?period=ThisMonth", null),
        ["GET api/analytics/merchant-spending"] = ("/api/analytics/merchant-spending?merchant=Starbucks&period=ThisMonth", null),
        ["GET api/analytics/merchants"] = ("/api/analytics/merchants", null),
        ["GET api/analytics/merchant-group-spending"] = ("/api/analytics/merchant-group-spending?merchants=Starbucks&period=ThisMonth", null),
        ["GET api/analytics/period-comparison"] = ("/api/analytics/period-comparison?comparison=PreviousFullMonth", null),
        ["GET api/analytics/monthly-trend"] = ("/api/analytics/monthly-trend?months=6", null),
        ["GET api/analytics/highest-transactions"] = ("/api/analytics/highest-transactions?period=ThisMonth", null),
        ["GET api/analytics/recurring-payments"] = ("/api/analytics/recurring-payments", null),
        ["GET api/analytics/upcoming-payments"] = ("/api/analytics/upcoming-payments", null),
        ["GET api/analytics/budget-check"] = ("/api/analytics/budget-check?amount=1000&currencyCode=TRY", null),
        ["GET api/analytics/savings-simulation"] = ("/api/analytics/savings-simulation?reductionPercent=10", null),
        ["GET api/analytics/income-share"] = ("/api/analytics/income-share?period=LastMonth", null),
        ["GET api/analytics/spending-advice"] = ("/api/analytics/spending-advice?period=LastMonth", null),
        ["GET api/analytics/what-if/one-off"] = ("/api/analytics/what-if/one-off?period=LastMonth", null),
        ["GET api/analytics/what-if/monthly"] = ("/api/analytics/what-if/monthly?fromAdvice=true", null),
        // The AI service is never reached here: the token and customer checks answer first.
        ["POST api/assistant/messages"] = ("/api/assistant/messages", """{"message":"Dün ne harcadım?"}"""),
        ["GET api/assistant/status"] = ("/api/assistant/status", null),
        ["GET api/assistant/suggestions"] = ("/api/assistant/suggestions", null),
    };

    // The only endpoints a client may call without a token: health, login, and in development the OpenAPI contract with
    // the Scalar page that browses it (API-15) and its static files.
    private static readonly string[] Anonymous =
    [
        "GET api/health", "POST api/auth/login", "GET openapi/{documentName}.json",
        "GET scalar/{documentName?}", "GET scalar/scalar.js", "GET scalar/scalar.aspnetcore.js", "GET scalar/favicon.svg"
    ];

    public static TheoryData<string> ProtectedEndpoints => new(Protected.Keys);

    private static async Task<(string Endpoint, bool Anonymous)[]> RouteTableAsync()
    {
        await using var api = TestApi.Create();
        using var client = api.CreateClient(); // builds the host, so the route table is complete
        return api.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .SelectMany(e => (e.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? ["*"])
                .Select(method => ($"{method} {e.RoutePattern.RawText!.TrimStart('/')}", e.Metadata.GetMetadata<IAllowAnonymous>() is not null)))
            .ToArray();
    }

    private static async Task<JsonObject> SendAsync(string endpoint, string? token, HttpStatusCode expected)
    {
        var method = new HttpMethod(endpoint[..endpoint.IndexOf(' ')]);
        var (path, body) = Protected[endpoint];
        await using var api = TestApi.Create();
        using var client = api.CreateClient();
        if (token is not null)
            client.WithToken(token);
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        if (method == HttpMethod.Post && path.EndsWith("/transactions"))
            request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var response = await client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode == expected, $"{endpoint}: {(int)response.StatusCode} {text}");
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        return JsonNode.Parse(text)!.AsObject();
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Every_endpoint_of_the_api_has_a_sample_request_here()
    {
        var endpoints = (await RouteTableAsync()).Select(e => e.Endpoint).Order(StringComparer.Ordinal);

        // A new endpoint fails here until it gets a sample, and with it the sweeps below.
        Assert.Equal(Protected.Keys.Concat(Anonymous).Order(StringComparer.Ordinal), endpoints);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Only_health_login_and_the_development_docs_are_open_without_a_token()
    {
        var open = (await RouteTableAsync()).Where(e => e.Anonymous).Select(e => e.Endpoint).Order(StringComparer.Ordinal);

        Assert.Equal(Anonymous.Order(StringComparer.Ordinal), open);
    }

    [Theory]
    [Trait("Category", "PostgreSQL")]
    [MemberData(nameof(ProtectedEndpoints))]
    public async Task Every_protected_endpoint_refuses_a_request_without_a_token(string endpoint)
    {
        var problem = await SendAsync(endpoint, null, HttpStatusCode.Unauthorized);

        Assert.Equal(("unauthorized", "Giriş gerekli."), (problem["code"]!.GetValue<string>(), problem["title"]!.GetValue<string>()));
    }

    [Theory]
    [Trait("Category", "PostgreSQL")]
    [MemberData(nameof(ProtectedEndpoints))]
    public async Task Every_protected_endpoint_tells_a_customer_that_no_longer_exists_to_sign_in_again(string endpoint)
    {
        // A valid token of a customer that does not exist. The sample is a valid request, so a 400 here would mean a bad
        // sample, and anything other than session_invalid means the endpoint skipped the customer check.
        var problem = await SendAsync(endpoint, TestTokens.Create(Guid.NewGuid()), HttpStatusCode.Unauthorized);

        Assert.Equal(("session_invalid", "Oturum geçersiz; yeniden giriş yapın."),
            (problem["code"]!.GetValue<string>(), problem["title"]!.GetValue<string>()));
    }
}
