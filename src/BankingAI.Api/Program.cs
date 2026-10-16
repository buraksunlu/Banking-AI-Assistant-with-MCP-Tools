using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using BankingAI.Api.Controllers;
using BankingAI.Api.Errors;
using BankingAI.Api.OpenApi;
using BankingAI.Api.Security;
using BankingAI.Api.Time;
using BankingAI.Application.Accounts;
using BankingAI.Application.Analytics;
using BankingAI.Application.Transactions;
using BankingAI.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
const string ConnectionStringKey = "BANKINGAI_CONNECTION_STRING";

var builder = WebApplication.CreateBuilder(args);

// Enums travel as their exact names ("Purchase"): readable for people and for the LLM, which would otherwise have
// to guess what 2 means. Numbers and other casings are refused as input, so nothing is silently converted.
// Action names keep their "Async" suffix, so CreatedAtAction(nameof(GetAsync), ...) finds the action it names.
builder.Services.AddControllers(options =>
    {
        options.SuppressAsyncSuffixInActionNames = false;
        ApiValidation.UseTurkishMessages(options.ModelBindingMessageProvider);
    })
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new StrictEnumConverter());
        // Money is a JSON number, as the contract says (API-15, decision K1-A); the web defaults would also read "100".
        options.JsonSerializerOptions.NumberHandling = JsonNumberHandling.Strict;
    })
    .ConfigureApiBehaviorOptions(options => options.InvalidModelStateResponseFactory = ApiValidation.InvalidModelState);
// A lambda, not the method group: the XML-comment source generator only intercepts this form, and without it every
// summary silently disappears from the document.
builder.Services.AddOpenApi(options => ApiDocument.Configure(options));

// Every error is a ProblemDetails body with a Turkish title, a machine code and a traceId (API-13). Exceptions
// from services are mapped in one place, so controllers carry no try/catch.
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = ApiProblems.Customize);
builder.Services.AddExceptionHandler<ApiExceptionHandler>();

// One DbContext per HTTP request (scoped); never shared between requests. The connection string is
// read from configuration (the existing environment variable) when the context is created, so test
// hosts can override it. Migrations are never applied here: schema changes only run through the CLI.
builder.Services.AddDbContext<BankingDbContext>((services, options) =>
    options.UseNpgsql(services.GetRequiredService<IConfiguration>()[ConnectionStringKey]));

// Token settings are read lazily from configuration, like the connection string, so test hosts can override them.
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(services => JwtSettings.FromConfiguration(services.GetRequiredService<IConfiguration>()));
builder.Services.AddSingleton<TokenIssuer>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();

// Application services. Anything that uses the DbContext is scoped like the context itself; a singleton
// would keep the first request's context forever and share it between concurrent requests.
// Business time (analytics periods, the time of new transactions) may be fixed to the demo moment with
// BANKINGAI_DEMO_NOW; security keeps the real clock above, so tokens never look expired (decision ANA-09 2-A).
builder.Services.AddSingleton(services => BusinessClock.FromConfiguration(
    services.GetRequiredService<IConfiguration>(), services.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton(services => new PeriodResolver(services.GetRequiredService<BusinessClock>().Time));
builder.Services.AddScoped<IAccountReadService, AccountReadService>();
builder.Services.AddScoped<IAccountWriteService, AccountWriteService>();
builder.Services.AddScoped<TransactionSearchService>();
builder.Services.AddScoped<SpendingSummaryService>();
builder.Services.AddScoped<CategoryBreakdownService>();
builder.Services.AddScoped<MerchantSpendingService>();
builder.Services.AddScoped<SpendingComparisonService>();
builder.Services.AddScoped<HighestTransactionsService>();
builder.Services.AddScoped<RecurringPaymentService>();
builder.Services.AddScoped<UpcomingPaymentService>();
builder.Services.AddScoped<BudgetCheckService>();
builder.Services.AddScoped<SavingsSimulationService>();
builder.Services.AddScoped<ITransactionWriteService>(services => new TransactionWriteService(
    services.GetRequiredService<BankingDbContext>(), services.GetRequiredService<PeriodResolver>(),
    services.GetRequiredService<BusinessClock>().Time, services.GetRequiredService<TransactionSearchService>()));
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<JwtSettings>((options, jwt) =>
    {
        // Keep "sub" as "sub" instead of the long legacy claim type name.
        options.MapInboundClaims = false;
        options.TokenValidationParameters = jwt.ValidationParameters();
    });

// Every endpoint requires a token unless it opts out with [AllowAnonymous]; a forgotten attribute fails
// closed, and without a token even unknown routes answer 401 instead of revealing what exists.
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

// Only the direct connection's IP is used; X-Forwarded-For is client-controlled and not trusted (OPS-04).
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(AuthController.LoginRateLimitPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

var app = builder.Build();

// Fail at startup with a clear message instead of on the first request.
if (string.IsNullOrWhiteSpace(app.Configuration[ConnectionStringKey]))
    throw new InvalidOperationException($"Set {ConnectionStringKey} before starting the API.");
app.Services.GetRequiredService<JwtSettings>();
app.Services.GetRequiredService<BusinessClock>();

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
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

/// <summary>
/// Exposes the generated entry point to the integration tests (WebApplicationFactory&lt;Program&gt;).
/// </summary>
public partial class Program;
