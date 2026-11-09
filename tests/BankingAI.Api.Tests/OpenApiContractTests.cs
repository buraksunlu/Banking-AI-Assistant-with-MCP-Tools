using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace BankingAI.Api.Tests;

// API-15. The OpenAPI document is the machine-readable contract for the iOS client and the MCP tools, so it must say
// what the API really does: enums by name, money as JSON numbers, the token requirement, every status code with its
// problem body. A copy is committed (docs/openapi/v1.json) so that any change to the contract shows up in a pull request;
// run the tests with BANKINGAI_UPDATE_OPENAPI=1 to rewrite it on purpose.
public sealed class OpenApiContractTests
{
    private const string UpdateKey = "BANKINGAI_UPDATE_OPENAPI";

    private static readonly JsonSerializerOptions Written = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static async Task<JsonObject> DocumentAsync()
    {
        await using var api = TestApi.Create();
        using var client = api.CreateClient();
        return JsonNode.Parse(await client.GetStringAsync("/openapi/v1.json"))!.AsObject();
    }

    private static IEnumerable<(string Method, string Path, JsonObject Operation)> Operations(JsonObject document) =>
        document["paths"]!.AsObject().SelectMany(path => path.Value!.AsObject()
            .Where(op => op.Key is "get" or "post" or "put" or "delete" or "patch")
            .Select(op => (op.Key.ToUpperInvariant(), path.Key, op.Value!.AsObject())));

    private static IEnumerable<JsonNode> Descendants(JsonNode node) =>
        node switch
        {
            JsonObject o => o.Select(p => p.Value).OfType<JsonNode>().SelectMany(v => Descendants(v).Prepend(v)),
            JsonArray a => a.OfType<JsonNode>().SelectMany(v => Descendants(v).Prepend(v)),
            _ => []
        };

