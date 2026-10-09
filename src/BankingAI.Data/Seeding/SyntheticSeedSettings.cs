using Bogus;

namespace BankingAI.Data.Seeding;

/// <summary>
/// Synthetic dataset üretiminin değişmeyen girdilerini tutar.
/// Bu sınıf tek başına veritabanına kayıt yazmaz.
/// </summary>
public static class SyntheticSeedSettings
{
    public const int RandomSeed = 20261009;
    public const string DatasetVersion = "synthetic-v1";

    public static readonly DateTimeOffset ReferenceUtc =
        new(2025, 10, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Her üretim çağrısı için bağımsız, aynı başlangıçtan ilerleyen Faker verir.
    /// </summary>
    public static Faker<T> CreateFaker<T>() where T : class =>
        new Faker<T>("tr")
            .UseSeed(RandomSeed)
            .UseDateTimeReference(ReferenceUtc.UtcDateTime);
}
