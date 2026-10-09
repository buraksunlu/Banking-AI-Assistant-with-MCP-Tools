using BankingAI.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BankingAI.Data.Seeding;

public static class SyntheticCustomerSeeder
{
    private const long SeedLockKey = 2026100905;

    private static readonly (Guid Id, string FirstName, string LastName, string PreferredCurrency, string TimeZone)[] Definitions =
    [
        (new Guid("10000000-0000-0000-0000-000000000001"), "Ada", "Kahve", "TRY", "Europe/Istanbul"),
        (new Guid("10000000-0000-0000-0000-000000000002"), "Deniz", "Market", "TRY", "Europe/Istanbul"),
        (new Guid("10000000-0000-0000-0000-000000000003"), "Ege", "Yolcu", "EUR", "Europe/Istanbul")
    ];

    public static async Task<int> SeedAsync(BankingDbContext db, CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            $"SELECT pg_advisory_xact_lock({SeedLockKey})", cancellationToken);

        var existing = await db.Customers
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var byId = existing.ToDictionary(customer => customer.Id);
        var additions = new List<Customer>();

        foreach (var definition in Definitions)
        {
            if (byId.TryGetValue(definition.Id, out var existingCustomer))
            {
                if (!Matches(existingCustomer, definition))
                    throw new InvalidOperationException(
                        $"Customer Id {definition.Id} mevcut fakat synthetic seed değeriyle çelişiyor.");

                continue;
            }

            additions.Add(new Customer
            {
                Id = definition.Id,
                FirstName = definition.FirstName,
                LastName = definition.LastName,
                PreferredCurrency = definition.PreferredCurrency,
                TimeZone = definition.TimeZone
            });
        }

        if (additions.Count > 0)
            await db.Customers.AddRangeAsync(additions, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return additions.Count;
    }

    private static bool Matches(
        Customer customer,
        (Guid Id, string FirstName, string LastName, string PreferredCurrency, string TimeZone) definition) =>
        customer.FirstName == definition.FirstName &&
        customer.LastName == definition.LastName &&
        customer.PreferredCurrency == definition.PreferredCurrency &&
        customer.TimeZone == definition.TimeZone;
}
