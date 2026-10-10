using BankingAI.Data.Entities;
using BankingAI.Data.Enums;
using Microsoft.EntityFrameworkCore;

namespace BankingAI.Data.Seeding;

public static class SyntheticTransactionSeeder
{
    private const short SubscriptionCategoryId = 6;
    private const long SeedAdvisoryLockId = 871945310246L;
    public const int CreditCount = 14;
    public const int SubscriptionCount = 6;
    public const int CategoryExampleCount = 6;
    public const int GroceryGrowthCount = 6;
    public const int CategorizationBoundaryCount = 2;
    public const int RecentSpendingCount = 36;
    public const int DemoCoverageCount = 28;

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

    public static Task<int> SeedCategoryExamplesAsync(BankingDbContext db, CancellationToken cancellationToken = default)
    {
        var adaTry = Guid.Parse("20000000-0000-0000-0000-000000000001");
        var denizTry = Guid.Parse("20000000-0000-0000-0000-000000000003");
        var definitions = new List<Transaction>
        {
            Purchase(201, adaTry, 185m, "POS 4412 STARBUCKS KADIKOY", "Starbucks", 1, new(2025, 5, 10, 9, 0, 0, TimeSpan.Zero)),
            Purchase(202, denizTry, 2500m, "POS 1820 AILE MARKETI KADIKOY", "Aile Marketi", 2, new(2025, 5, 14, 12, 0, 0, TimeSpan.Zero)),
            Purchase(203, denizTry, 3200m, "POS 0731 COCUK GIYIM", "Çocuk Giyim", 3, new(2025, 6, 2, 10, 0, 0, TimeSpan.Zero)),
            Purchase(204, denizTry, 450m, "ISTANBULKART ULASIM ODEME", "Şehir Ulaşım", 4, new(2025, 6, 4, 7, 0, 0, TimeSpan.Zero)),
            Purchase(205, denizTry, 1900m, "POS 6621 AKARYAKIT ISTASYONU", "Akaryakıt İstasyonu", 8, new(2025, 6, 8, 14, 0, 0, TimeSpan.Zero)),
            Purchase(206, denizTry, 1450m, "ELEKTRIK FATURA TAHSILATI", "Elektrik Faturası", 9, new(2025, 6, 15, 8, 0, 0, TimeSpan.Zero))
        };

        return SeedAsync(db, definitions, cancellationToken);
    }

    public static Task<int> SeedGroceryGrowthAsync(BankingDbContext db, CancellationToken cancellationToken = default)
    {
        var denizTry = Guid.Parse("20000000-0000-0000-0000-000000000003");
        var definitions = new List<Transaction>
        {
            Purchase(301, denizTry, 9800m, "POS 4801 AILE MARKETI SUBE 04", "Aile Marketi", 2, new(2025, 8, 2, 11, 0, 0, TimeSpan.Zero)),
            Purchase(302, denizTry, 8200m, "POS 4817 AILE MARKETI SUBE 04", "Aile Marketi", 2, new(2025, 8, 12, 11, 0, 0, TimeSpan.Zero)),
            Purchase(303, denizTry, 6000m, "POS 4893 AILE MARKETI SUBE 04", "Aile Marketi", 2, new(2025, 8, 23, 11, 0, 0, TimeSpan.Zero)),
            Purchase(304, denizTry, 12500m, "POS 5120 AILE MARKETI SUBE 04", "Aile Marketi", 2, new(2025, 9, 3, 11, 0, 0, TimeSpan.Zero)),
            Purchase(305, denizTry, 10500m, "POS 5174 AILE MARKETI SUBE 04", "Aile Marketi", 2, new(2025, 9, 13, 11, 0, 0, TimeSpan.Zero)),
            Purchase(306, denizTry, 8000m, "POS 5238 AILE MARKETI SUBE 04", "Aile Marketi", 2, new(2025, 9, 24, 11, 0, 0, TimeSpan.Zero))
        };

        return SeedAsync(db, definitions, cancellationToken);
    }

    public static Task<int> SeedCategorizationBoundariesAsync(BankingDbContext db, CancellationToken cancellationToken = default)
    {
        var adaTry = Guid.Parse("20000000-0000-0000-0000-000000000001");
        var definitions = new List<Transaction>
        {
            Create(401, adaTry, 420m, TransactionDirection.Debit, TransactionKind.Purchase, "TRY",
                "POS 9055 KADIKOY HARCAMA", null, null, null,
                new DateTimeOffset(2025, 6, 20, 13, 0, 0, TimeSpan.Zero)),
            Purchase(402, adaTry, 780m, "POS 3182 HOBI MALZEME DUKKANI", "Hobi Dükkânı", 11,
                new DateTimeOffset(2025, 6, 22, 10, 0, 0, TimeSpan.Zero))
        };

        return SeedAsync(db, definitions, cancellationToken);
    }

