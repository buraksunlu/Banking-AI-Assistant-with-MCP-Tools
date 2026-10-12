using System.Net;
using System.Net.Http.Json;
using BankingAI.Api.Controllers;
using BankingAI.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BankingAI.Api.Tests;

// Real HTTP pipeline in memory: routing, controllers, DI and the injected DbContext against PostgreSQL.
public sealed class HealthEndpointTests
{
    private const string ConnectionStringKey = "BANKINGAI_CONNECTION_STRING";

    private static WebApplicationFactory<Program> Api(string? connectionString = null)
    {
        Assert.False(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionStringKey)),
            "API testleri için önce .env ortam değişkenlerini yükleyin.");
        var factory = new WebApplicationFactory<Program>();
        return connectionString is null
            ? factory
            : factory.WithWebHostBuilder(host => host.UseSetting(ConnectionStringKey, connectionString));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Health_reports_a_reachable_database()
    {
        await using var api = Api();
        using var client = api.CreateClient();

        var response = await client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new HealthResponse("ok", true), await response.Content.ReadFromJsonAsync<HealthResponse>());
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Health_reports_503_when_the_database_is_unreachable()
    {
        // Port 1 has no PostgreSQL; the API itself must still answer, with 503.
        await using var api = Api("Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none;Timeout=2");
        using var client = api.CreateClient();

        var response = await client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(new HealthResponse("unavailable", false), await response.Content.ReadFromJsonAsync<HealthResponse>());
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Unknown_route_is_404()
    {
        await using var api = Api();
        using var client = api.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/does-not-exist")).StatusCode);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Missing_connection_string_stops_the_api_at_startup()
    {
        await using var api = Api("   ");

        var error = Assert.Throws<InvalidOperationException>(() => api.CreateClient());
        Assert.Contains(ConnectionStringKey, error.Message);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Starting_the_api_leaves_the_migration_history_unchanged()
    {
        // Limit: the demo database has no pending migration, so a Migrate() call added to the API would
        // still pass this test. The real guard is that Program.cs never calls Migrate (code review);
        // this test only proves startup does not alter the applied history.
        await using var db = new BankingDbContext();
        var before = (await db.Database.GetAppliedMigrationsAsync()).ToList();

        await using (var api = Api())
        {
            using var client = api.CreateClient();
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/health")).StatusCode);
        }

        // Schema changes only run through "BankingAI.Cli migrate"; the API startup must not touch them.
        Assert.Equal(before, await db.Database.GetAppliedMigrationsAsync());
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }
}
