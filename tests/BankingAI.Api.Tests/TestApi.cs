using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using BankingAI.Api.Security;
using BankingAI.Data;
using BankingAI.Data.Entities;
using BankingAI.Data.Security;
using BankingAI.TestSupport;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace BankingAI.Api.Tests;

/// <summary>
/// In-memory API host for integration tests, optionally with overridden settings.
/// </summary>
internal static class TestApi
{
    public const string ConnectionStringKey = "BANKINGAI_CONNECTION_STRING";

    public static WebApplicationFactory<Program> Create(params (string Key, string Value)[] settings)
    {
        Assert.False(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionStringKey)),
            "API testleri için önce .env ortam değişkenlerini yükleyin.");
        Assert.False(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(JwtSettings.SigningKeyKey)),
            "API testleri için önce .env ortam değişkenlerini yükleyin.");
        var factory = new WebApplicationFactory<Program>();
        return settings.Length == 0
            ? factory
            : factory.WithWebHostBuilder(host =>
            {
                foreach (var (key, value) in settings)
                    host.UseSetting(key, value);
            });
    }

    /// <summary>
    /// The API's own JSON settings (enum names, unmapped-member rules), loaded from a host that never touches
    /// the database, so contract tests run without PostgreSQL or a .env file.
    /// </summary>
    public static JsonSerializerOptions JsonOptions { get; } = LoadJsonOptions();

    private static JsonSerializerOptions LoadJsonOptions()
    {
        using var api = new WebApplicationFactory<Program>().WithWebHostBuilder(host => host
            .UseSetting(ConnectionStringKey, "Host=unused")
            .UseSetting(JwtSettings.SigningKeyKey, new string('k', JwtSettings.MinimumKeyBytes)));
        return api.Services.GetRequiredService<IOptions<Microsoft.AspNetCore.Mvc.JsonOptions>>().Value.JsonSerializerOptions;
    }

    public static HttpClient WithToken(this HttpClient client, string token)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}

/// <summary>
/// Signs tokens independently of the API's own issuer, so tests can forge expired or wrongly signed ones.
/// </summary>
internal static class TestTokens
{
    public static string Create(Guid customerId, string? signingKey = null, DateTime? expiresUtc = null)
    {
        var key = signingKey ?? Environment.GetEnvironmentVariable(JwtSettings.SigningKeyKey)!;
        var expires = expiresUtc ?? DateTime.UtcNow.AddMinutes(10);
        var issued = expires.AddMinutes(-60);
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = JwtSettings.Issuer,
            Audience = JwtSettings.Audience,
            Subject = new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Sub, customerId.ToString())]),
            IssuedAt = issued,
            NotBefore = issued,
            Expires = expires,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256)
        });
    }
}

/// <summary>
/// A throwaway customer with a login, committed so the API's own connection can see it, and deleted on
/// dispose. The password is random per run; the demo users' passwords are never used in tests.
/// </summary>
internal sealed class TestUser : IAsyncDisposable
{
    private const string UsernamePrefix = "apitest.";
    private const string FirstName = "Test";
    private const string LastName = "Api";

    private readonly CommittedRowsLock rowsLock;

    private TestUser(CommittedRowsLock rowsLock, Guid customerId, string username, string password)
    {
        this.rowsLock = rowsLock;
        CustomerId = customerId;
        Username = username;
        Password = password;
    }

    public Guid CustomerId { get; }
    public string Username { get; }
    public string Password { get; }

    public static async Task<TestUser> CreateAsync()
    {
        // Held from before the insert until after the delete, so a row-counting test in BankingAI.Data.Tests
        // never sees this customer come or go.
        var rowsLock = await CommittedRowsLock.SharedAsync();
        await using var db = new BankingDbContext();
        var customer = new Customer { FirstName = FirstName, LastName = LastName };
        try
        {
            await RemoveLeftoversAsync(db);

            db.Customers.Add(customer);
            await db.SaveChangesAsync();
            var username = UsernamePrefix + Guid.NewGuid().ToString("N")[..12];
            var password = "test-" + Guid.NewGuid().ToString("N");
            await CredentialStore.SetPasswordAsync(db, username, password, customer.Id);
            return new TestUser(rowsLock, customer.Id, username, password);
        }
        catch
        {
            // Do not leave a half-created user behind (deleting a row that was never inserted is a no-op).
            await DeleteAsync(db, [customer.Id]);
            await rowsLock.DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await using var db = new BankingDbContext();
            await DeleteAsync(db, [CustomerId]);
        }
        finally
        {
            await rowsLock.DisposeAsync();
        }
    }

    // A test run killed before dispose, or a failed delete, leaves a test customer behind (with or without
    // its login), so leftovers are found by the customer row itself. Only old ones are removed, so a test
    // running in parallel never loses the user it just created.
    private static async Task RemoveLeftoversAsync(BankingDbContext db)
    {
        var staleBefore = DateTimeOffset.UtcNow.AddMinutes(-10);
        var stale = await db.Customers
            .Where(c => c.FirstName == FirstName && c.LastName == LastName && c.CreatedAt < staleBefore)
            .Select(c => c.Id)
            .ToListAsync();
        await DeleteAsync(db, stale);
    }

    private static async Task DeleteAsync(BankingDbContext db, List<Guid> customerIds)
    {
        if (customerIds.Count == 0)
            return;
        // Every reference is ON DELETE RESTRICT, so the order is transactions, accounts, login, customer. The
        // name check on the owning customer keeps this from ever touching a demo customer's rows. Tests may add
        // accounts directly (e.g. a Business account), so all of the test customer's accounts are removed.
        await db.Transactions
            .Where(t => db.Accounts.Any(a => a.Id == t.AccountId && customerIds.Contains(a.CustomerId)
                        && db.Customers.Any(c => c.Id == a.CustomerId && c.FirstName == FirstName && c.LastName == LastName)))
            .ExecuteDeleteAsync();
        await db.Accounts
            .Where(a => customerIds.Contains(a.CustomerId)
                        && db.Customers.Any(c => c.Id == a.CustomerId && c.FirstName == FirstName && c.LastName == LastName))
            .ExecuteDeleteAsync();
        await db.CustomerCredentials
            .Where(c => customerIds.Contains(c.CustomerId) && c.Username.StartsWith(UsernamePrefix))
            .ExecuteDeleteAsync();
        await db.Customers
            .Where(c => customerIds.Contains(c.Id) && c.FirstName == FirstName && c.LastName == LastName)
            .ExecuteDeleteAsync();
    }
}
