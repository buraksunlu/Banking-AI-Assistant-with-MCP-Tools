using System.Runtime.CompilerServices;
using Npgsql;

namespace BankingAI.TestSupport;

/// <summary>
/// Stops a test run that points at the live database (RT-01 D4-A). The tests commit and delete temporary customers and accounts and
/// count the seeded rows, so they belong to the fixed demo database only; the live one is the simulator's and the demo's.
/// </summary>
internal static class LiveDatabaseGuard
{
    /// <summary>The connection the tests read, the same setting as the app's.</summary>
    public const string ConnectionStringKey = "BANKINGAI_CONNECTION_STRING";

    /// <summary>Throws when the connection string names a live database ("bankingai_live"); none at all is let through.</summary>
    public static void EnsureNotLive(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return;
        var database = new NpgsqlConnectionStringBuilder(connectionString).Database ?? "";
        // Only the database's name goes into the message: the connection string carries the password.
        if (database.Contains("live", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Tests must not run against the live database '{database}'. Point {ConnectionStringKey} at the fixed demo database.");
    }

    // Runs when the test assembly loads, before any test or fixture opens a connection.
    [ModuleInitializer]
    internal static void CheckOnLoad() => EnsureNotLive(Environment.GetEnvironmentVariable(ConnectionStringKey));
}
