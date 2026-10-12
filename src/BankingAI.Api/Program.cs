using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using BankingAI.Api.Controllers;
using BankingAI.Api.Security;
using BankingAI.Application.Accounts;
using BankingAI.Application.Analytics;
using BankingAI.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

const string ConnectionStringKey = "BANKINGAI_CONNECTION_STRING";

var builder = WebApplication.CreateBuilder(args);

// Enums travel as names ("Business", later "Purchase"): readable for people and for the LLM, which would
// otherwise have to guess what 2 means. Numbers are refused as input, so nothing is silently converted.
builder.Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false)));
builder.Services.AddOpenApi();

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
builder.Services.AddSingleton<PeriodResolver>();
builder.Services.AddScoped<IAccountReadService, AccountReadService>();
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

if (app.Environment.IsDevelopment())
    app.MapOpenApi().AllowAnonymous();

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