    public static Task<int> SeedRecentSpendingAsync(BankingDbContext db, CancellationToken cancellationToken = default)
    {
        var adaTry = Guid.Parse("20000000-0000-0000-0000-000000000001");
        var denizTry = Guid.Parse("20000000-0000-0000-0000-000000000003");
        var groceryAmounts = new[] { 12000m, 14000m, 16000m, 18000m, 24000m, 31000m };
        var definitions = new List<Transaction>();

        // Fixed 2026 dates keep this fixture reproducible when the command is rerun.
        for (var month = 4; month <= 9; month++)
        {
            var ordinal = 501 + (month - 4) * 6;
            definitions.Add(SalaryCredit(ordinal, adaTry, 45000m, $"Ada maaş ödemesi 2026-{month:D2}", month, 2026));
            definitions.Add(SalaryCredit(ordinal + 1, denizTry, 55000m, $"Deniz maaş ödemesi 2026-{month:D2}", month, 2026));
            definitions.Add(SubscriptionDebit(ordinal + 2, adaTry, 229.99m, "Netflix", month, 12, 2026));
            definitions.Add(SubscriptionDebit(ordinal + 3, adaTry, 99.99m, "Spotify", month, 18, 2026));
            definitions.Add(Purchase(ordinal + 4, adaTry, 185m, $"POS STARBUCKS KADIKOY 2026-{month:D2}", "Starbucks", 1,
                new DateTimeOffset(2026, month, 20, 9, 0, 0, TimeSpan.Zero)));
            definitions.Add(Purchase(ordinal + 5, denizTry, groceryAmounts[month - 4], $"POS AILE MARKETI 2026-{month:D2}", "Aile Marketi", 2,
                new DateTimeOffset(2026, month, 24, 11, 0, 0, TimeSpan.Zero)));
        }

        return SeedAsync(db, definitions, cancellationToken);
    }

    public static Task<int> SeedDemoCoverageAsync(BankingDbContext db, CancellationToken cancellationToken = default)
    {
        var adaTry = Guid.Parse("20000000-0000-0000-0000-000000000001");
        var denizTry = Guid.Parse("20000000-0000-0000-0000-000000000003");
        var egeTry = Guid.Parse("20000000-0000-0000-0000-000000000006");
        var definitions = new List<Transaction>
        {
            // Ada: sık ve küçük kahve harcamaları + belirsiz açıklamalı bilinmeyen işlem.
            SalaryCredit(601, adaTry, 45000m, "Ada maaş ödemesi 2026-10", 10, 2026),
            Purchase(602, adaTry, 210m, "POS 4412 STARBUCKS KADIKOY", "Starbucks", 1, new(2026, 10, 6, 9, 0, 0, TimeSpan.Zero)),
            Purchase(603, adaTry, 165m, "POS 4412 STARBUCKS KADIKOY", "Starbucks", 1, new(2026, 10, 8, 8, 30, 0, TimeSpan.Zero)),
            Create(604, adaTry, 145m, TransactionDirection.Debit, TransactionKind.Purchase, "TRY", "POS 6604 ONLINE ODEME 4471", null, null, null,
                new DateTimeOffset(2026, 10, 9, 10, 0, 0, TimeSpan.Zero)),

            // Deniz: aile market ve fatura harcamaları.
            Purchase(605, denizTry, 9500m, "POS AILE MARKETI 2026-10", "Aile Marketi", 2, new(2026, 10, 4, 11, 0, 0, TimeSpan.Zero)),
            SalaryCredit(606, denizTry, 55000m, "Deniz maaş ödemesi 2026-10", 10, 2026),
            Purchase(607, denizTry, 1850m, "ELEKTRIK FATURA TAHSILATI", "Elektrik Faturası", 9, new(2026, 10, 7, 8, 0, 0, TimeSpan.Zero))
        };

        // Ege's new TRY account starts at zero; salaries are listed first so debits never
        // hit insufficient balance, because definitions are written in list order.
        for (var month = 4; month <= 10; month++)
            definitions.Add(SalaryCredit(604 + month, egeTry, 40000m, $"Ege maaş ödemesi 2026-{month:D2}", month, 2026));

        var transportAmounts = new[] { (300m, 300m), (300m, 350m), (350m, 350m), (300m, 350m), (450m, 450m), (700m, 700m) };
        for (var month = 4; month <= 9; month++)
        {
            var (first, second) = transportAmounts[month - 4];
            var ordinal = 615 + (month - 4) * 2;
            definitions.Add(Purchase(ordinal, egeTry, first, "ISTANBULKART ULASIM ODEME", "Şehir Ulaşım", 4,
                new DateTimeOffset(2026, month, 11, 7, 30, 0, TimeSpan.Zero)));
            definitions.Add(Purchase(ordinal + 1, egeTry, second, "ISTANBULKART ULASIM ODEME", "Şehir Ulaşım", 4,
                new DateTimeOffset(2026, month, 25, 7, 30, 0, TimeSpan.Zero)));
        }

        definitions.Add(Purchase(627, egeTry, 1200m, "ONLINE OTOBUS BILETI ISTANBUL-ANKARA", "Şehirlerarası Otobüs", 7,
            new DateTimeOffset(2026, 8, 14, 15, 0, 0, TimeSpan.Zero)));
        definitions.Add(Purchase(628, egeTry, 700m, "ISTANBULKART ULASIM ODEME", "Şehir Ulaşım", 4,
            new DateTimeOffset(2026, 10, 3, 7, 30, 0, TimeSpan.Zero)));

        return SeedAsync(db, definitions, cancellationToken);
    }

