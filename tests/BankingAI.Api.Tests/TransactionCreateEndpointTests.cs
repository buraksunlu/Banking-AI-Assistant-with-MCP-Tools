using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using BankingAI.Data;
using BankingAI.Data.Entities;
using BankingAI.Data.Enums;
using Xunit;
using static BankingAI.Application.Tests.DemoData;

namespace BankingAI.Api.Tests;

// API-11. POST /api/accounts/{accountId}/transactions. Every write goes to an account of a temporary TestUser,
// deleted with it. Demo accounts are never the target, not even of a test that expects a refusal: a check broken
// on purpose (or by a bug) must only ever be able to touch temporary rows. That is why "another customer's
// account" is tested as Ada writing into the test user's account.
public sealed class TransactionCreateEndpointTests
{
    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, Guid accountId, string json) =>
        client.PostAsync($"/api/accounts/{accountId}/transactions", Json(json));

    private static string Salary(string amount) => $$"""{"kind":"Salary","amount":{{amount}},"description":"Maaş ödemesi"}""";

    private static string Purchase(string amount) => $$"""{"kind":"Purchase","amount":{{amount}},"description":"POS 4412 STARBUCKS KADIKOY","merchant":"Starbucks"}""";

    private static async Task<Guid> OpenAccountAsync(HttpClient client, string currencyCode = "TRY")
    {
        var response = await client.PostAsync("/api/accounts", Json($$"""{"accountName":"API-11 test","currencyCode":"{{currencyCode}}"}"""));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return JsonNode.Parse(await response.Content.ReadAsStringAsync())!["id"]!.GetValue<Guid>();
    }

    private static async Task<string> BalanceAsync(HttpClient client, Guid accountId) =>
        JsonNode.Parse(await client.GetStringAsync($"/api/accounts/{accountId}"))!["balance"]!.ToJsonString();

    private static async Task<JsonObject> BodyAsync(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();

    private static string WithoutTraceId(string body)
    {
        var json = JsonNode.Parse(body)!.AsObject();
        json.Remove("traceId");
        return json.ToJsonString();
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Adding_a_transaction_requires_a_token()
    {
        await using var api = TestApi.Create();
        using var client = api.CreateClient();

        var response = await PostAsync(client, Guid.NewGuid(), Salary("10"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Salary_and_purchases_return_201_and_move_the_balance_until_it_runs_out()
    {
        await using var user = await TestUser.CreateAsync();
        await using var api = TestApi.Create();
        using var client = api.CreateClient().WithToken(TestTokens.Create(user.CustomerId));
        var account = await OpenAccountAsync(client);

        var salary = await PostAsync(client, account, Salary("1000"));
        Assert.Equal(HttpStatusCode.Created, salary.StatusCode);
        var salaryBody = await BodyAsync(salary);
        Assert.Equal(("Salary", "Credit", "1000.00", "TRY"),
            (salaryBody["kind"]!.GetValue<string>(), salaryBody["direction"]!.GetValue<string>(),
             salaryBody["amount"]!.ToJsonString(), salaryBody["currencyCode"]!.GetValue<string>()));
        Assert.Equal("1000.00", await BalanceAsync(client, account));

        // The Location header points at the new transaction, which reads back exactly as it was returned.
        var purchase = await PostAsync(client, account, Purchase("250.75"));
        Assert.Equal(HttpStatusCode.Created, purchase.StatusCode);
        var purchaseJson = await purchase.Content.ReadAsStringAsync();
        var purchaseBody = JsonNode.Parse(purchaseJson)!.AsObject();
        var purchaseId = purchaseBody["id"]!.GetValue<Guid>();
        Assert.Equal($"/api/transactions/{purchaseId}", purchase.Headers.Location!.AbsolutePath);
        Assert.Equal(purchaseJson, await client.GetStringAsync(purchase.Headers.Location));
        Assert.Equal((account, "Debit", "250.75", "Starbucks", "Sınıflandırılmamış"),
            (purchaseBody["accountId"]!.GetValue<Guid>(), purchaseBody["direction"]!.GetValue<string>(),
             purchaseBody["amount"]!.ToJsonString(), purchaseBody["merchant"]!.GetValue<string>(),
             purchaseBody["categoryDisplayName"]!.GetValue<string>()));
        Assert.Equal("749.25", await BalanceAsync(client, account));

        // One cent more than the balance is refused and changes nothing; exactly the balance is allowed.
        var tooMuch = await PostAsync(client, account, Purchase("749.26"));
        Assert.Equal(HttpStatusCode.Conflict, tooMuch.StatusCode);
        var problem = await BodyAsync(tooMuch);
        Assert.Equal(("Bakiye sınırı", 409, "balance_limit"),
            (problem["title"]!.GetValue<string>(), problem["status"]!.GetValue<int>(), problem["code"]!.GetValue<string>()));
        Assert.Contains("Yetersiz bakiye", problem["detail"]!.GetValue<string>());
        Assert.Equal("749.25", await BalanceAsync(client, account));

        var rest = await PostAsync(client, account, Purchase("749.25"));
        Assert.Equal(HttpStatusCode.Created, rest.StatusCode);
        Assert.Equal("0.00", await BalanceAsync(client, account));

        // The account's statement shows the three written transactions, newest first.
        var page = JsonNode.Parse(await client.GetStringAsync($"/api/accounts/{account}/transactions"))!;
        Assert.Equal(3, page["totalCount"]!.GetValue<int>());
        Assert.Equal([(await BodyAsync(rest))["id"]!.GetValue<Guid>(), purchaseId, salaryBody["id"]!.GetValue<Guid>()],
            page["items"]!.AsArray().Select(i => i!["id"]!.GetValue<Guid>()));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Concurrent_purchases_never_overdraw_the_account()
    {
        await using var user = await TestUser.CreateAsync();
        await using var api = TestApi.Create();
        using var client = api.CreateClient().WithToken(TestTokens.Create(user.CustomerId));
        var account = await OpenAccountAsync(client);
        Assert.Equal(HttpStatusCode.Created, (await PostAsync(client, account, Salary("100"))).StatusCode);

        // 60 + 60 > 100: the atomic balance update lets exactly one of them through.
        var responses = await Task.WhenAll(PostAsync(client, account, Purchase("60")), PostAsync(client, account, Purchase("60")));

        Assert.Equal([HttpStatusCode.Created, HttpStatusCode.Conflict], responses.Select(r => r.StatusCode).Order());
        Assert.Equal("40.00", await BalanceAsync(client, account));
        var page = JsonNode.Parse(await client.GetStringAsync($"/api/accounts/{account}/transactions"))!;
        Assert.Equal(2, page["totalCount"]!.GetValue<int>());
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task A_salary_above_the_money_ceiling_is_409()
    {
        await using var user = await TestUser.CreateAsync();
        await using var api = TestApi.Create();
        using var client = api.CreateClient().WithToken(TestTokens.Create(user.CustomerId));
        var account = await OpenAccountAsync(client);

        Assert.Equal(HttpStatusCode.Created, (await PostAsync(client, account, Salary("9999999999999999.99"))).StatusCode);
        var overflow = await PostAsync(client, account, Salary("0.01"));

        Assert.Equal(HttpStatusCode.Conflict, overflow.StatusCode);
        Assert.Contains("üst sınır", (await BodyAsync(overflow))["detail"]!.GetValue<string>());
        Assert.Equal("9999999999999999.99", await BalanceAsync(client, account));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task The_currency_comes_from_the_account()
    {
        await using var user = await TestUser.CreateAsync();
        await using var api = TestApi.Create();
        using var client = api.CreateClient().WithToken(TestTokens.Create(user.CustomerId));
        var account = await OpenAccountAsync(client, "USD");

        var response = await PostAsync(client, account, Salary("10"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("USD", (await BodyAsync(response))["currencyCode"]!.GetValue<string>());
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task A_business_account_accepts_transactions_too()
    {
        // Decision 3-B. The API only opens personal accounts, so the test user's shop account is added directly.
        await using var user = await TestUser.CreateAsync();
        var shop = new Account
        {
            CustomerId = user.CustomerId, AccountName = "API-11 dükkân", AccountNumber = $"DUMMY-APITEST-BIZ-{Guid.NewGuid():N}",
            CurrencyCode = "TRY", AccountType = AccountType.Business
        };
        await using (var db = new BankingDbContext())
        {
            db.Accounts.Add(shop);
            await db.SaveChangesAsync();
        }
        await using var api = TestApi.Create();
        using var client = api.CreateClient().WithToken(TestTokens.Create(user.CustomerId));

        Assert.Equal(HttpStatusCode.Created, (await PostAsync(client, shop.Id, Salary("50"))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await PostAsync(client, shop.Id, Purchase("20"))).StatusCode);

        Assert.Equal("30.00", await BalanceAsync(client, shop.Id));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Another_customers_account_gets_exactly_the_404_of_a_missing_one()
    {
        await using var user = await TestUser.CreateAsync();
        await using var api = TestApi.Create();
        using var owner = api.CreateClient().WithToken(TestTokens.Create(user.CustomerId));
        var account = await OpenAccountAsync(owner);
        using var ada = api.CreateClient().WithToken(TestTokens.Create(Ada));

        var foreign = await PostAsync(ada, account, Salary("10"));
        var missing = await PostAsync(ada, Guid.NewGuid(), Salary("10"));

        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(WithoutTraceId(await missing.Content.ReadAsStringAsync()), WithoutTraceId(await foreign.Content.ReadAsStringAsync()));
        Assert.Equal("0.00", await BalanceAsync(owner, account));
    }

    [Theory]
    [Trait("Category", "PostgreSQL")]
    [InlineData("""{"kind":"Transfer","amount":10,"description":"Para gönderimi"}""")]                    // two-legged kinds are refused
    [InlineData("""{"kind":"Salary","amount":10,"description":"Maaş","currencyCode":"USD"}""")]         // backend-owned field
    [InlineData("""{"kind":"Purchase","amount":12.345,"description":"POS"}""")]                          // never rounded
    [InlineData("""{"kind":"Salary","amount":10,"description":"Maaş","merchant":"Starbucks"}""")]       // a salary has no shop
    [InlineData("""{"kind":"Salary","amount":10,"description":"POS\u0000X"}""")]                         // NUL, caught by the transaction rules
    [InlineData("")]
    public async Task Invalid_bodies_are_400_and_write_nothing(string json)
    {
        await using var user = await TestUser.CreateAsync();
        await using var api = TestApi.Create();
        using var client = api.CreateClient().WithToken(TestTokens.Create(user.CustomerId));
        var account = await OpenAccountAsync(client);

        var response = await PostAsync(client, account, json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("0.00", await BalanceAsync(client, account));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task A_malformed_account_id_is_400()
    {
        await using var api = TestApi.Create();
        using var client = api.CreateClient().WithToken(TestTokens.Create(Ada));

        var response = await client.PostAsync("/api/accounts/abc/transactions", Json(Salary("10")));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task A_customer_that_no_longer_exists_gets_401()
    {
        // API-13: a missing customer is told apart from a missing account.
        await using var api = TestApi.Create();
        using var client = api.CreateClient().WithToken(TestTokens.Create(Guid.NewGuid()));

        var response = await PostAsync(client, Guid.NewGuid(), Salary("10"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
