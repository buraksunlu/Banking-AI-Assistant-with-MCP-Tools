using BankingAI.Application.Investment;
using Xunit;

namespace BankingAI.Application.Tests;

// INV-01 PR1 without a database: price picking, the shared engine and the asset row. Prices are the real TCMB/gold rows of
// 1 September and 9 October 2026 (10 October is a Saturday).
public sealed class WhatIfMathTests
{
    private static readonly MarketAssetInfo Usd = new(1, "USDTRY", "ABD Doları / Türk Lirası", "USD");
    private static readonly MarketAssetInfo Gold = new(3, "GRAM_GOLD_TRY", "Gram Altın / Türk Lirası", "GRAM");

    private static readonly DateOnly May25 = new(2026, 5, 25);
    private static readonly DateOnly June1 = new(2026, 6, 1);

    private static IReadOnlyList<MarketPrice> Prices(params (DateOnly Date, decimal Price)[] rows) =>
        rows.Select(r => new MarketPrice(r.Date, r.Price, "TCMB")).ToList();

    // ---- OC-02: price picking

    [Fact]
    public void The_price_of_the_day_itself_is_taken()
    {
        var point = MarketPriceReader.Pick(June1, Prices((May25, 45.6312m), (June1, 46.1m)));

        Assert.Equal((June1, (DateOnly?)June1, (decimal?)46.1m, PriceStatus.Ok, "TCMB"),
            (point.RequestedDate, point.PriceDate, point.Price, point.Status, point.Source));
    }

    [Fact]
    public void A_day_without_a_price_takes_the_nearest_earlier_day()
    {
        // 26–31 May 2026 is the longest gap in the data (Kurban Bayramı).
        var point = MarketPriceReader.Pick(new DateOnly(2026, 5, 31), Prices((May25, 45.6312m), (June1, 46.1m)));

        Assert.Equal(((DateOnly?)May25, (decimal?)45.6312m, PriceStatus.Ok), (point.PriceDate, point.Price, point.Status));
    }

    [Fact]
    public void A_price_ten_days_old_is_still_usable() =>
        Assert.Equal(PriceStatus.Ok, MarketPriceReader.Pick(May25.AddDays(10), Prices((May25, 45.6312m))).Status);

    [Fact]
    public void A_price_eleven_days_old_is_stale_but_still_reported()
    {
        var point = MarketPriceReader.Pick(May25.AddDays(11), Prices((May25, 45.6312m)));

        Assert.Equal(((DateOnly?)May25, (decimal?)45.6312m, PriceStatus.Stale), (point.PriceDate, point.Price, point.Status));
    }

    [Fact]
    public void A_day_before_every_price_is_missing()
    {
        var point = MarketPriceReader.Pick(new DateOnly(2024, 11, 10), Prices((new DateOnly(2024, 11, 11), 34.3m)));

        Assert.Equal(((DateOnly?)null, (decimal?)null, PriceStatus.Missing, (string?)null),
            (point.PriceDate, point.Price, point.Status, point.Source));
    }

    [Fact]
    public void A_later_price_is_never_used_for_an_earlier_day() =>
        Assert.Equal(PriceStatus.Missing, MarketPriceReader.Pick(May25, Prices((June1, 46.1m))).Status);

    // ---- OC-01: the shared engine

    [Fact]
    public void Units_value_and_difference_come_from_the_exact_units()
    {
        // 2.253,50 / 48,1891 = 46,763686… USD; × 49,1274 = 2.297,377… → 2.297,38; +43,88 = +%1,947… → 1,95.
        Assert.Equal((46.763687m, 2297.38m, 43.88m, 1.95m), WhatIfMath.OneOff(2253.50m, 48.1891m, 49.1274m));
    }

    [Fact]
    public void A_loss_is_reported_as_a_negative_difference() =>
        // Gram gold fell between 1 September and 9 October 2026: 0,331896 g → 2.194,89 TL, −58,61 = −%2,60.
        Assert.Equal((0.331896m, 2194.89m, -58.61m, -2.60m), WhatIfMath.OneOff(2253.50m, 6789.78069267m, 6613.19001236m));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void The_amount_must_be_positive(int amount) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => WhatIfMath.OneOff(amount, 48m, 49m));

    // ---- OC-06/07: one asset row

    [Fact]
    public void An_asset_row_carries_the_days_the_prices_and_the_figures()
    {
        var row = WhatIfMath.Asset(Usd, 2253.50m,
            new PricePoint(new(2026, 9, 1), new(2026, 9, 1), 48.1891m, PriceStatus.Ok, "TCMB"),
            new PricePoint(new(2026, 10, 10), new(2026, 10, 9), 49.1274m, PriceStatus.Ok, "TCMB"));

        Assert.Equal(
            ("USDTRY", "USD", new DateOnly(2026, 9, 1), (DateOnly?)new DateOnly(2026, 9, 1), (decimal?)48.1891m,
             new DateOnly(2026, 10, 10), (DateOnly?)new DateOnly(2026, 10, 9), (decimal?)49.1274m),
            (row.Symbol, row.BaseUnit, row.BuyDate, row.BuyPriceDate, row.BuyPrice, row.ValueDate, row.ValuePriceDate, row.ValuePrice));
        Assert.Equal(((decimal?)46.763687m, (decimal?)2297.38m, (decimal?)43.88m, (decimal?)1.95m, PriceStatus.Ok, "TCMB"),
            (row.Units, row.Value, row.Difference, row.DifferencePercent, row.Status, row.Source));
    }

    [Theory]
    [InlineData(PriceStatus.Stale, PriceStatus.Ok, PriceStatus.Stale)]
    [InlineData(PriceStatus.Ok, PriceStatus.Stale, PriceStatus.Stale)]
    [InlineData(PriceStatus.Missing, PriceStatus.Stale, PriceStatus.Missing)]
    [InlineData(PriceStatus.Stale, PriceStatus.Missing, PriceStatus.Missing)]
    public void Without_two_usable_prices_there_are_no_figures_and_the_worse_status_wins(
        PriceStatus buy, PriceStatus value, PriceStatus expected)
    {
        static PricePoint Point(PriceStatus status, DateOnly day) => status == PriceStatus.Missing
            ? new PricePoint(day, null, null, status, null)
            : new PricePoint(day, day.AddDays(-12), 6700m, status, "GOLDAPI+TCMB");

        var row = WhatIfMath.Asset(Gold, 1000m, Point(buy, new(2026, 9, 1)), Point(value, new(2026, 10, 10)));

        Assert.Equal(expected, row.Status);
        Assert.Equal(((decimal?)null, (decimal?)null, (decimal?)null, (decimal?)null),
            (row.Units, row.Value, row.Difference, row.DifferencePercent));
    }

    // ---- OC-09: the fixed warning

    [Fact]
    public void The_warning_says_what_the_figures_are_not() =>
        Assert.Equal(
            "Geçmiş fiyatlarla yapılmış bir hesaptır; gelecekteki getiriyi göstermez ve yatırım tavsiyesi değildir. "
            + "Alış-satış farkı, vergi, komisyon, faiz ve enflasyon dahil değildir.",
            WhatIfMath.Disclaimer);
}
