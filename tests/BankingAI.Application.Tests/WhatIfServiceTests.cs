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
        return await Service(db).OneOffAsync(query);
    }

    private static WhatIfService Service(BankingDbContext db)
    {
        var periods = new PeriodResolver(new FixedTimeProvider(DemoNowUtc));
        var categories = new CategoryBreakdownService(db, periods);
        var advice = new SpendingAdviceService(db, periods, new IncomeShareService(db, periods, categories));
        return new WhatIfService(db, periods, categories, advice, new MarketPriceReader(db));
    }

    private static async Task<MonthlyWhatIf> MonthlyAsync(MonthlyWhatIfQuery query)
    {
        await using var db = new BankingDbContext();
        return await Service(db).MonthlyAsync(query);
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

    // ---- INV-01 PR2: monthly saving (OC-04/05). Buys on every 1st from 1 Oct 2025 to 1 Sep 2026, valued on 10 Oct 2026.

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Selins_advice_saving_is_bought_on_every_first_of_the_last_twelve_months()
    {
        var result = await MonthlyAsync(new(Selin, FromAdvice: true, Months: 12));

        // INC-02's only advice for September: Eğlence, ayda 236,18.
        Assert.Equal((WhatIfAmountSource.Advice, (AdviceAmountStatus?)AdviceAmountStatus.Used, (decimal?)236.18m, "TRY"),
            (result.AmountSource, result.AdviceStatus, result.Amount, result.CurrencyCode));
        Assert.Equal(((DateOnly?)new DateOnly(2026, 9, 1), (DateOnly?)new DateOnly(2026, 9, 30)),
            (result.AdvicePeriod?.StartDate, result.AdvicePeriod?.EndDate));
        Assert.Equal((12, new DateOnly(2025, 10, 1), new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 10), (decimal?)2834.16m),
            (result.Months, result.FirstBuyDate, result.LastBuyDate, result.ValueDate, result.Invested));
        Assert.Equal(("MonthlyBuyOnFirstDay", WhatIfMath.Disclaimer), (result.Method, result.Disclaimer));
        Assert.Equal(
            [("USDTRY", 63.934549m, 3140.94m, 306.78m, 10.82m), ("EURTRY", 54.858260m, 3023.30m, 189.14m, 6.67m),
             ("GRAM_GOLD_TRY", 0.453730m, 3000.60m, 166.44m, 5.87m)],
            result.Assets.Select(a => (a.Symbol, a.Units, a.Value, a.Difference, a.DifferencePercent)));
        Assert.All(result.Assets, a => Assert.Equal(
            (PriceStatus.Ok, 12, new DateOnly(2025, 11, 1), (DateOnly?)new DateOnly(2025, 10, 31)),
            (a.Status, a.Buys.Count, a.Buys[1].BuyDate, a.Buys[1].PriceDate)));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task A_users_amount_gives_the_hand_computed_figures_and_volatility()
    {
        var result = await MonthlyAsync(new(Ada, 1000m, "TRY", Months: 12));

        Assert.Equal((WhatIfAmountSource.User, (AdviceAmountStatus?)null, (decimal?)1000m, (decimal?)12000m),
            (result.AmountSource, result.AdviceStatus, result.Amount, result.Invested));
        Assert.Null(result.AdvicePeriod);
        Assert.Equal([13298.92m, 12800.85m, 12704.72m], result.Assets.Select(a => a.Value!.Value));
        var gold = result.Assets[2].Volatility!;
        Assert.Equal((-9.13m, 18.54m, -17.97m, -3.24m),
            (gold.WorstMonth.ChangePercent, gold.BestMonth.ChangePercent, gold.MaxDrawdown.Percent, gold.WorstMoment.ReturnPercent));
        Assert.Equal(0m, result.Assets[0].Volatility!.MaxDrawdown.Percent);
    }

    [Theory]
    [Trait("Category", "PostgreSQL")]
    [InlineData("Ada")]    // spending within every rule (NoRuleMatched)
    [InlineData("Elif")]   // only euro: no lira advice
    public async Task Without_lira_advice_there_is_no_amount_and_no_asset_row(string name)
    {
        var result = await MonthlyAsync(new(name == "Ada" ? Ada : Elif, FromAdvice: true, Months: 12));

        Assert.Equal((WhatIfAmountSource.Advice, (AdviceAmountStatus?)AdviceAmountStatus.NoAdvice, (decimal?)null, (decimal?)null),
            (result.AmountSource, result.AdviceStatus, result.Amount, result.Invested));
        Assert.Empty(result.Assets);
        Assert.Equal(new DateOnly(2025, 10, 1), result.FirstBuyDate);
    }

    [Theory]
    [InlineData(null, null, false, "Amount", "Aylık tutar zorunlu: amount ile currencyCode ya da fromAdvice=true verilmeli.")]
    [InlineData("1000", "TRY", true, "Amount", "Tutar (amount) ile fromAdvice birlikte verilemez.")]
    [InlineData(null, "TRY", true, "CurrencyCode", "fromAdvice ile para birimi verilmez; öneri tutarı TL'dir.")]
    [InlineData("1000", null, false, "CurrencyCode", "Tutarın para birimi (currencyCode) verilmeli.")]
    [InlineData("1000", "USD", false, "CurrencyCode", "Birikim senaryosu yalnız TL (TRY) tutarla hesaplanır; varlık fiyatları TL'dir.")]
    [InlineData("1000", "XYZ", false, "CurrencyCode", "TRY/USD/EUR/GBP olmalı.")]
    [InlineData("0", "TRY", false, "Amount", "Tutar 0.01 ile parasal üst sınır arasında olmalı.")]
    [InlineData("1000000.01", "TRY", false, "Amount", "Aylık tutar en fazla 1.000.000 olmalı.")]
    // INV-03 K3-A: the number of months is never a silent default; K1-A, K10: 2 to 22 (prices start in November 2024).
    [InlineData("1000", "TRY", false, "Months", "Ay sayısı zorunlu: months 2 ile 22 arasında verilmeli.", null)]
    [InlineData(null, null, true, "Months", "Ay sayısı zorunlu: months 2 ile 22 arasında verilmeli.", null)]
    [InlineData("1000", "TRY", false, "Months", "Ay sayısı 2 ile 22 arasında olmalı.", 1)]
    [InlineData("1000", "TRY", false, "Months", "Ay sayısı 2 ile 22 arasında olmalı.", 23)]
    [InlineData(null, null, true, "Months", "Ay sayısı 2 ile 22 arasında olmalı.", 0)]
    public async Task Amount_and_source_are_checked_before_the_database(
        string? amount, string? currency, bool fromAdvice, string field, string message, int? months = 12)
    {
        // No connection is opened: the query is refused first, so this test needs no PostgreSQL.
        await using var db = new BankingDbContext();
        var error = await Assert.ThrowsAsync<ArgumentException>(() => Service(db).MonthlyAsync(
            new(Selin, amount is null ? null : decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture), currency, fromAdvice,
                months)));

        Assert.Equal((field, message), (error.ParamName, error.Message.Split(" (Parameter")[0]));
    }

    // INV-03: three months at the demo moment are July, August and September 2026; figures by hand from the price rows (independent
    // SQL, nearest earlier day: 1 August is Friday 31 July's price) with exact units: USD 1000/46.5867 + 1000/47.3452 + 1000/48.1891.
    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Three_months_buy_on_the_last_three_firsts()
    {
        var result = await MonthlyAsync(new(Ada, 1000m, "TRY", Months: 3));

        Assert.Equal((3, new DateOnly(2026, 7, 1), new DateOnly(2026, 9, 1), (decimal?)3000m),
            (result.Months, result.FirstBuyDate, result.LastBuyDate, result.Invested));
        Assert.Equal(
            [("USDTRY", 63.338400m, 3111.65m, 111.65m, 3.72m), ("EURTRY", 55.086755m, 3035.90m, 35.90m, 1.20m),
             ("GRAM_GOLD_TRY", 0.475138m, 3142.18m, 142.18m, 4.74m)],
            result.Assets.Select(a => (a.Symbol, a.Units, a.Value, a.Difference, a.DifferencePercent)));
        Assert.All(result.Assets, a => Assert.Equal(3, a.Buys.Count));
    }

    // K1-A: 22 months reach back to 1 December 2024, the first first with a price (rows start on 11 November 2024).
    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Twenty_two_months_still_have_every_price()
    {
        var result = await MonthlyAsync(new(Ada, 1000m, "TRY", Months: 22));

        Assert.Equal((22, new DateOnly(2024, 12, 1), new DateOnly(2026, 9, 1), (decimal?)22000m),
            (result.Months, result.FirstBuyDate, result.LastBuyDate, result.Invested));
        Assert.All(result.Assets, a => Assert.Equal((PriceStatus.Ok, 22, (DateOnly?)new DateOnly(2024, 11, 29)),
            (a.Status, a.Buys.Count, a.Buys[0].PriceDate)));
    }

    [Fact]
    public void One_million_is_still_allowed() =>
        Assert.Null(Record.Exception(() => WhatIfService.ValidateMonthly(new(Selin, 1_000_000m, "TRY", Months: 12))));

    [Theory]
    [Trait("Category", "PostgreSQL")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task An_unknown_customer_is_refused_for_the_saving(bool fromAdvice) =>
        await Assert.ThrowsAsync<CustomerNotFoundException>(() => MonthlyAsync(
            fromAdvice ? new(Guid.NewGuid(), FromAdvice: true, Months: 12) : new(Guid.NewGuid(), 1000m, "TRY", Months: 12)));
}
