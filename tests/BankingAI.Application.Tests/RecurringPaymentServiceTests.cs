using BankingAI.Application.Analytics;
using BankingAI.Data;
using Xunit;
using static BankingAI.Application.Tests.DemoData;

namespace BankingAI.Application.Tests;

// Reads the seeded demo data. Expected values are hand-computed acceptance fixtures.
public sealed class RecurringPaymentServiceTests
{
    private static async Task<RecurringPayments> GetAsync(RecurringPaymentQuery query)
    {
        Assert.False(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BANKINGAI_CONNECTION_STRING")),
            "PostgreSQL testi için önce .env ortam değişkenlerini yükleyin.");
        await using var db = new BankingDbContext();
        return await new RecurringPaymentService(db, new PeriodResolver(new FixedTimeProvider(DemoNowUtc))).GetAsync(query);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Ada_has_netflix_and_spotify_but_not_her_monthly_coffee()
    {
        var result = await GetAsync(new(Ada));

        // Starbucks is 185 on the 20th every month too, but the 6 and 8 October visits break the rhythm.
        Assert.Equal(new[] { "Netflix", "Spotify" }, result.Payments.Select(p => p.Merchant));
        var netflix = result.Payments[0];
        Assert.Equal(("SUBSCRIPTION", "Abonelik", "Ada günlük TRY", "TRY"),
            (netflix.CategoryCode, netflix.CategoryDisplayName, netflix.AccountName, netflix.CurrencyCode));
        Assert.Equal((6, 229.99m, new DateOnly(2026, 4, 12), new DateOnly(2026, 9, 12), 30, 31, new DateOnly(2026, 10, 12)),
            (netflix.Pattern.OccurrenceCount, netflix.Pattern.LatestAmount, netflix.Pattern.FirstDate, netflix.Pattern.LastDate,
             netflix.Pattern.MinIntervalDays, netflix.Pattern.MaxIntervalDays, netflix.Pattern.NextExpectedDate));
        Assert.Equal((99.99m, new DateOnly(2026, 10, 18)), (result.Payments[1].Pattern.LatestAmount, result.Payments[1].Pattern.NextExpectedDate));
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Monthly_total_adds_the_latest_amounts_per_currency()
    {
        var result = await GetAsync(new(Ada));

        Assert.Equal(new[] { new CurrencyRecurringTotal("TRY", 329.98m) }, result.MonthlyTotals);
        // Six-month window, same as the other analyses: 10 April – 10 October 2026.
        Assert.Equal((new DateOnly(2026, 4, 10), new DateOnly(2026, 10, 10)), (result.Period.StartDate, result.Period.EndDate));
    }

    [Theory]
    [Trait("Category", "PostgreSQL")]
    [InlineData("Deniz")] // Aile Marketi is monthly but its amount grows from 12.000 to 31.000
    [InlineData("Ege")]   // transport is twice a month
    public async Task Regular_shopping_is_not_reported_as_a_subscription(string customer)
    {
        var result = await GetAsync(new(customer == "Deniz" ? Deniz : Ege));

        Assert.Empty(result.Payments);
        Assert.Empty(result.MonthlyTotals);
    }

    [Fact]
    [Trait("Category", "PostgreSQL")]
    public async Task Scope_rules_match_the_other_services()
    {
        Assert.Equal(2, (await GetAsync(new(Ada, AdaTryAccount))).Payments.Count);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => GetAsync(new(Ada, DenizTryAccount)));
        await Assert.ThrowsAsync<ArgumentException>(() => GetAsync(new(Deniz, DenizShopAccount)));
    }
}
