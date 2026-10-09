using BankingAI.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BankingAI.Data.Seeding;

public static class TransactionCategorySeeder
{
    private const long SeedLockKey = 2026100902;

    public static async Task<int> SeedAsync(BankingDbContext db, CancellationToken cancellationToken = default)
    {
        var definitions = new[]
        {
            (Id: (short)1, Code: "FOOD", DisplayName: "Yeme & İçme"),
            (Id: (short)2, Code: "GROCERY", DisplayName: "Market"),
            (Id: (short)3, Code: "CLOTHING", DisplayName: "Giyim"),
            (Id: (short)4, Code: "TRANSPORTATION", DisplayName: "Ulaşım"),
            (Id: (short)5, Code: "ENTERTAINMENT", DisplayName: "Eğlence"),
            (Id: (short)6, Code: "SUBSCRIPTION", DisplayName: "Abonelik"),
            (Id: (short)7, Code: "TRAVEL", DisplayName: "Seyahat"),
            (Id: (short)8, Code: "FUEL", DisplayName: "Yakıt"),
            (Id: (short)9, Code: "BILLS", DisplayName: "Faturalar"),
            (Id: (short)10, Code: "HEALTH", DisplayName: "Sağlık"),
            (Id: (short)11, Code: "OTHER", DisplayName: "Diğer")
        };

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        // Transaction-scoped lock serializes concurrent category seed commands.
        await db.Database.ExecuteSqlRawAsync(
            $"SELECT pg_advisory_xact_lock({SeedLockKey})", cancellationToken);
        var existing = await db.TransactionCategories
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var byId = existing.ToDictionary(category => category.Id);
        var byCode = existing.ToDictionary(category => category.Code, StringComparer.Ordinal);
        var additions = new List<TransactionCategory>();

        foreach (var definition in definitions)
        {
            if (byId.TryGetValue(definition.Id, out var byExistingId))
            {
                if (byExistingId.Code != definition.Code || byExistingId.DisplayName != definition.DisplayName)
                    throw new InvalidOperationException(
                        $"TransactionCategory Id {definition.Id} mevcut fakat seed değeriyle çelişiyor.");

                continue;
            }

            if (byCode.TryGetValue(definition.Code, out var byExistingCode) && byExistingCode.Id != definition.Id)
                throw new InvalidOperationException(
                    $"TransactionCategory Code '{definition.Code}' farklı bir Id ile mevcut.");

            additions.Add(new TransactionCategory
            {
                Id = definition.Id,
                Code = definition.Code,
                DisplayName = definition.DisplayName
            });
        }

        if (additions.Count > 0)
            await db.TransactionCategories.AddRangeAsync(additions, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return additions.Count;
    }
}
