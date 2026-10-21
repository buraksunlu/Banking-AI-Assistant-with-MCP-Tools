using BankingAI.Data;
using BankingAI.Data.Seeding;
using BankingAI.Data.Security;
using Microsoft.EntityFrameworkCore;

var isSeedCategories = args.Length == 2 && args[0] == "seed" && args[1] == "categories";
var isSeedMarketAssets = args.Length == 2 && args[0] == "seed" && args[1] == "market-assets";
var isSeedCustomers = args.Length == 2 && args[0] == "seed" && args[1] == "customers";
var isSeedAccounts = args.Length == 2 && args[0] == "seed" && args[1] == "accounts";
var isSeedCredits = args.Length == 3 && args[0] == "seed" && args[1] == "transactions" && args[2] == "credits";
var isSeedSubscriptions = args.Length == 3 && args[0] == "seed" && args[1] == "transactions" && args[2] == "subscriptions";
var isSeedCategoryExamples = args.Length == 3 && args[0] == "seed" && args[1] == "transactions" && args[2] == "category-examples";
var isSeedGroceryGrowth = args.Length == 3 && args[0] == "seed" && args[1] == "transactions" && args[2] == "grocery-growth";
var isSeedCategorizationBoundaries = args.Length == 3 && args[0] == "seed" && args[1] == "transactions" && args[2] == "categorization-boundaries";
var isSeedRecentSpending = args.Length == 3 && args[0] == "seed" && args[1] == "transactions" && args[2] == "recent-spending";
var isSeedDemoCoverage = args.Length == 3 && args[0] == "seed" && args[1] == "transactions" && args[2] == "demo-coverage";
var isSeedPairs = args.Length == 3 && args[0] == "seed" && args[1] == "transactions" && args[2] == "pairs";
var isSeedRealistic = args.Length == 3 && args[0] == "seed" && args[1] == "transactions" && args[2] == "realistic";
// users set-password <username> [--customer <customer-id>]
var isSetPassword = args.Length is 3 or 5 && args[0] == "users" && args[1] == "set-password"
    && (args.Length == 3 || args[3] == "--customer");
var isSingleCommand = args.Length == 1 && (args[0] == "migrate" || args[0] == "show");
if (!isSingleCommand && !isSeedCategories && !isSeedMarketAssets && !isSeedCustomers && !isSeedAccounts && !isSeedCredits && !isSeedSubscriptions && !isSeedCategoryExamples && !isSeedGroceryGrowth && !isSeedCategorizationBoundaries && !isSeedRecentSpending && !isSeedDemoCoverage && !isSeedPairs && !isSeedRealistic && !isSetPassword)
{
    Console.WriteLine("Usage: BankingAI.Cli migrate | show | seed categories | seed market-assets | seed customers | seed accounts | seed transactions credits | seed transactions subscriptions | seed transactions category-examples | seed transactions grocery-growth | seed transactions categorization-boundaries | seed transactions recent-spending | seed transactions demo-coverage | seed transactions pairs | seed transactions realistic | users set-password <username> [--customer <customer-id>]");
    return 1;
}

if (isSetPassword && args.Length == 5 && !Guid.TryParse(args[4], out _))
{
    Console.WriteLine("--customer bir müşteri GUID'i olmalı.");
    return 1;
}

await using var db = new BankingDbContext();

