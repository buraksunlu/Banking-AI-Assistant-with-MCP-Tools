using BankingAI.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BankingAI.Data.Market;

/// <summary>
/// Writes daily market prices idempotently: a new asset/day is added, the same value is skipped,
/// and a different value for an existing asset/day is reported as a conflict and never overwritten.
/// </summary>
public static class MarketPriceImporter
{
    private const long ImportLockKey = 2026110901;

    /// <summary>
    /// Imports <paramref name="rows"/>. Uses the caller's open transaction if there is one, otherwise its own.
    /// </summary>
    /// <exception cref="InvalidOperationException">A symbol is unknown or the same asset/day appears twice.</exception>
    public static async Task<MarketImportResult> ImportAsync(
        BankingDbContext db, IReadOnlyCollection<MarketPriceRow> rows, CancellationToken cancellationToken = default)
    {
        if (rows.Count == 0)
            return new MarketImportResult(0, 0, []);

        var duplicate = rows.GroupBy(r => (r.Symbol, r.PriceDate)).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException(
                $"{duplicate.Key.Symbol} {duplicate.Key.PriceDate:yyyy-MM-dd} aynı içe aktarmada birden çok kez var.");

        // Tests run the import inside their own transaction and roll it back; the CLI has none and gets one here.
        await using var ownTransaction = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;
        // Two imports at once would both see "missing" and the second would hit the unique index.
        await db.Database.ExecuteSqlRawAsync($"SELECT pg_advisory_xact_lock({ImportLockKey})", cancellationToken);

        var symbols = rows.Select(r => r.Symbol).Distinct(StringComparer.Ordinal).ToList();
        var assets = await db.MarketAssets.AsNoTracking()
            .Where(a => symbols.Contains(a.Symbol))
            .ToDictionaryAsync(a => a.Symbol, a => a.Id, StringComparer.Ordinal, cancellationToken);
        var unknown = symbols.Where(s => !assets.ContainsKey(s)).ToList();
        if (unknown.Count > 0)
            throw new InvalidOperationException($"Tanımsız varlık: {string.Join(", ", unknown)}. Önce 'seed market-assets'.");

        var assetIds = assets.Values.ToList();
        var first = rows.Min(r => r.PriceDate);
        var last = rows.Max(r => r.PriceDate);
        var stored = await db.DailyMarketPrices.AsNoTracking()
            .Where(p => assetIds.Contains(p.MarketAssetId) && p.PriceDate >= first && p.PriceDate <= last)
            .ToDictionaryAsync(p => (p.MarketAssetId, p.PriceDate), p => p.Price, cancellationToken);

        var added = 0;
        var unchanged = 0;
        var conflicts = new List<MarketPriceConflict>();
        foreach (var row in rows.OrderBy(r => r.Symbol, StringComparer.Ordinal).ThenBy(r => r.PriceDate))
        {
            var assetId = assets[row.Symbol];
            if (stored.TryGetValue((assetId, row.PriceDate), out var storedPrice))
            {
                // decimal equality ignores scale: 41.63880000 from numeric(20,8) equals 41.6388.
                if (storedPrice == row.Price)
                    unchanged++;
                else
                    conflicts.Add(new MarketPriceConflict(row.Symbol, row.PriceDate, storedPrice, row.Price));
                continue;
            }

            db.DailyMarketPrices.Add(new DailyMarketPrice
            {
                MarketAssetId = assetId, PriceDate = row.PriceDate, Price = row.Price, Source = row.Source
            });
            added++;
        }

        await db.SaveChangesAsync(cancellationToken);
        if (ownTransaction is not null)
            await ownTransaction.CommitAsync(cancellationToken);
        db.ChangeTracker.Clear();

        return new MarketImportResult(added, unchanged, conflicts);
    }
}

/// <summary>Counts and conflicts of one import.</summary>
public sealed record MarketImportResult(int Added, int Unchanged, IReadOnlyList<MarketPriceConflict> Conflicts);

/// <summary>An asset/day already stored with a different value; the stored value was kept.</summary>
public sealed record MarketPriceConflict(string Symbol, DateOnly PriceDate, decimal StoredPrice, decimal ImportedPrice);
