using System.Globalization;
using System.Threading.RateLimiting;
using BankingAI.Api.Assistant;
using BankingAI.Api.Controllers;
using BankingAI.Api.Errors;
using BankingAI.Api.OpenApi;
using BankingAI.Api.Security;
using BankingAI.Application.Accounts;
using BankingAI.Application.Analytics;
using BankingAI.Application.Transactions;
using BankingAI.Data;
using BankingAI.Hosting;
using BankingAI.Hosting.Json;
using BankingAI.Hosting.Security;
using BankingAI.Hosting.Time;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Enums travel as their exact names ("Purchase"): readable for people and for the LLM, which would otherwise have
// to guess what 2 means. Numbers and other casings are refused as input, so nothing is silently converted.
// Action names keep their "Async" suffix, so CreatedAtAction(nameof(GetAsync), ...) finds the action it names.
builder.Services.AddControllers(options =>
    {
        options.SuppressAsyncSuffixInActionNames = false;
        ApiValidation.UseTurkishMessages(options.ModelBindingMessageProvider);
    })
    .AddJsonOptions(options => BankingJson.Configure(options.JsonSerializerOptions))
    .ConfigureApiBehaviorOptions(options => options.InvalidModelStateResponseFactory = ApiValidation.InvalidModelState);
// A lambda, not the method group: the XML-comment source generator only intercepts this form, and without it every
// summary silently disappears from the document.
builder.Services.AddOpenApi(options => ApiDocument.Configure(options));

// Every error is a ProblemDetails body with a Turkish title, a machine code and a traceId (API-13). Exceptions
// from services are mapped in one place, so controllers carry no try/catch.
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = ApiProblems.Customize);
builder.Services.AddExceptionHandler<ApiExceptionHandler>();

// Database, business time, token validation, the token-by-default policy and the read-only analytics services are
// shared with the MCP server (BankingAI.Hosting, MCP-a K8-A). What follows is only what the API itself exposes.
builder.Services.AddBankingCore();
builder.Services.AddSingleton<TokenIssuer>();
builder.Services.AddScoped<IAccountReadService, AccountReadService>();
builder.Services.AddScoped<IAccountWriteService, AccountWriteService>();
builder.Services.AddScoped<ITransactionWriteService>(services => new TransactionWriteService(
    services.GetRequiredService<BankingDbContext>(), services.GetRequiredService<PeriodResolver>(),
    services.GetRequiredService<BusinessClock>().Time, services.GetRequiredService<TransactionSearchService>()));

// The AI service (AI-07). The client sets its own deadlines per call, so HttpClient's single timeout is switched off.
builder.Services.AddSingleton(services => AssistantSettings.FromConfiguration(services.GetRequiredService<IConfiguration>()));
builder.Services.AddHttpClient<IAssistantClient, AssistantClient>((services, http) =>
{
    http.BaseAddress = services.GetRequiredService<AssistantSettings>().BaseAddress;
    http.Timeout = Timeout.InfiniteTimeSpan;
});

// Only the direct connection's IP is used; X-Forwarded-For is client-controlled and not trusted (OPS-04).
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(AuthController.LoginRateLimitPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    // Every chat message keeps the single local model busy for seconds, so each customer gets their own allowance
    // (SEC-06, decision AI-14b K6-A); the key comes from the token, which is why the limiter runs after authentication.
    options.AddPolicy(AssistantController.RateLimitPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.GetCustomerId(),
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.OnRejected = (context, _) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            context.HttpContext.Response.Headers.RetryAfter =
                ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        return ValueTask.CompletedTask;
    };
});

var app = builder.Build();

// Fail at startup with a clear message instead of on the first request.
app.EnsureBankingCoreSettings();
var assistantSettings = app.Services.GetRequiredService<AssistantSettings>();
// The address may be a default (decision AI-14b K7-C), so the log says which one is in use.
app.Logger.LogInformation("AI service: {BaseAddress} (chat timeout {TimeoutSeconds} s)",
    assistantSettings.BaseAddress, assistantSettings.Timeout.TotalSeconds);

// The contract and a page to browse and try it (/scalar), for development only (API-15, decision K4-A).
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
    app.MapScalarApiReference().AllowAnonymous();
}

// First in the pipeline, so every later failure reaches it. The same generic 500 in every environment: the
// details of an unexpected error stay in the server log (decision 5-A), never in the response.
app.UseExceptionHandler();
// Gives a problem body to answers that have none (401 challenge, unknown route 404, 405, 429).
app.UseStatusCodePages();

// No HTTPS redirection yet: local development runs on HTTP only; TLS is decided with Docker (OPS-04).
// The rate limiter comes after authorization: the chat limit is per customer, so it needs the validated token, and a
// request without a token is refused with 401 before it uses up anyone's allowance.
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapControllers();

app.Run();

/// <summary>
/// Exposes the generated entry point to the integration tests (WebApplicationFactory&lt;Program&gt;).
/// </summary>
public partial class Program;
