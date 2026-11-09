using System.Globalization;
using BankingAI.Data;
using BankingAI.Data.Market;

/// <summary>
/// <c>market download tcmb --from yyyy-MM-dd --to yyyy-MM-dd</c> saves raw bulletins to the local archive;
/// <c>market import tcmb --from … --to …</c> writes the archived USD/EUR values to the database.
/// </summary>
internal static class MarketCommands
{
    public const string Usage =
        "market download tcmb --from <yyyy-MM-dd> --to <yyyy-MM-dd> | market import tcmb --from <yyyy-MM-dd> --to <yyyy-MM-dd>";

    /// <summary>True if the arguments name a market command, valid or not.</summary>
    public static bool Matches(string[] args) => args.Length > 0 && args[0] == "market";

    /// <summary>Runs the command; returns the process exit code.</summary>
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length != 7 || args[1] is not ("download" or "import") || args[2] != "tcmb"
            || args[3] != "--from" || args[5] != "--to"
            || !TryDay(args[4], out var from) || !TryDay(args[6], out var to) || to < from)
        {
            Console.WriteLine($"Usage: BankingAI.Cli {Usage}");
            return 1;
        }

        return args[1] == "download" ? await DownloadAsync(from, to) : await ImportAsync(from, to);
    }

    private static async Task<int> DownloadAsync(DateOnly from, DateOnly to)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("BankingAI.Cli/1.0");
        var downloader = new TcmbDownloader(http, pause: TimeSpan.FromMilliseconds(300), retryDelay: TimeSpan.FromSeconds(2));

        var results = await downloader.DownloadAsync(TcmbArchive.DefaultDirectory, from, to);

        Console.WriteLine($"TCMB {Day(from)} → {Day(to)}, klasör {TcmbArchive.DefaultDirectory}");
        foreach (var status in Enum.GetValues<TcmbDayStatus>())
            Console.WriteLine($"  {status}: {results.Count(r => r.Status == status)}");
        PrintDays("Yayın yok (404; tatil ya da henüz yayınlanmadı)", results.Where(r => r.Status == TcmbDayStatus.NotPublished).Select(r => r.Day));
        var failed = results.Where(r => r.Status == TcmbDayStatus.Failed).ToList();
        foreach (var f in failed)
            Console.WriteLine($"  BAŞARISIZ {Day(f.Day)}: {f.Detail}");

        return failed.Count == 0 ? 0 : 1;
    }

    private static async Task<int> ImportAsync(DateOnly from, DateOnly to)
    {
        var read = TcmbArchive.Read(TcmbArchive.DefaultDirectory, from, to);
        await using var db = new BankingDbContext();
        var result = await MarketPriceImporter.ImportAsync(db, read.Rows);

        Console.WriteLine($"TCMB içe aktarma {Day(from)} → {Day(to)}");
        Console.WriteLine($"  Okunan gün: {read.Rows.Select(r => r.PriceDate).Distinct().Count()}, satır: {read.Rows.Count}");
        Console.WriteLine($"  Eklenen: {result.Added}, değişmeyen: {result.Unchanged}, çelişen: {result.Conflicts.Count}");
        PrintDays("Dosyası olmayan hafta içi günler", read.MissingDays);
        foreach (var r in read.RejectedDays)
            Console.WriteLine($"  REDDEDİLDİ {Day(r.Day)}: {r.Reason}");
        foreach (var c in result.Conflicts)
            Console.WriteLine($"  ÇELİŞKİ {c.Symbol} {Day(c.PriceDate)}: DB {c.StoredPrice}, dosya {c.ImportedPrice} (DB korundu)");

        return read.RejectedDays.Count == 0 && result.Conflicts.Count == 0 ? 0 : 1;
    }

    private static void PrintDays(string title, IEnumerable<DateOnly> days)
    {
        var list = days.ToList();
        Console.WriteLine($"  {title}: {list.Count}");
        if (list.Count > 0)
            Console.WriteLine("    " + string.Join(", ", list.Select(Day)));
    }

    private static bool TryDay(string text, out DateOnly day) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day);

    private static string Day(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
