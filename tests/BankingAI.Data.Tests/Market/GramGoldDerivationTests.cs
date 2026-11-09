using System.Globalization;
using BankingAI.Data.Market;
using Xunit;

namespace BankingAI.Data.Tests.Market;

public class GramGoldDerivationTests
{
    private static readonly DateOnly Fri = new(2025, 10, 10);
    private static readonly DateOnly Mon = new(2025, 10, 13);
    private static readonly DateOnly Tue = new(2025, 10, 14);
    private static readonly DateOnly Wed = new(2025, 10, 15);

    [Theory]
    // Real gold-api ounce and TCMB USDTRY values; expected TRY per gram computed by hand to 8 decimals.
    [InlineData("2649.749750", "34.2869", "2920.95013324")]   // 2024-11-11
    [InlineData("3076.235024", "37.7656", "3735.14067798")]   // 2025-03-28
    [InlineData("3991.712202", "41.6388", "5343.77899633")]   // 2025-10-10 (…99632871… → rounds up)
    [InlineData("4186.934422", "49.1274", "6613.19001236")]   // 2026-10-09
    public void Gram_price_is_ounce_times_usdtry_over_grams_per_ounce(string ounce, string usd, string expected) =>
        Assert.Equal(M(expected), GramGoldDerivation.GramPrice(M(ounce), M(usd)));

    [Fact]
    public void Rounding_to_eight_decimals_is_half_to_even()
    {
        // 31.1034768 × 1.000000005 ÷ 31.1034768 = 1.000000005 exactly: a tie that goes to the even 8th decimal.
        Assert.Equal(1.00000000m, GramGoldDerivation.GramPrice(31.1034768m, 1.000000005m));
        Assert.Equal(1.00000002m, GramGoldDerivation.GramPrice(31.1034768m, 1.000000015m));
    }

    [Fact]
    public void Only_usdtry_days_get_a_row_and_a_missing_ounce_is_reported()
    {
        var ounces = new[]
        {
            new OuncePrice(Fri, 3991.712202m),
            new OuncePrice(new(2025, 10, 11), 4000m),   // Saturday: no USDTRY, no row
            new OuncePrice(Tue, 4100m)
        };
        var usd = new Dictionary<DateOnly, decimal> { [Fri] = 41.6388m, [Mon] = 41.70m, [Tue] = 41.80m };

        var result = GramGoldDerivation.Derive(ounces, usd);

        Assert.Equal(
            [new MarketPriceRow("GRAM_GOLD_TRY", Fri, 5343.77899633m, "GOLDAPI+TCMB"),
             new MarketPriceRow("GRAM_GOLD_TRY", Tue, GramGoldDerivation.GramPrice(4100m, 41.80m), "GOLDAPI+TCMB")],
            result.Rows);
        Assert.Equal([Mon], result.MissingOunceDays);
        Assert.Empty(result.Watch);
        Assert.Empty(result.Suspicious);
    }

    [Fact]
    public void A_move_above_ten_percent_is_written_and_listed()
    {
        var result = GramGoldDerivation.Derive(
            [new OuncePrice(Fri, 4000m), new OuncePrice(Mon, 3548.8m)],   // −11.28%, like 18 → 23 March 2026
            Usd(Fri, Mon));

        Assert.Equal(2, result.Rows.Count);
        Assert.Equal([new OunceJump(Mon, Fri, -0.1128m)], result.Watch);
        Assert.Empty(result.Suspicious);
    }

    [Theory]
    [InlineData("4400", false, false)]    // exactly +10%: not listed
    [InlineData("4400.04", true, false)]  // just above 10%
    [InlineData("5000", true, false)]     // exactly +25%: still written
    [InlineData("5000.04", false, true)]  // just above 25%
    public void Thresholds_are_strictly_greater_than(string monday, bool watch, bool suspicious)
    {
        var result = GramGoldDerivation.Derive(
            [new OuncePrice(Fri, 4000m), new OuncePrice(Mon, M(monday))], Usd(Fri, Mon));

        Assert.Equal(watch, result.Watch.Count == 1);
        Assert.Equal(suspicious, result.Suspicious.Count == 1);
        Assert.Equal(suspicious ? 1 : 2, result.Rows.Count);
    }

    [Fact]
    public void Exactly_minus_25_is_written()
    {
        var result = GramGoldDerivation.Derive([new OuncePrice(Fri, 4000m), new OuncePrice(Mon, 3000m)], Usd(Fri, Mon));

        Assert.Equal(2, result.Rows.Count);
        Assert.Equal(-0.25m, Assert.Single(result.Watch).Change);
        Assert.Empty(result.Suspicious);
    }

    [Fact]
    public void A_unit_mix_up_is_not_written_and_the_next_good_day_compares_with_the_last_accepted_one()
    {
        // Monday arrives in USD per gram (÷ 31.1): far beyond 25%. Tuesday is normal again.
        var result = GramGoldDerivation.Derive(
            [new OuncePrice(Fri, 4000m), new OuncePrice(Mon, 128.6m), new OuncePrice(Tue, 4040m)],
            Usd(Fri, Mon, Tue));

        Assert.Equal([Fri, Tue], result.Rows.Select(r => r.PriceDate));
        var suspicious = Assert.Single(result.Suspicious);
        Assert.Equal((Mon, Fri), (suspicious.Day, suspicious.ComparedWith));
        Assert.Empty(result.Watch);   // Tuesday +1% against Friday, not +3041% against the bad Monday
    }

    [Fact]
    public void The_comparison_skips_days_without_an_ounce()
    {
        var result = GramGoldDerivation.Derive(
            [new OuncePrice(Fri, 4000m), new OuncePrice(Wed, 4500m)], Usd(Fri, Mon, Tue, Wed));

        Assert.Equal([new OunceJump(Wed, Fri, 0.125m)], result.Watch);
        Assert.Equal([Mon, Tue], result.MissingOunceDays);
    }

    [Theory]
    // TCMB's own gram gold (XAU, 11:00, until 30 Jan 2026) on real days; the derived reference must stay within 3%
    // (K5-A). Measured on 21 days: mean 0.53%, worst 2.45% on 30 Jan 2026.
    [InlineData("2649.749750", "34.2869", "2949.39")]   // 2024-11-11
    [InlineData("3076.235024", "37.7656", "3762.1")]    // 2025-03-28
    [InlineData("3991.712202", "41.6388", "5340.87")]   // 2025-10-10
    [InlineData("5459.293248", "43.3443", "7745.09")]   // 2026-01-29
    [InlineData("5072.572085", "43.3414", "7245.68")]   // 2026-01-30
    public void Derived_value_stays_within_three_percent_of_tcmbs_official_gram_gold(string ounce, string usd, string official)
    {
        var derived = GramGoldDerivation.GramPrice(M(ounce), M(usd));
        var tcmb = M(official);

        Assert.InRange(Math.Abs(derived / tcmb - 1m), 0m, 0.03m);
    }

    private static decimal M(string text) => decimal.Parse(text, CultureInfo.InvariantCulture);

    private static Dictionary<DateOnly, decimal> Usd(params DateOnly[] days) => days.ToDictionary(d => d, _ => 40m);
}
