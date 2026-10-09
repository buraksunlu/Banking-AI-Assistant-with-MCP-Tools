using System.Collections.Immutable;

namespace BankingAI.Data.Seeding;

/// <summary>
/// SYN-02 için izin verilen üç küçük synthetic müşteri profili.
/// Immutable katalog olduğu için çalışma zamanında ortak liste değiştirilemez.
/// </summary>
public static class SyntheticCustomerProfileCatalog
{
    public static ImmutableArray<SyntheticCustomerProfile> All { get; } =
        ImmutableArray.Create(
            new SyntheticCustomerProfile
            {
                Code = "COFFEE_SUBSCRIPTIONS",
                Description = "Kahve ve abonelik ağırlıklı; düzenli küçük ödemeler.",
                PrimaryDebitCategoryCodes = ["FOOD", "SUBSCRIPTION"],
                SecondaryDebitCategoryCodes = ["ENTERTAINMENT", "BILLS"],
                RecurringMerchantNames = ["Starbucks", "Spotify", "Netflix"],
                OneOffMerchantNames = ["Kahve Dükkanı", "Kitapçı"]
            },
            new SyntheticCustomerProfile
            {
                Code = "FAMILY_MARKET",
                Description = "Aile ve market ağırlıklı; dönemsel giyim ve sağlık harcamaları.",
                PrimaryDebitCategoryCodes = ["GROCERY", "BILLS"],
                SecondaryDebitCategoryCodes = ["CLOTHING", "HEALTH", "FOOD"],
                RecurringMerchantNames = ["Aile Marketi", "Elektrik Faturası"],
                OneOffMerchantNames = ["Çocuk Giyim", "Eczane", "Mahalle Fırını"]
            },
            new SyntheticCustomerProfile
            {
                Code = "TRANSPORT_TRAVEL",
                Description = "Ulaşım ve seyahat ağırlıklı; yakıt ve dönemsel gezi harcamaları.",
                PrimaryDebitCategoryCodes = ["TRANSPORTATION", "TRAVEL"],
                SecondaryDebitCategoryCodes = ["FUEL", "ENTERTAINMENT", "FOOD"],
                RecurringMerchantNames = ["Şehir Ulaşım", "Otopark"],
                OneOffMerchantNames = ["Akaryakıt İstasyonu", "Otel", "Seyahat Bileti"]
            });
}
