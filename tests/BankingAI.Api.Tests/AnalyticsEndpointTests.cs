using System.Net;
using System.Text.Json.Nodes;
using BankingAI.Api.Time;
using Xunit;
using static BankingAI.Application.Tests.DemoData;

namespace BankingAI.Api.Tests;

// ANA-09. The analytics services over HTTP. Read-only against the seeded demo data, with the business clock fixed to
// the demo moment (decision 2-A), so symbolic periods give the hand-computed figures of docs/analytics.md.
public sealed class AnalyticsEndpointTests
{
    private static async Task<(HttpStatusCode Status, JsonNode? Body)> GetAsync(string path, Guid? customerId)
    {
        await using var api = TestApi.Create((BusinessClock.DemoNowKey, BusinessClockTests.DemoNow));
        using var client = api.CreateClient();
        if (customerId is { } id)
            client.WithToken(TestTokens.Create(id));
        var response = await client.GetAsync(path);
        var text = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, text.Length == 0 ? null : JsonNode.Parse(text));
    }

    private static JsonNode Ok((HttpStatusCode Status, JsonNode? Body) response)
    {
        Assert.True(response.Status == HttpStatusCode.OK, response.Body?.ToJsonString());
        return response.Body!;
    }

    private static string[] Errors(JsonNode problem, string field) =>
        problem["errors"]![field]!.AsArray().Select(m => m!.GetValue<string>()).ToArray();

    private static string Money(JsonNode? value) => value!.ToJsonString();

    // ---- spending-summary

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Spending_summary_counts_only_purchases_of_the_period()
    {
        var body = Ok(await GetAsync("/api/analytics/spending-summary?period=ThisMonth", Ada));

        Assert.Equal(["period", "currencies"], body.AsObject().Select(p => p.Key));
        Assert.Equal(("ThisMonth", "2026-10-01", "2026-10-10"),
            (body["period"]!["kind"]!.GetValue<string>(), body["period"]!["startDate"]!.GetValue<string>(),
             body["period"]!["endDate"]!.GetValue<string>()));
        // 735, not 2.235: the 1.500 transfer to Deniz is a Debit but not spending.
        var tryRow = Assert.Single(body["currencies"]!.AsArray())!;
        Assert.Equal(("TRY", "735.00", 5, "145.00", 1),
            (tryRow["currencyCode"]!.GetValue<string>(), Money(tryRow["totalAmount"]), tryRow["transactionCount"]!.GetValue<int>(),
             Money(tryRow["uncategorizedAmount"]), tryRow["uncategorizedCount"]!.GetValue<int>()));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Spending_summary_follows_the_named_period_and_dates()
    {
        var lastMonth = Ok(await GetAsync("/api/analytics/spending-summary?period=LastMonth", Deniz));
        var january = Ok(await GetAsync("/api/analytics/spending-summary?from=2026-01-01&to=2026-01-31", Ada));

        Assert.Equal(("31000.00", 1), (Money(lastMonth["currencies"]![0]!["totalAmount"]), lastMonth["currencies"]![0]!["transactionCount"]!.GetValue<int>()));
        // No spending is an empty list, never an invented "0" row.
        Assert.Empty(january["currencies"]!.AsArray());
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Spending_summary_can_be_limited_to_one_own_personal_account()
    {
        var own = Ok(await GetAsync($"/api/analytics/spending-summary?period=ThisMonth&accountId={AdaTryAccount}", Ada));
        var foreign = await GetAsync($"/api/analytics/spending-summary?period=ThisMonth&accountId={DenizTryAccount}", Ada);
        var shop = await GetAsync($"/api/analytics/spending-summary?period=ThisMonth&accountId={DenizShopAccount}", Deniz);

        Assert.Equal("735.00", Money(own["currencies"]![0]!["totalAmount"]));
        Assert.Equal(HttpStatusCode.NotFound, foreign.Status);
        // Spending analysis is for personal accounts; the shop's income is not spending.
        Assert.Equal(HttpStatusCode.BadRequest, shop.Status);
        Assert.Equal(["Harcama analizi yalnızca şahsi hesaplar içindir."], Errors(shop.Body!, "accountId"));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Analytics_need_a_token_and_a_period()
    {
        var noToken = await GetAsync("/api/analytics/spending-summary?period=ThisMonth", null);
        var noPeriod = await GetAsync("/api/analytics/spending-summary", Ada);
        var both = await GetAsync("/api/analytics/category-breakdown?period=ThisMonth&from=2026-10-01&to=2026-10-10", Ada);

        Assert.Equal(HttpStatusCode.Unauthorized, noToken.Status);
        Assert.Equal(HttpStatusCode.BadRequest, noPeriod.Status);
        Assert.Equal(["Dönem zorunlu: period ya da from ile to verilmeli."], Errors(noPeriod.Body!, "period"));
        Assert.Equal(HttpStatusCode.BadRequest, both.Status);
    }

    // ---- category-breakdown

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Category_breakdown_keeps_unclassified_apart()
    {
        var body = Ok(await GetAsync("/api/analytics/category-breakdown?period=ThisMonth", Ada));

        var currency = Assert.Single(body["currencies"]!.AsArray())!;
        Assert.Equal("735.00", Money(currency["totalAmount"]));
        Assert.Equal(new (string?, string, string, int, string)[]
            {
                ("FOOD", "Yeme & İçme", "590.00", 4, "80.27"),
                (null, "Sınıflandırılmamış", "145.00", 1, "19.73")
            },
            currency["categories"]!.AsArray().Select(c => (c!["categoryCode"]?.GetValue<string>(), c["displayName"]!.GetValue<string>(),
                Money(c["amount"]), c["transactionCount"]!.GetValue<int>(), Money(c["percentage"]))).ToArray());
    }

    // ---- merchant-breakdown

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Merchant_breakdown_lists_merchants_largest_first()
    {
        var body = Ok(await GetAsync("/api/analytics/merchant-breakdown?period=ThisMonth", Ada));

        Assert.Equal(new (string?, string, string)[]
            {
                ("Starbucks", "375.00", "51.02"),
                ("Kahve Durağı", "215.00", "29.25"),
                (null, "145.00", "19.73")
            },
            body["currencies"]![0]!["merchants"]!.AsArray()
                .Select(m => (m!["merchant"]?.GetValue<string>(), Money(m["amount"]), Money(m["percentage"]))).ToArray());
    }

    // ---- merchant-spending

    [Theory]
    [Trait("Category", "PostgreSQL")]
    [InlineData("starbucks", false)]
    [InlineData("starbuck", true)] // a single containing name is accepted, and the answer says so
    public async Task Merchant_spending_matches_the_customers_own_merchant(string merchant, bool partial)
    {
        var body = Ok(await GetAsync($"/api/analytics/merchant-spending?merchant={merchant}&period=Last3Months", Ada));

        Assert.Equal(("Matched", "Starbucks", partial),
            (body["match"]!["status"]!.GetValue<string>(), body["match"]!["merchant"]!.GetValue<string>(),
             body["match"]!["isPartialMatch"]!.GetValue<bool>()));
        // Kahve Durağı is not Starbucks: 5 Starbucks purchases between 10 July and 10 October.
        Assert.Equal(("TRY", "930.00", 5),
            (body["currencies"]![0]!["currencyCode"]!.GetValue<string>(), Money(body["currencies"]![0]!["totalAmount"]),
             body["currencies"]![0]!["transactionCount"]!.GetValue<int>()));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Merchant_spending_never_guesses_between_merchants()
    {
        var ambiguous = Ok(await GetAsync("/api/analytics/merchant-spending?merchant=şehir&period=Last6Months", Ege));
        var foreign = Ok(await GetAsync("/api/analytics/merchant-spending?merchant=Aile%20Marketi&period=ThisMonth", Ada));

        Assert.Equal("Ambiguous", ambiguous["match"]!["status"]!.GetValue<string>());
        Assert.Equal(["Şehir Ulaşım", "Şehirlerarası Otobüs"], ambiguous["match"]!["candidates"]!.AsArray().Select(c => c!.GetValue<string>()));
        Assert.Empty(ambiguous["currencies"]!.AsArray());
        // Deniz's market is not one of Ada's merchants.
        Assert.Equal("NotFound", foreign["match"]!["status"]!.GetValue<string>());
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Merchant_spending_needs_a_merchant()
    {
        var missing = await GetAsync("/api/analytics/merchant-spending?period=ThisMonth", Ada);

        Assert.Equal(HttpStatusCode.BadRequest, missing.Status);
        Assert.Equal(["Metin null, boş veya yalnızca whitespace olamaz."], Errors(missing.Body!, "merchant"));
    }

    // ==== Part 2: comparison, monthly trend, highest transactions, recurring payments

    // Sums of zero rows may come back as 0 or 0.00; the value, not its scale, is compared here.
    private static decimal Amount(JsonNode? value) => value!.GetValue<decimal>();

    private static (string Start, string End) Dates(JsonNode period) =>
        (period["startDate"]!.GetValue<string>(), period["endDate"]!.GetValue<string>());

    private static async Task AssertInvalidAsync(string path, Guid customerId, string field, string message)
    {
        var response = await GetAsync(path, customerId);

        Assert.True(response.Status == HttpStatusCode.BadRequest, response.Body?.ToJsonString());
        Assert.Equal([message], Errors(response.Body!, field));
    }

    // ---- period-comparison

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Comparison_of_the_last_full_month_explains_the_increase()
    {
        var body = Ok(await GetAsync("/api/analytics/period-comparison?comparison=PreviousFullMonth", Deniz));

        Assert.Equal(["current", "previous", "currencies"], body.AsObject().Select(p => p.Key));
        Assert.Equal(("2026-09-01", "2026-09-30"), Dates(body["current"]!));
        Assert.Equal(("2026-08-01", "2026-08-31"), Dates(body["previous"]!));
        var tl = Assert.Single(body["currencies"]!.AsArray())!;
        Assert.Equal(("TRY", 31000m, 1, 24000m, 1, 7000m, 29.17m),
            (tl["currencyCode"]!.GetValue<string>(), Amount(tl["currentAmount"]), tl["currentCount"]!.GetValue<int>(),
             Amount(tl["previousAmount"]), tl["previousCount"]!.GetValue<int>(), Amount(tl["change"]), Amount(tl["percentageChange"])));
        Assert.Equal([("GROCERY", 7000m)],
            tl["categories"]!.AsArray().Select(c => (c!["categoryCode"]!.GetValue<string>(), Amount(c["change"]))));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Comparison_lists_the_largest_cause_first_even_for_a_decrease()
    {
        var body = Ok(await GetAsync("/api/analytics/period-comparison?comparison=PreviousFullMonth", Ege));

        var tl = Assert.Single(body["currencies"]!.AsArray())!;
        Assert.Equal((1530m, 2100m, -570m, -27.14m),
            (Amount(tl["currentAmount"]), Amount(tl["previousAmount"]), Amount(tl["change"]), Amount(tl["percentageChange"])));
        // August's 1.200 bus ticket explains more than September's extra transport.
        Assert.Equal([("TRAVEL", -1200m), ("TRANSPORTATION", 500m), ("FOOD", 130m)],
            tl["categories"]!.AsArray().Select(c => (c!["categoryCode"]!.GetValue<string>(), Amount(c["change"]))));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Month_to_date_without_previous_spending_has_no_percentage()
    {
        var body = Ok(await GetAsync("/api/analytics/period-comparison?comparison=MonthToDate", Deniz));

        Assert.Equal(("2026-10-01", "2026-10-10"), Dates(body["current"]!));
        Assert.Equal(("2026-09-01", "2026-09-10"), Dates(body["previous"]!));
        var tl = Assert.Single(body["currencies"]!.AsArray())!.AsObject();
        Assert.Equal((11350m, 0m, 11350m), (Amount(tl["currentAmount"]), Amount(tl["previousAmount"]), Amount(tl["change"])));
        // A growth rate from zero is never invented: the field is there, and it is null.
        Assert.True(tl.ContainsKey("percentageChange"));
        Assert.Null(tl["percentageChange"]);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Comparison_of_two_given_ranges()
    {
        var body = Ok(await GetAsync(
            "/api/analytics/period-comparison?currentFrom=2026-09-01&currentTo=2026-09-30&previousFrom=2026-07-01&previousTo=2026-07-31", Ada));

        Assert.Equal(("Custom", "Custom"), (body["current"]!["kind"]!.GetValue<string>(), body["previous"]!["kind"]!.GetValue<string>()));
        var tl = Assert.Single(body["currencies"]!.AsArray())!;
        Assert.Equal((624.98m, 514.98m), (Amount(tl["currentAmount"]), Amount(tl["previousAmount"])));
    }

    [Theory]
    [Trait("Category", "PostgreSQL")]
    [InlineData("", "comparison", "Karşılaştırma zorunlu: comparison ya da currentFrom, currentTo, previousFrom ve previousTo verilmeli.")]
    [InlineData("comparison=Custom", "comparison", "PreviousFullMonth veya MonthToDate olmalı.")]
    [InlineData("comparison=previousfullmonth", "comparison", "PreviousFullMonth veya MonthToDate olmalı.")]
    [InlineData("comparison=1", "comparison", "PreviousFullMonth veya MonthToDate olmalı.")]
    [InlineData("comparison=PreviousFullMonth&currentFrom=2026-09-01&currentTo=2026-09-30&previousFrom=2026-07-01&previousTo=2026-07-31",
        "comparison", "Karşılaştırma adı ile tarih aralıkları birlikte verilemez.")]
    [InlineData("currentFrom=2026-09-01&currentTo=2026-09-30&previousFrom=2026-07-01",
        "currentFrom", "Dört tarih birlikte verilmeli: currentFrom, currentTo, previousFrom ve previousTo.")]
    [InlineData("currentFrom=2026-09-01&currentTo=2026-09-30&previousFrom=2026-09-15&previousTo=2026-10-05",
        "previousFrom", "Karşılaştırılan dönemler çakışmamalı.")]
    public async Task Comparison_parameters_are_strict(string query, string field, string message) =>
        await AssertInvalidAsync($"/api/analytics/period-comparison?{query}", Ada, field, message);

    // ---- monthly-trend

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Monthly_trend_lists_full_months_and_marks_the_running_month()
    {
        var body = Ok(await GetAsync("/api/analytics/monthly-trend?months=6", Deniz));

        Assert.Equal(["months", "merchantMatch", "currencies"], body.AsObject().Select(p => p.Key));
        Assert.Null(body["merchantMatch"]);
        var points = Assert.Single(body["currencies"]!.AsArray())!["months"]!.AsArray();
        Assert.Equal(
            [(4, false, 12000m), (5, false, 14000m), (6, false, 16000m), (7, false, 18000m), (8, false, 24000m),
             (9, false, 31000m), (10, true, 11350m)],
            points.Select(p => (p!["month"]!.GetValue<int>(), p["isPartial"]!.GetValue<bool>(), Amount(p["amount"]))));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Category_trend_keeps_months_without_spending_as_zero()
    {
        var transport = Ok(await GetAsync("/api/analytics/monthly-trend?months=6&categoryCode=TRANSPORTATION", Ege));
        var travel = Ok(await GetAsync("/api/analytics/monthly-trend?months=6&categoryCode=TRAVEL", Ege));

        Assert.Equal([(600m, 2), (650m, 2), (700m, 2), (650m, 2), (900m, 2), (1400m, 2), (700m, 1)],
            transport["currencies"]![0]!["months"]!.AsArray().Select(p => (Amount(p!["amount"]), p["transactionCount"]!.GetValue<int>())));
        Assert.Equal([0m, 0m, 0m, 0m, 1200m, 0m, 0m],
            travel["currencies"]![0]!["months"]!.AsArray().Select(p => Amount(p!["amount"])));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Merchant_trend_never_guesses_between_merchants()
    {
        var starbucks = Ok(await GetAsync("/api/analytics/monthly-trend?months=6&merchant=starbucks", Ada));
        var ambiguous = Ok(await GetAsync("/api/analytics/monthly-trend?months=6&merchant=şehir", Ege));

        Assert.Equal(("Matched", "Starbucks"),
            (starbucks["merchantMatch"]!["status"]!.GetValue<string>(), starbucks["merchantMatch"]!["merchant"]!.GetValue<string>()));
        Assert.Equal([185m, 185m, 185m, 185m, 185m, 185m, 375m],
            starbucks["currencies"]![0]!["months"]!.AsArray().Select(p => Amount(p!["amount"])));
        Assert.Equal("Ambiguous", ambiguous["merchantMatch"]!["status"]!.GetValue<string>());
        Assert.Empty(ambiguous["currencies"]!.AsArray());
    }

    [Theory]
    [Trait("Category", "PostgreSQL")]
    [InlineData("", "months", "Ay sayısı zorunlu: months 1 ile 24 arasında verilmeli.")]
    [InlineData("months=0", "months", "1 ile 24 arasında olmalı.")]
    [InlineData("months=25", "months", "1 ile 24 arasında olmalı.")]
    [InlineData("months=abc", "months", "'abc' geçerli bir değer değil.")]
    [InlineData("months=6&categoryCode=FOOD&merchant=Starbucks", "merchant", "Kategori ve merchant filtresi birlikte kullanılamaz.")]
    [InlineData("months=6&categoryCode=YEMEK", "categoryCode", "Tanımlı bir kategori kodu olmalı.")]
    public async Task Trend_parameters_are_checked(string query, string field, string message) =>
        await AssertInvalidAsync($"/api/analytics/monthly-trend?{query}", Ada, field, message);

    // ---- highest-transactions

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Highest_transactions_are_the_five_largest_purchases_by_default()
    {
        var body = Ok(await GetAsync("/api/analytics/highest-transactions?period=ThisMonth", Ada));

        Assert.Equal(["period", "currencies"], body.AsObject().Select(p => p.Key));
        var items = Assert.Single(body["currencies"]!.AsArray())!["items"]!.AsArray();
        // The 45.000 salary and the 1.500 transfer are not spending.
        Assert.Equal(
            [("2026-10-06", 210m, "Starbucks"), ("2026-10-08", 165m, "Starbucks"), ("2026-10-09", 145m, null),
             ("2026-10-02", 120m, "Kahve Durağı"), ("2026-10-07", 95m, "Kahve Durağı")],
            items.Select(i => (i!["localDate"]!.GetValue<string>(), Amount(i["amount"]), i["merchant"]?.GetValue<string>())));
        Assert.All(items, i => Assert.Equal("Purchase", i!["kind"]!.GetValue<string>()));
        Assert.Equal("Kahve Durağı – Kadıköy", items[3]!["counterparty"]!.GetValue<string>());
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Highest_transactions_return_the_requested_count()
    {
        var body = Ok(await GetAsync("/api/analytics/highest-transactions?period=Last6Months&limit=3", Deniz));

        Assert.Equal([31000m, 24000m, 18000m],
            body["currencies"]![0]!["items"]!.AsArray().Select(i => Amount(i!["amount"])));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task All_transactions_on_request_include_salary_transfers_and_the_shop()
    {
        var ada = Ok(await GetAsync("/api/analytics/highest-transactions?period=ThisMonth&includeAllTransactions=true", Ada));
        var shop = Ok(await GetAsync(
            $"/api/analytics/highest-transactions?period=ThisMonth&includeAllTransactions=true&accountId={DenizShopAccount}", Deniz));

        var items = ada["currencies"]![0]!["items"]!.AsArray();
        Assert.Equal(
            [("Salary", "Credit", 45000m), ("Transfer", "Debit", 1500m), ("Transfer", "Credit", 300m),
             ("Purchase", "Debit", 210m), ("Purchase", "Debit", 165m)],
            items.Select(i => (i!["kind"]!.GetValue<string>(), i["direction"]!.GetValue<string>(), Amount(i["amount"]))));
        Assert.Equal("Deniz M***", items[1]!["counterparty"]!.GetValue<string>());
        // The shop's sales are not spending, but they are transactions.
        Assert.Equal([("Sale", 140m), ("Sale", 120m), ("Sale", 95m)],
            shop["currencies"]![0]!["items"]!.AsArray().Select(i => (i!["kind"]!.GetValue<string>(), Amount(i["amount"]))));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Highest_transactions_within_an_amount_range()
    {
        var purchases = Ok(await GetAsync("/api/analytics/highest-transactions?period=ThisMonth&currencyCode=TRY&maxAmount=150", Ada));
        var all = Ok(await GetAsync(
            "/api/analytics/highest-transactions?period=ThisMonth&includeAllTransactions=true&currencyCode=TRY&minAmount=1000", Ada));

        // ANA-11: the 210 and 165 purchases are above the maximum; only salary and the sent transfer reach 1.000.
        Assert.Equal([145m, 120m, 95m], purchases["currencies"]![0]!["items"]!.AsArray().Select(i => Amount(i!["amount"])));
        Assert.Equal([("Salary", 45000m), ("Transfer", 1500m)],
            all["currencies"]![0]!["items"]!.AsArray().Select(i => (i!["kind"]!.GetValue<string>(), Amount(i["amount"]))));
    }

    [Theory]
    [Trait("Category", "PostgreSQL")]
    [InlineData("", "period", "Dönem zorunlu: period ya da from ile to verilmeli.")]
    [InlineData("period=ThisMonth&limit=0", "limit", "1 ile 100 arasında olmalı.")]
    [InlineData("period=ThisMonth&limit=101", "limit", "1 ile 100 arasında olmalı.")]
    [InlineData("period=ThisMonth&includeAllTransactions=evet", "includeAllTransactions", "'evet' geçerli bir değer değil.")]
    [InlineData("period=ThisMonth&minAmount=100", "currencyCode", "Tutar filtresi için para birimi (currencyCode) verilmeli.")]
    [InlineData("period=ThisMonth&maxAmount=500,50&currencyCode=TRY", "maxAmount", "'500,50' geçerli bir değer değil.")]
    [InlineData("period=ThisMonth&minAmount=200&maxAmount=100&currencyCode=TRY", "minAmount", "En az tutar en çok tutardan büyük olamaz.")]
    public async Task Highest_transaction_parameters_are_checked(string query, string field, string message) =>
        await AssertInvalidAsync($"/api/analytics/highest-transactions?{query}", Ada, field, message);

    // ---- recurring-payments

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Recurring_payments_explain_why_they_count_as_monthly()
    {
        var body = Ok(await GetAsync("/api/analytics/recurring-payments", Ada));

        Assert.Equal(["period", "payments", "monthlyTotals"], body.AsObject().Select(p => p.Key));
        // The rule itself looks back six months; it is not a period the client chooses.
        Assert.Equal(("2026-04-10", "2026-10-10"), Dates(body["period"]!));
        var payments = body["payments"]!.AsArray();
        Assert.Equal(["Netflix", "Spotify"], payments.Select(p => p!["merchant"]!.GetValue<string>()));
        var netflix = payments[0]!;
        Assert.Equal(("SUBSCRIPTION", "Abonelik", "Ada günlük TRY", "TRY"),
            (netflix["categoryCode"]!.GetValue<string>(), netflix["categoryDisplayName"]!.GetValue<string>(),
             netflix["accountName"]!.GetValue<string>(), netflix["currencyCode"]!.GetValue<string>()));
        var pattern = netflix["pattern"]!;
        Assert.Equal((6, 229.99m, "2026-04-12", "2026-09-12", 30, 31, "2026-10-12"),
            (pattern["occurrenceCount"]!.GetValue<int>(), Amount(pattern["latestAmount"]), pattern["firstDate"]!.GetValue<string>(),
             pattern["lastDate"]!.GetValue<string>(), pattern["minIntervalDays"]!.GetValue<int>(),
             pattern["maxIntervalDays"]!.GetValue<int>(), pattern["nextExpectedDate"]!.GetValue<string>()));
        Assert.Equal((99.99m, "2026-10-18"),
            (Amount(payments[1]!["pattern"]!["latestAmount"]), payments[1]!["pattern"]!["nextExpectedDate"]!.GetValue<string>()));
        Assert.Equal([("TRY", 329.98m)],
            body["monthlyTotals"]!.AsArray().Select(t => (t!["currencyCode"]!.GetValue<string>(), Amount(t["monthlyTotal"]))));
    }

    [Theory]
    [Trait("Category", "PostgreSQL")]
    [InlineData("Deniz")] // Aile Marketi is monthly, but its amount grows from 12.000 to 31.000
    [InlineData("Ege")]   // transport is twice a month
    public async Task Regular_shopping_is_not_a_recurring_payment(string customer)
    {
        var body = Ok(await GetAsync("/api/analytics/recurring-payments", customer == "Deniz" ? Deniz : Ege));

        Assert.Empty(body["payments"]!.AsArray());
        Assert.Empty(body["monthlyTotals"]!.AsArray());
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Recurring_payments_can_be_limited_to_one_account()
    {
        var own = Ok(await GetAsync($"/api/analytics/recurring-payments?accountId={AdaTryAccount}", Ada));
        var usd = Ok(await GetAsync($"/api/analytics/recurring-payments?accountId={AdaUsdAccount}", Ada));

        Assert.Equal(2, own["payments"]!.AsArray().Count);
        Assert.Empty(usd["payments"]!.AsArray());
    }

    // ---- shared rules of the part 2 endpoints

    [Theory]
    [Trait("Category", "PostgreSQL")]
    [InlineData("period-comparison?comparison=PreviousFullMonth")]
    [InlineData("monthly-trend?months=6")]
    [InlineData("highest-transactions?period=ThisMonth")]
    [InlineData("recurring-payments?")]
    public async Task Part_two_endpoints_apply_the_token_and_account_rules(string pathAndQuery)
    {
        var path = $"/api/analytics/{pathAndQuery}";
        var separator = path.EndsWith('?') ? "" : "&";

        var noToken = await GetAsync(path, null);
        var own = await GetAsync($"{path}{separator}accountId={AdaTryAccount}", Ada);
        var foreign = await GetAsync($"{path}{separator}accountId={DenizTryAccount}", Ada);

        Assert.Equal(HttpStatusCode.Unauthorized, noToken.Status);
        Ok(own);
        // Another customer's account is answered exactly like an account that does not exist.
        Assert.Equal(HttpStatusCode.NotFound, foreign.Status);
        await AssertInvalidAsync($"{path}{separator}accountId={DenizShopAccount}", Deniz,
            "accountId", "Harcama analizi yalnızca şahsi hesaplar içindir.");
    }

    // ---- upcoming-payments (ANA-12)

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Upcoming_payments_list_the_estimated_dates_of_the_next_thirty_days()
    {
        var body = Ok(await GetAsync("/api/analytics/upcoming-payments", Ada));

        Assert.Equal(["from", "to", "payments", "totals"], body.AsObject().Select(p => p.Key));
        // Decision K3-A: 30 days by default, and the answer names its window.
        Assert.Equal(("2026-10-10", "2026-11-09"), (body["from"]!.GetValue<string>(), body["to"]!.GetValue<string>()));
        var payments = body["payments"]!.AsArray();
        Assert.Equal([("Netflix", "2026-10-12", 229.99m, 2, false), ("Spotify", "2026-10-18", 99.99m, 8, false)],
            payments.Select(p => (p!["merchant"]!.GetValue<string>(), p["estimatedDate"]!.GetValue<string>(), Amount(p["estimatedAmount"]),
                p["daysUntil"]!.GetValue<int>(), p["isOverdue"]!.GetValue<bool>())));
        // The evidence travels with every estimate.
        Assert.Equal(6, payments[0]!["pattern"]!["occurrenceCount"]!.GetValue<int>());
        Assert.Equal([("TRY", 329.98m)],
            body["totals"]!.AsArray().Select(t => (t!["currencyCode"]!.GetValue<string>(), Amount(t["estimatedTotal"]))));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Upcoming_payments_follow_the_requested_window()
    {
        var body = Ok(await GetAsync("/api/analytics/upcoming-payments?days=5", Ada));

        Assert.Equal("2026-10-15", body["to"]!.GetValue<string>());
        Assert.Equal(["Netflix"], body["payments"]!.AsArray().Select(p => p!["merchant"]!.GetValue<string>()));
    }

    [Theory]
    [Trait("Category", "PostgreSQL")]
    [InlineData("days=0", "1 ile 31 arasında olmalı.")]
    [InlineData("days=32", "1 ile 31 arasında olmalı.")]
    [InlineData("days=abc", "'abc' geçerli bir değer değil.")]
    public async Task Upcoming_payment_days_are_checked(string query, string message) =>
        await AssertInvalidAsync($"/api/analytics/upcoming-payments?{query}", Ada, "days", message);

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Upcoming_payments_apply_the_token_and_account_rules()
    {
        var noToken = await GetAsync("/api/analytics/upcoming-payments", null);
        var own = Ok(await GetAsync($"/api/analytics/upcoming-payments?accountId={AdaTryAccount}", Ada));
        var usd = Ok(await GetAsync($"/api/analytics/upcoming-payments?accountId={AdaUsdAccount}", Ada));
        var foreign = await GetAsync($"/api/analytics/upcoming-payments?accountId={DenizTryAccount}", Ada);

        Assert.Equal(HttpStatusCode.Unauthorized, noToken.Status);
        Assert.Equal(2, own["payments"]!.AsArray().Count);
        Assert.Empty(usd["payments"]!.AsArray());
        Assert.Equal(HttpStatusCode.NotFound, foreign.Status);
        await AssertInvalidAsync($"/api/analytics/upcoming-payments?accountId={DenizShopAccount}", Deniz,
            "accountId", "Harcama analizi yalnızca şahsi hesaplar içindir.");
    }

    // ---- budget-check (ANA-13)

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Budget_check_reports_the_month_so_far_and_its_projection()
    {
        var body = Ok(await GetAsync("/api/analytics/budget-check?amount=1000&currencyCode=TRY&categoryCode=FOOD", Ada));

        Assert.Equal(["period", "currencyCode", "budgetAmount", "categoryCode", "categoryDisplayName", "merchantMatch", "figures"],
            body.AsObject().Select(p => p.Key));
        Assert.Equal(("ThisMonth", "Yeme & İçme"), (body["period"]!["kind"]!.GetValue<string>(), body["categoryDisplayName"]!.GetValue<string>()));
        var f = body["figures"]!;
        // 590 spent, 410 left; 59 a day → 1.829 by 31 October, 829 over the budget.
        Assert.Equal((590m, 410m, 0m, false, 59m, 1829m, 829m, true),
            (Amount(f["spentAmount"]), Amount(f["remainingAmount"]), Amount(f["overAmount"]), f["isOverBudget"]!.GetValue<bool>(),
             Amount(f["dailyAverage"]), Amount(f["projectedMonthEnd"]), Amount(f["projectedOverAmount"]), f["willExceed"]!.GetValue<bool>()));
        Assert.Equal((10, 31, "LinearDailyAverage"),
            (f["elapsedDays"]!.GetValue<int>(), f["daysInMonth"]!.GetValue<int>(), f["projectionMethod"]!.GetValue<string>()));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Merchant_budget_is_matched_without_guessing()
    {
        var starbucks = Ok(await GetAsync("/api/analytics/budget-check?amount=300&currencyCode=TRY&merchant=starbucks", Ada));
        var ambiguous = Ok(await GetAsync("/api/analytics/budget-check?amount=500&currencyCode=TRY&merchant=şehir", Ege));

        Assert.Equal(("Matched", 375m, 75m, true),
            (starbucks["merchantMatch"]!["status"]!.GetValue<string>(), Amount(starbucks["figures"]!["spentAmount"]),
             Amount(starbucks["figures"]!["overAmount"]), starbucks["figures"]!["isOverBudget"]!.GetValue<bool>()));
        Assert.Equal("Ambiguous", ambiguous["merchantMatch"]!["status"]!.GetValue<string>());
        Assert.True(ambiguous.AsObject().ContainsKey("figures"));
        Assert.Null(ambiguous["figures"]);
    }

    [Theory]
    [Trait("Category", "PostgreSQL")]
    [InlineData("currencyCode=TRY", "amount", "Bütçe tutarı zorunlu.")]
    [InlineData("amount=1000,50&currencyCode=TRY", "amount", "'1000,50' geçerli bir değer değil.")]
    [InlineData("amount=0&currencyCode=TRY", "amount", "Tutar 0.01 ile parasal üst sınır arasında olmalı.")]
    [InlineData("amount=1000", "currencyCode", "Bütçenin para birimi (currencyCode) verilmeli.")]
    [InlineData("amount=1000&currencyCode=try", "currencyCode", "TRY/USD/EUR/GBP olmalı.")]
    [InlineData("amount=1000&currencyCode=TRY&categoryCode=FOOD&merchant=Starbucks", "merchant", "Kategori ve merchant filtresi birlikte kullanılamaz.")]
    [InlineData("amount=1000&currencyCode=TRY&categoryCode=YEMEK", "categoryCode", "Tanımlı bir kategori kodu olmalı.")]
    public async Task Budget_check_parameters_are_checked(string query, string field, string message) =>
        await AssertInvalidAsync($"/api/analytics/budget-check?{query}", Ada, field, message);

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Budget_check_applies_the_token_and_account_rules()
    {
        var path = "/api/analytics/budget-check?amount=1000&currencyCode=TRY";

        var noToken = await GetAsync(path, null);
        var own = Ok(await GetAsync($"{path}&accountId={AdaTryAccount}", Ada));
        var foreign = await GetAsync($"{path}&accountId={DenizTryAccount}", Ada);

        Assert.Equal(HttpStatusCode.Unauthorized, noToken.Status);
        Assert.Equal(735m, Amount(own["figures"]!["spentAmount"]));
        Assert.Equal(HttpStatusCode.NotFound, foreign.Status);
        await AssertInvalidAsync($"{path}&accountId={DenizShopAccount}", Deniz, "accountId", "Harcama analizi yalnızca şahsi hesaplar içindir.");
    }

    // ---- savings-simulation (ANA-14)

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Savings_simulation_states_its_months_assumption_and_result()
    {
        var body = Ok(await GetAsync("/api/analytics/savings-simulation?reductionPercent=50&merchant=starbucks", Ada));

        Assert.Equal(["from", "to", "months", "reductionPercent", "categoryCode", "merchantMatch", "method", "currencies"],
            body.AsObject().Select(p => p.Key));
        // Decision K3-A: six complete months by default; the running October is not averaged.
        Assert.Equal(("2026-04-01", "2026-09-30", 6, "FullMonthAverageTimesReduction"),
            (body["from"]!.GetValue<string>(), body["to"]!.GetValue<string>(), body["months"]!.GetValue<int>(), body["method"]!.GetValue<string>()));
        var tl = Assert.Single(body["currencies"]!.AsArray())!;
        Assert.Equal(("TRY", 1110m, 185m, 92.50m, 1110m),
            (tl["currencyCode"]!.GetValue<string>(), Amount(tl["totalSpent"]), Amount(tl["monthlyAverage"]),
             Amount(tl["monthlySaving"]), Amount(tl["annualSaving"])));
        Assert.Equal(6, tl["monthlyAmounts"]!.AsArray().Count);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Savings_simulation_follows_the_requested_months_and_category()
    {
        var body = Ok(await GetAsync("/api/analytics/savings-simulation?reductionPercent=20&categoryCode=GROCERY&months=3", Deniz));

        Assert.Equal(("2026-07-01", "GROCERY"), (body["from"]!.GetValue<string>(), body["categoryCode"]!.GetValue<string>()));
        Assert.Equal((24333.33m, 4866.67m, 58400m),
            (Amount(body["currencies"]![0]!["monthlyAverage"]), Amount(body["currencies"]![0]!["monthlySaving"]),
             Amount(body["currencies"]![0]!["annualSaving"])));
    }

    [Theory]
    [Trait("Category", "PostgreSQL")]
    [InlineData("", "reductionPercent", "Azaltma oranı zorunlu.")]
    [InlineData("reductionPercent=0", "reductionPercent", "1 ile 100 arasında olmalı.")]
    [InlineData("reductionPercent=abc", "reductionPercent", "'abc' geçerli bir değer değil.")]
    [InlineData("reductionPercent=50&months=25", "months", "1 ile 24 arasında olmalı.")]
    [InlineData("reductionPercent=50&categoryCode=FOOD&merchant=Starbucks", "merchant", "Kategori ve merchant filtresi birlikte kullanılamaz.")]
    [InlineData("reductionPercent=50&categoryCode=YEMEK", "categoryCode", "Tanımlı bir kategori kodu olmalı.")]
    public async Task Savings_simulation_parameters_are_checked(string query, string field, string message) =>
        await AssertInvalidAsync($"/api/analytics/savings-simulation?{query}", Ada, field, message);

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Savings_simulation_applies_the_token_and_account_rules()
    {
        var path = "/api/analytics/savings-simulation?reductionPercent=10";

        var noToken = await GetAsync(path, null);
        var own = Ok(await GetAsync($"{path}&accountId={AdaTryAccount}", Ada));
        var foreign = await GetAsync($"{path}&accountId={DenizTryAccount}", Ada);

        Assert.Equal(HttpStatusCode.Unauthorized, noToken.Status);
        Assert.Equal(3199.88m, Amount(own["currencies"]![0]!["totalSpent"]));
        Assert.Equal(HttpStatusCode.NotFound, foreign.Status);
        await AssertInvalidAsync($"{path}&accountId={DenizShopAccount}", Deniz, "accountId", "Harcama analizi yalnızca şahsi hesaplar içindir.");
    }
}