    private static async Task<int> SeedAsync(
        BankingDbContext db,
        IReadOnlyCollection<Transaction> definitions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        if (db.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("Synthetic transaction seed mevcut bir DB transaction'ı içinde çalıştırılamaz.");

        // A session-level PostgreSQL advisory lock covers both the existence check and
        // the writes. Holding the connection open keeps the lock while each writer uses
        // its own transaction on this same connection.
        await db.Database.OpenConnectionAsync(cancellationToken);
        var lockAcquired = false;
        try
        {
            await ExecuteAdvisoryLockCommandAsync(db, "pg_advisory_lock", cancellationToken);
            lockAcquired = true;
            return await SeedWhileLockedAsync(db, definitions, cancellationToken);
        }
        finally
        {
            try
            {
                if (lockAcquired)
                    await ExecuteAdvisoryLockCommandAsync(db, "pg_advisory_unlock", CancellationToken.None);
            }
            finally
            {
                await db.Database.CloseConnectionAsync();
            }
        }
    }

    private static async Task<int> SeedWhileLockedAsync(
        BankingDbContext db,
        IReadOnlyCollection<Transaction> definitions,
        CancellationToken cancellationToken)
    {
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

        var categoryIds = definitions.Where(t => t.CategoryId.HasValue)
            .Select(t => t.CategoryId!.Value).Distinct().ToArray();
        if (categoryIds.Length > 0)
        {
            var categories = await db.TransactionCategories.AsNoTracking()
                .Where(c => categoryIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Code, cancellationToken);
            foreach (var categoryId in categoryIds)
            {
                if (!categories.TryGetValue(categoryId, out var code) || code != CategoryCode(categoryId))
                    throw new InvalidOperationException(
                        $"Synthetic transaction seed için kategori Id {categoryId} eksik veya beklenen kodla eşleşmiyor. Önce 'seed categories' çalıştırın.");
            }
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

    private static async Task ExecuteAdvisoryLockCommandAsync(
        BankingDbContext db,
        string functionName,
        CancellationToken cancellationToken)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"SELECT {functionName}(@lock_id)";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "lock_id";
        parameter.Value = SeedAdvisoryLockId;
        command.Parameters.Add(parameter);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static Transaction OpeningCredit(int ordinal, Guid accountId, decimal amount, string currency, string description) =>
        Create(ordinal, accountId, amount, TransactionDirection.Credit, TransactionKind.OpeningFunding, currency, description, null, null, null,
            new DateTimeOffset(2025, 4, 1, 8, 0, 0, TimeSpan.Zero));

    private static Transaction SalaryCredit(int ordinal, Guid accountId, decimal amount, string description, int month, int year = 2025) =>
        Create(ordinal, accountId, amount, TransactionDirection.Credit, TransactionKind.Salary, "TRY", description, null, null, null,
            new DateTimeOffset(year, month, 5, 6, 0, 0, TimeSpan.Zero));

    private static Transaction SubscriptionDebit(int ordinal, Guid accountId, decimal amount, string merchant, int month, int day, int year = 2025) =>
        Create(ordinal, accountId, amount, TransactionDirection.Debit, TransactionKind.Purchase, "TRY", $"{merchant} aylık abonelik ödemesi",
            merchant, SubscriptionCategoryId, CategorizationSource.Rule,
            new DateTimeOffset(year, month, day, 7, 0, 0, TimeSpan.Zero));

    private static Transaction Purchase(
        int ordinal,
        Guid accountId,
        decimal amount,
        string rawDescription,
        string normalizedMerchant,
        short categoryId,
        DateTimeOffset occurredAt) =>
        Create(ordinal, accountId, amount, TransactionDirection.Debit, TransactionKind.Purchase, "TRY", rawDescription,
            normalizedMerchant, categoryId, CategorizationSource.Rule, occurredAt);

    private static string CategoryCode(short id) => id switch
    {
        1 => "FOOD",
        2 => "GROCERY",
        3 => "CLOTHING",
        4 => "TRANSPORTATION",
        6 => "SUBSCRIPTION",
        7 => "TRAVEL",
        8 => "FUEL",
        9 => "BILLS",
        11 => "OTHER",
        _ => throw new InvalidOperationException($"Synthetic seed için beklenmeyen kategori Id {id}.")
    };

    private static Transaction Create(
        int ordinal,
        Guid accountId,
        decimal amount,
        TransactionDirection direction,
        TransactionKind kind,
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
            Kind = kind,
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
        stored.Kind == expected.Kind &&
        stored.PairId == expected.PairId &&
        stored.CurrencyCode == expected.CurrencyCode &&
        stored.RawDescription == expected.RawDescription &&
        stored.NormalizedMerchant == expected.NormalizedMerchant &&
        stored.CategoryId == expected.CategoryId &&
        stored.CategorizationSource == expected.CategorizationSource &&
        stored.OccurredAt == expected.OccurredAt;
}