if (args[0] == "migrate")
{
    // Apply only migrations already present in this project.
    await db.Database.MigrateAsync();
    Console.WriteLine("Database migrations applied.");
}
else if (isSetPassword)
{
    // The password is read twice and never echoed or printed.
    var password = ReadPassword("Şifre: ");
    if (password != ReadPassword("Şifre (tekrar): "))
    {
        Console.WriteLine("Şifreler eşleşmiyor; hiçbir şey değişmedi.");
        return 1;
    }

    Guid? customerId = args.Length == 5 ? Guid.Parse(args[4]) : null;
    var change = await CredentialStore.SetPasswordAsync(db, args[2], password, customerId);
    Console.WriteLine($"Şifre ayarlandı: '{PasswordCredentials.NormalizeUsername(args[2])}' ({(change == CredentialChange.Created ? "yeni kullanıcı" : "güncellendi")}).");
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
else if (isSeedCustomers)
{
    var added = await SyntheticCustomerSeeder.SeedAsync(db);
    Console.WriteLine($"Synthetic customers seeded. Added: {added}; unchanged: {SyntheticCustomerSeeder.CustomerCount - added}.");
}
else if (isSeedAccounts)
{
    var added = await SyntheticAccountSeeder.SeedAsync(db);
    Console.WriteLine($"Synthetic accounts seeded. Added: {added}; unchanged: {SyntheticAccountSeeder.AccountCount - added}.");
}
else if (isSeedCredits)
{
    var added = await SyntheticTransactionSeeder.SeedCreditsAsync(db);
    Console.WriteLine($"Synthetic Credit transactions seeded. Added: {added}; unchanged: {SyntheticTransactionSeeder.CreditCount - added}.");
}
else if (isSeedSubscriptions)
{
    var added = await SyntheticTransactionSeeder.SeedSubscriptionsAsync(db);
    Console.WriteLine($"Synthetic subscription transactions seeded. Added: {added}; unchanged: {SyntheticTransactionSeeder.SubscriptionCount - added}.");
}
else if (isSeedCategoryExamples)
{
    var added = await SyntheticTransactionSeeder.SeedCategoryExamplesAsync(db);
    Console.WriteLine($"Synthetic category examples seeded. Added: {added}; unchanged: {SyntheticTransactionSeeder.CategoryExampleCount - added}.");
}
else if (isSeedGroceryGrowth)
{
    var added = await SyntheticTransactionSeeder.SeedGroceryGrowthAsync(db);
    Console.WriteLine($"Synthetic grocery growth seeded. Added: {added}; unchanged: {SyntheticTransactionSeeder.GroceryGrowthCount - added}.");
}
else if (isSeedCategorizationBoundaries)
{
    var added = await SyntheticTransactionSeeder.SeedCategorizationBoundariesAsync(db);
    Console.WriteLine($"Synthetic categorization boundary transactions seeded. Added: {added}; unchanged: {SyntheticTransactionSeeder.CategorizationBoundaryCount - added}.");
}
else if (isSeedRecentSpending)
{
    var added = await SyntheticTransactionSeeder.SeedRecentSpendingAsync(db);
    Console.WriteLine($"Synthetic recent spending seeded. Added: {added}; unchanged: {SyntheticTransactionSeeder.RecentSpendingCount - added}.");
}
else if (isSeedDemoCoverage)
{
    var added = await SyntheticTransactionSeeder.SeedDemoCoverageAsync(db);
    Console.WriteLine($"Synthetic demo coverage seeded. Added: {added}; unchanged: {SyntheticTransactionSeeder.DemoCoverageCount - added}.");
}
else if (isSeedPairs)
{
    var added = await SyntheticTransactionSeeder.SeedPairsAsync(db);
    Console.WriteLine($"Synthetic transfer and shop pairs seeded. Added rows: {added}; unchanged: {SyntheticTransactionSeeder.PairsCount - added}.");
}
else if (isSeedRealistic)
{
    // SYN-15: a year for the six new personas; needs 'seed customers' and 'seed accounts' first.
    var total = SyntheticTransactionSeeder.RealisticDefinitions().Count;
    var added = await SyntheticTransactionSeeder.SeedRealisticAsync(db);
    Console.WriteLine($"Realistic year seeded. Added rows: {added}; unchanged: {total - added}.");
}
else
{
    var added = await MarketAssetSeeder.SeedAsync(db);
    Console.WriteLine($"Market assets seeded. Added: {added}; unchanged: {3 - added}.");
}

return 0;

// From a terminal: hidden typing. From a pipe: one line per prompt (used by scripts).
static string ReadPassword(string prompt)
{
    if (Console.IsInputRedirected)
        return Console.ReadLine() ?? "";

    Console.Write(prompt);
    var password = new System.Text.StringBuilder();
    while (true)
    {
        var key = Console.ReadKey(intercept: true);
        if (key.Key == ConsoleKey.Enter)
            break;
        if (key.Key == ConsoleKey.Backspace)
        {
            if (password.Length > 0)
                password.Length--;
            continue;
        }
        if (!char.IsControl(key.KeyChar))
            password.Append(key.KeyChar);
    }
    Console.WriteLine();
    return password.ToString();
}
