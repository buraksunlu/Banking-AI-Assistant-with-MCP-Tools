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
    /// period is required.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Incomplete, conflicting or unknown period parameters; the parameter name is the client's field name.
    /// </exception>
    public static PeriodKind Resolve(string? period, DateOnly? from, DateOnly? to, PeriodKind? fallback)
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

        // Exact names only, like enums in JSON. Enum.TryParse would also accept "2" or "lastmonth", and refusing every
        // value with a digit refused "Last3Months" too (a bug from API-09). A custom range comes from dates only.
        if (!Enum.GetNames<PeriodKind>().Contains(period) || period == nameof(PeriodKind.Custom))
            throw new ArgumentException("ThisMonth, LastMonth, Last3Months veya Last6Months olmalı.", nameof(period));
        return Enum.Parse<PeriodKind>(period);
    }
}
