using Npgsql;

namespace BankingAI.TestSupport;

/// <summary>
/// Coordinates test projects that run at the same time against the same database. Tests that commit
/// temporary rows (API tests) hold the lock <b>shared</b>, so they still run in parallel with each other;
/// a test that compares whole-table row counts holds it <b>exclusive</b>, so no temporary row appears or
/// disappears between its counts. Compiled into all three test projects from this one file. Hold at most one
/// lock per test: a second shared request queues behind a waiting exclusive one, which waits for the first.
/// </summary>
internal sealed class CommittedRowsLock : IAsyncDisposable
{
    // Date-style like the seed lock keys (2026100902–06), and distinct from all of them.
    private const long Key = 2026101201;

    private readonly NpgsqlConnection connection;
    private readonly string unlockFunction;

    private CommittedRowsLock(NpgsqlConnection connection, string unlockFunction)
    {
        this.connection = connection;
        this.unlockFunction = unlockFunction;
    }

    /// <summary>For tests that commit rows and delete them again.</summary>
    public static Task<CommittedRowsLock> SharedAsync() =>
        AcquireAsync("pg_advisory_lock_shared", "pg_advisory_unlock_shared");

    /// <summary>For tests that expect table row counts not to change while they run.</summary>
    public static Task<CommittedRowsLock> ExclusiveAsync() =>
        AcquireAsync("pg_advisory_lock", "pg_advisory_unlock");

    private static async Task<CommittedRowsLock> AcquireAsync(string lockFunction, string unlockFunction)
    {
        // A session-level lock lives as long as its connection, so it gets a connection of its own that is
        // independent of the DbContexts the test opens and closes.
        var connection = new NpgsqlConnection(Environment.GetEnvironmentVariable("BANKINGAI_CONNECTION_STRING"));
        await connection.OpenAsync();
        await ExecuteAsync(connection, lockFunction);
        return new CommittedRowsLock(connection, unlockFunction);
    }

    public async ValueTask DisposeAsync()
    {
        // Unlock explicitly: closing only returns the connection to the pool, where the lock would stay held.
        await ExecuteAsync(connection, unlockFunction);
        await connection.DisposeAsync();
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string function)
    {
        await using var command = new NpgsqlCommand($"SELECT {function}(@key)", connection);
        command.Parameters.AddWithValue("key", Key);
        await command.ExecuteNonQueryAsync();
    }
}
