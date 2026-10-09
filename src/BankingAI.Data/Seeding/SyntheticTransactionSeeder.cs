using BankingAI.Data.Entities;
using BankingAI.Data.Enums;
using Microsoft.EntityFrameworkCore;

namespace BankingAI.Data.Seeding;

public static class SyntheticTransactionSeeder
{
    private const short SubscriptionCategoryId = 6;
    public const int CreditCount = 14;
    public const int SubscriptionCount = 6;

    public static Task<int> SeedCreditsAsync(BankingDbContext db, CancellationToken cancellationToken = default)
    {
        var adaTry = Guid.Parse("20000000-0000-0000-0000-000000000001");
        var adaUsd = Guid.Parse("20000000-0000-0000-0000-000000000002");
        var denizTry = Guid.Parse("20000000-0000-0000-0000-000000000003");
        var egeEur = Guid.Parse("20000000-0000-0000-0000-000000000004");
        var definitions = new List<Transaction>
        {
            OpeningCredit(1, adaUsd, 500m, "USD", "USD hesabı açılış fonlaması"),
            OpeningCredit(2, egeEur, 1000m, "EUR", "EUR hesabı açılış fonlaması")
        };

        for (var month = 4; month <= 9; month++)
        {
            var ordinal = month - 4;
            definitions.Add(SalaryCredit(3 + ordinal * 2, adaTry, 45000m, $"Ada maaş ödemesi 2025-{month:D2}", month));
            definitions.Add(SalaryCredit(4 + ordinal * 2, denizTry, 55000m, $"Deniz maaş ödemesi 2025-{month:D2}", month));
        }

        return SeedAsync(db, definitions, cancellationToken);
    }

    public static Task<int> SeedSubscriptionsAsync(BankingDbContext db, CancellationToken cancellationToken = default)
    {
        var adaTry = Guid.Parse("20000000-0000-0000-0000-000000000001");
        var definitions = new List<Transaction>();
        var ordinal = 101;
        for (var month = 7; month <= 9; month++)
        {
            definitions.Add(SubscriptionDebit(ordinal++, adaTry, 229.99m, "Netflix", month, 12));
            definitions.Add(SubscriptionDebit(ordinal++, adaTry, 99.99m, "Spotify", month, 18));
        }

        return SeedAsync(db, definitions, cancellationToken);
    }

    private static async Task<int> SeedAsync(
        BankingDbContext db,
        IReadOnlyCollection<Transaction> definitions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        var ids = definitions.Select(t => t.Id).ToArray();
        if (ids.Distinct().Count() != ids.Length)
            throw new InvalidOperationException("Synthetic transaction seed içinde tekrarlanan Id var.");

        var accountIds = definitions.Select(t => t.AccountId).Distinct().ToArray();
        var availableAccounts = await db.Accounts.AsNoTracking()
            .Where(a => accountIds.Contains(a.Id))
            .Select(a => new { a.Id, a.CurrencyCode })
            .ToListAsync(cancellationToken);
        foreach (var expected in definitions.Select(t => (t.AccountId, t.CurrencyCode)).Distinct())
        {
            if (!availableAccounts.Any(a => a.Id == expected.AccountId && a.CurrencyCode == expected.CurrencyCode))
                throw new InvalidOperationException(
                    $"Synthetic transaction seed hesabı bulunamadı veya para birimi eşleşmiyor: {expected.AccountId}/{expected.CurrencyCode}. Önce 'seed accounts' çalıştırın.");
        }

        if (definitions.Any(t => t.CategoryId == SubscriptionCategoryId))
        {
            var categoryExists = await db.TransactionCategories.AsNoTracking()
                .AnyAsync(c => c.Id == SubscriptionCategoryId && c.Code == "SUBSCRIPTION", cancellationToken);
            if (!categoryExists)
                throw new InvalidOperationException("SUBSCRIPTION kategorisi yok. Önce 'seed categories' çalıştırın.");
        }

        var existing = await db.Transactions.AsNoTracking()
            .Where(t => ids.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, cancellationToken);
        var additions = new List<Transaction>();
        foreach (var definition in definitions)
        {
            if (existing.TryGetValue(definition.Id, out var stored))
            {
                if (!Matches(stored, definition))
                    throw new InvalidOperationException($"Transaction Id {definition.Id} mevcut fakat synthetic seed değeriyle çelişiyor.");
                continue;
            }

            additions.Add(definition);
        }

        foreach (var transaction in additions)
            await TransactionSeedWriter.AddAsync(db, transaction, cancellationToken);

        return additions.Count;
    }

    private static Transaction OpeningCredit(int ordinal, Guid accountId, decimal amount, string currency, string description) =>
        Create(ordinal, accountId, amount, TransactionDirection.Credit, currency, description, null, null, null,
            new DateTimeOffset(2025, 4, 1, 8, 0, 0, TimeSpan.Zero));

    private static Transaction SalaryCredit(int ordinal, Guid accountId, decimal amount, string description, int month) =>
        Create(ordinal, accountId, amount, TransactionDirection.Credit, "TRY", description, null, null, null,
            new DateTimeOffset(2025, month, 5, 6, 0, 0, TimeSpan.Zero));

    private static Transaction SubscriptionDebit(int ordinal, Guid accountId, decimal amount, string merchant, int month, int day) =>
        Create(ordinal, accountId, amount, TransactionDirection.Debit, "TRY", $"{merchant} aylık abonelik ödemesi",
            merchant, SubscriptionCategoryId, CategorizationSource.Rule,
            new DateTimeOffset(2025, month, day, 7, 0, 0, TimeSpan.Zero));

    private static Transaction Create(
        int ordinal,
        Guid accountId,
        decimal amount,
        TransactionDirection direction,
        string currency,
        string description,
        string? merchant,
        short? categoryId,
        CategorizationSource? source,
        DateTimeOffset occurredAt)
    {
        var id = Guid.Parse($"30000000-0000-0000-0000-{ordinal:D12}");
        return new Transaction
        {
            Id = id,
            AccountId = accountId,
            Amount = amount,
            Direction = direction,
            CurrencyCode = currency,
            RawDescription = description,
            NormalizedMerchant = merchant,
            CategoryId = categoryId,
            CategorizationSource = source,
            CategorizedAt = source is null ? null : DateTimeOffset.UtcNow,
            OccurredAt = occurredAt
        };
    }

    private static bool Matches(Transaction stored, Transaction expected) =>
        stored.AccountId == expected.AccountId &&
        stored.Amount == expected.Amount &&
        stored.Direction == expected.Direction &&
        stored.CurrencyCode == expected.CurrencyCode &&
        stored.RawDescription == expected.RawDescription &&
        stored.NormalizedMerchant == expected.NormalizedMerchant &&
        stored.CategoryId == expected.CategoryId &&
        stored.CategorizationSource == expected.CategorizationSource &&
        stored.OccurredAt == expected.OccurredAt;
}
