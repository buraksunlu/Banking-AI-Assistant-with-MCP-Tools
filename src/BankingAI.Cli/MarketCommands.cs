using System.Globalization;
using BankingAI.Data;
using BankingAI.Data.Market;

/// <summary>
/// <c>market download tcmb --from yyyy-MM-dd --to yyyy-MM-dd</c> saves raw bulletins to the local archive;
/// <c>market import tcmb --from … --to …</c> writes the archived USD/EUR values to the database.
/// <c>market download goldapi --from … --to …</c> saves the gold-api XAU history (key: <c>GOLD_API_KEY</c>);
/// <c>market import gold --from … --to …</c> derives gram gold in TRY from that file and the stored USDTRY.
/// </summary>
internal static class MarketCommands
{
    public const string Usage =
        "market download tcmb|goldapi --from <yyyy-MM-dd> --to <yyyy-MM-dd> | market import tcmb|gold --from <yyyy-MM-dd> --to <yyyy-MM-dd>";

    /// <summary>True if the arguments name a market command, valid or not.</summary>
    public static bool Matches(string[] args) => args.Length > 0 && args[0] == "market";

    /// <summary>Runs the command; returns the process exit code.</summary>
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length != 7 || args[3] != "--from" || args[5] != "--to"
            || !TryDay(args[4], out var from) || !TryDay(args[6], out var to) || to < from)
            return PrintUsage();

        return (args[1], args[2]) switch
        {
            ("download", "tcmb") => await DownloadAsync(from, to),
            ("import", "tcmb") => await ImportAsync(from, to),
            ("download", "goldapi") => await DownloadGoldAsync(from, to),
            ("import", "gold") => await ImportGoldAsync(from, to),
            _ => PrintUsage()
        };
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

    private static async Task<int> DownloadGoldAsync(DateOnly from, DateOnly to)
    {
        var key = Environment.GetEnvironmentVariable("GOLD_API_KEY");
        if (string.IsNullOrWhiteSpace(key))
        {
            Console.WriteLine("GOLD_API_KEY tanımlı değil; önce .env'i yükleyin.");
            return 1;
        }

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("BankingAI.Cli/1.0");
        var result = await new GoldApiDownloader(http, retryDelay: TimeSpan.FromSeconds(5))
            .DownloadAsync(GoldApiDownloader.DefaultDirectory, from, to, key);

        Console.WriteLine($"gold-api XAU {Day(from)} → {Day(to)}: {result.Status}, {result.Path}"
            + (result.Days > 0 ? $", {result.Days} gün" : "") + (result.Detail is null ? "" : $" — {result.Detail}"));
        return result.Status == GoldApiDownloadStatus.Failed ? 1 : 0;
    }

    private static async Task<int> ImportGoldAsync(DateOnly from, DateOnly to)
    {
        var path = Path.Combine(GoldApiDownloader.DefaultDirectory, GoldApiDownloader.FileName(from, to));
        if (!File.Exists(path))
        {
            Console.WriteLine($"{path} yok; önce 'market download goldapi' aynı aralıkla.");
            return 1;
        }

        var ounces = GoldApiHistoryParser.Parse(await File.ReadAllTextAsync(path));
        await using var db = new BankingDbContext();
        var usdTry = await MarketPriceReader.SeriesAsync(db, "USDTRY", from, to);
        // gold-api's average for the current day is still moving; a value written now would conflict tomorrow.
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTimeOffset.UtcNow, "Europe/Istanbul").DateTime);
        var completeDays = usdTry.Where(p => p.Key < today).ToDictionary(p => p.Key, p => p.Value);
        var derived = GramGoldDerivation.Derive(ounces, completeDays);
        var result = await MarketPriceImporter.ImportAsync(db, derived.Rows);

        Console.WriteLine($"Gram altın içe aktarma {Day(from)} → {Day(to)} ({GramGoldDerivation.Source})");
        Console.WriteLine($"  USDTRY günü: {completeDays.Count}, ons günü: {ounces.Count}, türetilen satır: {derived.Rows.Count}");
        Console.WriteLine($"  Eklenen: {result.Added}, değişmeyen: {result.Unchanged}, çelişen: {result.Conflicts.Count}");
        PrintDays("Onsu olmayan USDTRY günleri", derived.MissingOunceDays);
        Console.WriteLine($"  Dikkat (ons > %{GramGoldDerivation.WatchChange * 100:0}, yazıldı): {derived.Watch.Count}");
        foreach (var j in derived.Watch)
            Console.WriteLine($"    {Day(j.Day)} ← {Day(j.ComparedWith)}: {j.Change:+0.00%;-0.00%}");
        Console.WriteLine($"  Şüpheli (ons > %{GramGoldDerivation.SuspiciousChange * 100:0}, yazılmadı): {derived.Suspicious.Count}");
        foreach (var j in derived.Suspicious)
            Console.WriteLine($"    {Day(j.Day)} ← {Day(j.ComparedWith)}: {j.Change:+0.00%;-0.00%}");
        foreach (var c in result.Conflicts)
            Console.WriteLine($"  ÇELİŞKİ {c.Symbol} {Day(c.PriceDate)}: DB {c.StoredPrice}, yeni {c.ImportedPrice} (DB korundu)");

        return derived.MissingOunceDays.Count == 0 && derived.Suspicious.Count == 0 && result.Conflicts.Count == 0 ? 0 : 1;
    }

    private static int PrintUsage()
    {
        Console.WriteLine($"Usage: BankingAI.Cli {Usage}");
        return 1;
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
