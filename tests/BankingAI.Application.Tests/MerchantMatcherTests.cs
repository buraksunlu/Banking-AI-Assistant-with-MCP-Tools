using BankingAI.Application.Analytics;
using Xunit;

namespace BankingAI.Application.Tests;

// Pure matching rules; no database. The model's merchant text is untrusted input.
public sealed class MerchantMatcherTests
{
    private static readonly string[] Known =
    [
        "Starbucks", "Netflix", "Spotify", "Kahve Durağı", "Hobi Dükkânı", "Aile Marketi",
        "Şehir Ulaşım", "Şehirlerarası Otobüs"
    ];

    [Theory]
    [InlineData("starbucks", "Starbucks")]
    [InlineData("STARBUCKS", "Starbucks")]
    [InlineData("  Starbucks ", "Starbucks")]
    [InlineData("kahve duragi", "Kahve Durağı")]
    [InlineData("KAHVE   DURAĞI", "Kahve Durağı")]
    [InlineData("hobi dukkani", "Hobi Dükkânı")]
    [InlineData("aile marketi", "Aile Marketi")]
    [InlineData("AİLE MARKETİ", "Aile Marketi")]
    [InlineData("AILE MARKETI", "Aile Marketi")]
    public void Exact_match_ignores_case_spacing_and_turkish_letters(string requested, string expected)
    {
        var match = MerchantMatcher.Match(requested, Known);

        Assert.Equal((MerchantMatchStatus.Matched, expected, false), (match.Status, match.Merchant, match.IsPartialMatch));
        Assert.Empty(match.Candidates);
    }

    [Fact]
    public void Fold_does_not_depend_on_the_machine_culture()
    {
        // Under tr-TR, "I".ToLower() is "ı"; folding must give the same key in every culture.
        var original = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("tr-TR");
            var turkish = MerchantMatcher.Fold("Aile Marketi");
            Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
            Assert.Equal(turkish, MerchantMatcher.Fold("AILE MARKETI"));
            Assert.Equal("AILE MARKETI", turkish);
        }
        finally { Thread.CurrentThread.CurrentCulture = original; }
    }

    [Fact]
    public void Single_containing_name_is_a_partial_match()
    {
        var match = MerchantMatcher.Match("starbuck", Known);

        Assert.Equal((MerchantMatchStatus.Matched, "Starbucks", true), (match.Status, match.Merchant, match.IsPartialMatch));
    }

    [Fact]
    public void Several_containing_names_are_ambiguous_and_carry_no_merchant()
    {
        var match = MerchantMatcher.Match("şehir", Known);

        Assert.Equal(MerchantMatchStatus.Ambiguous, match.Status);
        Assert.Null(match.Merchant);
        Assert.Equal(new[] { "Şehir Ulaşım", "Şehirlerarası Otobüs" }, match.Candidates);
    }

    [Theory]
    [InlineData("Starbcks")] // typos are not auto-corrected
    [InlineData("Migros")]
    [InlineData("ne")]       // under three letters, no partial guess (would hit Netflix)
    public void Unknown_or_too_short_text_is_not_found(string requested)
    {
        var match = MerchantMatcher.Match(requested, Known);

        Assert.Equal((MerchantMatchStatus.NotFound, (string?)null), (match.Status, match.Merchant));
        Assert.Empty(match.Candidates);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void Blank_text_is_rejected(string requested) =>
        Assert.Throws<ArgumentException>(() => MerchantMatcher.Match(requested, Known));

    [Fact]
    public void Text_longer_than_the_stored_column_is_rejected() =>
        Assert.Throws<ArgumentException>(() => MerchantMatcher.Match(new string('a', 151), Known));

    // ---- MatchExact (ANA-10): only the exact folded step

    [Theory]
    [InlineData("starbucks", "Starbucks")]
    [InlineData("kahve duragi", "Kahve Durağı")]           // Turkish letters written without their marks
    [InlineData("  KAHVE   DURAĞI ", "Kahve Durağı")]       // case and spacing
    public void Exact_only_accepts_another_spelling_of_the_same_name(string requested, string expected)
    {
        var match = MerchantMatcher.MatchExact(requested, Known);

        Assert.Equal((MerchantMatchStatus.Matched, expected, false), (match.Status, match.Merchant, match.IsPartialMatch));
    }

    [Theory]
    [InlineData("Starbuck")]  // Match would accept this as a single containing name
    [InlineData("şehir")]     // Match would call this ambiguous
    [InlineData("Kahve")]
    public void Exact_only_never_completes_a_partial_name(string requested)
    {
        var match = MerchantMatcher.MatchExact(requested, Known);

        Assert.Equal((MerchantMatchStatus.NotFound, (string?)null), (match.Status, match.Merchant));
        Assert.Empty(match.Candidates);
    }

    [Fact]
    public void Exact_only_reports_names_that_fold_to_the_same_text_as_ambiguous()
    {
        var match = MerchantMatcher.MatchExact("kahve durağı", ["Kahve Durağı", "Kahve Duragi", "Starbucks"]);

        Assert.Equal(MerchantMatchStatus.Ambiguous, match.Status);
        Assert.Equal(["Kahve Duragi", "Kahve Durağı"], match.Candidates);
    }

    [Fact]
    public void Exact_only_follows_the_same_text_rule() =>
        Assert.Equal("Merchant", Assert.Throws<ArgumentException>(() => MerchantMatcher.MatchExact("   ", Known)).ParamName);
}
