using BankingAI.Data.Entities;
using BankingAI.Data.Enums;
using BankingAI.Data.Validation;
using Microsoft.EntityFrameworkCore;

namespace BankingAI.Data.Seeding;

public static class SyntheticAccountSeeder
{
    private const long SeedLockKey = 2026100906;
    public const int AccountCount = 16;

    public static async Task<int> SeedAsync(BankingDbContext db, CancellationToken cancellationToken = default)
    {
        var adaId = Guid.Parse("10000000-0000-0000-0000-000000000001");
        var denizId = Guid.Parse("10000000-0000-0000-0000-000000000002");
        var egeId = Guid.Parse("10000000-0000-0000-0000-000000000003");
        // Fresh entities per call: no shared mutable Account instances.
        Account[] definitions =
        [
            new() { Id = Guid.Parse("20000000-0000-0000-0000-000000000001"), CustomerId = adaId, AccountName = "Ada günlük TRY", AccountNumber = "DUMMY-ADA-TRY-001", CurrencyCode = "TRY" },
            new() { Id = Guid.Parse("20000000-0000-0000-0000-000000000002"), CustomerId = adaId, AccountName = "Ada USD", AccountNumber = "DUMMY-ADA-USD-001", CurrencyCode = "USD" },
            new() { Id = Guid.Parse("20000000-0000-0000-0000-000000000003"), CustomerId = denizId, AccountName = "Deniz aile TRY", AccountNumber = "DUMMY-DENIZ-TRY-001", CurrencyCode = "TRY" },
            new() { Id = Guid.Parse("20000000-0000-0000-0000-000000000004"), CustomerId = egeId, AccountName = "Ege seyahat EUR", AccountNumber = "DUMMY-EGE-EUR-001", CurrencyCode = "EUR" },
            new() { Id = Guid.Parse("20000000-0000-0000-0000-000000000005"), CustomerId = adaId, AccountName = "Ada boş TRY", AccountNumber = "DUMMY-ADA-EMPTY-001", CurrencyCode = "TRY" },
            new() { Id = Guid.Parse("20000000-0000-0000-0000-000000000006"), CustomerId = egeId, AccountName = "Ege günlük TRY", AccountNumber = "DUMMY-EGE-TRY-001", CurrencyCode = "TRY" },
            // Fictional shop owned by Deniz; receives Purchase→Sale income (KIND-04b).
            new() { Id = Guid.Parse("20000000-0000-0000-0000-000000000007"), CustomerId = denizId, AccountName = "Kahve Durağı – Kadıköy", AccountNumber = "DUMMY-DENIZ-SHOP-001", CurrencyCode = "TRY", AccountType = AccountType.Business },
            // SYN-15 personas (customers ...0004–0009).
            new() { Id = Guid.Parse("20000000-0000-0000-0000-000000000008"), CustomerId = Customer(4), AccountName = "Selin günlük TRY", AccountNumber = "DUMMY-SELIN-TRY-001", CurrencyCode = "TRY" },
            new() { Id = Guid.Parse("20000000-0000-0000-0000-000000000009"), CustomerId = Customer(5), AccountName = "Murat aile TRY", AccountNumber = "DUMMY-MURAT-TRY-001", CurrencyCode = "TRY" },
            new() { Id = Guid.Parse("20000000-0000-0000-0000-000000000010"), CustomerId = Customer(6), AccountName = "Zeynep TRY", AccountNumber = "DUMMY-ZEYNEP-TRY-001", CurrencyCode = "TRY" },
            new() { Id = Guid.Parse("20000000-0000-0000-0000-000000000011"), CustomerId = Customer(6), AccountName = "Zeynep USD", AccountNumber = "DUMMY-ZEYNEP-USD-001", CurrencyCode = "USD" },
            new() { Id = Guid.Parse("20000000-0000-0000-0000-000000000012"), CustomerId = Customer(7), AccountName = "Hasan emekli TRY", AccountNumber = "DUMMY-HASAN-TRY-001", CurrencyCode = "TRY" },
            new() { Id = Guid.Parse("20000000-0000-0000-0000-000000000013"), CustomerId = Customer(8), AccountName = "Elif Berlin EUR", AccountNumber = "DUMMY-ELIF-EUR-001", CurrencyCode = "EUR" },
            new() { Id = Guid.Parse("20000000-0000-0000-0000-000000000014"), CustomerId = Customer(8), AccountName = "Elif TRY", AccountNumber = "DUMMY-ELIF-TRY-001", CurrencyCode = "TRY" },
            new() { Id = Guid.Parse("20000000-0000-0000-0000-000000000015"), CustomerId = Customer(9), AccountName = "Can kişisel TRY", AccountNumber = "DUMMY-CAN-TRY-001", CurrencyCode = "TRY" },
            // Fictional stationery shop owned by Can; its income is the other personas' purchases (Sale legs).
            new() { Id = Guid.Parse("20000000-0000-0000-0000-000000000016"), CustomerId = Customer(9), AccountName = "Can Kırtasiye – Moda", AccountNumber = "DUMMY-CAN-SHOP-001", CurrencyCode = "TRY", AccountType = AccountType.Business }
        ];

        var referenceUtc = DateTimeOffset.UtcNow;
        foreach (var definition in definitions)
        {
            EntityValidation.Validate(definition, referenceUtc);
            if (definition.Balance != 0m || !definition.AccountNumber.StartsWith("DUMMY-", StringComparison.Ordinal))
                throw new InvalidOperationException("Account seed girdisi DUMMY- numarası ve sıfır başlangıç bakiyesi kullanmalı.");
        }
        if (definitions.Select(a => a.Id).Distinct().Count() != definitions.Length ||
            definitions.Select(a => a.AccountNumber).Distinct(StringComparer.Ordinal).Count() != definitions.Length)
            throw new InvalidOperationException("Account seed girdilerinde tekrarlanan Id veya AccountNumber var.");

        // A caller-owned transaction allows integration checks to roll everything back.
        // Otherwise the command owns and commits its transaction.
        await using var transaction = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;
        await db.Database.ExecuteSqlRawAsync($"SELECT pg_advisory_xact_lock({SeedLockKey})", cancellationToken);

        var customerIds = definitions.Select(a => a.CustomerId).Distinct().ToArray();
        var existingCustomerIds = await db.Customers.Where(c => customerIds.Contains(c.Id))
            .Select(c => c.Id).ToListAsync(cancellationToken);
        var missing = customerIds.Except(existingCustomerIds).ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException($"Account seed müşterileri bulunamadı: {string.Join(", ", missing)}. Önce 'seed customers' çalıştırın.");

        var ids = definitions.Select(a => a.Id).ToArray();
        var numbers = definitions.Select(a => a.AccountNumber).ToArray();
        var existing = await db.Accounts.AsNoTracking()
            .Where(a => ids.Contains(a.Id) || numbers.Contains(a.AccountNumber))
            .ToListAsync(cancellationToken);
        var additions = new List<Account>();
        foreach (var definition in definitions)
        {
            var sameId = existing.SingleOrDefault(a => a.Id == definition.Id);
            if (sameId is not null)
            {
                if (sameId.CustomerId != definition.CustomerId || sameId.AccountNumber != definition.AccountNumber ||
                    sameId.CurrencyCode != definition.CurrencyCode || sameId.AccountName != definition.AccountName ||
                    sameId.AccountType != definition.AccountType)
                    throw new InvalidOperationException($"Account Id {definition.Id} mevcut fakat seed sahiplik/numara/para birimi/ad/tür bilgileriyle çelişiyor.");
                // Existing balances and timestamps belong to persisted history; never reset them.
                continue;
            }
            if (existing.Any(a => a.AccountNumber == definition.AccountNumber))
                throw new InvalidOperationException($"AccountNumber '{definition.AccountNumber}' farklı bir Id ile mevcut.");
            additions.Add(definition);
        }

        db.Accounts.AddRange(additions);
        await db.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
            await transaction.CommitAsync(cancellationToken);
        return additions.Count;
    }

    private static Guid Customer(int number) => Guid.Parse($"10000000-0000-0000-0000-{number:D12}");
}
