using BankingAI.Data;
using BankingAI.Data.Entities;
using BankingAI.Data.Security;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace BankingAI.Data.Tests;

// SEC-01a. Every password here is a throwaway test value generated per run; the demo users' real
// passwords live only in the local .env and never appear in tests or in the repository.
public sealed class PasswordCredentialTests
{
    private static string TestPassword() => "test-" + Guid.NewGuid().ToString("N");

    [Fact]
    public void Hash_verifies_the_right_password_only()
    {
        var password = TestPassword();
        var hash = PasswordCredentials.Hash(password);

        Assert.DoesNotContain(password, hash); // the password itself is never stored
        Assert.True(PasswordCredentials.Verify(hash, password));
        Assert.False(PasswordCredentials.Verify(hash, password + "x"));
        Assert.False(PasswordCredentials.Verify(hash, password.ToUpperInvariant()));
    }

    [Fact]
    public void Same_password_gets_a_different_hash_each_time()
    {
        // A random salt per hash: equal passwords are not visible as equal hashes.
        var password = TestPassword();

        Assert.NotEqual(PasswordCredentials.Hash(password), PasswordCredentials.Hash(password));
    }

    [Theory]
    [InlineData("1234567")] // 7 characters: too short
    [InlineData("")]
    public void Short_passwords_are_rejected(string password) =>
        Assert.Throws<ArgumentException>(() => PasswordCredentials.Hash(password));

    [Fact]
    public void Very_long_passwords_are_rejected() =>
        Assert.Throws<ArgumentException>(() => PasswordCredentials.Hash(new string('a', 129)));

    [Theory]
    [InlineData("ada", true)]
    [InlineData("deniz.market", true)]
    [InlineData("ege_2026", true)]
    [InlineData("ab", false)]          // too short
    [InlineData("Ada", false)]         // must already be lowercase
    [InlineData("ada market", false)]  // no spaces
    [InlineData(".ada", false)]        // must start with a letter or digit
    [InlineData("ayşe", false)]        // ASCII only
    public void Username_rule(string username, bool valid) =>
        Assert.Equal(valid, PasswordCredentials.IsValidUsername(username));

    [Fact]
    public void Usernames_are_trimmed_and_lowercased() =>
        Assert.Equal("ada", PasswordCredentials.NormalizeUsername("  Ada "));
}

[Collection(PostgreSqlCollection.Name)]
public sealed class CredentialStoreTests
{
    private static readonly Guid Ada = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid Deniz = Guid.Parse("10000000-0000-0000-0000-000000000002");
    private static readonly Guid EmptyCustomer = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static string TestPassword() => "test-" + Guid.NewGuid().ToString("N");
    private static string TestUsername() => "test." + Guid.NewGuid().ToString("N")[..12];

    private static BankingDbContext Db()
    {
        Assert.False(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BANKINGAI_CONNECTION_STRING")),
            "PostgreSQL testi için önce .env ortam değişkenlerini yükleyin.");
        return new BankingDbContext();
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Creating_and_changing_a_password_keeps_only_a_verifiable_hash()
    {
        await using var db = Db();
        await using var scope = await db.Database.BeginTransactionAsync();
        var username = TestUsername();
        var first = TestPassword();
        var second = TestPassword();

        // The demo customer without accounts has no login yet, so it can host a temporary one.
        Assert.Equal(CredentialChange.Created, await CredentialStore.SetPasswordAsync(db, username.ToUpperInvariant(), first, EmptyCustomer));
        var created = await db.CustomerCredentials.AsNoTracking().SingleAsync(c => c.CustomerId == EmptyCustomer);
        Assert.Equal(username, created.Username); // stored normalized
        Assert.True(PasswordCredentials.Verify(created.PasswordHash, first));
        Assert.Equal(created.CreatedAt, created.UpdatedAt);

        db.ChangeTracker.Clear();
        Assert.Equal(CredentialChange.Updated, await CredentialStore.SetPasswordAsync(db, username, second));
        var updated = await db.CustomerCredentials.AsNoTracking().SingleAsync(c => c.CustomerId == EmptyCustomer);
        Assert.False(PasswordCredentials.Verify(updated.PasswordHash, first));
        Assert.True(PasswordCredentials.Verify(updated.PasswordHash, second));
        Assert.Equal(created.CreatedAt, updated.CreatedAt);
        Assert.True(updated.UpdatedAt >= created.UpdatedAt);

        await scope.RollbackAsync();
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Conflicts_are_errors_not_silent_overwrites()
    {
        await using var db = Db();
        await using var scope = await db.Database.BeginTransactionAsync();
        var username = TestUsername();
        await CredentialStore.SetPasswordAsync(db, username, TestPassword(), EmptyCustomer);
        db.ChangeTracker.Clear();

        // The username belongs to another customer.
        await Assert.ThrowsAsync<InvalidOperationException>(() => CredentialStore.SetPasswordAsync(db, username, TestPassword(), Ada));
        // The customer already has a different username.
        await Assert.ThrowsAsync<InvalidOperationException>(() => CredentialStore.SetPasswordAsync(db, TestUsername(), TestPassword(), EmptyCustomer));
        // A new username without a customer, and a customer that does not exist.
        await Assert.ThrowsAsync<InvalidOperationException>(() => CredentialStore.SetPasswordAsync(db, TestUsername(), TestPassword()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => CredentialStore.SetPasswordAsync(db, TestUsername(), TestPassword(), Guid.NewGuid()));
        // Invalid input never reaches the database.
        await Assert.ThrowsAsync<ArgumentException>(() => CredentialStore.SetPasswordAsync(db, "a b", TestPassword(), Deniz));
        await Assert.ThrowsAsync<ArgumentException>(() => CredentialStore.SetPasswordAsync(db, TestUsername(), "short", Deniz));

        await scope.RollbackAsync();
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Database_constraints_guard_credentials()
    {
        await using var db = Db();
        await using var scope = await db.Database.BeginTransactionAsync();
        var username = TestUsername();
        await CredentialStore.SetPasswordAsync(db, username, TestPassword(), EmptyCustomer);

        async Task Reject(string sql, string sqlState, string constraint)
        {
            await scope.CreateSavepointAsync("invalid");
            var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql));
            Assert.Equal((sqlState, constraint), (error.SqlState, error.ConstraintName));
            await scope.RollbackToSavepointAsync("invalid");
        }

        var insert = """INSERT INTO "CustomerCredentials" ("CustomerId", "Username", "PasswordHash", "CreatedAt", "UpdatedAt") VALUES ('{0}', '{1}', '{2}', now(), now())""";
        await Reject(string.Format(insert, Ada, username, "x"), "23505", "IX_CustomerCredentials_Username");                 // duplicate username
        await Reject(string.Format(insert, EmptyCustomer, "other.name", "x"), "23505", "PK_CustomerCredentials");             // second login for a customer
        await Reject(string.Format(insert, Guid.NewGuid(), "ghost.user", "x"), "23503", "FK_CustomerCredentials_Customers_CustomerId"); // no such customer
        await Reject(string.Format(insert, Ada, "Ada Upper", "x"), "23514", "CK_CustomerCredentials_Username");
        await Reject(string.Format(insert, Ada, "ada.blank", " "), "23514", "CK_CustomerCredentials_PasswordHash_NotBlank");

        await scope.RollbackAsync();
    }
}
