using BankingAI.Data;
using Microsoft.EntityFrameworkCore;

namespace BankingAI.Application.Investment;

/// <summary>Whether a price could be used for a day.</summary>
public enum PriceStatus
{
    /// <summary>A price of the day or of an earlier day at most <see cref="MarketPriceReader.MaximumAgeDays"/> old.</summary>
    Ok,

    /// <summary>The nearest earlier price is older than <see cref="MarketPriceReader.MaximumAgeDays"/>; reported, never used.</summary>
    Stale,

    /// <summary>No price on or before the day.</summary>
    Missing,
}

/// <summary>An active market asset quoted in lira: its key, symbol, name and unit (USD, EUR, GRAM).</summary>
public sealed record MarketAssetInfo(short Id, string Symbol, string Name, string BaseUnit);

/// <summary>One stored daily price.</summary>
public sealed record MarketPrice(DateOnly Date, decimal Price, string? Source);

/// <summary>
/// The price chosen for <c>RequestedDate</c>: the day itself or the nearest earlier day (<c>PriceDate</c>). Price and source are
/// null when <c>Status</c> is <see cref="PriceStatus.Missing"/>.
/// </summary>
public sealed record PricePoint(DateOnly RequestedDate, DateOnly? PriceDate, decimal? Price, PriceStatus Status, string? Source);

/// <summary>
/// Reads daily market prices (OC-02). Missing days are never stored (IMP-a), so a day takes the nearest earlier price; one older
/// than <see cref="MaximumAgeDays"/> days is stale.
/// </summary>
public sealed class MarketPriceReader(BankingDbContext db)
{
    /// <summary>
    /// The longest gap in the imported data is 7 days (25 May → 1 June 2026, Kurban Bayramı), plus a 3-day margin (K5-D).
    /// </summary>
    public const int MaximumAgeDays = 10;

    /// <summary>Active assets quoted in TRY, in key order.</summary>
    public async Task<IReadOnlyList<MarketAssetInfo>> TryAssetsAsync(CancellationToken cancellationToken) =>
        await db.MarketAssets.AsNoTracking()
            .Where(a => a.IsActive && a.QuoteCurrency == "TRY")
            .OrderBy(a => a.Id)
            .Select(a => new MarketAssetInfo(a.Id, a.Symbol, a.Name, a.BaseUnit))
            .ToListAsync(cancellationToken);

    /// <summary>The price of every asset for every requested day.</summary>
    public async Task<IReadOnlyDictionary<(short AssetId, DateOnly Day), PricePoint>> GetAsync(
        IReadOnlyCollection<short> assetIds, IReadOnlyCollection<DateOnly> days, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(assetIds);
        ArgumentNullException.ThrowIfNull(days);
        if (assetIds.Count == 0 || days.Count == 0)
            return new Dictionary<(short, DateOnly), PricePoint>();

        // One query for every asset and day. Two years are about 500 rows per asset, so reading everything up to the last day
        // is cheaper than a lookup per day, and it still tells a stale price (older row exists) from a missing one (none).
        var last = days.Max();
        var rows = await db.DailyMarketPrices.AsNoTracking()
            .Where(p => assetIds.Contains(p.MarketAssetId) && p.PriceDate <= last)
            .OrderBy(p => p.PriceDate)
            .Select(p => new { p.MarketAssetId, Price = new MarketPrice(p.PriceDate, p.Price, p.Source) })
            .ToListAsync(cancellationToken);
        var byAsset = rows.GroupBy(r => r.MarketAssetId).ToDictionary(g => g.Key, g => (IReadOnlyList<MarketPrice>)g.Select(r => r.Price).ToList());

        return assetIds
            .SelectMany(id => days.Select(day => (id, day)))
            .Distinct()
            .ToDictionary(k => k, k => Pick(k.day, byAsset.GetValueOrDefault(k.id) ?? []));
    }

    /// <summary>The price for <paramref name="day"/> among one asset's prices sorted by date.</summary>
    public static PricePoint Pick(DateOnly day, IReadOnlyList<MarketPrice> pricesByDate)
    {
        ArgumentNullException.ThrowIfNull(pricesByDate);
        // A later price is never used for an earlier day: that would be hindsight.
        var price = pricesByDate.LastOrDefault(p => p.Date <= day);
        if (price is null)
            return new PricePoint(day, null, null, PriceStatus.Missing, null);
        var status = day.DayNumber - price.Date.DayNumber > MaximumAgeDays ? PriceStatus.Stale : PriceStatus.Ok;
        return new PricePoint(day, price.Date, price.Price, status, price.Source);
    }
}
