using BankingAI.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BankingAI.Data.Seeding;

public static class SyntheticCustomerSeeder
{
    private const long SeedLockKey = 2026100905;

    /// <summary>How many customers the seed defines (the three demo customers and the six SYN-15 personas).</summary>
    public static int CustomerCount => Definitions.Length;

    private static readonly (Guid Id, string FirstName, string LastName, string PreferredCurrency, string TimeZone)[] Definitions =
    [
        (new Guid("10000000-0000-0000-0000-000000000001"), "Ada", "Kahve", "TRY", "Europe/Istanbul"),
        (new Guid("10000000-0000-0000-0000-000000000002"), "Deniz", "Market", "TRY", "Europe/Istanbul"),
        (new Guid("10000000-0000-0000-0000-000000000003"), "Ege", "Yolcu", "EUR", "Europe/Istanbul"),
        // SYN-15: realistic personas with a year of data; the three above stay the hand-computed demo.
        (new Guid("10000000-0000-0000-0000-000000000004"), "Selin", "Kampüs", "TRY", "Europe/Istanbul"),
        (new Guid("10000000-0000-0000-0000-000000000005"), "Murat", "Aile", "TRY", "Europe/Istanbul"),
        (new Guid("10000000-0000-0000-0000-000000000006"), "Zeynep", "Serbest", "TRY", "Europe/Istanbul"),
        (new Guid("10000000-0000-0000-0000-000000000007"), "Hasan", "Emekli", "TRY", "Europe/Istanbul"),
        // Lives in Berlin: her months begin an hour earlier in UTC than an Istanbul customer's (month-boundary cases).
        (new Guid("10000000-0000-0000-0000-000000000008"), "Elif", "Berlin", "EUR", "Europe/Berlin"),
        (new Guid("10000000-0000-0000-0000-000000000009"), "Can", "Kırtasiye", "TRY", "Europe/Istanbul")
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
