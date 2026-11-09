using System.Net;
using BankingAI.Data.Market;
using Xunit;

namespace BankingAI.Data.Tests.Market;

public sealed class TcmbDownloaderTests : IDisposable
{
    private static readonly DateOnly Friday = new(2025, 10, 10);
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "tcmb-download-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void Bulletin_address_uses_the_month_folder_and_day_first_file_name() =>
        Assert.Equal("https://www.tcmb.gov.tr/kurlar/202510/10102025.xml", TcmbDownloader.BulletinUri(Friday).ToString());

    [Fact]
    public async Task A_valid_answer_is_saved_under_the_iso_day()
    {
        var handler = new FakeHandler(_ => Ok(TcmbFixture.Bulletin()));

        var results = await Downloader(handler).DownloadAsync(_directory, Friday, Friday);

        Assert.Equal(TcmbDayStatus.Saved, Assert.Single(results).Status);
        Assert.Equal(TcmbFixture.Bulletin(), await File.ReadAllTextAsync(Path.Combine(_directory, "2025-10-10.xml")));
        Assert.Equal(["https://www.tcmb.gov.tr/kurlar/202510/10102025.xml"], handler.Requests);
    }

    [Fact]
    public async Task Weekends_are_never_requested()
    {
        var handler = new FakeHandler(_ => Ok(TcmbFixture.Bulletin()));

        var results = await Downloader(handler).DownloadAsync(_directory, new DateOnly(2025, 10, 11), new DateOnly(2025, 10, 12));

        Assert.Empty(results);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task A_day_already_in_the_archive_is_not_requested_again()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(Path.Combine(_directory, "2025-10-10.xml"), "kept as is");
        var handler = new FakeHandler(_ => Ok(TcmbFixture.Bulletin()));

        var results = await Downloader(handler).DownloadAsync(_directory, Friday, Friday);

        Assert.Equal(TcmbDayStatus.AlreadyPresent, Assert.Single(results).Status);
        Assert.Empty(handler.Requests);
        Assert.Equal("kept as is", await File.ReadAllTextAsync(Path.Combine(_directory, "2025-10-10.xml")));
    }

    [Fact]
    public async Task Not_found_means_not_published_is_not_retried_and_saves_nothing()
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var results = await Downloader(handler).DownloadAsync(_directory, Friday, Friday);

        Assert.Equal(TcmbDayStatus.NotPublished, Assert.Single(results).Status);
        Assert.Single(handler.Requests);
        Assert.False(File.Exists(Path.Combine(_directory, "2025-10-10.xml")));
    }

    [Fact]
    public async Task A_server_error_is_retried_and_a_later_success_is_saved()
    {
        var answers = new Queue<HttpResponseMessage>(
        [
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
            new HttpResponseMessage(HttpStatusCode.InternalServerError),
            Ok(TcmbFixture.Bulletin())
        ]);
        var handler = new FakeHandler(_ => answers.Dequeue());

        var results = await Downloader(handler).DownloadAsync(_directory, Friday, Friday);

        Assert.Equal(TcmbDayStatus.Saved, Assert.Single(results).Status);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task After_the_last_attempt_the_day_fails_and_the_next_day_still_runs()
    {
        var handler = new FakeHandler(request => request.RequestUri!.AbsolutePath.EndsWith("10102025.xml")
            ? throw new HttpRequestException("bağlantı koptu")
            : Ok(TcmbFixture.Bulletin(tarih: "13.10.2025")));

        var results = await Downloader(handler).DownloadAsync(_directory, Friday, new DateOnly(2025, 10, 13));

        Assert.Equal(TcmbDayStatus.Failed, results[0].Status);
        Assert.Contains("bağlantı koptu", results[0].Detail);
        Assert.Equal(TcmbDayStatus.Saved, results[1].Status);
        Assert.Equal(TcmbDownloader.MaxAttempts + 1, handler.Requests.Count);
        Assert.False(File.Exists(Path.Combine(_directory, "2025-10-10.xml")));
    }

    [Fact]
    public async Task A_timeout_is_retried_like_a_server_error()
    {
        var calls = 0;
        var handler = new FakeHandler(_ => ++calls == 1
            ? throw new TaskCanceledException("timeout")
            : Ok(TcmbFixture.Bulletin()));

        var results = await Downloader(handler).DownloadAsync(_directory, Friday, Friday);

        Assert.Equal(TcmbDayStatus.Saved, Assert.Single(results).Status);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task A_success_answer_that_is_not_the_days_bulletin_is_not_saved()
    {
        var handler = new FakeHandler(_ => Ok("<html>Bakım çalışması</html>"));

        var results = await Downloader(handler).DownloadAsync(_directory, Friday, Friday);

        Assert.Equal(TcmbDayStatus.Failed, Assert.Single(results).Status);
        Assert.False(File.Exists(Path.Combine(_directory, "2025-10-10.xml")));
        Assert.Single(handler.Requests);   // a wrong body is not a passing network fault
    }

    [Fact]
    public async Task Cancellation_stops_the_download()
    {
        using var cancel = new CancellationTokenSource();
        await cancel.CancelAsync();
        var handler = new FakeHandler(_ => Ok(TcmbFixture.Bulletin()));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Downloader(handler).DownloadAsync(_directory, Friday, Friday, cancel.Token));
    }

    private static TcmbDownloader Downloader(FakeHandler handler) =>
        new(new HttpClient(handler), TimeSpan.Zero, TimeSpan.Zero);

    private static HttpResponseMessage Ok(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request.RequestUri!.ToString());
            return Task.FromResult(answer(request));
        }
    }
}
