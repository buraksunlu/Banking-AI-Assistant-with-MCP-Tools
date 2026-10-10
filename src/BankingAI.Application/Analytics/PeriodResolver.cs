namespace BankingAI.Application.Analytics;

// StartUtc dahil, EndUtc hariç: OccurredAt >= StartUtc AND OccurredAt < EndUtc.
// StartDate/EndDate müşterinin yerel takvimindeki dahil günlerdir; cevapta gösterilir.
public sealed record ResolvedPeriod(
    PeriodKind Kind, DateTimeOffset StartUtc, DateTimeOffset EndUtc, DateOnly StartDate, DateOnly EndDate);

public sealed class PeriodResolver(TimeProvider timeProvider)
{
    public const int MaximumCustomMonths = 24;

    public ResolvedPeriod Resolve(PeriodKind kind, string timeZoneId, DateOnly? from = null, DateOnly? to = null)
    {
        if (!Enum.IsDefined(kind))
            throw new ArgumentException("Bilinmeyen dönem türü.", nameof(kind));
        if (kind != PeriodKind.Custom && (from is not null || to is not null))
            throw new ArgumentException("Başlangıç/bitiş tarihi yalnızca Custom dönemde verilebilir.", nameof(from));

        var timeZone = FindTimeZone(timeZoneId);
        // "Bugün" sunucu saatinden değil, müşterinin saat diliminden belirlenir.
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), timeZone).DateTime);
        var monthStart = new DateOnly(today.Year, today.Month, 1);

        var (start, end) = kind switch
        {
            PeriodKind.ThisMonth => (monthStart, today),
            PeriodKind.LastMonth => (monthStart.AddMonths(-1), monthStart.AddDays(-1)),
            // DateOnly.AddMonths kısa ayda son güne sabitlenir: 31 Mayıs - 3 ay = 28 Şubat.
            PeriodKind.Last3Months => (today.AddMonths(-3), today),
            PeriodKind.Last6Months => (today.AddMonths(-6), today),
            _ => ValidateCustom(from, to, today)
        };

        return new ResolvedPeriod(kind, StartOfDayUtc(start, timeZone), StartOfDayUtc(end.AddDays(1), timeZone), start, end);
    }

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
