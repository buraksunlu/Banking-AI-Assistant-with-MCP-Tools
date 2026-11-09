using System.Globalization;

namespace BankingAI.Application.Analytics;

/// <summary>Which rule produced a piece of advice.</summary>
public enum AdviceRule
{
    /// <summary>Discretionary spending is above the 50/30/20 limit of 30 % of income (K2-A).</summary>
    DiscretionaryShare,

    /// <summary>A discretionary category is well above its usual month (K3-A).</summary>
    CategoryAboveUsual,
}

/// <summary>Why a currency has no advice.</summary>
public enum NoAdviceReason
{
    /// <summary>No salary in the income window, so nothing can be compared with income (K9-A).</summary>
    NoIncome,

    /// <summary>Spending is within every rule; no advice is given on purpose ("her şeye öneri vermeyiz").</summary>
    NoRuleMatched,
}

/// <summary>
/// The fixed thresholds, returned with every result so the reader sees why advice was or was not given.
/// </summary>
public sealed record AdviceThresholds(
    int DiscretionaryLimitPercent, decimal UsualRatio, int MinimumImpactPercent, int UsualMonths, int ReductionPercent,
    int MaximumAdvice);

/// <summary>
/// One category of the month as the rules see it: its amount, its total over the usual months before the month
/// (<c>UsualTotal</c>) and its total over the savings window (<c>SavingsTotal</c>).
/// </summary>
public sealed record AdviceCategoryInput(
    string? CategoryCode, string DisplayName, decimal Amount, decimal UsualTotal, decimal SavingsTotal);

/// <summary>One group's spending; the share is null without income.</summary>
public sealed record GroupSpending(SpendingGroup Group, decimal Amount, decimal? IncomeShare);

/// <summary>One category of the month with its group and its usual monthly amount (usual total / usual months).</summary>
public sealed record AdviceCategory(
    string? CategoryCode, string DisplayName, SpendingGroup Group, decimal Amount, decimal? IncomeShare, decimal UsualMonthlyAmount);

/// <summary>
/// One piece of advice. <c>Amount</c> is the month's spending the rule looked at, <c>Reference</c> what it is compared with
/// (30 % of income, or the usual month), <c>Excess</c> the difference and <c>ExcessShare</c> that difference as a share of
/// income. <c>Ratio</c> is <c>Amount</c> / usual month, null for the share rule and for a category with no usual spending.
/// The saving is ANA-14's arithmetic over the savings window. <c>Message</c> is a fixed Turkish template filled by code.
/// </summary>
public sealed record AdviceItem(
    AdviceRule Rule, string? CategoryCode, string? CategoryDisplayName, decimal Amount, decimal Reference, decimal? Ratio,
    decimal Excess, decimal ExcessShare, int ReductionPercent, decimal MonthlySaving, decimal AnnualSaving, string Message);

/// <summary>
/// One currency of the month: the income figures of INC-01, the three groups, the categories, at most
/// <see cref="SpendingAdviceRules.MaximumAdvice"/> pieces of advice and, when there is none, the reason.
/// </summary>
public sealed record CurrencyAdvice(
    string CurrencyCode, decimal TotalSpent, decimal IncomeTotal, decimal? IncomeBase, decimal? SpentShare,
    IReadOnlyList<GroupSpending> Groups, IReadOnlyList<AdviceCategory> Categories, IReadOnlyList<AdviceItem> Advice,
    NoAdviceReason? Reason);

/// <summary>
/// The INC-02 rules as pure code: no database, no model. Advice is given only when a rule holds and its effect is at least
/// <see cref="MinimumImpactPercent"/> % of income; the largest excess first, at most <see cref="MaximumAdvice"/>. Comparisons use
/// exact amounts; only reported figures are rounded.
/// </summary>
public static class SpendingAdviceRules
{
    public const int DiscretionaryLimitPercent = 30;
    public const decimal UsualRatio = 1.5m;
    public const int MinimumImpactPercent = 2;
    public const int UsualMonths = 6;
    public const int ReductionPercent = 20;
    public const int MaximumAdvice = 2;

    /// <summary>The thresholds above as one value.</summary>
    public static AdviceThresholds Thresholds { get; } = new(
        DiscretionaryLimitPercent, UsualRatio, MinimumImpactPercent, UsualMonths, ReductionPercent, MaximumAdvice);

    /// <summary>Months of the savings window ending with the month (ANA-14's default).</summary>
    public const int SavingsMonths = SavingsSimulationService.DefaultMonths;

    private static readonly NumberFormatInfo TurkishNumbers = new() { NumberGroupSeparator = ".", NumberDecimalSeparator = "," };

    // Written out so the text does not depend on the machine's ICU data.
    private static readonly string[] TurkishMonths =
        ["Ocak", "Şubat", "Mart", "Nisan", "Mayıs", "Haziran", "Temmuz", "Ağustos", "Eylül", "Ekim", "Kasım", "Aralık"];

