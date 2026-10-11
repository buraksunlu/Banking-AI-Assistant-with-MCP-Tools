namespace BankingAI.Application.Analytics;

/// <summary>
/// One payment of a group (same account, currency and merchant), on the customer's local calendar day.
/// </summary>
public sealed record PaymentPoint(DateOnly Date, decimal Amount);

/// <summary>
/// Why a group counts as recurring: how many payments, their amount range and day gaps.
/// <c>NextExpectedDate</c> is an estimate (last payment + one month), never a promise.
/// </summary>
public sealed record RecurringPattern(
    int OccurrenceCount, decimal LatestAmount, decimal MinAmount, decimal MaxAmount,
    DateOnly FirstDate, DateOnly LastDate, int MinIntervalDays, int MaxIntervalDays, DateOnly NextExpectedDate);

/// <summary>
/// Deterministic monthly-payment rule (REC-01, user decisions). The backend, not the LLM, decides
/// whether a payment is recurring.
/// </summary>
/// <remarks>
/// All five conditions must hold for one group's payments:
/// 1. at least 3 payments;
/// 2+4. every gap between consecutive payments is 26–35 days. This one check also rejects extra
///      visits to the same merchant (a coffee bought 2 days later) and a skipped month (~61 days);
/// 3. every amount is within ±10 % of the group's median amount (a small price increase passes);
/// 5. the last payment is at most 35 days before today (otherwise it was probably cancelled).
/// </remarks>
public static class RecurringPaymentDetector
{
    public const int MinimumOccurrences = 3;
    public const int MinimumIntervalDays = 26;
    public const int MaximumIntervalDays = 35;
    public const decimal AmountTolerance = 0.10m;
    public const int MaximumDaysSinceLastPayment = 35;

    /// <summary>
    /// Decides whether one group's payments form an active monthly payment.
    /// </summary>
    /// <param name="payments">The group's payments, in any order.</param>
    /// <param name="today">The customer's local "today", used for the activity check.</param>
    /// <returns>The pattern with its evidence, or <c>null</c> when any condition fails.</returns>
    public static RecurringPattern? Detect(IEnumerable<PaymentPoint> payments, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(payments);
        var sorted = payments.OrderBy(p => p.Date).ToList();

        // 1. Two payments can be a coincidence.
        if (sorted.Count < MinimumOccurrences)
            return null;

        // 2 + 4. Day gaps between consecutive payments; DayNumber turns a date into a day count.
        var intervals = sorted.Zip(sorted.Skip(1), (earlier, later) => later.Date.DayNumber - earlier.Date.DayNumber).ToList();
        if (intervals.Any(days => days is < MinimumIntervalDays or > MaximumIntervalDays))
            return null;

        // 3. Amounts close to the median. The median ignores one odd value better than the average does.
        var median = Median(sorted.Select(p => p.Amount));
        if (sorted.Any(p => Math.Abs(p.Amount - median) > median * AmountTolerance))
            return null;

        // 5. Still active: a subscription that stopped months ago is not a current payment.
        var last = sorted[^1];
        if (today.DayNumber - last.Date.DayNumber > MaximumDaysSinceLastPayment)
            return null;

        return new RecurringPattern(
            sorted.Count, last.Amount, sorted.Min(p => p.Amount), sorted.Max(p => p.Amount),
            sorted[0].Date, last.Date, intervals.Min(), intervals.Max(),
            NextExpected(last.Date, sorted.Max(p => p.Date.Day)));
    }

    private static decimal Median(IEnumerable<decimal> values)
    {
        var ordered = values.Order().ToList();
        var middle = ordered.Count / 2;
        return ordered.Count % 2 == 1 ? ordered[middle] : (ordered[middle - 1] + ordered[middle]) / 2m;
    }

    // Next month on the payment's usual day. The largest observed day is used so a month-end payment
    // (31 Jan, 28 Feb, 31 Mar) is expected on 30 April, not drifting to the 28th; short months clamp.
    private static DateOnly NextExpected(DateOnly last, int usualDay)
    {
        var nextMonth = last.AddMonths(1);
        return new DateOnly(nextMonth.Year, nextMonth.Month, Math.Min(usualDay, DateTime.DaysInMonth(nextMonth.Year, nextMonth.Month)));
    }
}
