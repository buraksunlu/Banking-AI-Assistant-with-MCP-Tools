using System.Globalization;
using System.Runtime.CompilerServices;
using BankingAI.Data.Seeding;
using BankingAI.Data.Simulation;
using Xunit;

namespace BankingAI.Data.Tests.Simulation;

// DEMO-01: the live database already holds the day plan's rows from the fixed data's end to 19 November 2026. A changed row there would
// be the same identifier with other content, which the writers refuse, so every row of that window is kept in a golden file and must be
// planned again exactly as it was. New rows are free. BANKINGAI_UPDATE_DAYPLAN_GOLDEN=1 writes the file (and fails, so it gets committed).
public sealed class DayPlanGoldenTests
{
    private static readonly DateTimeOffset Until = new(2026, 11, 19, 21, 0, 0, TimeSpan.Zero);

    private static string GoldenPath([CallerFilePath] string source = "") =>
        Path.Combine(Path.GetDirectoryName(source)!, "day-plan-golden.txt");

    private static IEnumerable<string> CurrentLines() =>
        DayPlan.Between(SyntheticTransactionSeeder.RealisticCutoff, Until)
            .SelectMany(p => p.ToTransactions())
            .Select(t => string.Join('|', t.Id, t.AccountId, t.Amount.ToString(CultureInfo.InvariantCulture), t.CurrencyCode, t.Kind,
                t.RawDescription, t.OccurredAt.ToString("O", CultureInfo.InvariantCulture)));

    [Fact]
    public void Every_row_already_planned_for_the_live_database_is_still_planned_the_same_way()
    {
        var path = GoldenPath();
        if (Environment.GetEnvironmentVariable("BANKINGAI_UPDATE_DAYPLAN_GOLDEN") == "1" || !File.Exists(path))
        {
            File.WriteAllLines(path, CurrentLines());
            Assert.Fail($"Generated {path}; commit it.");
        }

        var current = CurrentLines().ToHashSet(StringComparer.Ordinal);
        var lost = File.ReadAllLines(path).Where(line => !current.Contains(line)).ToList();

        Assert.True(lost.Count == 0, $"{lost.Count} rows changed or vanished, first: {string.Join(Environment.NewLine, lost.Take(5))}");
    }
}
