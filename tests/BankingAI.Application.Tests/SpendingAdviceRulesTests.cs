using BankingAI.Application.Analytics;
using Xunit;

namespace BankingAI.Application.Tests;

// INC-02 rules without a database. Income total 30.000 in three months = base 10.000, so 1 % of income is 100.
public sealed class SpendingAdviceRulesTests
{
    private static readonly DateOnly September = new(2026, 9, 1);

    private static AdviceCategoryInput Category(string? code, decimal amount, decimal history, decimal savings = 0m) =>
        new(code, code ?? "Sınıflandırılmamış", amount, history, savings);

    private static CurrencyAdvice Evaluate(decimal incomeTotal, params AdviceCategoryInput[] categories) =>
        SpendingAdviceRules.Evaluate("TRY", September, incomeTotal, categories);

    [Theory]
    [InlineData("GROCERY", SpendingGroup.Essential)]
    [InlineData("FOOD", SpendingGroup.Essential)]
    [InlineData("BILLS", SpendingGroup.Essential)]
    [InlineData("TRANSPORTATION", SpendingGroup.Essential)]
    [InlineData("FUEL", SpendingGroup.Essential)]
    [InlineData("HEALTH", SpendingGroup.Essential)]
    [InlineData("ENTERTAINMENT", SpendingGroup.Discretionary)]
    [InlineData("SUBSCRIPTION", SpendingGroup.Discretionary)]
    [InlineData("CLOTHING", SpendingGroup.Discretionary)]
    [InlineData("TRAVEL", SpendingGroup.Discretionary)]
    [InlineData("OTHER", SpendingGroup.Unknown)]
    [InlineData(null, SpendingGroup.Unknown)]
    public void Every_category_has_the_group_of_decision_K1_B(string? code, SpendingGroup expected) =>
        Assert.Equal(expected, SpendingGroups.Of(code));

    [Fact]
    public void A_category_missing_from_the_table_is_unknown_and_never_advised() =>
        Assert.Equal(SpendingGroup.Unknown, SpendingGroups.Of("NEW_CATEGORY"));

    [Fact]
    public void Groups_split_the_month_and_their_shares_come_from_the_exact_income()
    {
        var result = Evaluate(30000m,
            Category("GROCERY", 4000m, 24000m), Category("ENTERTAINMENT", 1000m, 6000m), Category("OTHER", 333.33m, 0m),
            Category(null, 100m, 0m));

        Assert.Equal(("TRY", 5433.33m, 30000m, (decimal?)10000m, (decimal?)54.33m), (result.CurrencyCode, result.TotalSpent, result.IncomeTotal, result.IncomeBase, result.SpentShare));
        Assert.Equal(
            [(SpendingGroup.Essential, 4000m, (decimal?)40.00m), (SpendingGroup.Discretionary, 1000m, 10.00m), (SpendingGroup.Unknown, 433.33m, 4.33m)],
            result.Groups.Select(g => (g.Group, g.Amount, g.IncomeShare)));
        Assert.Equal(
            [("GROCERY", SpendingGroup.Essential, 4000m), ("ENTERTAINMENT", SpendingGroup.Discretionary, 1000m),
             ("OTHER", SpendingGroup.Unknown, 333.33m), (null, SpendingGroup.Unknown, 100m)],
            result.Categories.Select(c => (c.CategoryCode, c.Group, c.Amount)));
        Assert.Equal(1000m, result.Categories[1].UsualMonthlyAmount);
        Assert.Empty(result.Advice);
        Assert.Equal(NoAdviceReason.NoRuleMatched, result.Reason);
    }

    // ---- K3-A: a discretionary category at least 1,5 times its usual month and at least 2 % of income above it.

