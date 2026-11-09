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

    // ---- OC-04/05: monthly saving and volatility (PR2). The real rows of every 1st from 1 Oct 2025 to 1 Oct 2026 and of 9 Oct
    // 2026 (the price of Saturday 10 October), as returned by the price reader.

    private static readonly MarketAssetInfo Eur = new(2, "EURTRY", "Euro / Türk Lirası", "EUR");

    private static readonly (DateOnly Day, DateOnly PriceDate)[] Firsts =
    [
        (new(2025, 10, 1), new(2025, 10, 1)), (new(2025, 11, 1), new(2025, 10, 31)), (new(2025, 12, 1), new(2025, 12, 1)),
        (new(2026, 1, 1), new(2025, 12, 31)), (new(2026, 2, 1), new(2026, 1, 30)), (new(2026, 3, 1), new(2026, 2, 27)),
        (new(2026, 4, 1), new(2026, 4, 1)), (new(2026, 5, 1), new(2026, 4, 30)), (new(2026, 6, 1), new(2026, 6, 1)),
        (new(2026, 7, 1), new(2026, 7, 1)), (new(2026, 8, 1), new(2026, 7, 31)), (new(2026, 9, 1), new(2026, 9, 1)),
        (new(2026, 10, 1), new(2026, 10, 1)),
    ];

    private static readonly decimal[] UsdFirsts =
        [41.513m, 41.8922m, 42.416m, 42.8457m, 43.3414m, 43.8m, 44.3938m, 44.9692m, 45.8248m, 46.5867m, 47.3452m, 48.1891m, 48.9466m];

    private static readonly decimal[] EurFirsts =
        [48.7508m, 48.462m, 49.2708m, 50.2859m, 51.699m, 51.7008m, 51.4787m, 52.5723m, 53.4042m, 53.089m, 54.4841m, 55.8769m, 55.2967m];

    private static readonly decimal[] GoldFirsts =
    [
        5166.27701575m, 5400.92775127m, 5783.06204160m, 5963.02008344m, 7068.41801573m, 7341.43300024m, 6761.18862755m,
        6659.36323437m, 6627.36793970m, 6022.29268152m, 6180.15000344m, 6789.78069267m, 6563.80267736m,
    ];

    private static readonly DateOnly Today = new(2026, 10, 10);

    private static MonthlyAssetWhatIf Monthly(MarketAssetInfo asset, decimal amount, decimal[] firsts, decimal today,
        PriceStatus status = PriceStatus.Ok, int statusAt = -1)
    {
        var points = Firsts
            .Select((f, i) => i == statusAt && status == PriceStatus.Missing
                ? new PricePoint(f.Day, null, null, status, null)
                : new PricePoint(f.Day, f.PriceDate, firsts[i], i == statusAt ? status : PriceStatus.Ok, "TCMB"))
            .ToList();
        return WhatIfMath.Monthly(asset, amount, points.Take(12).ToList(), points[12],
            new PricePoint(Today, new(2026, 10, 9), today, statusAt == 13 ? status : PriceStatus.Ok, "TCMB"));
    }

    private static (decimal?, decimal?, decimal?, decimal?) Totals(MonthlyAssetWhatIf a) =>
        (a.Units, a.Value, a.Difference, a.DifferencePercent);

    [Fact]
    public void A_thousand_lira_on_every_first_for_twelve_months_is_valued_today()
    {
        // Σ 1.000 / price over the twelve buys, × the price of 9 October; invested 12.000.
        Assert.Equal((270.702639m, 13298.92m, 1298.92m, 10.82m), Totals(Monthly(Usd, 1000m, UsdFirsts, 49.1274m)));
        Assert.Equal((232.273098m, 12800.85m, 800.85m, 6.67m), Totals(Monthly(Eur, 1000m, EurFirsts, 55.1112m)));
        Assert.Equal((1.921118m, 12704.72m, 704.72m, 5.87m), Totals(Monthly(Gold, 1000m, GoldFirsts, 6613.19001236m)));
    }

    [Fact]
    public void Selins_advice_amount_gives_the_same_percent_as_a_thousand()
    {
        var gold = Monthly(Gold, 236.18m, GoldFirsts, 6613.19001236m);

        Assert.Equal((0.453730m, 3000.60m, 166.44m, 5.87m), Totals(gold));
        Assert.Equal((2834.16m, -3.24m), (gold.Invested, gold.Volatility!.WorstMoment.ReturnPercent));
    }

    [Fact]
    public void Every_buy_row_carries_its_day_its_price_and_its_units()
    {
        var gold = Monthly(Gold, 1000m, GoldFirsts, 6613.19001236m);

        Assert.Equal(12, gold.Buys.Count);
        Assert.Equal((new DateOnly(2025, 10, 1), (DateOnly?)new DateOnly(2025, 10, 1), (decimal?)5166.27701575m, (decimal?)0.193563m, PriceStatus.Ok),
            (gold.Buys[0].BuyDate, gold.Buys[0].PriceDate, gold.Buys[0].Price, gold.Buys[0].Units, gold.Buys[0].Status));
        // 1 November 2025 is a Saturday: Friday 31 October's price.
        Assert.Equal((DateOnly?)new DateOnly(2025, 10, 31), gold.Buys[1].PriceDate);
        Assert.Equal((new DateOnly(2026, 9, 1), (decimal?)0.147280m), (gold.Buys[11].BuyDate, gold.Buys[11].Units));
        Assert.Equal(("GRAM_GOLD_TRY", "GRAM", Today, (DateOnly?)new DateOnly(2026, 10, 9), (decimal?)6613.19001236m, PriceStatus.Ok, "TCMB"),
            (gold.Symbol, gold.BaseUnit, gold.ValueDate, gold.ValuePriceDate, gold.ValuePrice, gold.Status, gold.Source));
    }

    [Fact]
    public void Gold_shows_its_worst_and_best_month_and_the_largest_fall()
    {
        var volatility = Monthly(Gold, 1000m, GoldFirsts, 6613.19001236m).Volatility!;

        // June: 1 Jun 6.627,37 → 1 Jul 6.022,29 = −%9,13; January: 5.963,02 → 7.068,42 = +%18,54.
        Assert.Equal(new MonthChange(new(2026, 6, 1), -9.13m), volatility.WorstMonth);
        Assert.Equal(new MonthChange(new(2026, 1, 1), 18.54m), volatility.BestMonth);
        // The price peak of 27 Feb (bought on 1 Mar) to the trough of 1 Jul: −%17,97.
        Assert.Equal(new Drawdown(-17.97m, new(2026, 2, 27), 7341.43300024m, new(2026, 7, 1), 6022.29268152m), volatility.MaxDrawdown);
        // The saving itself: on 1 Jul, before the July buy, 9.000 invested was worth 8.708,11.
        Assert.Equal(new RunningReturn(new(2026, 7, 1), 9000m, 8708.11m, -3.24m), volatility.WorstMoment);
    }

    [Fact]
    public void Euro_falls_at_the_end_from_its_peak_to_today()
    {
        var volatility = Monthly(Eur, 1000m, EurFirsts, 55.1112m).Volatility!;

        Assert.Equal(new MonthChange(new(2026, 9, 1), -1.04m), volatility.WorstMonth);
        Assert.Equal(new MonthChange(new(2026, 1, 1), 2.81m), volatility.BestMonth);
        // The trough is the valuation price of 9 October.
        Assert.Equal(new Drawdown(-1.37m, new(2026, 9, 1), 55.8769m, new(2026, 10, 9), 55.1112m), volatility.MaxDrawdown);
        Assert.Equal(new RunningReturn(new(2025, 11, 1), 1000m, 994.08m, -0.59m), volatility.WorstMoment);
    }

    [Fact]
    public void A_price_that_never_fell_has_a_zero_drawdown_without_days()
    {
        var volatility = Monthly(Usd, 1000m, UsdFirsts, 49.1274m).Volatility!;

        // Even the worst month rose: October 2025 +%0,91; the saving was never at a loss.
        Assert.Equal(new MonthChange(new(2025, 10, 1), 0.91m), volatility.WorstMonth);
        Assert.Equal(new MonthChange(new(2026, 5, 1), 1.90m), volatility.BestMonth);
        Assert.Equal(new Drawdown(0m, null, null, null, null), volatility.MaxDrawdown);
        Assert.Equal(new RunningReturn(new(2025, 11, 1), 1000m, 1009.13m, 0.91m), volatility.WorstMoment);
    }

    [Theory]
    [InlineData(PriceStatus.Stale, 3)]
    [InlineData(PriceStatus.Missing, 0)]
    [InlineData(PriceStatus.Stale, 12)]
    [InlineData(PriceStatus.Stale, 13)]
    public void One_unusable_price_anywhere_leaves_the_asset_without_figures(PriceStatus status, int at)
    {
        var gold = Monthly(Gold, 1000m, GoldFirsts, 6613.19001236m, status, at);

        Assert.Equal(status, gold.Status);
        Assert.Equal(((decimal?)null, (decimal?)null, (decimal?)null, (decimal?)null), Totals(gold));
        Assert.Null(gold.Volatility);
        // The days and prices are still shown, only the units are not.
        Assert.Equal(12, gold.Buys.Count);
        Assert.All(gold.Buys, b => Assert.Null(b.Units));
        Assert.Equal(12000m, gold.Invested);
    }

    [Fact]
    public void The_monthly_engine_needs_twelve_buys_and_a_positive_amount()
    {
        var point = new PricePoint(Today, Today, 49m, PriceStatus.Ok, "TCMB");
        Assert.Throws<ArgumentException>(() => WhatIfMath.Monthly(Usd, 1000m, [point], point, point));
        Assert.Throws<ArgumentOutOfRangeException>(() => WhatIfMath.Monthly(Usd, 0m, Enumerable.Repeat(point, 12).ToList(), point, point));
    }

    // ---- OC-09: the fixed warning

    [Fact]
    public void The_warning_says_what_the_figures_are_not() =>
        Assert.Equal(
            "Geçmiş fiyatlarla yapılmış bir hesaptır; gelecekteki getiriyi göstermez ve yatırım tavsiyesi değildir. "
            + "Alış-satış farkı, vergi, komisyon, faiz ve enflasyon dahil değildir.",
            WhatIfMath.Disclaimer);
}
