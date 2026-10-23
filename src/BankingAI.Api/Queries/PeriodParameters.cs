using BankingAI.Application.Analytics;

namespace BankingAI.Api.Queries;

/// <summary>
/// Reads the period of a request from its query string the same way for every endpoint: either a symbolic
/// <c>period</c> by its exact name, or both <c>from</c> and <c>to</c> (local dates, inclusive).
/// </summary>
public static class PeriodParameters
{
    /// <summary>
    /// The period the query asks for, or <paramref name="fallback"/> when it names none. Without a fallback a
    /// period is required. <see cref="PeriodKind.AllTime"/> is accepted only where <paramref name="allowAllTime"/>
    /// says so: a transaction list pages through the whole history, an analysis does not (API-17).
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Incomplete, conflicting or unknown period parameters; the parameter name is the client's field name.
    /// </exception>
    public static PeriodKind Resolve(string? period, DateOnly? from, DateOnly? to, PeriodKind? fallback, bool allowAllTime = false)
    {
        if ((from is null) != (to is null))
            throw new ArgumentException("Başlangıç ve bitiş tarihi birlikte verilmeli.", nameof(from));
        if (from is not null)
        {
            if (period is not null)
                throw new ArgumentException("Dönem adı ile tarih aralığı birlikte verilemez.", nameof(period));
            return PeriodKind.Custom;
        }
        if (period is null)
            return fallback ?? throw new ArgumentException("Dönem zorunlu: period ya da from ile to verilmeli.", nameof(period));

        // Exact names only. Refusing every value with a digit refused "Last3Months" too (a bug from API-09). A custom
        // range comes from dates only.
        const string names = "ThisMonth, LastMonth, Last3Months, Last6Months, Today, Yesterday, ThisWeek, LastWeek, Last7Days, Last30Days";
        return allowAllTime
            ? ExactEnum.Parse(period, nameof(period), $"{names}, ThisYear veya AllTime olmalı.", PeriodKind.Custom)!.Value
            : ExactEnum.Parse(period, nameof(period), $"{names} veya ThisYear olmalı.", PeriodKind.Custom, PeriodKind.AllTime)!.Value;
    }

    /// <summary>
    /// The comparison the query asks for (required): either a <c>comparison</c> by its exact name, or all four
    /// dates, which mean <see cref="ComparisonKind.Custom"/>. The dates themselves are checked by the resolver.
    /// </summary>
    /// <exception cref="ArgumentException">Missing, incomplete, conflicting or unknown comparison parameters.</exception>
    public static ComparisonKind ResolveComparison(
        string? comparison, DateOnly? currentFrom, DateOnly? currentTo, DateOnly? previousFrom, DateOnly? previousTo)
    {
        var dates = new[] { currentFrom, currentTo, previousFrom, previousTo }.Count(d => d is not null);
        if (dates is not (0 or 4))
            throw new ArgumentException(
                "Dört tarih birlikte verilmeli: currentFrom, currentTo, previousFrom ve previousTo.", nameof(currentFrom));
        if (dates == 4)
        {
            if (comparison is not null)
                throw new ArgumentException("Karşılaştırma adı ile tarih aralıkları birlikte verilemez.", nameof(comparison));
            return ComparisonKind.Custom;
        }
        if (comparison is null)
            throw new ArgumentException(
                "Karşılaştırma zorunlu: comparison ya da currentFrom, currentTo, previousFrom ve previousTo verilmeli.",
                nameof(comparison));

        return ExactEnum.Parse<ComparisonKind>(comparison, nameof(comparison), "PreviousFullMonth veya MonthToDate olmalı.", ComparisonKind.Custom)!.Value;
    }

    /// <summary>
    /// The number of complete months of a monthly trend (required, decision K2-A). Its range is the service's rule.
    /// </summary>
    /// <exception cref="ArgumentException">No number of months was given.</exception>
    public static int TrendMonths(int? months) =>
        months ?? throw new ArgumentException(
            $"Ay sayısı zorunlu: months 1 ile {PeriodResolver.MaximumTrendMonths} arasında verilmeli.", nameof(months));
}
