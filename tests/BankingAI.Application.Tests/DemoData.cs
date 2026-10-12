namespace BankingAI.Application.Tests;

// Fixed identities of the seeded demo dataset (SyntheticCustomerSeeder / SyntheticAccountSeeder) and
// the demo reference moment. One place, so a seed change is updated once instead of in every test file.
internal static class DemoData
{
    // 10 Ekim 2026 15:00 Istanbul (UTC+3); all hand-computed expectations assume this "now".
    public static readonly DateTimeOffset DemoNowUtc = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);

    // Customers (10000000-…)
    public static readonly Guid Ada = Guid.Parse("10000000-0000-0000-0000-000000000001");
    public static readonly Guid Deniz = Guid.Parse("10000000-0000-0000-0000-000000000002");
    public static readonly Guid Ege = Guid.Parse("10000000-0000-0000-0000-000000000003");

    // The first demo customer (DONE-09); it has no accounts, so every analysis must be empty.
    public static readonly Guid EmptyCustomer = Guid.Parse("11111111-1111-1111-1111-111111111111");

    // Accounts (20000000-…)
    public static readonly Guid AdaTryAccount = Guid.Parse("20000000-0000-0000-0000-000000000001");
    public static readonly Guid AdaUsdAccount = Guid.Parse("20000000-0000-0000-0000-000000000002");
    public static readonly Guid AdaEmptyAccount = Guid.Parse("20000000-0000-0000-0000-000000000005");
    public static readonly Guid DenizTryAccount = Guid.Parse("20000000-0000-0000-0000-000000000003");
    public static readonly Guid EgeEurAccount = Guid.Parse("20000000-0000-0000-0000-000000000004");
    public static readonly Guid EgeTryAccount = Guid.Parse("20000000-0000-0000-0000-000000000006");
    public static readonly Guid DenizShopAccount = Guid.Parse("20000000-0000-0000-0000-000000000007"); // Business
}
