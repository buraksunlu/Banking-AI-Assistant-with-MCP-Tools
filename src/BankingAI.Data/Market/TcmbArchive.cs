using System.Globalization;

namespace BankingAI.Data.Market;

/// <summary>
/// Local folder of raw TCMB bulletins, one <c>yyyy-MM-dd.xml</c> file per publication day.
/// </summary>
public static class TcmbArchive
{
    /// <summary>Default folder, relative to the repository root and ignored by git.</summary>
    public const string DefaultDirectory = "data/raw/market/tcmb";

    /// <summary>File name used for the bulletin of <paramref name="day"/>.</summary>
    public static string FileName(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".xml";

    /// <summary>Weekdays from <paramref name="from"/> to <paramref name="to"/>, both included; TCMB publishes no weekend bulletin.</summary>
    public static IReadOnlyList<DateOnly> Weekdays(DateOnly from, DateOnly to)
    {
        if (to < from)
            throw new ArgumentException($"Bitiş ({to:yyyy-MM-dd}) başlangıçtan ({from:yyyy-MM-dd}) önce olamaz.", nameof(to));

        var days = new List<DateOnly>();
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            if (day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
                days.Add(day);
        }

        return days;
    }

    /// <summary>
    /// Parses every weekday file in the range. Missing files and unreadable files are reported, never filled in.
    /// </summary>
    public static TcmbArchiveRead Read(string directory, DateOnly from, DateOnly to)
    {
        var rows = new List<MarketPriceRow>();
        var missing = new List<DateOnly>();
        var rejected = new List<RejectedDay>();

        foreach (var day in Weekdays(from, to))
        {
            var path = Path.Combine(directory, FileName(day));
            if (!File.Exists(path))
            {
                missing.Add(day);
                continue;
            }

            try
            {
                rows.AddRange(TcmbBulletinParser.Parse(File.ReadAllText(path), day));
            }
            catch (InvalidDataException e)
            {
                rejected.Add(new RejectedDay(day, e.Message));
            }
        }

        return new TcmbArchiveRead(rows, missing, rejected);
    }
}

/// <summary>Result of reading the archive for a date range.</summary>
/// <param name="Rows">Prices from every readable bulletin.</param>
/// <param name="MissingDays">Weekdays without a file (holiday, not published yet, or not downloaded).</param>
/// <param name="RejectedDays">Weekdays whose file could not be read, with the reason.</param>
public sealed record TcmbArchiveRead(
    IReadOnlyList<MarketPriceRow> Rows,
    IReadOnlyList<DateOnly> MissingDays,
    IReadOnlyList<RejectedDay> RejectedDays);

/// <summary>A day whose source data was refused.</summary>
public sealed record RejectedDay(DateOnly Day, string Reason);
