using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using BankingAI.Hosting.Time;
using BankingAI.Data;
using BankingAI.Data.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BankingAI.Api.Tests;

// API-14. What the API writes is what the database holds: rows are read back directly, because the answers hide some of
// it on purpose (the owning customer, the category fields, server-set times). All writes go to a temporary TestUser.
public sealed class PersistenceEndpointTests
{
    private static async Task<JsonNode> PostAsync(HttpClient client, string path, string json, bool withKey = false)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        if (withKey)
            request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var response = await client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Created, text);
        return JsonNode.Parse(text)!;
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task An_opened_account_is_stored_for_the_token_owner()
    {
        await using var user = await TestUser.CreateAsync();
        await using var api = TestApi.Create();
        using var client = api.CreateClient().WithToken(TestTokens.Create(user.CustomerId));

        var before = DateTimeOffset.UtcNow.AddSeconds(-5);
        var answer = await PostAsync(client, "/api/accounts", """{"accountName":"Kalıcılık","currencyCode":"TRY"}""");
        var after = DateTimeOffset.UtcNow.AddSeconds(5);

        await using var db = new BankingDbContext();
        var row = await db.Accounts.AsNoTracking().SingleAsync(a => a.Id == answer["id"]!.GetValue<Guid>());
        // The owner is the token's customer; the answer never shows it, so only the row can prove it.
        Assert.Equal((user.CustomerId, "Kalıcılık", "TRY", AccountType.Personal, 0m),
            (row.CustomerId, row.AccountName, row.CurrencyCode, row.AccountType, row.Balance));
        Assert.Equal(answer["accountNumber"]!.GetValue<string>(), row.AccountNumber);
        Assert.StartsWith("ACC-", row.AccountNumber);
        Assert.InRange(row.CreatedAt, before, after);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task An_added_transaction_is_stored_with_the_fields_the_backend_decides()
    {
        await using var user = await TestUser.CreateAsync();
        await using var api = TestApi.Create((BusinessClock.DemoNowKey, BusinessClockTests.DemoNow));
        using var client = api.CreateClient().WithToken(TestTokens.Create(user.CustomerId));
        var account = (await PostAsync(client, "/api/accounts", """{"accountName":"Kalıcılık","currencyCode":"TRY"}"""))["id"]!.GetValue<Guid>();

        var answer = await PostAsync(client, $"/api/accounts/{account}/transactions",
            """{"kind":"Salary","amount":1000,"description":"Ekim maaşı"}""", withKey: true);

        await using var db = new BankingDbContext();
        var row = await db.Transactions.AsNoTracking().SingleAsync(t => t.Id == answer["id"]!.GetValue<Guid>());
        // The client said only kind, amount and text; direction, currency, account, time and category are the backend's.
        Assert.Equal((account, TransactionKind.Salary, TransactionDirection.Credit, 1000m, "TRY", "Ekim maaşı"),
            (row.AccountId, row.Kind, row.Direction, row.Amount, row.CurrencyCode, row.RawDescription));
        Assert.Equal(DateTimeOffset.Parse(BusinessClockTests.DemoNow), row.OccurredAt);
        Assert.Equal(((short?)null, (CategorizationSource?)null, (decimal?)null, (DateTimeOffset?)null, (Guid?)null, (string?)null),
            (row.CategoryId, row.CategorizationSource, row.CategoryConfidence, row.CategorizedAt, row.PairId, row.NormalizedMerchant));
        Assert.Equal(1000m, (await db.Accounts.AsNoTracking().SingleAsync(a => a.Id == account)).Balance);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Written_data_is_read_back_unchanged_by_a_new_api_instance()
    {
        await using var user = await TestUser.CreateAsync();
        var token = TestTokens.Create(user.CustomerId);
        Guid accountId, transactionId;
        string accountJson, transactionJson;
        await using (var first = TestApi.Create())
        {
            using var client = first.CreateClient().WithToken(token);
            accountId = (await PostAsync(client, "/api/accounts", """{"accountName":"Kalıcılık","currencyCode":"EUR"}"""))["id"]!.GetValue<Guid>();
            transactionId = (await PostAsync(client, $"/api/accounts/{accountId}/transactions",
                """{"kind":"Salary","amount":250.50,"description":"Avans"}""", withKey: true))["id"]!.GetValue<Guid>();
            accountJson = await client.GetStringAsync($"/api/accounts/{accountId}");
            transactionJson = await client.GetStringAsync($"/api/transactions/{transactionId}");
        }

        // A fresh host shares nothing in memory with the first one: only the database connects them.
        await using var second = TestApi.Create();
        using var reader = second.CreateClient().WithToken(token);
        Assert.Equal(accountJson, await reader.GetStringAsync($"/api/accounts/{accountId}"));
        Assert.Equal(transactionJson, await reader.GetStringAsync($"/api/transactions/{transactionId}"));
        Assert.Equal("250.50", JsonNode.Parse(accountJson)!["balance"]!.ToJsonString());
    }
}
