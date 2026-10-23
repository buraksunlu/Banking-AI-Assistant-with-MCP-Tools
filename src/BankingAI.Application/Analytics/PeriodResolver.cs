namespace BankingAI.Application.Analytics;

// StartUtc dahil, EndUtc hariç: OccurredAt >= StartUtc AND OccurredAt < EndUtc.
// StartDate/EndDate müşterinin yerel takvimindeki dahil günlerdir; cevapta gösterilir.
public sealed record ResolvedPeriod(
    PeriodKind Kind, DateTimeOffset StartUtc, DateTimeOffset EndUtc, DateOnly StartDate, DateOnly EndDate);

public enum ComparisonKind
{
    PreviousFullMonth = 1, // last complete month vs the month before it (default for "geçen aya göre")
    MonthToDate = 2,       // this month so far vs the same days of the previous month
    Custom = 3             // two explicit, non-overlapping ranges
}

public sealed record ComparisonPeriods(ResolvedPeriod Current, ResolvedPeriod Previous);

// One calendar month in the customer's time zone; IsPartial marks the running month (up to today).
public sealed record MonthWindow(int Year, int Month, bool IsPartial, ResolvedPeriod Period);

public sealed class PeriodResolver(TimeProvider timeProvider)
{
    public const int MaximumCustomMonths = 24;
    public const int MaximumTrendMonths = 24;

    public ResolvedPeriod Resolve(PeriodKind kind, string timeZoneId, DateOnly? from = null, DateOnly? to = null)
    {
        if (!Enum.IsDefined(kind))
            throw new ArgumentException("Bilinmeyen dönem türü.", nameof(kind));
        // A total over "everything" is not a question the analyses answer; only a transaction list pages through it.
        if (kind == PeriodKind.AllTime)
            throw new ArgumentException("Bütün geçmiş (AllTime) yalnızca işlem listesinde kullanılabilir.", "period");
        if (kind != PeriodKind.Custom && (from is not null || to is not null))
            throw new ArgumentException("Başlangıç/bitiş tarihi yalnızca Custom dönemde verilebilir.", nameof(from));

        var timeZone = FindTimeZone(timeZoneId);
        var today = Today(timeZone);
        var monthStart = new DateOnly(today.Year, today.Month, 1);

        var (start, end) = kind switch
        {
            PeriodKind.ThisMonth => (monthStart, today),
            PeriodKind.LastMonth => (monthStart.AddMonths(-1), monthStart.AddDays(-1)),
            // DateOnly.AddMonths kısa ayda son güne sabitlenir: 31 Mayıs - 3 ay = 28 Şubat.
            PeriodKind.Last3Months => (today.AddMonths(-3), today),
            PeriodKind.Last6Months => (today.AddMonths(-6), today),
            PeriodKind.Today => (today, today),
            PeriodKind.Yesterday => (today.AddDays(-1), today.AddDays(-1)),
            PeriodKind.ThisWeek => (WeekStart(today), today),
            PeriodKind.LastWeek => (WeekStart(today).AddDays(-7), WeekStart(today).AddDays(-1)),
            // "Son 7 gün" counts today as one of the seven (K3-A), unlike Last3Months, which starts on the same day
            // three months back. Both rules are written down in docs/analytics.md.
            PeriodKind.Last7Days => (today.AddDays(-6), today),
            PeriodKind.Last30Days => (today.AddDays(-29), today),
            PeriodKind.ThisYear => (new DateOnly(today.Year, 1, 1), today),
            _ => ValidateCustom(from, to, today)
        };

        return Build(kind, start, end, timeZone);
    }

