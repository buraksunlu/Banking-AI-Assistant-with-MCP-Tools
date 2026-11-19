using BankingAI.Application.Analytics;
using Xunit;

namespace BankingAI.Application.Tests;

// DEMO-02 K3-A, K4-A: a merchant of the non-routine discretionary spending is advised when the period is at least 1,5 times its
// usual month (the six full months before) and the difference is at least 500; only for periods of at least 28 days; two at most.
public sealed class HabitAdviceRulesTests
{
    [Fact]
    public void A_merchant_well_above_its_usual_month_is_advised()
    {
        var advice = Assert.Single(HabitAdviceRules.Evaluate([new HabitInput("Trendyol", 3, 6100m, 12000m)], periodDays: 31));

        Assert.Equal(("Trendyol", 3, 6100m, 2000m, 4100m), (advice.Merchant, advice.Count, advice.Amount, advice.UsualMonthly, advice.Excess));
        Assert.Equal("Trendyol için bu dönemde 3 kez 6.100,00 TL harcadınız; son 6 ayın aylık ortalaması 2.000,00 TL. "
            + "Ortalamanızda kalsaydınız 4.100,00 TL size kalırdı.", advice.Message);
    }

    [Fact]
    public void The_limits_are_inclusive()
    {
        // 1.500 = 1,5 × 1.000 and the difference is exactly 500.
        Assert.Single(HabitAdviceRules.Evaluate([new HabitInput("Zara", 1, 1500m, 6000m)], 30));
        Assert.Empty(HabitAdviceRules.Evaluate([new HabitInput("Zara", 1, 1499.99m, 6000m)], 30));
        // 1,5 times, but only 499,99 more.
        Assert.Empty(HabitAdviceRules.Evaluate([new HabitInput("D&R", 2, 1499.97m, 5999.88m)], 30));
    }

    [Fact]
    public void Two_at_most_and_the_largest_difference_first()
    {
        var advice = HabitAdviceRules.Evaluate(
            [new HabitInput("A", 1, 3000m, 6000m), new HabitInput("B", 1, 9000m, 12000m), new HabitInput("C", 1, 5000m, 6000m)], 30);

        Assert.Equal(["B", "C"], advice.Select(a => a.Merchant));
    }

    [Fact]
    public void A_short_period_gets_no_advice() =>
        Assert.Empty(HabitAdviceRules.Evaluate([new HabitInput("Trendyol", 3, 6100m, 12000m)], periodDays: 27));

    [Fact]
    public void A_merchant_new_in_the_period_is_advised_on_its_whole_amount()
    {
        var advice = Assert.Single(HabitAdviceRules.Evaluate([new HabitInput("Biletix", 1, 2800m, 0m)], 31));

        Assert.Equal((0m, 2800m), (advice.UsualMonthly, advice.Excess));
        // "an average of 0,00 TL" reads like an error; a merchant new in the period is named as new.
        Assert.Equal("Biletix için bu dönemde 1 kez 2.800,00 TL harcadınız; son 6 ayda bu işyerine harcamanız yoktu. "
            + "Bu harcamayı yapmasaydınız 2.800,00 TL size kalırdı.", advice.Message);
        Assert.EndsWith("Bu harcamaları yapmasaydınız 3.000,00 TL size kalırdı.",
            Assert.Single(HabitAdviceRules.Evaluate([new HabitInput("Zara", 2, 3000m, 0m)], 31)).Message);
    }
}
