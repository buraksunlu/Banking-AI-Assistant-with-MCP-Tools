using BankingAI.Data.Seeding;
using BankingAI.Data.Writing;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BankingAI.Data.Simulation;

/// <summary>
/// Writes the day plans into the live database (RT-02a): every planned transaction after the fixed data's end
/// (<see cref="SyntheticTransactionSeeder.RealisticCutoff"/>) up to a moment, through the same writers as the seeds (S4-A), so
/// validation and the atomic balance rules hold. Running it again writes nothing new: the plan's identifiers are stable and the
/// writers answer a stored row with <see cref="WriteOutcome.AlreadyExisted"/>.
/// </summary>
public static class LiveSimulator
{
    /// <summary>What a catch-up did: rows of the plan, newly written, already there, and skipped for a balance that could not carry them.</summary>
    public sealed record CatchUpResult(int Planned, int Written, int AlreadyThere, int Skipped);

    /// <summary>Throws unless the context points at a live database (S7-A); it reads the connection string only, no query is sent.</summary>
    public static void EnsureLive(BankingDbContext db)
    {
        ArgumentNullException.ThrowIfNull(db);
        var database = new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString()).Database ?? "";
        if (!database.Contains("live", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"The simulator writes to the live database only; '{database}' is not one. Use scripts/run-live.sh or the live connection.");
    }

    /// <summary>
    /// Writes the plan from the end of the fixed data up to <paramref name="until"/>. With <paramref name="dryRun"/> it only counts what
    /// is missing; a debit the balance cannot carry is skipped and counted (S8-A), as a card is refused, and the rest goes on.
    /// </summary>
    public static async Task<CatchUpResult> CatchUpAsync(BankingDbContext db, DateTimeOffset until, bool dryRun,
        Action<string>? log = null, CancellationToken cancellationToken = default)
    {
        EnsureLive(db);
        var plan = DayPlan.Between(SyntheticTransactionSeeder.RealisticCutoff, until);
        var ids = plan.SelectMany(p => p.ToTransactions()).Select(t => t.Id).ToArray();
        var stored = (await db.Transactions.AsNoTracking().Where(t => ids.Contains(t.Id)).Select(t => t.Id).ToListAsync(cancellationToken))
            .ToHashSet();

        int written = 0, already = 0, skipped = 0;
        foreach (var planned in plan)
        {
            var rows = planned.ToTransactions();
            if (rows.All(r => stored.Contains(r.Id)))
            {
                already++;
                continue;
            }
            if (dryRun)
            {
                written++;
                continue;
            }
            try
            {
                var outcome = rows.Count == 1
                    ? await TransactionWriter.AddAsync(db, rows[0], cancellationToken)
                    : await PairedTransactionWriter.AddAsync(db, rows[0], rows[1], cancellationToken);
                if (outcome == WriteOutcome.Created)
                    written++;
                else
                    already++;
            }
            catch (BalanceLimitException)
            {
                skipped++;
                log?.Invoke($"Skipped for the balance: {planned.At:u} {planned.RawDescription} {planned.Amount} {planned.Currency}.");
            }
        }
        return new CatchUpResult(plan.Count, written, already, skipped);
    }
}
