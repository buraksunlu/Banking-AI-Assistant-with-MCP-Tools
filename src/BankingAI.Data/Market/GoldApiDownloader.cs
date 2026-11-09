using System.Globalization;
using System.Net;

namespace BankingAI.Data.Market;

/// <summary>
/// Downloads the daily XAU history of gold-api.com for a date range into one raw JSON file.
/// The API key is sent only to that host, only in the <c>x-api-key</c> header, and never written anywhere.
/// </summary>
/// <param name="http">Client whose timeout limits each request.</param>
/// <param name="retryDelay">Wait before retrying a failed request.</param>
public sealed class GoldApiDownloader(HttpClient http, TimeSpan retryDelay)
{
    /// <summary>Attempts before the download is reported as failed.</summary>
    public const int MaxAttempts = 3;

    /// <summary>Default folder, relative to the repository root and ignored by git.</summary>
    public const string DefaultDirectory = "data/raw/market/goldapi";

    /// <summary>File name used for the range.</summary>
    public static string FileName(DateOnly from, DateOnly to) =>
        $"xau-{from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}_{to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.json";

    /// <summary>History address; the end timestamp is the UTC midnight after <paramref name="to"/> (exclusive).</summary>
    public static Uri HistoryUri(DateOnly from, DateOnly to) => new(
        "https://api.gold-api.com/history?symbol=XAU"
        + $"&startTimestamp={UnixMidnight(from)}&endTimestamp={UnixMidnight(to.AddDays(1))}"
        + "&groupBy=day&aggregation=avg&orderBy=asc");

    /// <summary>
    /// Downloads the range unless its file already exists. The answer is saved only if it parses,
    /// is not empty and every day lies inside the range.
    /// </summary>
    public async Task<GoldApiDownloadResult> DownloadAsync(
        string directory, DateOnly from, DateOnly to, string apiKey, CancellationToken cancellationToken = default)
    {
        if (to < from)
            throw new ArgumentException("Bitiş başlangıçtan önce olamaz.", nameof(to));
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("gold-api anahtarı boş.", nameof(apiKey));

        var path = Path.Combine(directory, FileName(from, to));
        if (File.Exists(path))
            return new GoldApiDownloadResult(GoldApiDownloadStatus.AlreadyPresent, path);

        string? lastError = null;
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            if (attempt > 1)
                await Task.Delay(retryDelay, cancellationToken);

            string body;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, HistoryUri(from, to));
                request.Headers.Add("x-api-key", apiKey);
                using var response = await http.SendAsync(request, cancellationToken);
                // A refused key or the hourly limit will not change in two seconds; the body is not echoed
                // because a provider may repeat the key in it.
                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
                    return Failed(path, $"HTTP {(int)response.StatusCode} (anahtar ya da saatlik istek sınırı)");
                if (!response.IsSuccessStatusCode)
                {
                    lastError = $"HTTP {(int)response.StatusCode}";
                    continue;
                }

                body = await response.Content.ReadAsStringAsync(cancellationToken);
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                lastError = e.Message;
                continue;
            }

            IReadOnlyList<OuncePrice> days;
            try
            {
                days = GoldApiHistoryParser.Parse(body);
            }
            catch (InvalidDataException e)
            {
                return Failed(path, e.Message);
            }

            if (days.Count == 0)
                return Failed(path, "gold-api boş cevap verdi.");
            if (days[0].Day < from || days[^1].Day > to)
                return Failed(path, $"Cevap aralık dışında gün içeriyor ({days[0].Day:yyyy-MM-dd} … {days[^1].Day:yyyy-MM-dd}).");

            Directory.CreateDirectory(directory);
            var temporary = path + ".part";
            await File.WriteAllTextAsync(temporary, body, cancellationToken);
            File.Move(temporary, path, overwrite: false);
            return new GoldApiDownloadResult(GoldApiDownloadStatus.Saved, path, days.Count);
        }

        return Failed(path, lastError);
    }

    private static GoldApiDownloadResult Failed(string path, string? detail) =>
        new(GoldApiDownloadStatus.Failed, path, Detail: detail);

    private static long UnixMidnight(DateOnly day) =>
        new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToUnixTimeSeconds();
}

/// <summary>What happened to a gold-api download.</summary>
public enum GoldApiDownloadStatus
{
    /// <summary>Downloaded, checked and saved.</summary>
    Saved,
    /// <summary>The range file was already there; no request was made.</summary>
    AlreadyPresent,
    /// <summary>Nothing was saved; see the detail.</summary>
    Failed
}

/// <summary>Outcome of a gold-api download.</summary>
public sealed record GoldApiDownloadResult(GoldApiDownloadStatus Status, string Path, int Days = 0, string? Detail = null);