    /// <summary>Groups, categories and advice of one currency for the month starting on <paramref name="month"/>.</summary>
    public static CurrencyAdvice Evaluate(
        string currencyCode, DateOnly month, decimal incomeTotal, IReadOnlyList<AdviceCategoryInput> categories)
    {
        ArgumentNullException.ThrowIfNull(categories);
        decimal? Share(decimal amount) => IncomeShareService.Share(amount, incomeTotal);

        var rows = categories
            .Select(c => new AdviceCategory(c.CategoryCode, c.DisplayName, SpendingGroups.Of(c.CategoryCode), c.Amount,
                Share(c.Amount), Round(c.UsualTotal / UsualMonths)))
            .ToList();
        var groups = Enum.GetValues<SpendingGroup>()
            .Select(g =>
            {
                var amount = rows.Where(r => r.Group == g).Sum(r => r.Amount);
                return new GroupSpending(g, amount, Share(amount));
            })
            .ToList();
        var total = rows.Sum(r => r.Amount);
        var incomeBase = incomeTotal > 0m ? Round(incomeTotal / IncomeShareService.IncomeMonths) : (decimal?)null;
        if (incomeTotal <= 0m)
            return new CurrencyAdvice(currencyCode, total, incomeTotal, null, null, groups, rows, [], NoAdviceReason.NoIncome);

        var text = new Text(currencyCode, month);
        var candidates = new List<(decimal Excess, AdviceItem Item)>();
        var discretionary = categories.Where(c => SpendingGroups.Of(c.CategoryCode) == SpendingGroup.Discretionary).ToList();

        // K2-A: the 50/30/20 limit on the exact income, not on the rounded base.
        var spent = discretionary.Sum(c => c.Amount);
        var limit = incomeTotal * DiscretionaryLimitPercent / (100m * IncomeShareService.IncomeMonths);
        if (IsLargeEnough(spent - limit, incomeTotal))
        {
            var (monthly, annual) = SavingsSimulationService.Saving(discretionary.Sum(c => c.SavingsTotal), SavingsMonths, ReductionPercent);
            var excess = spent - limit;
            candidates.Add((excess, new AdviceItem(
                AdviceRule.DiscretionaryShare, null, null, spent, Round(limit), null, Round(excess), Share(excess)!.Value,
                ReductionPercent, monthly, annual, text.DiscretionaryShare(spent, Share(spent)!.Value, excess, monthly, annual))));
        }

        // K3-A: only discretionary categories; essential spending is never questioned (K8-A).
        foreach (var c in discretionary)
        {
            // amount ≥ 1,5 × usual total / 6, compared without dividing.
            if (c.Amount * UsualMonths < UsualRatio * c.UsualTotal)
                continue;
            var usual = c.UsualTotal / UsualMonths;
            var excess = c.Amount - usual;
            if (!IsLargeEnough(excess, incomeTotal))
                continue;
            var ratio = c.UsualTotal > 0m ? Round(c.Amount * UsualMonths / c.UsualTotal) : (decimal?)null;
            var (monthly, annual) = SavingsSimulationService.Saving(c.SavingsTotal, SavingsMonths, ReductionPercent);
            candidates.Add((excess, new AdviceItem(
                AdviceRule.CategoryAboveUsual, c.CategoryCode, c.DisplayName, c.Amount, Round(usual), ratio, Round(excess),
                Share(excess)!.Value, ReductionPercent, monthly, annual,
                text.CategoryAboveUsual(c.DisplayName, c.Amount, usual, ratio, monthly, annual))));
        }

        // K9-A: the largest effect in money first; ties by code, so the order never depends on input order. The share rule has
        // no code (null sorts first), so it wins a tie with a category.
        var advice = candidates
            .OrderByDescending(a => a.Excess)
            .ThenBy(a => a.Item.CategoryCode, StringComparer.Ordinal)
            .Take(MaximumAdvice)
            .Select(a => a.Item)
            .ToList();
        return new CurrencyAdvice(currencyCode, total, incomeTotal, incomeBase, Share(total), groups, rows, advice,
            advice.Count == 0 ? NoAdviceReason.NoRuleMatched : null);
    }

    // At least MinimumImpactPercent of the monthly income base: excess × 3 × 100 ≥ 2 × income total.
    private static bool IsLargeEnough(decimal excess, decimal incomeTotal) =>
        excess > 0m && excess * IncomeShareService.IncomeMonths * 100m >= MinimumImpactPercent * incomeTotal;

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    // Fixed templates (K7-A). Sentences avoid Turkish suffixes on names and years ("Eylül 2026 ayında", "Eğlence harcamasını"),
    // which would otherwise change with every category and year.
    private sealed class Text(string currencyCode, DateOnly month)
    {
        private readonly string _month = $"{TurkishMonths[month.Month - 1]} {month.Year}";

        public string DiscretionaryShare(decimal spent, decimal share, decimal excess, decimal monthly, decimal annual) =>
            $"{_month} ayında isteğe bağlı harcamalarınız {Money(spent)} oldu (gelirinizin %{Number(share)} kadarı); "
            + $"50/30/20 kuralındaki %{DiscretionaryLimitPercent} sınırını {Money(excess)} aşıyor. "
            + $"İsteğe bağlı harcamaları %{ReductionPercent} azaltırsanız son {SavingsMonths} ayın ortalamasıyla ayda "
            + $"{Money(monthly)}, yılda {Money(annual)} kalır.";

        public string CategoryAboveUsual(string name, decimal amount, decimal usual, decimal? ratio, decimal monthly, decimal annual) =>
            $"{_month} ayında {name} harcamanız {Money(amount)} oldu; "
            + (ratio is { } r
                ? $"önceki {UsualMonths} ayın aylık ortalaması {Money(usual)} ({Number(r)} katı). "
                : $"önceki {UsualMonths} ayda bu kategoride harcamanız yoktu. ")
            + $"{name} harcamasını %{ReductionPercent} azaltırsanız son {SavingsMonths} ayın ortalamasıyla ayda "
            + $"{Money(monthly)}, yılda {Money(annual)} kalır.";

        private string Money(decimal value) => $"{Number(value)} {(currencyCode == "TRY" ? "TL" : currencyCode)}";

        private static string Number(decimal value) => Round(value).ToString("#,##0.00", TurkishNumbers);
    }
}
