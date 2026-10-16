using BankingAI.Data.Validation;

namespace BankingAI.Application.Analytics;

/// <summary>
/// Input of <see cref="UpcomingPaymentService"/>: how many days ahead to look (1–31, default 30) and optionally one
/// personal account. <c>CustomerId</c> comes from the trusted caller context.
/// </summary>
public sealed record UpcomingPaymentQuery(Guid CustomerId, int Days = UpcomingPaymentService.DefaultDays, Guid? AccountId = null);

/// <summary>
/// One expected payment. Date and amount are estimates: the date is the recurring rule's next expected date (last
/// payment + one month on its usual day) and the amount is the latest payment's. <c>IsOverdue</c> marks an expected
/// date that has passed without a payment yet; <c>DaysUntil</c> is then negative. <c>Pattern</c> is the evidence.
/// </summary>
public sealed record UpcomingPayment(
    string Merchant, string? CategoryCode, string CategoryDisplayName, string AccountName, string CurrencyCode,
    DateOnly EstimatedDate, decimal EstimatedAmount, int DaysUntil, bool IsOverdue, RecurringPattern Pattern);

/// <summary>Sum of the estimated amounts in one currency.</summary>
public sealed record CurrencyUpcomingTotal(string CurrencyCode, decimal EstimatedTotal);

/// <summary>
/// Result of <see cref="UpcomingPaymentService"/>: the window <c>From</c> (today) – <c>To</c> (today + days, inclusive),
/// the expected payments (earliest first, overdue ones included) and the estimated total per currency.
/// </summary>
public sealed record UpcomingPayments(
    DateOnly From, DateOnly To, IReadOnlyList<UpcomingPayment> Payments, IReadOnlyList<CurrencyUpcomingTotal> Totals);

/// <summary>
/// Expected payments of the next days (ANA-12), built on <see cref="RecurringPaymentService"/>: which payments are
/// monthly is decided only by <see cref="RecurringPaymentDetector"/>; this service only picks the expected dates
/// that fall into the window. No query of its own.
/// </summary>
public sealed class UpcomingPaymentService(RecurringPaymentService recurring)
{
    public const int DefaultDays = 30;
    public const int MaximumDays = 31;

    /// <summary>Returns the payments expected up to <c>Days</c> days ahead, overdue ones included.</summary>
    /// <exception cref="ArgumentException"><c>Days</c> is outside 1–31, or the account is not a personal account.</exception>
    /// <exception cref="KeyNotFoundException">The customer, or the given account for this customer, does not exist.</exception>
    public async Task<UpcomingPayments> GetAsync(UpcomingPaymentQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        InputRules.Require(query.Days is >= 1 and <= MaximumDays, nameof(query.Days), $"1 ile {MaximumDays} arasında olmalı.");

        var found = await recurring.GetAsync(new RecurringPaymentQuery(query.CustomerId, query.AccountId), cancellationToken);
        // The recurring look-back ends on the customer's local today, so the time-zone rule is not repeated here.
        var today = found.Period.EndDate;
        var to = today.AddDays(query.Days);

        // No lower bound: an expected date that has passed is still a payment to come (decision K1-A). The recurring
        // rule drops it by itself once the last payment is more than 35 days old.
        var payments = found.Payments
            .Where(p => p.Pattern.NextExpectedDate <= to)
            .Select(p => new UpcomingPayment(
                p.Merchant, p.CategoryCode, p.CategoryDisplayName, p.AccountName, p.CurrencyCode,
                p.Pattern.NextExpectedDate, p.Pattern.LatestAmount,
                p.Pattern.NextExpectedDate.DayNumber - today.DayNumber, p.Pattern.NextExpectedDate < today, p.Pattern))
            .OrderBy(p => p.EstimatedDate)
            .ThenBy(p => p.Merchant, StringComparer.Ordinal)
            .ToList();

        var totals = payments
            .GroupBy(p => p.CurrencyCode)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new CurrencyUpcomingTotal(g.Key, g.Sum(p => p.EstimatedAmount)))
            .ToList();

        return new UpcomingPayments(today, to, payments, totals);
    }
}
