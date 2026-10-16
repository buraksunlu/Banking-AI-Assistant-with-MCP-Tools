using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using BankingAI.Application.Accounts;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static BankingAI.Application.Tests.DemoData;

namespace BankingAI.Api.Tests;

// API-13. Every error is a ProblemDetails body with a Turkish title, a fixed machine code and a traceId, and never
// shows internals (type names, parameter suffixes, JSON paths, stack traces). Writes are only attempted for a
// customer that does not exist, or are refused before any service runs, so nothing can ever reach demo data.
public sealed class ErrorResponseTests
{
    private static readonly string[] Leaks = ["BankingAI.", "System.", "(Parameter", "LineNumber", "$.", "   at ", "could not be"];

    private static Guid Tx(int ordinal) => Guid.Parse($"30000000-0000-0000-0000-{ordinal:D12}");

    private static async Task<(HttpStatusCode Status, string? MediaType, string Body)> SendAsync(
        HttpMethod method, string path, string? token, string? body = null, string mediaType = "application/json",
        WebApplicationFactory<Program>? host = null)
    {
        await using var api = host is null ? TestApi.Create() : null;
        using var client = (host ?? api!).CreateClient();
        if (token is not null)
            client.WithToken(token);
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
            request.Content = new StringContent(body, Encoding.UTF8, mediaType);
        // Adding a transaction needs a key (API-12); its absence is tested where it matters.
        if (method == HttpMethod.Post && path.EndsWith("/transactions"))
            request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var response = await client.SendAsync(request);
        return (response.StatusCode, response.Content.Headers.ContentType?.MediaType, await response.Content.ReadAsStringAsync());
    }

