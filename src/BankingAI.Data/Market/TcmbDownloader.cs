using System.Globalization;
using System.Net;

namespace BankingAI.Data.Market;

/// <summary>
/// Downloads daily TCMB bulletins into the local archive, one request at a time.
/// </summary>
/// <param name="http">Client whose timeout limits each request.</param>
/// <param name="pause">Wait between two requests, to stay polite to the public endpoint.</param>
/// <param name="retryDelay">Wait before retrying a failed request.</param>
public sealed class TcmbDownloader(HttpClient http, TimeSpan pause, TimeSpan retryDelay)
{
    /// <summary>Attempts per day before the day is reported as failed.</summary>
    public const int MaxAttempts = 3;

    /// <summary>Public address of the bulletin for <paramref name="day"/>.</summary>
    public static Uri BulletinUri(DateOnly day) => new(
        "https://www.tcmb.gov.tr/kurlar/"
        + day.ToString("yyyyMM", CultureInfo.InvariantCulture) + "/"
        + day.ToString("ddMMyyyy", CultureInfo.InvariantCulture) + ".xml");

    /// <summary>
    /// Downloads every weekday in the range that is not already in <paramref name="directory"/>.
    /// A file is saved only if it parses as the bulletin of that day.
    /// </summary>
    public async Task<IReadOnlyList<TcmbDayResult>> DownloadAsync(
        string directory, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var results = new List<TcmbDayResult>();
        var requested = false;

        foreach (var day in TcmbArchive.Weekdays(from, to))
        {
            var path = Path.Combine(directory, TcmbArchive.FileName(day));
            if (File.Exists(path))
            {
                results.Add(new TcmbDayResult(day, TcmbDayStatus.AlreadyPresent));
                continue;
            }

            if (requested)
                await Task.Delay(pause, cancellationToken);
            requested = true;

            results.Add(await DownloadDayAsync(day, directory, path, cancellationToken));
        }

        return results;
    }

    private async Task<TcmbDayResult> DownloadDayAsync(DateOnly day, string directory, string path, CancellationToken cancellationToken)
    {
        string? lastError = null;
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            if (attempt > 1)
                await Task.Delay(retryDelay, cancellationToken);

            string body;
            try
            {
                using var response = await http.GetAsync(BulletinUri(day), cancellationToken);
                if (response.StatusCode == HttpStatusCode.NotFound)
                    return new TcmbDayResult(day, TcmbDayStatus.NotPublished);
                if (!response.IsSuccessStatusCode)
                {
                    lastError = $"HTTP {(int)response.StatusCode}";
                    continue;
                }

                body = await response.Content.ReadAsStringAsync(cancellationToken);
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                // HttpClient reports its own timeout as TaskCanceledException; only the caller's token means "stop".
                lastError = e.Message;
                continue;
            }

            try
            {
                TcmbBulletinParser.Parse(body, day);
            }
            catch (InvalidDataException e)
            {
                // A wrong body (maintenance page, another day) is not a passing network fault; retrying would not help.
                return new TcmbDayResult(day, TcmbDayStatus.Failed, e.Message);
            }

            Directory.CreateDirectory(directory);
            var temporary = path + ".part";
            await File.WriteAllTextAsync(temporary, body, cancellationToken);
            // Rename last so an interrupted run never leaves a half-written file that later counts as "present".
            File.Move(temporary, path, overwrite: false);
            return new TcmbDayResult(day, TcmbDayStatus.Saved);
        }

        return new TcmbDayResult(day, TcmbDayStatus.Failed, lastError);
    }
}

/// <summary>What happened to one weekday during a download.</summary>
public enum TcmbDayStatus
{
    /// <summary>Downloaded, checked and saved.</summary>
    Saved,
    /// <summary>The file was already in the archive; no request was made.</summary>
    AlreadyPresent,
    /// <summary>TCMB answered 404: no bulletin for that day (holiday, or not published yet).</summary>
    NotPublished,
    /// <summary>All attempts failed or the answer was not a valid bulletin; nothing was saved.</summary>
    Failed
}

/// <summary>Download outcome for one weekday.</summary>
public sealed record TcmbDayResult(DateOnly Day, TcmbDayStatus Status, string? Detail = null);
