using BankingAI.Application.Analytics;
using BankingAI.Application.Customers;
using BankingAI.Data;
using BankingAI.Hosting.Security;
using BankingAI.Hosting.Time;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BankingAI.Hosting;

/// <summary>
/// The setup the API and the MCP server share (MCP-a, decision K8-A): the database, business time, token validation and
/// the read-only analytics services. Write services are deliberately not here; each program adds only what it exposes,
/// so the MCP server cannot even resolve a service that writes.
/// </summary>
public static class BankingServiceCollectionExtensions
{
    /// <summary>Configuration key of the PostgreSQL connection string.</summary>
    public const string ConnectionStringKey = "BANKINGAI_CONNECTION_STRING";

    /// <summary>
    /// Registers the shared services. Settings are read lazily from configuration, so test hosts can override them;
    /// <see cref="EnsureBankingCoreSettings"/> checks them at startup.
    /// </summary>
    public static IServiceCollection AddBankingCore(this IServiceCollection services)
    {
        // One DbContext per request (scoped); never shared between requests. Migrations are never applied here:
        // schema changes only run through the CLI.
        services.AddDbContext<BankingDbContext>((provider, options) =>
            options.UseNpgsql(provider.GetRequiredService<IConfiguration>()[ConnectionStringKey]));

        // Security keeps the real clock, so tokens never look expired; business time (analytics periods, new
        // transactions) may be fixed to the demo moment with BANKINGAI_DEMO_NOW (decision ANA-09 2-A).
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(provider => JwtSettings.FromConfiguration(provider.GetRequiredService<IConfiguration>()));
        services.AddSingleton(provider => BusinessClock.FromConfiguration(
            provider.GetRequiredService<IConfiguration>(), provider.GetRequiredService<TimeProvider>()));
        services.AddSingleton(provider => new PeriodResolver(provider.GetRequiredService<BusinessClock>().Time));

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<JwtSettings>((options, jwt) =>
            {
                // Keep "sub" as "sub" instead of the long legacy claim type name.
                options.MapInboundClaims = false;
                options.TokenValidationParameters = jwt.ValidationParameters();
            });
        // Every endpoint requires a token unless it opts out with [AllowAnonymous]; a forgotten attribute fails closed.
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        // Anything that uses the DbContext is scoped like the context itself; a singleton would keep the first
        // request's context forever and share it between concurrent requests.
        services.AddScoped<ICustomerCheck, CustomerCheck>();
        services.AddScoped<TransactionSearchService>();
        services.AddScoped<SpendingSummaryService>();
        services.AddScoped<CategoryBreakdownService>();
        services.AddScoped<MerchantSpendingService>();
        services.AddScoped<SpendingComparisonService>();
        services.AddScoped<HighestTransactionsService>();
        services.AddScoped<RecurringPaymentService>();
        services.AddScoped<UpcomingPaymentService>();
        services.AddScoped<BudgetCheckService>();
        services.AddScoped<SavingsSimulationService>();
        return services;
    }

    /// <summary>
    /// Fails at startup with a clear message instead of on the first request: the connection string, the signing key
    /// and the demo moment are read and checked here.
    /// </summary>
    /// <exception cref="InvalidOperationException">A required setting is missing or invalid.</exception>
    public static void EnsureBankingCoreSettings(this WebApplication app)
    {
        if (string.IsNullOrWhiteSpace(app.Configuration[ConnectionStringKey]))
            throw new InvalidOperationException($"Set {ConnectionStringKey} before starting.");
        app.Services.GetRequiredService<JwtSettings>();
        app.Services.GetRequiredService<BusinessClock>();
    }
}
