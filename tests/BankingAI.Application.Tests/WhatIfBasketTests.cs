using BankingAI.Application.Investment;
using Xunit;

namespace BankingAI.Application.Tests;

// DEMO-02 (design: each purchase bought on its own day): the units of every buy add up and are valued at one price; a buy without a
// usable price leaves the asset without figures, as everywhere else.
public sealed class WhatIfBasketTests
{
    private static readonly MarketAssetInfo Usd = new(1, "USDTRY", "ABD Doları / Türk Lirası", "USD");
    private static readonly DateOnly Value = new(2026, 10, 10);

    private static PricePoint Ok(DateOnly day, decimal price) => new(day, day, price, PriceStatus.Ok, "TCMB");

    [Fact]
    public void One_buy_is_the_one_off_scenario()
    {
        // Murat's cinema of 13 September 2026 (the price of the 11th), valued on 10 October 2026 (the price of the 9th).
        var buy = new DateOnly(2026, 9, 13);
        var result = WhatIfMath.Basket(Usd, [(new BasketBuy(buy, 642.50m), Ok(buy, 48.4305m))], Ok(Value, 49.1274m));

        Assert.Equal((13.266433m, 651.75m, 9.25m, 1.44m), (result.Units, result.Value, result.Difference, result.DifferencePercent));
        Assert.Equal((buy, PriceStatus.Ok), (result.BuyDate, result.Status));
    }

    [Fact]
    public void Units_of_every_buy_add_up()
    {
        var first = new DateOnly(2026, 9, 1);
        var second = new DateOnly(2026, 9, 20);

        var result = WhatIfMath.Basket(Usd,
            [(new BasketBuy(first, 100m), Ok(first, 50m)), (new BasketBuy(second, 200m), Ok(second, 40m))], Ok(Value, 45m));

        Assert.Equal((7m, 315m, 15m, 5m), (result.Units, result.Value, result.Difference, result.DifferencePercent));
        Assert.Equal(first, result.BuyDate);
    }

    [Fact]
    public void One_stale_price_leaves_no_figures()
    {
        var day = new DateOnly(2026, 9, 1);
        var stale = new PricePoint(day, day.AddDays(-15), 50m, PriceStatus.Stale, "TCMB");

        var result = WhatIfMath.Basket(Usd, [(new BasketBuy(day, 100m), stale)], Ok(Value, 45m));

        Assert.Equal((PriceStatus.Stale, (decimal?)null), (result.Status, result.Value));
    }
}
