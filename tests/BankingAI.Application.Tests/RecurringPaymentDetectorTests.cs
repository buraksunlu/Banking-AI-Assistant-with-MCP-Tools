using BankingAI.Application.Analytics;
using Xunit;

namespace BankingAI.Application.Tests;

// Pure rule tests (REC-01/03); no database. Every scenario is a hand-written payment history.
public sealed class RecurringPaymentDetectorTests
{
    private static readonly DateOnly Today = new(2026, 10, 10);

    private static PaymentPoint P(int month, int day, decimal amount, int year = 2026) => new(new DateOnly(year, month, day), amount);

    // Netflix-like: the 12th of every month, April–September.
    private static PaymentPoint[] Monthly(decimal amount, int day = 12) =>
        Enumerable.Range(4, 6).Select(month => P(month, day, amount)).ToArray();

    [Fact]
    public void Same_amount_every_month_is_recurring()
    {
        var pattern = RecurringPaymentDetector.Detect(Monthly(229.99m), Today);

        Assert.NotNull(pattern);
        Assert.Equal((6, 229.99m, 229.99m, 229.99m), (pattern.OccurrenceCount, pattern.LatestAmount, pattern.MinAmount, pattern.MaxAmount));
        Assert.Equal((new DateOnly(2026, 4, 12), new DateOnly(2026, 9, 12)), (pattern.FirstDate, pattern.LastDate));
        Assert.Equal((30, 31), (pattern.MinIntervalDays, pattern.MaxIntervalDays));
        Assert.Equal(new DateOnly(2026, 10, 12), pattern.NextExpectedDate);
    }

    [Fact]
    public void Month_end_payments_follow_short_and_long_months()
    {
        // 31 Jan → 28 Feb → 31 Mar: intervals 28 and 31 days; the next one is the last day of April.
        var payments = new[] { P(1, 31, 50m), P(2, 28, 50m), P(3, 31, 50m) };

        var pattern = RecurringPaymentDetector.Detect(payments, new DateOnly(2026, 4, 10));

        Assert.NotNull(pattern);
        Assert.Equal((28, 31), (pattern.MinIntervalDays, pattern.MaxIntervalDays));
        Assert.Equal(new DateOnly(2026, 4, 30), pattern.NextExpectedDate);
    }

    [Fact]
    public void A_small_price_increase_keeps_the_subscription()
    {
        var payments = new[] { P(4, 12, 229.99m), P(5, 12, 229.99m), P(6, 12, 229.99m), P(7, 12, 249.99m), P(8, 12, 249.99m), P(9, 12, 249.99m) };

        var pattern = RecurringPaymentDetector.Detect(payments, Today);

        Assert.NotNull(pattern);
        Assert.Equal((249.99m, 229.99m, 249.99m), (pattern.LatestAmount, pattern.MinAmount, pattern.MaxAmount));
    }

    [Fact]
    public void A_large_price_difference_is_not_recurring()
    {
        // Median 100; 150 is 50 % away, beyond the ±10 % tolerance.
        var payments = new[] { P(7, 12, 100m), P(8, 12, 100m), P(9, 12, 150m) };

        Assert.Null(RecurringPaymentDetector.Detect(payments, Today));
    }

    [Fact]
    public void Monthly_amounts_that_keep_growing_are_not_recurring()
    {
        // Grocery-like: monthly on the 24th, 12.000 → 31.000.
        var payments = new[] { P(4, 24, 12000m), P(5, 24, 14000m), P(6, 24, 16000m), P(7, 24, 18000m), P(8, 24, 24000m), P(9, 24, 31000m) };

        Assert.Null(RecurringPaymentDetector.Detect(payments, Today));
    }

    [Fact]
    public void Extra_visits_turn_a_monthly_coffee_into_a_habit()
    {
        var monthlyCoffee = Monthly(185m, day: 20);
        var withExtras = monthlyCoffee.Append(P(10, 6, 210m)).Append(P(10, 8, 165m));

        // Known limit of a deterministic rule: a perfectly regular coffee looks like a subscription.
        Assert.NotNull(RecurringPaymentDetector.Detect(monthlyCoffee, Today));
        // Extra purchases at the same merchant break the monthly rhythm (16 and 2 day gaps).
        Assert.Null(RecurringPaymentDetector.Detect(withExtras, Today));
    }

    [Fact]
    public void One_or_two_payments_are_not_enough()
    {
        Assert.Null(RecurringPaymentDetector.Detect([P(9, 12, 229.99m)], Today));
        Assert.Null(RecurringPaymentDetector.Detect([P(8, 12, 229.99m), P(9, 12, 229.99m)], Today));
        Assert.Null(RecurringPaymentDetector.Detect([], Today));
    }

    [Fact]
    public void A_skipped_month_breaks_the_pattern()
    {
        // User decision: no tolerance for a missing month in the first version.
        var payments = new[] { P(4, 12, 229.99m), P(5, 12, 229.99m), P(7, 12, 229.99m), P(8, 12, 229.99m), P(9, 12, 229.99m) };

        Assert.Null(RecurringPaymentDetector.Detect(payments, Today));
    }

    [Fact]
    public void Twice_a_month_is_not_a_monthly_payment()
    {
        var payments = Enumerable.Range(4, 6).SelectMany(month => new[] { P(month, 11, 300m), P(month, 25, 300m) });

        Assert.Null(RecurringPaymentDetector.Detect(payments, Today));
    }

    [Fact]
    public void A_cancelled_subscription_is_no_longer_listed()
    {
        // Regular April–June, nothing since: the last payment is more than 35 days old.
        var payments = new[] { P(4, 12, 99.99m), P(5, 12, 99.99m), P(6, 12, 99.99m) };

        Assert.Null(RecurringPaymentDetector.Detect(payments, Today));
    }

    [Fact]
    public void Two_charges_on_the_same_day_are_not_monthly()
    {
        var payments = Monthly(99.99m, day: 18).Append(P(9, 18, 99.99m));

        Assert.Null(RecurringPaymentDetector.Detect(payments, Today));
    }

    [Fact]
    public void Input_order_does_not_matter()
    {
        var shuffled = Monthly(99.99m, day: 18).Reverse();

        var pattern = RecurringPaymentDetector.Detect(shuffled, Today);

        Assert.NotNull(pattern);
        Assert.Equal((new DateOnly(2026, 4, 18), new DateOnly(2026, 9, 18), new DateOnly(2026, 10, 18)),
            (pattern.FirstDate, pattern.LastDate, pattern.NextExpectedDate));
    }
}
