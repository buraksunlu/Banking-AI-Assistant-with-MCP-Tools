using BankingAI.Data.Entities;
using BankingAI.Data.Market;
using BankingAI.TestSupport;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BankingAI.Data.Tests.Market;

/// <summary>
/// Every test writes to a temporary asset inside a transaction that is rolled back, so the real
/// USDTRY/EURTRY rows are never touched.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
public class MarketPriceImporterTests
{
    private static readonly DateOnly Day1 = new(2025, 10, 10);
    private static readonly DateOnly Day2 = new(2025, 10, 13);

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task New_days_are_added_with_their_exact_value_and_source()
    {
        await using var db = new BankingDbContext();
        await using var scope = await db.Database.BeginTransactionAsync();
        var asset = await TemporaryAssetAsync(db);

        var result = await MarketPriceImporter.ImportAsync(db,
            [Row(asset, Day1, 41.6388m), Row(asset, Day2, 41.9876m)]);

        Assert.Equal(2, result.Added);
        Assert.Equal(0, result.Unchanged);
        Assert.Empty(result.Conflicts);
        var stored = await Stored(db, asset);
        Assert.Equal([(Day1, 41.6388m, "TCMB"), (Day2, 41.9876m, "TCMB")], stored);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Importing_the_same_values_again_adds_nothing()
    {
        await using var db = new BankingDbContext();
        await using var scope = await db.Database.BeginTransactionAsync();
        var asset = await TemporaryAssetAsync(db);
        await MarketPriceImporter.ImportAsync(db, [Row(asset, Day1, 41.6388m)]);

        // numeric(20,8) reads back as 41.63880000; the value, not the scale, decides "same".
        var again = await MarketPriceImporter.ImportAsync(db, [Row(asset, Day1, 41.6388m), Row(asset, Day2, 42m)]);

        Assert.Equal(1, again.Added);
        Assert.Equal(1, again.Unchanged);
        Assert.Empty(again.Conflicts);
        Assert.Equal(2, (await Stored(db, asset)).Count);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task A_different_value_for_a_stored_day_is_reported_and_never_overwrites()
    {
        await using var db = new BankingDbContext();
        await using var scope = await db.Database.BeginTransactionAsync();
        var asset = await TemporaryAssetAsync(db);
        await MarketPriceImporter.ImportAsync(db, [Row(asset, Day1, 41.6388m)]);

        var result = await MarketPriceImporter.ImportAsync(db, [Row(asset, Day1, 41.7138m), Row(asset, Day2, 42m)]);

        Assert.Equal(1, result.Added);
        Assert.Equal(0, result.Unchanged);
        Assert.Equal([new MarketPriceConflict(asset.Symbol, Day1, 41.6388m, 41.7138m)], result.Conflicts);
        Assert.Equal(41.6388m, (await Stored(db, asset)).Single(p => p.Day == Day1).Price);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task An_unknown_symbol_writes_nothing()
    {
        await using var db = new BankingDbContext();
        await using var scope = await db.Database.BeginTransactionAsync();
        var asset = await TemporaryAssetAsync(db);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => MarketPriceImporter.ImportAsync(db,
            [Row(asset, Day1, 41.6388m), new MarketPriceRow("NOPE_" + Guid.NewGuid().ToString("N")[..10], Day1, 1m, "TCMB")]));

        Assert.Contains("NOPE_", error.Message);
        Assert.Empty(await Stored(db, asset));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task The_same_asset_and_day_twice_in_one_import_writes_nothing()
    {
        await using var db = new BankingDbContext();
        await using var scope = await db.Database.BeginTransactionAsync();
        var asset = await TemporaryAssetAsync(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() => MarketPriceImporter.ImportAsync(db,
            [Row(asset, Day1, 41.6388m), Row(asset, Day1, 41.6388m)]));

        Assert.Empty(await Stored(db, asset));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Validation_still_refuses_a_future_day()
    {
        await using var db = new BankingDbContext();
        await using var scope = await db.Database.BeginTransactionAsync();
        var asset = await TemporaryAssetAsync(db);

        await Assert.ThrowsAsync<ArgumentException>(() => MarketPriceImporter.ImportAsync(db,
            [Row(asset, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(3), 1m)]));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Without_an_open_transaction_the_import_commits_its_own()
    {
        // A temporary asset must be committed for this case, so it is deleted again in finally; the shared
        // lock keeps tests that count every MarketAsset row from seeing it.
        await using var rowsLock = await CommittedRowsLock.SharedAsync();
        await using var db = new BankingDbContext();
        var asset = await TemporaryAssetAsync(db);
        try
        {
            var result = await MarketPriceImporter.ImportAsync(db, [Row(asset, Day1, 41.6388m)]);

            Assert.Equal(1, result.Added);
            await using var fresh = new BankingDbContext();
            Assert.Equal(1, await fresh.DailyMarketPrices.CountAsync(p => p.MarketAssetId == asset.Id));
        }
        finally
        {
            await db.DailyMarketPrices.Where(p => p.MarketAssetId == asset.Id).ExecuteDeleteAsync();
            await db.MarketAssets.Where(a => a.Id == asset.Id).ExecuteDeleteAsync();
        }
    }

    private static MarketPriceRow Row(MarketAsset asset, DateOnly day, decimal price) => new(asset.Symbol, day, price, "TCMB");

    private static async Task<MarketAsset> TemporaryAssetAsync(BankingDbContext db)
    {
        var used = await db.MarketAssets.Select(a => a.Id).ToListAsync();
        var id = (short)Enumerable.Range(1000, short.MaxValue - 1000).First(i => !used.Contains((short)i));
        var asset = new MarketAsset
        {
            Id = id, Symbol = "TEST_" + Guid.NewGuid().ToString("N")[..20], Name = "Import test",
            AssetType = "Currency", BaseUnit = "USD", QuoteCurrency = "TRY", IsActive = true
        };
        db.Add(asset);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return asset;
    }

    private static async Task<List<(DateOnly Day, decimal Price, string? Source)>> Stored(BankingDbContext db, MarketAsset asset) =>
        (await db.DailyMarketPrices.AsNoTracking()
            .Where(p => p.MarketAssetId == asset.Id)
            .OrderBy(p => p.PriceDate)
            .Select(p => new { p.PriceDate, p.Price, p.Source })
            .ToListAsync())
        .Select(p => (p.PriceDate, p.Price, p.Source))
        .ToList();
}
