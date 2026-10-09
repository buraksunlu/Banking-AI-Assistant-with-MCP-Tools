using BankingAI.Data.Seeding;
using Xunit;

namespace BankingAI.Data.Tests;

public sealed class SyntheticCustomerProfileTests
{
    private static readonly HashSet<string> KnownCategoryCodes =
    [
        "FOOD", "GROCERY", "CLOTHING", "TRANSPORTATION", "ENTERTAINMENT",
        "SUBSCRIPTION", "TRAVEL", "FUEL", "BILLS", "HEALTH", "OTHER"
    ];

    [Fact]
    public void Catalog_contains_three_distinct_controlled_profiles()
    {
        var profiles = SyntheticCustomerProfileCatalog.All;

        Assert.Equal(3, profiles.Length);
        Assert.Equal(3, profiles.Select(profile => profile.Code).Distinct(StringComparer.Ordinal).Count());
        Assert.All(profiles, profile => Assert.NotEmpty(profile.Description));
    }

    [Fact]
    public void Profile_categories_are_existing_lookup_codes()
    {
        foreach (var profile in SyntheticCustomerProfileCatalog.All)
        {
            var categories = profile.PrimaryDebitCategoryCodes
                .Concat(profile.SecondaryDebitCategoryCodes);

            Assert.All(categories, category => Assert.Contains(category, KnownCategoryCodes));
        }
    }

    [Fact]
    public void Profiles_define_only_completed_debit_patterns()
    {
        var forbiddenWords = new[] { "transfer", "refund", "nakit", "yatırım", "bekleyen" };

        foreach (var profile in SyntheticCustomerProfileCatalog.All)
        {
            var text = $"{profile.Code} {profile.Description}".ToLowerInvariant();
            Assert.DoesNotContain(forbiddenWords, word => text.Contains(word, StringComparison.Ordinal));
        }
    }
}