    // The shared shape: problem+json, the expected status and code, a title, a traceId and no internals.
    private static JsonObject AssertProblem((HttpStatusCode Status, string? MediaType, string Body) response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.Status);
        Assert.Equal("application/problem+json", response.MediaType);
        var problem = JsonNode.Parse(response.Body)!.AsObject();
        Assert.Equal(((int)status, code), (problem["status"]!.GetValue<int>(), problem["code"]!.GetValue<string>()));
        Assert.False(string.IsNullOrWhiteSpace(problem["title"]?.GetValue<string>()));
        Assert.False(string.IsNullOrWhiteSpace(problem["traceId"]?.GetValue<string>()));
        foreach (var leak in Leaks)
            Assert.DoesNotContain(leak, response.Body);
        return problem;
    }

    private static Dictionary<string, string[]> Errors(JsonObject problem) =>
        problem["errors"]!.AsObject().ToDictionary(e => e.Key, e => e.Value!.AsArray().Select(m => m!.GetValue<string>()).ToArray());

    private static string AdaToken() => TestTokens.Create(Ada);

    // A customer that does not exist: any write it attempts is refused, so these tests cannot change data.
    private static string Ghost() => TestTokens.Create(Guid.NewGuid());

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Missing_or_expired_token_is_a_401_problem()
    {
        var problem = AssertProblem(await SendAsync(HttpMethod.Get, "/api/accounts", null), HttpStatusCode.Unauthorized, "unauthorized");
        Assert.Equal("Giriş gerekli.", problem["title"]!.GetValue<string>());

        AssertProblem(await SendAsync(HttpMethod.Get, "/api/accounts", TestTokens.Create(Ada, expiresUtc: DateTime.UtcNow.AddMinutes(-1))),
            HttpStatusCode.Unauthorized, "unauthorized");
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Unknown_route_wrong_method_and_non_json_body_are_problems()
    {
        AssertProblem(await SendAsync(HttpMethod.Get, "/api/yok", AdaToken()), HttpStatusCode.NotFound, "not_found");
        AssertProblem(await SendAsync(HttpMethod.Delete, "/api/accounts", AdaToken()), HttpStatusCode.MethodNotAllowed, "method_not_allowed");
        AssertProblem(await SendAsync(HttpMethod.Post, "/api/accounts", Ghost(), "accountName=x", "text/plain"),
            HttpStatusCode.UnsupportedMediaType, "unsupported_media_type");
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Another_customers_account_is_a_404_problem_without_details()
    {
        var problem = AssertProblem(await SendAsync(HttpMethod.Get, $"/api/accounts/{DenizTryAccount}", AdaToken()), HttpStatusCode.NotFound, "not_found");

        Assert.Equal("Bulunamadı.", problem["title"]!.GetValue<string>());
        Assert.Null(problem["detail"]);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task A_service_rule_is_a_400_without_the_parameter_suffix()
    {
        var problem = AssertProblem(await SendAsync(HttpMethod.Get, $"/api/accounts/{AdaTryAccount}/transactions?limit=0", AdaToken()),
            HttpStatusCode.BadRequest, "validation_failed");

        Assert.Equal("İstek geçersiz.", problem["title"]!.GetValue<string>());
        Assert.Equal(["1 ile 50 arasında olmalı."], Errors(problem)["limit"]);
    }

    [Theory]
    [Trait("Category", "PostgreSQL")]
    [InlineData("""{"accountName":"Tatil","currencyCode":"TRY","balance":5}""", "balance", "Tanınmayan alan.")]
    [InlineData("""{"accountName": "Tatil", """, "body", "Geçersiz JSON.")]
    [InlineData("", "body", "İstek gövdesi zorunlu.")]
    [InlineData("""{"currencyCode":"TRY"}""", "accountName", "Zorunlu alan.")]
    [InlineData("""{"accountName":"Tatil\u0000","currencyCode":"TRY"}""", "accountName", "Geçerli Unicode metin olmalı; NUL içeremez.")]
    public async Task Account_body_errors_name_the_clients_field_in_turkish(string body, string field, string message)
    {
        var problem = AssertProblem(await SendAsync(HttpMethod.Post, "/api/accounts", Ghost(), body), HttpStatusCode.BadRequest, "validation_failed");

        // Exactly one entry: no extra "request field is required" line next to the real problem.
        Assert.Equal(new Dictionary<string, string[]> { [field] = [message] }, Errors(problem));
    }

    [Theory]
    [Trait("Category", "PostgreSQL")]
    [InlineData("""{"kind":1,"amount":10,"description":"x"}""", "kind", "Geçersiz değer.")]          // numbers are not enum names
    [InlineData("""{"kind":"purchase","amount":10,"description":"x"}""", "kind", "Geçersiz değer.")] // names are exact (4-A)
    [InlineData("""{"kind":"Salary","amount":"on","description":"x"}""", "amount", "Geçersiz değer.")]
    [InlineData("""{"kind":"Salary","amount":10,"description":"POS\u0000X"}""", "description", "Geçerli Unicode metin olmalı; NUL içeremez.")]
    public async Task Transaction_body_errors_name_the_clients_field_in_turkish(string body, string field, string message)
    {
        var problem = AssertProblem(await SendAsync(HttpMethod.Post, $"/api/accounts/{Guid.NewGuid()}/transactions", Ghost(), body),
            HttpStatusCode.BadRequest, "validation_failed");

        Assert.Equal(new Dictionary<string, string[]> { [field] = [message] }, Errors(problem));
    }

    // API-14: an empty body and the JSON literal null are both "no request". Neither may reach a controller, where a null
    // request would be dereferenced; the writes are sent for a customer that does not exist anyway.
    [Theory]
    [Trait("Category", "PostgreSQL")]
    [InlineData("/api/accounts", "")]
    [InlineData("/api/accounts", "null")]
    [InlineData("/api/accounts/20000000-0000-0000-0000-000000000099/transactions", "")]
    [InlineData("/api/accounts/20000000-0000-0000-0000-000000000099/transactions", "null")]
    [InlineData("/api/auth/login", "")]
    [InlineData("/api/auth/login", "null")]
    public async Task An_empty_or_null_body_is_a_400_on_every_write(string path, string body)
    {
        var problem = AssertProblem(await SendAsync(HttpMethod.Post, path, Ghost(), body), HttpStatusCode.BadRequest, "validation_failed");

        Assert.Equal(new Dictionary<string, string[]> { ["body"] = ["İstek gövdesi zorunlu."] }, Errors(problem));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Route_and_query_errors_are_turkish_and_names_are_exact()
    {
        var malformed = AssertProblem(await SendAsync(HttpMethod.Get, "/api/accounts/abc", AdaToken()), HttpStatusCode.BadRequest, "validation_failed");
        Assert.Equal(["'abc' geçerli bir değer değil."], Errors(malformed)["id"]);

        // Period names are exact like enum names in JSON.
        var lowercase = AssertProblem(await SendAsync(HttpMethod.Get, $"/api/accounts/{AdaTryAccount}/transactions?period=lastmonth", AdaToken()),
            HttpStatusCode.BadRequest, "validation_failed");
        Assert.True(Errors(lowercase).ContainsKey("period"));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Login_field_errors_use_the_json_names()
    {
        var problem = AssertProblem(await SendAsync(HttpMethod.Post, "/api/auth/login", null, """{"username":"","password":""}"""),
            HttpStatusCode.BadRequest, "validation_failed");

        Assert.Equal(new Dictionary<string, string[]> { ["username"] = ["Zorunlu alan."], ["password"] = ["Zorunlu alan."] }, Errors(problem));
    }

    [Theory]
    [Trait("Category", "PostgreSQL")]
    [InlineData("GET", "/api/me")]
    [InlineData("GET", "/api/accounts")]
    [InlineData("GET", "/api/accounts/20000000-0000-0000-0000-000000000001")]
    [InlineData("GET", "/api/accounts/20000000-0000-0000-0000-000000000001/transactions")]
    [InlineData("GET", "/api/transactions/30000000-0000-0000-0000-000000000602")]
    [InlineData("POST", "/api/accounts")]
    [InlineData("POST", "/api/accounts/20000000-0000-0000-0000-000000000099/transactions")]
    public async Task A_customer_that_no_longer_exists_is_told_to_sign_in_again_everywhere(string method, string path)
    {
        var body = method == "GET" ? null
            : path.EndsWith("/transactions") ? """{"kind":"Salary","amount":10,"description":"Maaş"}"""
            : """{"accountName":"Tatil","currencyCode":"TRY"}""";

        var problem = AssertProblem(await SendAsync(new HttpMethod(method), path, Ghost(), body), HttpStatusCode.Unauthorized, "session_invalid");

        Assert.Equal("Oturum geçersiz; yeniden giriş yapın.", problem["title"]!.GetValue<string>());
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task An_unexpected_error_is_a_500_without_any_detail()
    {
        await using var api = TestApi.Create().WithWebHostBuilder(host => host.ConfigureTestServices(services =>
            services.AddScoped<IAccountReadService, BrokenAccountReadService>()));

        var response = await SendAsync(HttpMethod.Get, "/api/accounts", AdaToken(), host: api);

        var problem = AssertProblem(response, HttpStatusCode.InternalServerError, "internal_error");
        Assert.Equal("Beklenmeyen bir hata oluştu.", problem["title"]!.GetValue<string>());
        Assert.DoesNotContain("Password", response.Body);
        Assert.DoesNotContain("secret", response.Body);
    }

    // Fails the way an infrastructure bug would, with text that must never reach the client.
    private sealed class BrokenAccountReadService : IAccountReadService
    {
        public Task<IReadOnlyList<AccountDto>> ListAsync(Guid customerId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("secret: Host=db;Password=hunter2");

        public Task<AccountDto> GetAsync(Guid customerId, Guid accountId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("secret: Host=db;Password=hunter2");
    }
}