    private static string RepositoryFile(string relative)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "BankingAI.slnx")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        return Path.Combine(directory.FullName, relative);
    }

    [Theory]
    [Trait("Category", "PostgreSQL")]
    [InlineData("TransactionKind", new[] { "Purchase", "Sale", "Transfer", "Salary", "OpeningFunding" })]
    [InlineData("TransactionDirection", new[] { "Debit", "Credit" })]
    [InlineData("AccountType", new[] { "Personal", "Business" })]
    [InlineData("PeriodKind", new[] { "ThisMonth", "LastMonth", "Last3Months", "Last6Months", "Custom",
        "Today", "Yesterday", "ThisWeek", "LastWeek", "Last7Days", "Last30Days", "ThisYear", "AllTime" })]
    [InlineData("MerchantMatchStatus", new[] { "Matched", "Ambiguous", "NotFound" })]
    public async Task Enums_are_documented_by_their_exact_names(string schema, string[] names)
    {
        var enumSchema = (await DocumentAsync())["components"]!["schemas"]![schema]!;

        // The API reads and writes only these names (decision API-13 4-A); a number is a 400.
        Assert.Equal("string", enumSchema["type"]!.GetValue<string>());
        Assert.Equal(names, enumSchema["enum"]!.AsArray().Select(n => n!.GetValue<string>()));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Money_is_a_json_number_never_a_string_or_a_double()
    {
        var document = await DocumentAsync();
        var schemas = document["components"]!["schemas"]!.AsObject();

        var money = schemas.SelectMany(s => (s.Value!["properties"]?.AsObject() ?? []).Select(p => (Name: $"{s.Key}.{p.Key}", Schema: p.Value!)))
            .Where(p => p.Schema["format"]?.GetValue<string>() is "decimal" or "double")
            .ToList();

        // AccountDto.balance, CreateTransactionRequest.amount, BudgetFigures.* … 37 decimal fields today.
        Assert.True(money.Count >= 30, $"Para alanı bulunamadı: {money.Count}");
        Assert.All(money, p =>
        {
            Assert.Equal("decimal", p.Schema["format"]!.GetValue<string>());
            var types = p.Schema["type"] is JsonArray a ? a.Select(t => t!.GetValue<string>()).ToArray() : [p.Schema["type"]!.GetValue<string>()];
            Assert.DoesNotContain("string", types);
            Assert.Contains("number", types);
        });
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Every_operation_has_a_summary()
    {
        var missing = Operations(await DocumentAsync())
            .Where(o => string.IsNullOrWhiteSpace(o.Operation["summary"]?.GetValue<string>()))
            .Select(o => $"{o.Method} {o.Path}");

        Assert.Empty(missing);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Protected_operations_require_the_bearer_token_and_open_ones_do_not()
    {
        var document = await DocumentAsync();

        var scheme = document["components"]!["securitySchemes"]!["Bearer"]!;
        Assert.Equal(("http", "bearer", "JWT"),
            (scheme["type"]!.GetValue<string>(), scheme["scheme"]!.GetValue<string>(), scheme["bearerFormat"]!.GetValue<string>()));
        var open = new[] { "GET /api/health", "POST /api/auth/login" };
        foreach (var (method, path, operation) in Operations(document))
        {
            var bearer = operation["security"]?.AsArray().Any(r => r!.AsObject().ContainsKey("Bearer")) ?? false;
            Assert.True(bearer != open.Contains($"{method} {path}"), $"{method} {path}: security {bearer}");
        }
        // 25 protected operations (INC-01 added income-share) + health and login.
        Assert.Equal(27, Operations(document).Count());
    }

    [Theory]
    [Trait("Category", "PostgreSQL")]
    [InlineData("GET", "/api/health", new[] { "200", "500", "503" })]
    [InlineData("POST", "/api/auth/login", new[] { "200", "400", "401", "429", "500" })]
    [InlineData("GET", "/api/me", new[] { "200", "401", "500" })]
    [InlineData("GET", "/api/accounts", new[] { "200", "401", "500" })]
    [InlineData("GET", "/api/accounts/{id}", new[] { "200", "400", "401", "404", "500" })]
    [InlineData("POST", "/api/accounts", new[] { "201", "400", "401", "409", "500" })]
    [InlineData("POST", "/api/accounts/{accountId}/transactions", new[] { "201", "400", "401", "404", "409", "500" })]
    [InlineData("GET", "/api/analytics/spending-summary", new[] { "200", "400", "401", "404", "500" })]
    [InlineData("POST", "/api/assistant/messages", new[] { "200", "400", "401", "429", "500", "502", "503", "504" })]
    [InlineData("GET", "/api/assistant/status", new[] { "200", "401", "500" })]
    [InlineData("GET", "/api/assistant/suggestions", new[] { "200", "401", "500", "502", "503", "504" })]
    public async Task Operations_document_their_real_status_codes(string method, string path, string[] codes)
    {
        var operation = Operations(await DocumentAsync()).Single(o => o.Method == method && o.Path == path).Operation;

        Assert.Equal(codes, operation["responses"]!.AsObject().Select(r => r.Key).Order(StringComparer.Ordinal));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Every_error_answer_is_the_problem_body()
    {
        var document = await DocumentAsync();
        Assert.Equal(["type", "title", "status", "code", "traceId", "errors"],
            document["components"]!["schemas"]!["Problem"]!["properties"]!.AsObject().Select(p => p.Key));

        foreach (var (method, path, operation) in Operations(document))
        foreach (var (code, response) in operation["responses"]!.AsObject().Where(r => r.Key[0] is '4' or '5'))
        {
            var content = response!["content"]!.AsObject();
            Assert.True(content.Select(c => c.Key).SequenceEqual(["application/problem+json"]), $"{method} {path} {code}");
            Assert.Equal("#/components/schemas/Problem", content["application/problem+json"]!["schema"]!["$ref"]!.GetValue<string>());
        }
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Only_json_media_types_are_documented()
    {
        var mediaTypes = Descendants(await DocumentAsync())
            .OfType<JsonObject>()
            .Where(o => o.ContainsKey("content") && o["content"] is JsonObject)
            .SelectMany(o => o["content"]!.AsObject().Select(c => c.Key))
            .Distinct()
            .Order(StringComparer.Ordinal);

        Assert.Equal(["application/json", "application/problem+json"], mediaTypes);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task The_document_is_the_same_as_the_committed_contract()
    {
        var actual = (await DocumentAsync()).ToJsonString(Written);
        var file = RepositoryFile(Path.Combine("docs", "openapi", "v1.json"));

        if (Environment.GetEnvironmentVariable(UpdateKey) == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            await File.WriteAllTextAsync(file, actual + "\n");
        }

        Assert.True(File.Exists(file), $"Sözleşme dosyası yok; {UpdateKey}=1 ile üretin.");
        var committed = JsonNode.Parse(await File.ReadAllTextAsync(file))!.ToJsonString(Written);
        // A difference is a change to the contract: if it is intended, rewrite the file with BANKINGAI_UPDATE_OPENAPI=1
        // and let the pull request show it.
        Assert.Equal(committed, actual);
    }
}
