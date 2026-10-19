using System.Collections.Concurrent;
using System.Net;
using System.Text;
using BankingAI.Api.Assistant;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BankingAI.Api.Tests;

/// <summary>
/// Stands in for the FastAPI service behind the API's assistant client: records every request it gets and answers as
/// the test says, so no model or Python process is needed.
/// </summary>
internal sealed class FakeAssistantService(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    public const string SampleReply = """
        {"type":"answer","answer":"Dün 145,00 TL harcadınız.",
         "toolCalls":[{"tool":"get_spending_summary","arguments":{"period":"Yesterday"}}],
         "facts":[{"label":"Toplam harcama","text":"145,00 TL"}],
         "model":"qwen3.5:4b"}
        """;

    private readonly ConcurrentQueue<RecordedRequest> requests = new();
    private readonly TaskCompletionSource cancelled = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public IReadOnlyList<RecordedRequest> Requests => requests.ToArray();

    /// <summary>Completes when a request the fake was still working on is cancelled by the API.</summary>
    public Task Cancelled => cancelled.Task;

    public static FakeAssistantService Answering(HttpStatusCode status, string body) =>
        new((_, _) => Task.FromResult(Json(status, body)));

    public static FakeAssistantService Answering(string body = SampleReply) => Answering(HttpStatusCode.OK, body);

    public static FakeAssistantService Failing(string code, HttpStatusCode status) =>
        Answering(status, $$"""{"title":"Hata","status":{{(int)status}},"code":"{{code}}","traceId":"t"}""");

    public static FakeAssistantService Unreachable() =>
        new((_, _) => throw new HttpRequestException("Connection refused (127.0.0.1:8000)"));

    public static FakeAssistantService Slow(TimeSpan delay) =>
        new(async (_, cancellationToken) =>
        {
            await Task.Delay(delay, cancellationToken);
            return Json(HttpStatusCode.OK, SampleReply);
        });

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        requests.Enqueue(new RecordedRequest(
            request.Method, request.RequestUri!.AbsolutePath, request.Headers.Authorization?.ToString(),
            request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));
        try
        {
            return await respond(request, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            cancelled.TrySetResult();
            throw;
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, status == HttpStatusCode.OK ? "application/json" : "application/problem+json") };

    /// <summary>An API host whose assistant client talks to <paramref name="fake"/>, with optional settings and log capture.</summary>
    public static WebApplicationFactory<Program> Api(FakeAssistantService fake, CapturedLogs? logs = null, params (string Key, string Value)[] settings) =>
        TestApi.Create(settings).WithWebHostBuilder(host => host.ConfigureTestServices(services =>
        {
            services.AddHttpClient<IAssistantClient, AssistantClient>().ConfigurePrimaryHttpMessageHandler(() => fake);
            if (logs is not null)
                services.AddSingleton<ILoggerProvider>(logs);
        }));
}

internal sealed record RecordedRequest(HttpMethod Method, string Path, string? Authorization, string? Body);

/// <summary>
/// Every log line the API writes, formatted, with any exception text, so a test can prove what never gets logged.
/// </summary>
internal sealed class CapturedLogs : ILoggerProvider
{
    private readonly ConcurrentQueue<string> lines = new();

    public IReadOnlyList<string> Lines => lines.ToArray();

    public ILogger CreateLogger(string categoryName) => new Logger(categoryName, lines);

    public void Dispose()
    {
    }

    private sealed class Logger(string category, ConcurrentQueue<string> lines) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            lines.Enqueue($"{logLevel} {category}: {formatter(state, exception)} {exception}");
    }
}
