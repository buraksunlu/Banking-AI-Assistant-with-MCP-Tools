using BankingAI.Data.Simulation;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BankingAI.Data.Tests.Simulation;

// RT-02a S7-A: the simulator writes to the live database only; the fixed demo database (tests, measurements) is refused before any
// query, so a mistyped connection never adds a row to it.
public class LiveSimulatorTests
{
    private static BankingDbContext Context(string database) =>
        new(new DbContextOptionsBuilder<BankingDbContext>()
            .UseNpgsql($"Host=127.0.0.1;Port=1;Database={database};Username=nobody;Timeout=1").Options);

    [Theory]
    [InlineData("bankingai_foundation")]
    [InlineData("postgres")]
    public async Task A_database_that_is_not_live_is_refused_before_any_query(string database)
    {
        await using var db = Context(database);

        // Port 1 has no server: reaching the database would fail differently, so this error proves nothing was tried.
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => LiveSimulator.CatchUpAsync(db, new DateTimeOffset(2026, 11, 16, 9, 0, 0, TimeSpan.Zero), dryRun: true));

        Assert.Contains(database, error.Message);
    }

    [Fact]
    public void The_live_database_is_let_through()
    {
        using var db = Context("bankingai_live");

        LiveSimulator.EnsureLive(db);
    }

    // DEMO-01: the catch-up writes Murat's history before the fixed year too, and still nothing after the moment asked for.
    [Fact]
    public void The_catch_up_plan_starts_with_murats_history()
    {
        var until = new DateTimeOffset(2026, 11, 19, 21, 0, 0, TimeSpan.Zero);

        var plan = LiveSimulator.PlanUntil(until);

        Assert.Equal(new DateTimeOffset(2024, 5, 15, 6, 0, 0, TimeSpan.Zero), plan[0].At);
        Assert.True(plan[^1].At <= until);
        Assert.Contains(plan, p => p.At > Seeding.SyntheticTransactionSeeder.RealisticCutoff);
        Assert.Equal(plan.Count, plan.Select(p => p.Id).Distinct().Count());
    }
}
