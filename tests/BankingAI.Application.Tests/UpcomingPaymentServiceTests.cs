using BankingAI.Application.Analytics;
using BankingAI.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static BankingAI.Application.Tests.DemoData;

namespace BankingAI.Application.Tests;

// ANA-12. Reads the seeded demo data; expected values are hand-computed acceptance fixtures. Ada pays Netflix 229,99 on
// the 12th and Spotify 99,99 on the 18th of every month (12/18 April – 12/18 September); Deniz and Ege have no
// recurring payment. "Today" is the demo moment unless a test moves it.
public sealed class UpcomingPaymentServiceTests
{
    private static async Task<UpcomingPayments> GetAsync(UpcomingPaymentQuery query, DateTimeOffset? now = null)
    {
        Assert.False(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BANKINGAI_CONNECTION_STRING")),
            "PostgreSQL testi için önce .env ortam değişkenlerini yükleyin.");
        await using var db = new BankingDbContext();
        var periods = new PeriodResolver(new FixedTimeProvider(now ?? DemoNowUtc));
        return await new UpcomingPaymentService(new RecurringPaymentService(db, periods)).GetAsync(query);
    }

    private static (string, DateOnly, decimal, int, bool)[] Rows(UpcomingPayments result) =>
        result.Payments.Select(p => (p.Merchant, p.EstimatedDate, p.EstimatedAmount, p.DaysUntil, p.IsOverdue)).ToArray();

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Next_thirty_days_list_the_expected_subscriptions_earliest_first()
    {
        var result = await GetAsync(new(Ada));

        Assert.Equal((new DateOnly(2026, 10, 10), new DateOnly(2026, 11, 9)), (result.From, result.To));
        Assert.Equal(new (string, DateOnly, decimal, int, bool)[]
        {
            ("Netflix", new(2026, 10, 12), 229.99m, 2, false),
            ("Spotify", new(2026, 10, 18), 99.99m, 8, false)
        }, Rows(result));
        Assert.Equal(new[] { new CurrencyUpcomingTotal("TRY", 329.98m) }, result.Totals);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Each_payment_carries_its_evidence_and_labels()
    {
        var netflix = (await GetAsync(new(Ada))).Payments[0];

        Assert.Equal(("SUBSCRIPTION", "Abonelik", "Ada günlük TRY", "TRY"),
            (netflix.CategoryCode, netflix.CategoryDisplayName, netflix.AccountName, netflix.CurrencyCode));
        // The estimate is the recurring rule's own next date and latest amount, not a new calculation.
        Assert.Equal((6, new DateOnly(2026, 9, 12), new DateOnly(2026, 10, 12), 229.99m),
            (netflix.Pattern.OccurrenceCount, netflix.Pattern.LastDate, netflix.Pattern.NextExpectedDate, netflix.Pattern.LatestAmount));
    }

    [Theory]
    [Trait("Category", "PostgreSQL")]
    [InlineData(5, new[] { "Netflix" })]   // 10–15 October: Spotify on the 18th is outside
    [InlineData(2, new[] { "Netflix" })]   // 12 October is exactly today + 2: the window's end is included
    [InlineData(1, new string[0])]         // 10–11 October: nothing expected
    public async Task The_window_ends_on_today_plus_days(int days, string[] merchants)
    {
        var result = await GetAsync(new(Ada, days));

        Assert.Equal(new DateOnly(2026, 10, 10).AddDays(days), result.To);
        Assert.Equal(merchants, result.Payments.Select(p => p.Merchant));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Totals_follow_the_listed_payments_and_are_empty_without_any()
    {
        var five = await GetAsync(new(Ada, 5));
        var one = await GetAsync(new(Ada, 1));

        Assert.Equal(new[] { new CurrencyUpcomingTotal("TRY", 229.99m) }, five.Totals);
        // No invented "0" row.
        Assert.Empty(one.Totals);
    }

    [Theory]
    [Trait("Category", "PostgreSQL")]
    [InlineData("Deniz")] // the market amount grows every month
    [InlineData("Ege")]   // transport is twice a month
    public async Task Customers_without_recurring_payments_get_empty_lists(string customer)
    {
        var result = await GetAsync(new(customer == "Deniz" ? Deniz : Ege));

        Assert.Empty(result.Payments);
        Assert.Empty(result.Totals);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task A_payment_past_its_expected_date_stays_listed_as_overdue()
    {
        // 14 October: Netflix was expected on the 12th and has not been paid; its last payment (12 September) is
        // 32 days ago, so the recurring rule still counts it as active.
        var result = await GetAsync(new(Ada), new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.Zero));

        Assert.Equal(new (string, DateOnly, decimal, int, bool)[]
        {
            ("Netflix", new(2026, 10, 12), 229.99m, -2, true),
            ("Spotify", new(2026, 10, 18), 99.99m, 4, false)
        }, Rows(result));
        Assert.Equal(329.98m, Assert.Single(result.Totals).EstimatedTotal);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task An_overdue_payment_disappears_once_the_rule_considers_it_cancelled()
    {
        // 20 October: Netflix's last payment is 38 days ago (more than 35), so it is no longer a current payment;
        // Spotify, expected on the 18th, is now the overdue one.
        var result = await GetAsync(new(Ada), new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero));

        Assert.Equal(new (string, DateOnly, decimal, int, bool)[] { ("Spotify", new(2026, 10, 18), 99.99m, -2, true) }, Rows(result));
        Assert.Equal(99.99m, Assert.Single(result.Totals).EstimatedTotal);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task The_estimate_uses_the_latest_amount_after_a_price_increase()
    {
        Assert.False(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BANKINGAI_CONNECTION_STRING")),
            "PostgreSQL testi için önce .env ortam değişkenlerini yükleyin.");
        await using var db = new BankingDbContext();
        await using var scope = await db.Database.BeginTransactionAsync();
        // A temporary subscription visible only inside this transaction (rolled back below): 100 on 25 July and
        // 25 August, then 105 on 25 September. 105 is within ±10 % of the median, so it is still recurring.
        foreach (var (date, amount) in new[] { ("2026-07-25", 100.00m), ("2026-08-25", 100.00m), ("2026-09-25", 105.00m) })
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Transactions" ("Id", "AccountId", "Amount", "Direction", "Kind", "CurrencyCode", "RawDescription", "NormalizedMerchant", "OccurredAt", "CreatedAt")
                VALUES ({Guid.NewGuid()}, {AdaTryAccount}, {amount}, 1, 1, 'TRY', 'ANA-12 price test', 'Disney+', {DateTimeOffset.Parse(date + "T09:00:00Z")}, now())
                """);
        var periods = new PeriodResolver(new FixedTimeProvider(DemoNowUtc));

        var result = await new UpcomingPaymentService(new RecurringPaymentService(db, periods)).GetAsync(new(Ada));
        await scope.RollbackAsync();

        var disney = Assert.Single(result.Payments, p => p.Merchant == "Disney+");
        // The last price, not the lowest or the median: what the customer will most likely pay next.
        Assert.Equal((new DateOnly(2026, 10, 25), 105.00m, 15, 100.00m),
            (disney.EstimatedDate, disney.EstimatedAmount, disney.DaysUntil, disney.Pattern.MinAmount));
        Assert.Equal(new[] { "Netflix", "Spotify", "Disney+" }, result.Payments.Select(p => p.Merchant));
        Assert.Equal(229.99m + 99.99m + 105.00m, Assert.Single(result.Totals).EstimatedTotal);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task One_personal_account_can_be_chosen()
    {
        var own = await GetAsync(new(Ada, AccountId: AdaTryAccount));
        var usd = await GetAsync(new(Ada, AccountId: AdaUsdAccount));

        Assert.Equal(new[] { "Netflix", "Spotify" }, own.Payments.Select(p => p.Merchant));
        Assert.Empty(usd.Payments);
    }

    [Theory]
    [Trait("Category", "PostgreSQL")]
    [InlineData(0)]
    [InlineData(32)]
    public async Task Days_must_be_between_one_and_thirty_one(int days)
    {
        var error = await Assert.ThrowsAsync<ArgumentException>(() => GetAsync(new(Ada, days)));

        Assert.Equal("Days", error.ParamName);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Scope_rules_match_the_recurring_payments()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() => GetAsync(new(Ada, AccountId: DenizTryAccount)));
        await Assert.ThrowsAsync<ArgumentException>(() => GetAsync(new(Deniz, AccountId: DenizShopAccount)));
    }
}
