using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Xunit;

namespace BankingAI.Api.Tests;

// API-12. The Idempotency-Key header: a retried request returns the first answer and changes nothing. All writes go
// to a temporary TestUser; the same key under another customer is covered by IdempotentIdTests (no second lock here).
public sealed class IdempotencyEndpointTests
{
    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string path, string json, string? key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        if (key is not null)
            request.Headers.Add("Idempotency-Key", key);
        return await client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> AddAsync(HttpClient client, Guid accountId, string amount, string? key, string kind = "Salary") =>
        PostAsync(client, $"/api/accounts/{accountId}/transactions",
            $$"""{"kind":"{{kind}}","amount":{{amount}},"description":"API-12 test"}""", key);

    private static async Task<Guid> OpenAccountAsync(HttpClient client)
    {
        var response = await PostAsync(client, "/api/accounts", """{"accountName":"API-12 test","currencyCode":"TRY"}""", null);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return JsonNode.Parse(await response.Content.ReadAsStringAsync())!["id"]!.GetValue<Guid>();
    }

    private static async Task<(string Balance, int Transactions)> StateAsync(HttpClient client, Guid accountId)
    {
        var balance = JsonNode.Parse(await client.GetStringAsync($"/api/accounts/{accountId}"))!["balance"]!.ToJsonString();
        var page = JsonNode.Parse(await client.GetStringAsync($"/api/accounts/{accountId}/transactions"))!;
        return (balance, page["totalCount"]!.GetValue<int>());
    }

    private static bool Replayed(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Idempotent-Replayed", out var values) && values.Single() == "true";

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task The_same_key_replays_the_first_answer_and_moves_the_balance_once()
    {
        await using var user = await TestUser.CreateAsync();
        await using var api = TestApi.Create();
        using var client = api.CreateClient().WithToken(TestTokens.Create(user.CustomerId));
        var account = await OpenAccountAsync(client);

        var first = await AddAsync(client, account, "100", "maas-ekim");
        var retry = await AddAsync(client, account, "100", "maas-ekim");

        Assert.Equal((HttpStatusCode.Created, HttpStatusCode.Created), (first.StatusCode, retry.StatusCode));
        Assert.Equal((false, true), (Replayed(first), Replayed(retry)));
        Assert.Equal(await first.Content.ReadAsStringAsync(), await retry.Content.ReadAsStringAsync());
        Assert.Equal(first.Headers.Location, retry.Headers.Location);
        Assert.Equal(("100.00", 1), await StateAsync(client, account));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task The_same_key_for_a_different_request_is_409()
    {
        await using var user = await TestUser.CreateAsync();
        await using var api = TestApi.Create();
        using var client = api.CreateClient().WithToken(TestTokens.Create(user.CustomerId));
        var account = await OpenAccountAsync(client);
        var other = await OpenAccountAsync(client);
        await AddAsync(client, account, "100", "maas-ekim");

        var otherAmount = await AddAsync(client, account, "200", "maas-ekim");
        var otherAccount = await AddAsync(client, other, "100", "maas-ekim");

        foreach (var response in new[] { otherAmount, otherAccount })
        {
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal("idempotency_conflict", JsonNode.Parse(await response.Content.ReadAsStringAsync())!["code"]!.GetValue<string>());
        }
        Assert.Equal(("100.00", 1), await StateAsync(client, account));
        Assert.Equal(("0.00", 0), await StateAsync(client, other));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Concurrent_requests_with_the_same_key_write_once()
    {
        await using var user = await TestUser.CreateAsync();
        await using var api = TestApi.Create();
        using var client = api.CreateClient().WithToken(TestTokens.Create(user.CustomerId));
        var account = await OpenAccountAsync(client);

        var responses = await Task.WhenAll(AddAsync(client, account, "100", "maas-ekim"), AddAsync(client, account, "100", "maas-ekim"));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        Assert.Equal(1, responses.Count(Replayed));
        Assert.Equal(("100.00", 1), await StateAsync(client, account));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task A_failed_attempt_can_be_retried_with_the_same_key()
    {
        await using var user = await TestUser.CreateAsync();
        await using var api = TestApi.Create();
        using var client = api.CreateClient().WithToken(TestTokens.Create(user.CustomerId));
        var account = await OpenAccountAsync(client);

        Assert.Equal(HttpStatusCode.Conflict, (await AddAsync(client, account, "50", "kahve-1", "Purchase")).StatusCode);
        await AddAsync(client, account, "100", "maas-ekim");
        var retry = await AddAsync(client, account, "50", "kahve-1", "Purchase");

        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        Assert.False(Replayed(retry));
        Assert.Equal(("50.00", 2), await StateAsync(client, account));
    }

    [Theory]
    [Trait("Category", "PostgreSQL")]
    [InlineData(null, "Zorunlu alan.")]
    [InlineData("iki kelime", "1–100 görünür ASCII karakter olmalı (boşluk yok).")]
    [InlineData("şifre", "1–100 görünür ASCII karakter olmalı (boşluk yok).")]
    public async Task Adding_a_transaction_needs_a_valid_key(string? key, string message)
    {
        await using var user = await TestUser.CreateAsync();
        await using var api = TestApi.Create();
        using var client = api.CreateClient().WithToken(TestTokens.Create(user.CustomerId));
        var account = await OpenAccountAsync(client);

        var response = await AddAsync(client, account, "100", key);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = JsonNode.Parse(await response.Content.ReadAsStringAsync())!["errors"]!.AsObject();
        Assert.Equal([message], errors["idempotencyKey"]!.AsArray().Select(m => m!.GetValue<string>()));
        Assert.Equal(("0.00", 0), await StateAsync(client, account));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Opening_an_account_with_a_key_opens_it_once()
    {
        await using var user = await TestUser.CreateAsync();
        await using var api = TestApi.Create();
        using var client = api.CreateClient().WithToken(TestTokens.Create(user.CustomerId));
        const string body = """{"accountName":"Tatil","currencyCode":"EUR"}""";

        var first = await PostAsync(client, "/api/accounts", body, "hesap-tatil");
        var retry = await PostAsync(client, "/api/accounts", body, "hesap-tatil");
        var different = await PostAsync(client, "/api/accounts", """{"accountName":"Tatil","currencyCode":"USD"}""", "hesap-tatil");

        Assert.Equal((HttpStatusCode.Created, HttpStatusCode.Created, HttpStatusCode.Conflict), (first.StatusCode, retry.StatusCode, different.StatusCode));
        Assert.True(Replayed(retry));
        Assert.Equal(await first.Content.ReadAsStringAsync(), await retry.Content.ReadAsStringAsync());
        Assert.Single(JsonNode.Parse(await client.GetStringAsync("/api/accounts"))!.AsArray());
    }
}
