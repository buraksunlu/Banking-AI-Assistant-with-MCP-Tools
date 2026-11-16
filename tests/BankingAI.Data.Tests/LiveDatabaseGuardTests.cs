using BankingAI.TestSupport;
using Xunit;

namespace BankingAI.Data.Tests;

// RT-01 D4-A: the tests write and delete temporary rows, so a run pointed at the live database stops before its first query.
public class LiveDatabaseGuardTests
{
    [Theory]
    [InlineData("Host=127.0.0.1;Port=55432;Database=bankingai_live;Username=u;Password=p")]
    [InlineData("Host=127.0.0.1;Database=BANKINGAI_LIVE;Username=u")]
    [InlineData("host=127.0.0.1;database=bankingai_live_copy")]
    public void A_live_database_is_refused(string connectionString)
    {
        var error = Assert.Throws<InvalidOperationException>(() => LiveDatabaseGuard.EnsureNotLive(connectionString));

        // The message names the database, never the connection string: it holds the password.
        Assert.Contains("live", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Password", error.Message);
    }

    [Theory]
    [InlineData("Host=127.0.0.1;Port=55432;Database=bankingai_foundation;Username=u;Password=p")]
    [InlineData(null)]
    [InlineData("")]
    public void The_fixed_database_or_none_is_let_through(string? connectionString)
    {
        LiveDatabaseGuard.EnsureNotLive(connectionString);
    }
}
