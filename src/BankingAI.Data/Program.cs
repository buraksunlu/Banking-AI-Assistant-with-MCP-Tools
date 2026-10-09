using BankingAI.Data;
using BankingAI.Data.Seeding;
using Microsoft.EntityFrameworkCore;

var isSeedCategories = args.Length == 2 && args[0] == "seed" && args[1] == "categories";
var isSeedMarketAssets = args.Length == 2 && args[0] == "seed" && args[1] == "market-assets";
var isSingleCommand = args.Length == 1 && (args[0] == "migrate" || args[0] == "show");
if (!isSingleCommand && !isSeedCategories && !isSeedMarketAssets)
{
    Console.WriteLine("Usage: BankingAI.Data migrate | show | seed categories | seed market-assets");
    return 1;
}

await using var db = new BankingDbContext();

if (args[0] == "migrate")
{
    // Apply only migrations already present in this project.
    await db.Database.MigrateAsync();
    Console.WriteLine("Database migrations applied.");
}
else if (args[0] == "show")
{
    // Read a small sample without tracking entities for updates.
    var customers = await db.Customers.AsNoTracking()
        .OrderBy(c => c.Id).Take(10).ToListAsync();

    Console.WriteLine("CustomerNumber | FirstName | LastName | PreferredCurrency | TimeZone | CreatedAt (UTC)");
    foreach (var customer in customers)
        Console.WriteLine($"{customer.CustomerNumber} | {customer.FirstName} | {customer.LastName} | {customer.PreferredCurrency} | {customer.TimeZone} | {customer.CreatedAt:O}");
}
else if (isSeedCategories)
{
    var added = await TransactionCategorySeeder.SeedAsync(db);
    Console.WriteLine($"Transaction categories seeded. Added: {added}; unchanged: {11 - added}.");
}
else
{
    var added = await MarketAssetSeeder.SeedAsync(db);
    Console.WriteLine($"Market assets seeded. Added: {added}; unchanged: {3 - added}.");
}

return 0;
