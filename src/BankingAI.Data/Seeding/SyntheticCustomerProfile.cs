using System.Collections.Immutable;

namespace BankingAI.Data.Seeding;

/// <summary>
/// Synthetic müşteri üretiminde kullanılacak davranış kalıbını tanımlar.
/// Bu sınıf müşteri veya transaction kaydı oluşturmaz.
/// </summary>
public sealed record SyntheticCustomerProfile
{
    public required string Code { get; init; }
    public required string Description { get; init; }
    public required ImmutableArray<string> PrimaryDebitCategoryCodes { get; init; }
    public required ImmutableArray<string> SecondaryDebitCategoryCodes { get; init; }
    public required ImmutableArray<string> RecurringMerchantNames { get; init; }
    public required ImmutableArray<string> OneOffMerchantNames { get; init; }
}
