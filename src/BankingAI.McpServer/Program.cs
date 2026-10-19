using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using BankingAI.Hosting;
using BankingAI.Hosting.Json;
using BankingAI.McpServer.Tools;

var builder = WebApplication.CreateBuilder(args);

// The database, business time (BANKINGAI_DEMO_NOW), token validation, the token-by-default policy and the read-only
// analytics services, exactly as the API sets them up (K8-A). No write service is registered here.
builder.Services.AddBankingCore();
builder.Services.AddHttpContextAccessor();

// Stateless HTTP: every call carries its own token and nothing is kept between calls. Tool results use the API's JSON
// rules, so they have the same shape as the API's answers (K9-A).
// The SDK freezes the options and needs an explicit resolver; ASP.NET adds one to the API's options by itself.
var toolJson = BankingJson.ConfigureForTools(new JsonSerializerOptions(JsonSerializerDefaults.Web) { TypeInfoResolver = new DefaultJsonTypeInfoResolver() });
builder.Services.AddMcpServer(options => options.ServerInfo = new() { Name = "BankingAI", Version = "1.0" })
    .WithHttpTransport(options => options.Stateless = true)
    .WithTools<SpendingTools>(toolJson)
    // Model arguments are checked against the published schema before any tool runs (untrusted input), and every tool
    // error leaves as the bare {code, message} JSON (K10-A).
    .WithRequestFilters(filters => filters.AddCallToolFilter(ToolCallFilter.Create))
    .AddAuthorizationFilters();

var app = builder.Build();

// Fail at startup with a clear message instead of on the first call.
app.EnsureBankingCoreSettings();

app.UseAuthentication();
app.UseAuthorization();
// The fallback policy makes this endpoint require a token; without one the answer is 401 and no tool runs.
app.MapMcp("/mcp");

app.Run();

/// <summary>
/// Exposes the generated entry point to the integration tests (WebApplicationFactory&lt;Program&gt;).
/// </summary>
public partial class Program;