    [Fact]
    public void A_category_exactly_one_and_a_half_times_its_usual_month_is_advised()
    {
        // Usual 6.000 / 6 = 1.000; 1.500 is 1,5 times; 500 above = 5 % of income.
        var advice = Assert.Single(Evaluate(30000m, Category("ENTERTAINMENT", 1500m, 6000m, 7200m)).Advice);

        Assert.Equal(
            (AdviceRule.CategoryAboveUsual, "ENTERTAINMENT", 1500m, 1000m, (decimal?)1.50m, 500m, 5.00m),
            (advice.Rule, advice.CategoryCode, advice.Amount, advice.Reference, advice.Ratio, advice.Excess, advice.ExcessShare));
        // 7.200 in the savings window: 7.200 × 20 % / 6 = 240 a month, 2.880 a year.
        Assert.Equal((20, 240m, 2880m), (advice.ReductionPercent, advice.MonthlySaving, advice.AnnualSaving));
    }

    [Fact]
    public void A_category_just_below_one_and_a_half_times_is_not_advised() =>
        Assert.Empty(Evaluate(30000m, Category("ENTERTAINMENT", 1499.99m, 6000m)).Advice);

    [Fact]
    public void An_excess_of_exactly_two_percent_of_income_is_advised()
    {
        // Usual 100, spent 300: 200 above = exactly 2 % of the 10.000 base.
        var advice = Assert.Single(Evaluate(30000m, Category("SUBSCRIPTION", 300m, 600m)).Advice);

        Assert.Equal((200m, 2.00m, (decimal?)3.00m), (advice.Excess, advice.ExcessShare, advice.Ratio));
    }

    [Fact]
    public void An_excess_just_below_two_percent_of_income_is_too_small_to_advise() =>
        Assert.Empty(Evaluate(30000m, Category("SUBSCRIPTION", 299.99m, 600m)).Advice);

    [Fact]
    public void A_discretionary_category_without_history_counts_against_a_usual_month_of_zero()
    {
        var advice = Assert.Single(Evaluate(30000m, Category("TRAVEL", 800m, 0m, 800m)).Advice);

        Assert.Equal((0m, (decimal?)null, 800m, 8.00m), (advice.Reference, advice.Ratio, advice.Excess, advice.ExcessShare));
    }

    [Theory]
    [InlineData("GROCERY")]
    [InlineData("BILLS")]
    [InlineData("FOOD")]
    [InlineData("OTHER")]
    [InlineData(null)]
    public void Essential_and_unknown_categories_are_never_advised_however_far_above_usual(string? code) =>
        // K8-A: eleven times the usual month and 30 % of income above it.
        Assert.Empty(Evaluate(30000m, Category(code, 3300m, 1800m)).Advice);

    // ---- K2-A: discretionary spending above 30 % of income, at least 2 % above the limit.

    [Fact]
    public void Discretionary_spending_two_points_above_thirty_percent_is_advised()
    {
        // Steady months (no category deviation): 2.000 + 1.200 = 3.200 = 32 % of the 10.000 base.
        var result = Evaluate(30000m,
            Category("ENTERTAINMENT", 2000m, 12000m, 12000m), Category("SUBSCRIPTION", 1200m, 7200m, 6000m));

        var advice = Assert.Single(result.Advice);
        Assert.Equal(
            (AdviceRule.DiscretionaryShare, (string?)null, (string?)null, 3200m, 3000m, (decimal?)null, 200m, 2.00m),
            (advice.Rule, advice.CategoryCode, advice.CategoryDisplayName, advice.Amount, advice.Reference, advice.Ratio, advice.Excess, advice.ExcessShare));
        // Savings window of all discretionary categories: 18.000 × 20 % / 6 = 600 a month, 7.200 a year.
        Assert.Equal((600m, 7200m), (advice.MonthlySaving, advice.AnnualSaving));
        Assert.Null(result.Reason);
    }

    [Fact]
    public void Discretionary_spending_above_thirty_but_below_thirty_two_percent_is_too_small_to_advise() =>
        Assert.Empty(Evaluate(30000m, Category("ENTERTAINMENT", 3199.99m, 19199.94m)).Advice);

    [Fact]
    public void Essential_spending_never_counts_towards_the_discretionary_limit() =>
        Assert.Empty(Evaluate(30000m, Category("FOOD", 9000m, 54000m), Category("ENTERTAINMENT", 100m, 600m)).Advice);

    // ---- K9-A: at most two, largest excess first.

