using BankingAI.Data;
using Microsoft.EntityFrameworkCore;

namespace BankingAI.Application.Analytics;

/// <summary>
/// Input of <see cref="RecurringPaymentService"/>. <c>CustomerId</c> comes from the trusted caller
/// context, never from a model argument; without <c>AccountId</c> all personal accounts are used.
/// </summary>
public sealed record RecurringPaymentQuery(Guid CustomerId, Guid? AccountId = null);

/// <summary>
/// One recurring payment. The category comes from the group's latest payment;
/// <c>Pattern</c> explains why it counts as recurring.
/// </summary>
public sealed record RecurringPayment(
    string Merchant, string? CategoryCode, string CategoryDisplayName, string AccountName, string CurrencyCode,
    RecurringPattern Pattern);

/// <summary>
/// Sum of the latest amounts: what the customer currently pays per month in one currency.
/// </summary>
public sealed record CurrencyRecurringTotal(string CurrencyCode, decimal MonthlyTotal);

/// <summary>
/// Result of <see cref="RecurringPaymentService"/>: the examined period, the recurring payments
/// (largest latest amount first) and the monthly total per currency.
/// </summary>
public sealed record RecurringPayments(
    ResolvedPeriod Period, IReadOnlyList<RecurringPayment> Payments, IReadOnlyList<CurrencyRecurringTotal> MonthlyTotals);

/// <summary>
/// Finds monthly payments from Transactions with <see cref="RecurringPaymentDetector"/>; nothing is
/// stored (no RecurringPayments table). Spending scope over the last six months: personal accounts,
/// <c>Kind = Purchase</c> only.
/// </summary>
public sealed class RecurringPaymentService(BankingDbContext db, PeriodResolver periodResolver)
{
    private readonly PurchaseScope _scope = new(db, periodResolver);

    /// <summary>
    /// Returns the customer's active monthly payments of the last six months.
    /// </summary>
    /// <exception cref="KeyNotFoundException">The customer, or the given account for this customer, does not exist.</exception>
    /// <exception cref="ArgumentException">The given account is not a personal account.</exception>
    public async Task<RecurringPayments> GetAsync(RecurringPaymentQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var scoped = await _scope.ResolveAsync(
            new SpendingQuery(query.CustomerId, PeriodKind.Last6Months, AccountId: query.AccountId), cancellationToken);

        // The rule needs each payment's date (gaps between payments), so the rows themselves are read;
        // this cannot be a single SQL aggregate. Only one customer's six months of purchases that
        // have a merchant are read; a purchase without a merchant cannot form a group.
        var rows = await (
                from t in scoped.Purchases.Where(t => t.NormalizedMerchant != null)
                join a in db.Accounts on t.AccountId equals a.Id
                join c in db.TransactionCategories on t.CategoryId equals (short?)c.Id into categories
                from c in categories.DefaultIfEmpty()
                select new
                {
                    t.AccountId, a.AccountName, t.CurrencyCode, Merchant = t.NormalizedMerchant!, t.OccurredAt, t.Amount,
                    CategoryCode = (string?)c!.Code, CategoryName = (string?)c!.DisplayName
                })
            .ToListAsync(cancellationToken);

        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(scoped.TimeZone);
        var today = scoped.Period.EndDate;
        DateOnly LocalDate(DateTimeOffset utc) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utc, timeZone).DateTime);

        // One group per account + currency + merchant; the detector decides each group on its own.
        var payments = rows
            .GroupBy(r => (r.AccountId, r.CurrencyCode, r.Merchant))
            .Select(g =>
            {
                var pattern = RecurringPaymentDetector.Detect(g.Select(r => new PaymentPoint(LocalDate(r.OccurredAt), r.Amount)), today);
                if (pattern is null)
                    return null;
                var latest = g.MaxBy(r => r.OccurredAt)!;
                return new RecurringPayment(g.Key.Merchant, latest.CategoryCode, latest.CategoryName ?? AnalyticsLabels.Unclassified,
                    latest.AccountName, g.Key.CurrencyCode, pattern);
            })
            .OfType<RecurringPayment>() // keeps only the groups the detector accepted (drops the nulls)
            .OrderByDescending(p => p.Pattern.LatestAmount)
            .ThenBy(p => p.Merchant, StringComparer.Ordinal)
            .ToList();

        var totals = payments
            .GroupBy(p => p.CurrencyCode)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new CurrencyRecurringTotal(g.Key, g.Sum(p => p.Pattern.LatestAmount)))
            .ToList();

        return new RecurringPayments(scoped.Period, payments, totals);
    }
}
