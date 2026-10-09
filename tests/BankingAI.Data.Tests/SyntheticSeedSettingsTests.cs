using BankingAI.Data.Seeding;
using Xunit;

namespace BankingAI.Data.Tests;

public sealed class SyntheticSeedSettingsTests
{
    private sealed class Sample
    {
        public int Value { get; set; }
    }

    [Fact]
    public void Faker_produces_the_same_sequence_for_the_same_seed()
    {
        var first = SyntheticSeedSettings.CreateFaker<Sample>()
            .RuleFor(x => x.Value, f => f.Random.Int());
        var second = SyntheticSeedSettings.CreateFaker<Sample>()
            .RuleFor(x => x.Value, f => f.Random.Int());

        var firstValues = first.Generate(5)
            .Select(x => x.Value)
            .ToArray();
        var secondValues = second.Generate(5)
            .Select(x => x.Value)
            .ToArray();

        Assert.Equal(firstValues, secondValues);
    }

    [Fact]
    public void Reference_date_and_dataset_version_are_fixed()
    {
        Assert.Equal(2025, SyntheticSeedSettings.ReferenceUtc.Year);
        Assert.Equal(TimeSpan.Zero, SyntheticSeedSettings.ReferenceUtc.Offset);
        Assert.Equal("synthetic-v1", SyntheticSeedSettings.DatasetVersion);
    }
}
