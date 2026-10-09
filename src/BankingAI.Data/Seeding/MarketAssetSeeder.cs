using BankingAI.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BankingAI.Data.Seeding;

public static class MarketAssetSeeder
{
    private const long SeedLockKey = 2026100904;

    public static async Task<int> SeedAsync(BankingDbContext db, CancellationToken cancellationToken = default)
    {
        var definitions = new[]
        {
            (Id: (short)1, Symbol: "USDTRY", Name: "ABD Doları / Türk Lirası", AssetType: "Currency", BaseUnit: "USD", QuoteCurrency: "TRY", IsActive: true),
            (Id: (short)2, Symbol: "EURTRY", Name: "Euro / Türk Lirası", AssetType: "Currency", BaseUnit: "EUR", QuoteCurrency: "TRY", IsActive: true),
            (Id: (short)3, Symbol: "GRAM_GOLD_TRY", Name: "Gram Altın / Türk Lirası", AssetType: "Commodity", BaseUnit: "GRAM", QuoteCurrency: "TRY", IsActive: true)
        };

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            $"SELECT pg_advisory_xact_lock({SeedLockKey})", cancellationToken);

        var existing = await db.MarketAssets
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var byId = existing.ToDictionary(asset => asset.Id);
        var bySymbol = existing.ToDictionary(asset => asset.Symbol, StringComparer.Ordinal);
        var additions = new List<MarketAsset>();

        foreach (var definition in definitions)
        {
            if (byId.TryGetValue(definition.Id, out var byExistingId))
            {
                if (!Matches(byExistingId, definition))
                    throw new InvalidOperationException(
                        $"MarketAsset Id {definition.Id} mevcut fakat seed değeriyle çelişiyor.");

                continue;
            }

            if (bySymbol.TryGetValue(definition.Symbol, out var byExistingSymbol) && byExistingSymbol.Id != definition.Id)
                throw new InvalidOperationException(
                    $"MarketAsset Symbol '{definition.Symbol}' farklı bir Id ile mevcut.");

            additions.Add(new MarketAsset
            {
                Id = definition.Id,
                Symbol = definition.Symbol,
                Name = definition.Name,
                AssetType = definition.AssetType,
                BaseUnit = definition.BaseUnit,
                QuoteCurrency = definition.QuoteCurrency,
                IsActive = definition.IsActive
            });
        }

        if (additions.Count > 0)
            await db.MarketAssets.AddRangeAsync(additions, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return additions.Count;
    }

    private static bool Matches(MarketAsset asset,
        (short Id, string Symbol, string Name, string AssetType, string BaseUnit, string QuoteCurrency, bool IsActive) definition) =>
        asset.Symbol == definition.Symbol &&
        asset.Name == definition.Name &&
        asset.AssetType == definition.AssetType &&
        asset.BaseUnit == definition.BaseUnit &&
        asset.QuoteCurrency == definition.QuoteCurrency &&
        asset.IsActive == definition.IsActive;
}
