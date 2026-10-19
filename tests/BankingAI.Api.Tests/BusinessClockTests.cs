using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using BankingAI.Hosting.Time;
using Xunit;
using static BankingAI.Application.Tests.DemoData;

namespace BankingAI.Api.Tests;

// ANA-09 decision 2-A. BANKINGAI_DEMO_NOW fixes the business clock (analytics periods, time of new transactions) to
// the demo moment; token lifetimes keep the real clock.
public sealed class BusinessClockTests
{
    public const string DemoNow = "2026-10-10T12:00:00Z";

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task The_demo_moment_decides_the_default_period()
    {
        await using var api = TestApi.Create((BusinessClock.DemoNowKey, DemoNow));
        using var client = api.CreateClient().WithToken(TestTokens.Create(Ada));

        var page = JsonNode.Parse(await client.GetStringAsync($"/api/accounts/{AdaTryAccount}/transactions"))!;

        // Last three months ending on 10 October 2026 in Istanbul, whatever the real date is.
        Assert.Equal(("Last3Months", "2026-07-10", "2026-10-10"),
            (page["period"]!["kind"]!.GetValue<string>(), page["period"]!["startDate"]!.GetValue<string>(),
             page["period"]!["endDate"]!.GetValue<string>()));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task A_new_transaction_happens_at_the_demo_moment()
    {
        await using var user = await TestUser.CreateAsync();
        await using var api = TestApi.Create((BusinessClock.DemoNowKey, DemoNow));
        using var client = api.CreateClient().WithToken(TestTokens.Create(user.CustomerId));
        var opened = await client.PostAsync("/api/accounts",
            new StringContent("""{"accountName":"Demo saati","currencyCode":"TRY"}""", Encoding.UTF8, "application/json"));
        var accountId = JsonNode.Parse(await opened.Content.ReadAsStringAsync())!["id"]!.GetValue<Guid>();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/accounts/{accountId}/transactions")
        {
            Content = new StringContent("""{"kind":"Salary","amount":10,"description":"Maaş"}""", Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var created = JsonNode.Parse(await (await client.SendAsync(request)).Content.ReadAsStringAsync())!;

        Assert.Equal(("2026-10-10T12:00:00+00:00", "2026-10-10"),
            (created["occurredAtUtc"]!.GetValue<string>(), created["localDate"]!.GetValue<string>()));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Tokens_keep_the_real_clock()
    {
        // A token issued on the demo clock would have expired on 10 October and be refused today.
        await using var user = await TestUser.CreateAsync();
        await using var api = TestApi.Create((BusinessClock.DemoNowKey, DemoNow));
        using var client = api.CreateClient();
        var login = await client.PostAsync("/api/auth/login", new StringContent(
            $$"""{"username":"{{user.Username}}","password":"{{user.Password}}"}""", Encoding.UTF8, "application/json"));
        var token = JsonNode.Parse(await login.Content.ReadAsStringAsync())!["token"]!.GetValue<string>();

        Assert.Equal(HttpStatusCode.OK, (await client.WithToken(token).GetAsync("/api/me")).StatusCode);
    }

    [Theory]
    [Trait("Category", "PostgreSQL")]
    [InlineData("2999-01-01T00:00:00Z")]   // future: every new transaction would be rejected
    [InlineData("yarın")]
    [InlineData("2026-10-10T15:00:00+03:00")] // UTC only, written with Z
    public async Task A_future_or_malformed_demo_moment_stops_the_api(string value)
    {
        await using var api = TestApi.Create((BusinessClock.DemoNowKey, value));

        var error = Assert.Throws<InvalidOperationException>(() => api.CreateClient());
        Assert.Contains(BusinessClock.DemoNowKey, error.Message);
    }
}
