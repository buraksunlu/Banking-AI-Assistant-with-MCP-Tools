using Microsoft.EntityFrameworkCore;

namespace BankingAI.Data.Market;

/// <summary>Reads stored daily prices of one market asset.</summary>
public static class MarketPriceReader
{
    /// <summary>Day → price for <paramref name="symbol"/> between <paramref name="from"/> and <paramref name="to"/>, both included.</summary>
    /// <exception cref="InvalidOperationException">The symbol is not a known market asset.</exception>
    public static async Task<IReadOnlyDictionary<DateOnly, decimal>> SeriesAsync(
        BankingDbContext db, string symbol, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        var assetId = await db.MarketAssets.AsNoTracking()
            .Where(a => a.Symbol == symbol)
            .Select(a => (short?)a.Id)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException($"Tanımsız varlık: {symbol}.");

        return await db.DailyMarketPrices.AsNoTracking()
            .Where(p => p.MarketAssetId == assetId && p.PriceDate >= from && p.PriceDate <= to)
            .ToDictionaryAsync(p => p.PriceDate, p => p.Price, cancellationToken);
    }
}