    /// <summary>
    /// The whole history of a transaction list (API-17): from the customer's local day of
    /// <paramref name="oldestUtc"/> through today, or only today when there is no transaction. Its start depends on
    /// the data, so the list resolves it after counting; <see cref="Resolve"/> refuses it.
    /// </summary>
    public ResolvedPeriod ResolveAllTime(string timeZoneId, DateTimeOffset? oldestUtc)
    {
        var timeZone = FindTimeZone(timeZoneId);
        var today = Today(timeZone);
        var start = oldestUtc is { } oldest
            ? DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(oldest, timeZone).DateTime)
            : today;
        return Build(PeriodKind.AllTime, start < today ? start : today, today, timeZone);
    }

    public ComparisonPeriods ResolveComparison(ComparisonKind kind, string timeZoneId,
        DateOnly? currentFrom = null, DateOnly? currentTo = null, DateOnly? previousFrom = null, DateOnly? previousTo = null)
    {
        if (!Enum.IsDefined(kind))
            throw new ArgumentException("Bilinmeyen karşılaştırma türü.", nameof(kind));
        var anyDate = currentFrom is not null || currentTo is not null || previousFrom is not null || previousTo is not null;
        if (kind != ComparisonKind.Custom && anyDate)
            throw new ArgumentException("Tarih aralıkları yalnızca Custom karşılaştırmada verilebilir.", nameof(currentFrom));

        var timeZone = FindTimeZone(timeZoneId);
        var today = Today(timeZone);
        var monthStart = new DateOnly(today.Year, today.Month, 1);

        switch (kind)
        {
            case ComparisonKind.PreviousFullMonth:
            {
                var lastMonth = monthStart.AddMonths(-1);
                var monthBefore = monthStart.AddMonths(-2);
                return new ComparisonPeriods(
                    Build(PeriodKind.LastMonth, lastMonth, monthStart.AddDays(-1), timeZone),
                    Build(PeriodKind.Custom, monthBefore, lastMonth.AddDays(-1), timeZone));
            }
            case ComparisonKind.MonthToDate:
            {
                // Same number of days in the previous month, clamped to its last day (31 Mar → 28 Feb).
                var previousStart = monthStart.AddMonths(-1);
                var previousEnd = previousStart.AddDays(Math.Min(today.Day, DateTime.DaysInMonth(previousStart.Year, previousStart.Month)) - 1);
                return new ComparisonPeriods(
                    Build(PeriodKind.ThisMonth, monthStart, today, timeZone),
                    Build(PeriodKind.Custom, previousStart, previousEnd, timeZone));
            }
            default:
            {
                var (currentStart, currentEnd) = ValidateCustom(currentFrom, currentTo, today);
                var (previousStart, previousEnd) = ValidateCustom(previousFrom, previousTo, today);
                // Overlapping ranges would count the same purchase in both periods.
                if (currentStart <= previousEnd && previousStart <= currentEnd)
                    throw new ArgumentException("Karşılaştırılan dönemler çakışmamalı.", nameof(previousFrom));
                return new ComparisonPeriods(
                    Build(PeriodKind.Custom, currentStart, currentEnd, timeZone),
                    Build(PeriodKind.Custom, previousStart, previousEnd, timeZone));
            }
        }
    }

    // The last N complete calendar months plus the running month (IsPartial), oldest first.
    public IReadOnlyList<MonthWindow> ResolveMonthlyWindows(int fullMonths, string timeZoneId)
    {
        if (fullMonths is < 1 or > MaximumTrendMonths)
            throw new ArgumentException($"Ay sayısı 1 ile {MaximumTrendMonths} arasında olmalı.", nameof(fullMonths));

        var timeZone = FindTimeZone(timeZoneId);
        var today = Today(timeZone);
        var monthStart = new DateOnly(today.Year, today.Month, 1);

        var windows = new List<MonthWindow>(fullMonths + 1);
        for (var offset = fullMonths; offset >= 1; offset--)
        {
            var start = monthStart.AddMonths(-offset);
            windows.Add(new MonthWindow(start.Year, start.Month, false,
                Build(PeriodKind.Custom, start, start.AddMonths(1).AddDays(-1), timeZone)));
        }
        windows.Add(new MonthWindow(monthStart.Year, monthStart.Month, true, Build(PeriodKind.ThisMonth, monthStart, today, timeZone)));
        return windows;
    }

    // Weeks start on Monday (K2-A), as in Türkiye and ISO 8601; DayOfWeek counts Sunday as 0.
    private static DateOnly WeekStart(DateOnly day) => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));

    private DateOnly Today(TimeZoneInfo timeZone) =>
        // "Bugün" sunucu saatinden değil, müşterinin saat diliminden belirlenir.
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), timeZone).DateTime);

    private static ResolvedPeriod Build(PeriodKind kind, DateOnly start, DateOnly end, TimeZoneInfo timeZone) =>
        new(kind, StartOfDayUtc(start, timeZone), StartOfDayUtc(end.AddDays(1), timeZone), start, end);

    private static (DateOnly Start, DateOnly End) ValidateCustom(DateOnly? from, DateOnly? to, DateOnly today)
    {
        if (from is not { } start || to is not { } end)
            throw new ArgumentException("Custom dönem için başlangıç ve bitiş tarihi zorunlu.", nameof(from));
        if (start > end)
            throw new ArgumentException("Başlangıç tarihi bitiş tarihinden sonra olamaz.", nameof(from));
        if (end > today)
            throw new ArgumentException("Bitiş tarihi gelecekte olamaz.", nameof(to));
        if (start < end.AddMonths(-MaximumCustomMonths))
            throw new ArgumentException($"Dönem en fazla {MaximumCustomMonths} ay olabilir.", nameof(from));
        return (start, end);
    }

    private static TimeZoneInfo FindTimeZone(string timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
            throw new ArgumentException("Saat dilimi zorunlu.", nameof(timeZoneId));
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new ArgumentException("Tanınmayan saat dilimi.", nameof(timeZoneId), ex);
        }
    }

    // Not: yerel gece yarısının DST geçişine denk geldiği bölgelerde ConvertTimeToUtc hata verir.
    // Europe/Istanbul 2016'dan beri DST kullanmaz; diğer bölgeler için bu sınır bilinçli bırakıldı.
    private static DateTimeOffset StartOfDayUtc(DateOnly date, TimeZoneInfo timeZone) =>
        new(TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(TimeOnly.MinValue), timeZone), TimeSpan.Zero);
}