    [Fact]
    public void At_most_two_pieces_of_advice_are_given_largest_excess_first()
    {
        var result = Evaluate(30000m,
            Category("ENTERTAINMENT", 2000m, 6000m),   // 1.000 above usual
            Category("CLOTHING", 1500m, 3000m),        // 1.000 above usual (tie: code order)
            Category("TRAVEL", 1000m, 0m));            // 1.000 above usual (tie)
        // Discretionary 4.500 = 45 %: 1.500 above the 3.000 limit, the largest.

        Assert.Equal(
            [(AdviceRule.DiscretionaryShare, (string?)null, 1500m), (AdviceRule.CategoryAboveUsual, "CLOTHING", 1000m)],
            result.Advice.Select(a => (a.Rule, a.CategoryCode, a.Excess)));
    }

    // ---- K9-A: no income, no advice.

    [Fact]
    public void Without_income_there_are_no_shares_and_no_advice()
    {
        var result = Evaluate(0m, Category("ENTERTAINMENT", 5000m, 600m));

        Assert.Equal(((decimal?)null, (decimal?)null), (result.IncomeBase, result.SpentShare));
        Assert.All(result.Groups, g => Assert.Null(g.IncomeShare));
        Assert.All(result.Categories, c => Assert.Null(c.IncomeShare));
        Assert.Empty(result.Advice);
        Assert.Equal(NoAdviceReason.NoIncome, result.Reason);
    }

    // ---- K7-A: fixed Turkish text written by code.

    [Fact]
    public void Category_advice_text_is_a_fixed_template_with_formatted_figures()
    {
        var advice = Assert.Single(Evaluate(24000m, new AdviceCategoryInput("ENTERTAINMENT", "Eğlence", 2253.50m, 5567m, 7085.50m)).Advice);

        Assert.Equal(
            "Eylül 2026 ayında Eğlence harcamanız 2.253,50 TL oldu; önceki 6 ayın aylık ortalaması 927,83 TL (2,43 katı). "
            + "Eğlence harcamasını %20 azaltırsanız son 6 ayın ortalamasıyla ayda 236,18 TL, yılda 2.834,20 TL kalır.",
            advice.Message);
    }

    [Fact]
    public void Category_advice_text_says_so_when_there_was_no_spending_before() =>
        Assert.Equal(
            "Eylül 2026 ayında Seyahat harcamanız 800,00 TL oldu; önceki 6 ayda bu kategoride harcamanız yoktu. "
            + "Seyahat harcamasını %20 azaltırsanız son 6 ayın ortalamasıyla ayda 26,67 TL, yılda 320,00 TL kalır.",
            Assert.Single(Evaluate(30000m, new AdviceCategoryInput("TRAVEL", "Seyahat", 800m, 0m, 800m)).Advice).Message);

    [Fact]
    public void Discretionary_share_text_names_the_rule_and_the_limit()
    {
        var advice = Assert.Single(SpendingAdviceRules.Evaluate("USD", September, 30000m,
            [new AdviceCategoryInput("ENTERTAINMENT", "Eğlence", 2000m, 12000m, 12000m),
             new AdviceCategoryInput("SUBSCRIPTION", "Abonelik", 1200m, 7200m, 6000m)]).Advice);

        Assert.Equal(
            "Eylül 2026 ayında isteğe bağlı harcamalarınız 3.200,00 USD oldu (gelirinizin %32,00 kadarı); 50/30/20 kuralındaki "
            + "%30 sınırını 200,00 USD aşıyor. İsteğe bağlı harcamaları %20 azaltırsanız son 6 ayın ortalamasıyla ayda "
            + "600,00 USD, yılda 7.200,00 USD kalır.",
            advice.Message);
    }

    // ---- K5-A: the saving arithmetic is ANA-14's.

    [Fact]
    public void The_saving_arithmetic_is_shared_with_the_savings_simulation() =>
        // 7.085,50 × 20 % / 6 = 236,183… → 236,18; × 12 = 2.834,20 from the exact total.
        Assert.Equal((236.18m, 2834.20m), SavingsSimulationService.Saving(7085.50m, 6, 20));
}
