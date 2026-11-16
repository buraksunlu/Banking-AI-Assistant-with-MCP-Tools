using System.Globalization;
using BankingAI.Data;
using BankingAI.Data.Simulation;

/// <summary>
/// <c>simulate catch-up [--until &lt;ISO-8601&gt;] [--dry-run]</c> (RT-02a): writes the live bank's day plans from the end of the fixed
/// data up to now (or the moment given), into the live database only. <c>--dry-run</c> counts what is missing and writes nothing.
/// Run it with the live connection (<c>scripts/live-cli.sh simulate catch-up</c>).
/// </summary>
internal static class SimulateCommands
{
    public const string Usage = "simulate catch-up [--until <ISO-8601>] [--dry-run]";

    /// <summary>True if the arguments name a simulate command, valid or not.</summary>
    public static bool Matches(string[] args) => args.Length > 0 && args[0] == "simulate";

    /// <summary>Runs the command; returns the process exit code.</summary>
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length < 2 || args[1] != "catch-up")
            return PrintUsage();
        var until = DateTimeOffset.UtcNow;
        var dryRun = false;
        for (var i = 2; i < args.Length; i++)
        {
            if (args[i] == "--dry-run")
                dryRun = true;
            else if (args[i] == "--until" && i + 1 < args.Length
                     && DateTimeOffset.TryParse(args[++i], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var moment))
                // A moment still to come would be written as if it had happened: never later than now.
                until = moment < DateTimeOffset.UtcNow ? moment.ToUniversalTime() : DateTimeOffset.UtcNow;
            else
                return PrintUsage();
        }

        await using var db = new BankingDbContext();
        var result = await LiveSimulator.CatchUpAsync(db, until, dryRun, Console.WriteLine);
        Console.WriteLine(dryRun
            ? $"Dry run up to {until:u}: planned {result.Planned}, missing {result.Written}, already there {result.AlreadyThere}. Nothing written."
            : $"Caught up to {until:u}: planned {result.Planned}, written {result.Written}, already there {result.AlreadyThere}, skipped {result.Skipped}.");
        return 0;
    }

    private static int PrintUsage()
    {
        Console.WriteLine($"Usage: BankingAI.Cli {Usage}");
        return 1;
    }
}
