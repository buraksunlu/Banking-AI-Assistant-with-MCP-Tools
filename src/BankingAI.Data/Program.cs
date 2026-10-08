using BankingAI.Data;
using Microsoft.EntityFrameworkCore;

if (args.Length != 1 || (args[0] != "migrate" && args[0] != "show"))
{
    Console.WriteLine("Usage: BankingAI.Data migrate | show");
    return 1;
}

await using var db = new BankingDbContext();

if (args[0] == "migrate")
{
    // Apply only migrations already present in this project.
    await db.Database.MigrateAsync();
    Console.WriteLine("Database migrations applied.");
}
else
{
    // Read a small sample without tracking entities for updates.
    var customers = await db.Customers.AsNoTracking()
        .OrderBy(c => c.Id).Take(10).ToListAsync();

    Console.WriteLine("CustomerNumber | FirstName | LastName | PreferredCurrency | TimeZone | CreatedAt (UTC)");
    foreach (var customer in customers)
        Console.WriteLine($"{customer.CustomerNumber} | {customer.FirstName} | {customer.LastName} | {customer.PreferredCurrency} | {customer.TimeZone} | {customer.CreatedAt:O}");
}

return 0;
