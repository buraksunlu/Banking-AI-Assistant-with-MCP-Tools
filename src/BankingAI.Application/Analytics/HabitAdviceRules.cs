using System.Globalization;

namespace BankingAI.Application.Analytics;

/// <summary>
/// One merchant of a period's non-routine discretionary spending: how often and how much in the period, and its total over the
/// <see cref="HabitAdviceRules.UsualMonths"/> full months before the period.
/// </summary>
public sealed record HabitInput(string Merchant, int Count, decimal Amount, decimal UsualTotal);

/// <summary>
/// One piece of habit advice: the merchant's period, its usual month, the difference and a fixed Turkish sentence filled by code.
/// </summary>
public sealed record HabitAdvice(string Merchant, int Count, decimal Amount, decimal UsualMonthly, decimal Excess, string Message);

/// <summary>
/// DEMO-02 K3-A and K4-A as pure code: a merchant is advised when the period is at least <see cref="UsualRatio"/> times its usual month
/// and the difference is at least <see cref="MinimumExcess"/>; only for periods of at least <see cref="MinimumPeriodDays"/> days (a day
/// compared with a month says nothing); the largest difference first, at most <see cref="MaximumAdvice"/>.
/// </summary>
public static class HabitAdviceRules
{
    public const decimal UsualRatio = 1.5m;
    public const decimal MinimumExcess = 500m;
    public const int UsualMonths = 6;
    public const int MinimumPeriodDays = 28;
    public const int MaximumAdvice = 2;

    private static readonly NumberFormatInfo TurkishNumbers = new() { NumberGroupSeparator = ".", NumberDecimalSeparator = "," };

    /// <summary>The advice for the period's merchants; empty for a short period or when no merchant passes both limits.</summary>
    public static IReadOnlyList<HabitAdvice> Evaluate(IEnumerable<HabitInput> inputs, int periodDays)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        if (periodDays < MinimumPeriodDays)
            return [];
        return inputs
            .Select(i => (Input: i, Usual: Math.Round(i.UsualTotal / UsualMonths, 2, MidpointRounding.AwayFromZero)))
            // Exact amounts are compared; only the reported usual month is rounded.
            .Where(x => x.Input.Amount >= x.Input.UsualTotal / UsualMonths * UsualRatio
                && x.Input.Amount - x.Input.UsualTotal / UsualMonths >= MinimumExcess)
            .Select(x => (x.Input, x.Usual, Excess: x.Input.Amount - x.Usual))
            .OrderByDescending(x => x.Excess).ThenBy(x => x.Input.Merchant, StringComparer.Ordinal)
            .Take(MaximumAdvice)
            .Select(x => new HabitAdvice(x.Input.Merchant, x.Input.Count, x.Input.Amount, x.Usual, x.Excess,
                $"{x.Input.Merchant} için bu dönemde {x.Input.Count} kez {Money(x.Input.Amount)} harcadınız; "
                + $"son {UsualMonths} ayın aylık ortalaması {Money(x.Usual)}. Ortalamanızda kalsaydınız {Money(x.Excess)} size kalırdı."))
            .ToList();
    }

    private static string Money(decimal value) => $"{value.ToString("N2", TurkishNumbers)} TL";
}
