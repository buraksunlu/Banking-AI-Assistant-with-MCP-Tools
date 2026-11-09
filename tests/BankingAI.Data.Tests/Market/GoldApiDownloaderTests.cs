using System.Net;
using BankingAI.Data.Market;
using Xunit;

namespace BankingAI.Data.Tests.Market;

public sealed class GoldApiDownloaderTests : IDisposable
{
    private const string Key = "test-key-not-real";
    private static readonly DateOnly From = new(2025, 10, 10);
    private static readonly DateOnly To = new(2025, 10, 13);
    private const string Valid = """
        [{"avg_price":"3991.712202","day":"2025-10-10 00:00:00"},
         {"avg_price":"4058.120000","day":"2025-10-13 00:00:00"}]
        """;

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "goldapi-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private string SavedPath => Path.Combine(_directory, "xau-2025-10-10_2025-10-13.json");

    [Fact]
    public void File_name_names_the_range() =>
        Assert.Equal("xau-2025-10-10_2025-10-13.json", GoldApiDownloader.FileName(From, To));

    [Fact]
    public void History_address_uses_utc_midnights_and_an_exclusive_end()
    {
        // 2025-10-10T00:00Z = 1760054400; the day after 13 Oct, 2025-10-14T00:00Z = 1760400000.
        Assert.Equal(
            "https://api.gold-api.com/history?symbol=XAU&startTimestamp=1760054400&endTimestamp=1760400000&groupBy=day&aggregation=avg&orderBy=asc",
            GoldApiDownloader.HistoryUri(From, To).ToString());
    }

    [Fact]
    public async Task A_valid_answer_is_saved_and_the_key_goes_only_in_the_header()
    {
        var handler = new FakeHandler(_ => Ok(Valid));

        var result = await Downloader(handler).DownloadAsync(_directory, From, To, Key);

        Assert.Equal(GoldApiDownloadStatus.Saved, result.Status);
        Assert.Equal(2, result.Days);
        Assert.Equal(Valid, await File.ReadAllTextAsync(SavedPath));
        var request = Assert.Single(handler.Requests);
        Assert.Equal("api.gold-api.com", request.Uri.Host);
        Assert.Equal(Key, request.Key);
        Assert.DoesNotContain(Key, request.Uri.ToString());
    }

    [Fact]
    public async Task An_existing_range_file_is_not_requested_again()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(SavedPath, "kept");
        var handler = new FakeHandler(_ => Ok(Valid));

        var result = await Downloader(handler).DownloadAsync(_directory, From, To, Key);

        Assert.Equal(GoldApiDownloadStatus.AlreadyPresent, result.Status);
        Assert.Empty(handler.Requests);
        Assert.Equal("kept", await File.ReadAllTextAsync(SavedPath));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task Key_and_rate_limit_errors_are_not_retried_and_never_echo_the_key(HttpStatusCode status)
    {
        var handler = new FakeHandler(_ => new HttpResponseMessage(status) { Content = new StringContent($"bad key {Key}") });

        var result = await Downloader(handler).DownloadAsync(_directory, From, To, Key);

        Assert.Equal(GoldApiDownloadStatus.Failed, result.Status);
        Assert.Single(handler.Requests);
        Assert.Contains(((int)status).ToString(), result.Detail);
        Assert.DoesNotContain(Key, result.Detail);
        Assert.False(File.Exists(SavedPath));
    }

    [Fact]
    public async Task A_server_error_or_timeout_is_retried()
    {
        var answers = new Queue<Func<HttpResponseMessage>>(
        [
            () => new HttpResponseMessage(HttpStatusCode.BadGateway),
            () => throw new TaskCanceledException("timeout"),
            () => Ok(Valid)
        ]);
        var handler = new FakeHandler(_ => answers.Dequeue()());

        var result = await Downloader(handler).DownloadAsync(_directory, From, To, Key);

        Assert.Equal(GoldApiDownloadStatus.Saved, result.Status);
        Assert.Equal(GoldApiDownloader.MaxAttempts, handler.Requests.Count);
    }

    [Fact]
    public async Task After_the_last_attempt_nothing_is_saved()
    {
        var handler = new FakeHandler(_ => throw new HttpRequestException("bağlantı koptu"));

        var result = await Downloader(handler).DownloadAsync(_directory, From, To, Key);

        Assert.Equal(GoldApiDownloadStatus.Failed, result.Status);
        Assert.Contains("bağlantı koptu", result.Detail);
        Assert.Equal(GoldApiDownloader.MaxAttempts, handler.Requests.Count);
        Assert.False(File.Exists(SavedPath));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{\"error\":\"x\"}")]
    [InlineData("""[{"avg_price":"3991.712202","day":"2025-10-09 00:00:00"}]""")]   // before the range
    [InlineData("""[{"avg_price":"3991.712202","day":"2025-10-14 00:00:00"}]""")]   // after the range
    public async Task An_empty_invalid_or_out_of_range_answer_is_not_saved(string body)
    {
        var handler = new FakeHandler(_ => Ok(body));

        var result = await Downloader(handler).DownloadAsync(_directory, From, To, Key);

        Assert.Equal(GoldApiDownloadStatus.Failed, result.Status);
        Assert.Single(handler.Requests);
        Assert.False(File.Exists(SavedPath));
    }

    [Fact]
    public async Task A_blank_key_is_refused_before_any_request()
    {
        var handler = new FakeHandler(_ => Ok(Valid));

        await Assert.ThrowsAsync<ArgumentException>(() => Downloader(handler).DownloadAsync(_directory, From, To, " "));
        Assert.Empty(handler.Requests);
    }

    private static GoldApiDownloader Downloader(FakeHandler handler) => new(new HttpClient(handler), TimeSpan.Zero);

    private static HttpResponseMessage Ok(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public List<(Uri Uri, string? Key)> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.RequestUri!,
                request.Headers.TryGetValues("x-api-key", out var values) ? values.Single() : null));
            return Task.FromResult(answer(request));
        }
    }
}
