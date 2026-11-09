using BankingAI.Data.Entities;
using BankingAI.Data.Market;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BankingAI.Data.Tests.Market;

[Collection(PostgreSqlCollection.Name)]
public class MarketPriceReaderTests
{
    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Returns_only_the_asked_asset_inside_the_range_both_ends_included()
    {
        await using var db = new BankingDbContext();
        await using var scope = await db.Database.BeginTransactionAsync();
        var asset = await TemporaryAssetAsync(db);
        var other = await TemporaryAssetAsync(db);
        await MarketPriceImporter.ImportAsync(db,
        [
            new(asset.Symbol, new(2025, 10, 9), 1m, "TCMB"),
            new(asset.Symbol, new(2025, 10, 10), 41.6388m, "TCMB"),
            new(asset.Symbol, new(2025, 10, 13), 41.7m, "TCMB"),
            new(asset.Symbol, new(2025, 10, 14), 2m, "TCMB"),
            new(other.Symbol, new(2025, 10, 10), 99m, "TCMB")
        ]);

        var series = await MarketPriceReader.SeriesAsync(db, asset.Symbol, new(2025, 10, 10), new(2025, 10, 13));

        Assert.Equal(new Dictionary<DateOnly, decimal> { [new(2025, 10, 10)] = 41.6388m, [new(2025, 10, 13)] = 41.7m }, series);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task An_unknown_symbol_is_an_error_not_an_empty_series()
    {
        await using var db = new BankingDbContext();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            MarketPriceReader.SeriesAsync(db, "NOPE_" + Guid.NewGuid().ToString("N")[..8], new(2025, 1, 1), new(2025, 1, 2)));
    }

    private static async Task<MarketAsset> TemporaryAssetAsync(BankingDbContext db)
    {
        var used = await db.MarketAssets.Select(a => a.Id).ToListAsync();
        var id = (short)Enumerable.Range(1000, short.MaxValue - 1000).First(i => !used.Contains((short)i));
        var asset = new MarketAsset
        {
            Id = id, Symbol = "TEST_" + Guid.NewGuid().ToString("N")[..20], Name = "Reader test",
            AssetType = "Currency", BaseUnit = "USD", QuoteCurrency = "TRY", IsActive = true
        };
        db.Add(asset);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return asset;
    }
}
