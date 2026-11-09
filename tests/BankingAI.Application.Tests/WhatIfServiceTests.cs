using BankingAI.Application.Analytics;
using BankingAI.Application.Investment;
using BankingAI.Data;
using Xunit;
using static BankingAI.Application.Tests.DemoData;

namespace BankingAI.Application.Tests;

// INV-01 PR1 on the seeded demo data and the imported market prices, at the demo moment (Saturday 10 October 2026: the valuation
// price is Friday 9 October's). Expected figures were computed by hand from the price rows (independent SQL) with exact units.
public sealed class WhatIfServiceTests
{
    private static async Task<OneOffWhatIf> OneOffAsync(OneOffWhatIfQuery query)
    {
        Assert.False(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BANKINGAI_CONNECTION_STRING")),
            "PostgreSQL testi için önce .env ortam değişkenlerini yükleyin.");
        await using var db = new BankingDbContext();
        var periods = new PeriodResolver(new FixedTimeProvider(DemoNowUtc));
        return await new WhatIfService(db, periods, new CategoryBreakdownService(db, periods), new MarketPriceReader(db))
            .OneOffAsync(query);
    }

    // Symbol, units, value, difference, percent.
    private static (string, decimal?, decimal?, decimal?, decimal?) Figures(AssetWhatIf a) =>
        (a.Symbol, a.Units, a.Value, a.Difference, a.DifferencePercent);

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Selins_september_entertainment_bought_on_the_first_and_valued_today()
    {
        var result = await OneOffAsync(new(Selin, PeriodKind.LastMonth, CategoryCode: "ENTERTAINMENT"));

        Assert.Equal(("ENTERTAINMENT", "Eğlence", "TRY", 2253.50m), (result.CategoryCode, result.CategoryDisplayName, result.CurrencyCode, result.SpentAmount));
        Assert.Equal((new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 10)), (result.BuyDate, result.ValueDate));
        Assert.Equal(("SingleBuyAtPeriodStart", WhatIfMath.Disclaimer), (result.Method, result.Disclaimer));
        // USD 48,1891 → 49,1274; EUR 55,8769 → 55,1112; gram gold 6.789,78069267 → 6.613,19001236 (prices of 1 Sep and 9 Oct).
        Assert.Equal(
            [("USDTRY", 46.763687m, 2297.38m, 43.88m, 1.95m), ("EURTRY", 40.329725m, 2222.62m, -30.88m, -1.37m),
             ("GRAM_GOLD_TRY", 0.331896m, 2194.89m, -58.61m, -2.60m)],
            result.Assets.Select(Figures));
        Assert.All(result.Assets, a => Assert.Equal((PriceStatus.Ok, (DateOnly?)new DateOnly(2026, 10, 9)), (a.Status, a.ValuePriceDate)));
        Assert.Equal(["TCMB", "TCMB", "GOLDAPI+TCMB"], result.Assets.Select(a => a.Source));
        Assert.Equal(["USD", "EUR", "GRAM"], result.Assets.Select(a => a.BaseUnit));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task A_period_starting_in_the_holiday_buys_at_the_last_price_before_it()
    {
        // 26–31 May 2026: no prices after 25 May. Selin spent 1.079,50 TL in TRY.
        var result = await OneOffAsync(new(Selin, PeriodKind.Custom, new DateOnly(2026, 5, 26), new DateOnly(2026, 5, 31)));

        Assert.Equal(1079.50m, result.SpentAmount);
        Assert.All(result.Assets, a => Assert.Equal((DateOnly?)new DateOnly(2026, 5, 25), a.BuyPriceDate));
        Assert.Equal(
            [("USDTRY", 23.657059m, 1162.21m, 82.71m, 7.66m), ("EURTRY", 20.320995m, 1119.91m, 40.41m, 3.74m),
             ("GRAM_GOLD_TRY", 0.161105m, 1065.42m, -14.08m, -1.30m)],
            result.Assets.Select(Figures));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Without_a_category_the_whole_try_spending_equals_the_category_breakdown()
    {
        await using var db = new BankingDbContext();
        var periods = new PeriodResolver(new FixedTimeProvider(DemoNowUtc));
        var breakdown = await new CategoryBreakdownService(db, periods).GetAsync(new SpendingQuery(Murat, PeriodKind.LastMonth));

        var result = await OneOffAsync(new(Murat, PeriodKind.LastMonth));

        Assert.Equal(breakdown.Currencies.Single(c => c.CurrencyCode == "TRY").TotalAmount, result.SpentAmount);
        Assert.Equal(((string?)null, (string?)null), (result.CategoryCode, result.CategoryDisplayName));
        Assert.Equal(("USDTRY", (decimal?)1107.893486m, (decimal?)54427.93m, (decimal?)1039.54m, (decimal?)1.95m), Figures(result.Assets[0]));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Only_try_spending_is_used()
    {
        // Zeynep spent 10.490 TL and 37,99 USD in September; dollars are not turned into lira.
        var result = await OneOffAsync(new(Zeynep, PeriodKind.LastMonth));

        Assert.Equal(10490m, result.SpentAmount);
    }

    [Theory]
    [Trait("Category", "PostgreSQL")]
    [InlineData("Ada", "CLOTHING")]   // no clothing in September
    [InlineData("Elif", null)]        // only euro spending
    public async Task Without_try_spending_there_are_no_asset_rows(string name, string? category)
    {
        var result = await OneOffAsync(new(name == "Ada" ? Ada : Elif, PeriodKind.LastMonth, CategoryCode: category));

        Assert.Equal(0m, result.SpentAmount);
        Assert.Empty(result.Assets);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task The_running_month_is_bought_on_its_first_day() =>
        Assert.Equal(new DateOnly(2026, 10, 1), (await OneOffAsync(new(Selin, PeriodKind.ThisMonth))).BuyDate);

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task An_unknown_category_is_refused()
    {
        var error = await Assert.ThrowsAsync<ArgumentException>(() => OneOffAsync(new(Selin, PeriodKind.LastMonth, CategoryCode: "COFFEE")));

        Assert.Equal("CategoryCode", error.ParamName);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task An_unknown_customer_is_refused() =>
        await Assert.ThrowsAsync<CustomerNotFoundException>(() => OneOffAsync(new(Guid.NewGuid(), PeriodKind.LastMonth)));
}
