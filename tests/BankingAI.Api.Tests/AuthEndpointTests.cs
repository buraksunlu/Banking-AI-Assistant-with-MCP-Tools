using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using BankingAI.Api.Controllers;
using BankingAI.Api.Security;
using Xunit;

namespace BankingAI.Api.Tests;

// SEC-01b. Login, token validation, GET /api/me and the login rate limit, through the real HTTP pipeline.
public sealed class AuthEndpointTests
{
    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string username, string password) =>
        client.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, password));

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Login_returns_a_token_valid_for_60_minutes()
    {
        await using var user = await TestUser.CreateAsync();
        await using var api = TestApi.Create();
        using var client = api.CreateClient();

        var before = DateTimeOffset.UtcNow;
        var response = await LoginAsync(client, user.Username, user.Password);
        var after = DateTimeOffset.UtcNow;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var login = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.False(string.IsNullOrWhiteSpace(login!.Token));
        // Token times are whole seconds, so the expiry may sit up to one second before "before + 60 min".
        Assert.InRange(login.ExpiresAt, before.AddMinutes(60).AddSeconds(-1), after.AddMinutes(60));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Wrong_password_and_unknown_username_get_the_same_401()
    {
        await using var user = await TestUser.CreateAsync();
        await using var api = TestApi.Create();
        using var client = api.CreateClient();

        var wrongPassword = await LoginAsync(client, user.Username, user.Password + "x");
        var unknownUser = await LoginAsync(client, "apitest.nobody", user.Password);

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownUser.StatusCode);
        // The per-request trace id is the only part allowed to differ.
        Assert.Equal(await BodyWithoutTraceId(wrongPassword), await BodyWithoutTraceId(unknownUser));
    }

    private static async Task<string> BodyWithoutTraceId(HttpResponseMessage response)
    {
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        body.Remove("traceId");
        return body.ToJsonString();
    }

    [Theory]
    [Trait("Category", "PostgreSQL")]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("""{"username":"ada"}""")]
    [InlineData("""{"username":"   ","password":"some-password"}""")]
    public async Task Missing_or_blank_fields_are_400(string body)
    {
        await using var api = TestApi.Create();
        using var client = api.CreateClient();

        var response = await client.PostAsync("/api/auth/login", new StringContent(body, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Sixth_login_attempt_within_a_minute_is_429()
    {
        await using var user = await TestUser.CreateAsync();
        await using var api = TestApi.Create();
        using var client = api.CreateClient();

        for (var attempt = 1; attempt <= 5; attempt++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(client, user.Username, "wrong-password")).StatusCode);

        // Every attempt counts, so even the right password is refused once the limit is reached.
        Assert.Equal(HttpStatusCode.TooManyRequests, (await LoginAsync(client, user.Username, user.Password)).StatusCode);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Me_requires_a_token()
    {
        await using var api = TestApi.Create();
        using var client = api.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/me")).StatusCode);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Me_returns_the_customer_of_the_token()
    {
        await using var user = await TestUser.CreateAsync();
        await using var api = TestApi.Create();
        using var client = api.CreateClient();
        var login = await (await LoginAsync(client, user.Username, user.Password)).Content.ReadFromJsonAsync<LoginResponse>();

        var response = await client.WithToken(login!.Token).GetAsync("/api/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var me = await response.Content.ReadFromJsonAsync<MeResponse>();
        Assert.Equal(user.Username, me!.Username);
        Assert.StartsWith("CUST-", me.CustomerNumber);
        Assert.Equal(("Test", "Api", "TRY", "Europe/Istanbul"), (me.FirstName, me.LastName, me.PreferredCurrency, me.TimeZone));
        Assert.DoesNotContain("customerId", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [Trait("Category", "PostgreSQL")]
    [InlineData("valid", HttpStatusCode.OK)] // control: the forging helper itself produces acceptable tokens
    [InlineData("other-key", HttpStatusCode.Unauthorized)]
    [InlineData("expired", HttpStatusCode.Unauthorized)]
    [InlineData("malformed", HttpStatusCode.Unauthorized)]
    public async Task Only_valid_tokens_reach_me(string kind, HttpStatusCode expected)
    {
        await using var user = await TestUser.CreateAsync();
        await using var api = TestApi.Create();
        using var client = api.CreateClient();
        var token = kind switch
        {
            "valid" => TestTokens.Create(user.CustomerId),
            "other-key" => TestTokens.Create(user.CustomerId, signingKey: "another-key-that-is-long-enough-0123456789"),
            // One minute late is inside the default 5-minute clock skew, so this also proves the skew is zero.
            "expired" => TestTokens.Create(user.CustomerId, expiresUtc: DateTime.UtcNow.AddMinutes(-1)),
            _ => "not-a-token"
        };

        Assert.Equal(expected, (await client.WithToken(token).GetAsync("/api/me")).StatusCode);
    }

    [Theory]
    [Trait("Category", "PostgreSQL")]
    [InlineData("/api/health")]
    [InlineData("/openapi/v1.json")]
    public async Task Public_endpoints_need_no_token(string path)
    {
        await using var api = TestApi.Create();
        using var client = api.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(path)).StatusCode);
    }

    [Theory]
    [Trait("Category", "PostgreSQL")]
    [InlineData("   ")]
    [InlineData("too-short-key")] // 13 bytes, below the 32-byte minimum
    public void Missing_or_short_signing_key_stops_the_api_at_startup(string key)
    {
        using var api = TestApi.Create((JwtSettings.SigningKeyKey, key));

        var error = Assert.Throws<InvalidOperationException>(() => api.CreateClient());
        Assert.Contains(JwtSettings.SigningKeyKey, error.Message);
    }
}
