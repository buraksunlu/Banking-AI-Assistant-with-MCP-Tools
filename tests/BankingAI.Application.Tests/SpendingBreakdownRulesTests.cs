using BankingAI.Application.Analytics;
using BankingAI.Data.Enums;
using Xunit;

namespace BankingAI.Application.Tests;

// DEMO-02 K5-A: a purchase joins the comparison once it is 7 days old; younger ones are only reported as left out.
public sealed class SpendingBreakdownRulesTests
{
    private static readonly DateOnly Today = new(2026, 11, 19);

    private static TransactionItem Purchase(DateOnly day, decimal amount) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "Hesap", default, day, TransactionKind.Purchase, TransactionDirection.Debit, amount, "TRY",
            "Trendyol", "CLOTHING", "Giyim", "TRENDYOL ONLINE SIPARIS", null, false);

    [Fact]
    public void Seven_days_old_joins_and_six_days_old_is_left_out()
    {
        var (old, excluded) = SpendingBreakdownRules.Investable(
            [Purchase(Today.AddDays(-7), 100m), Purchase(Today.AddDays(-6), 40m), Purchase(Today, 2.5m)], Today);

        Assert.Equal([100m], old.Select(i => i.Amount));
        Assert.Equal(42.5m, excluded);
    }

    [Fact]
    public void Everything_recent_leaves_nothing_to_compare()
    {
        var (old, excluded) = SpendingBreakdownRules.Investable([Purchase(Today.AddDays(-1), 300m)], Today);

        Assert.Empty(old);
        Assert.Equal(300m, excluded);
    }
}
