using System.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace BankingAI.Api.Errors;

/// <summary>
/// The machine codes and Turkish titles of every error response (docs/api.md). Kept in one table so clients can
/// switch on <c>code</c> instead of parsing titles.
/// </summary>
public static class ApiProblems
{
    public const string ValidationFailed = "validation_failed";
    public const string Unauthorized = "unauthorized";
    public const string SessionInvalid = "session_invalid";
    public const string NotFound = "not_found";
    public const string MethodNotAllowed = "method_not_allowed";
    public const string BalanceLimit = "balance_limit";
    public const string IdempotencyConflict = "idempotency_conflict";
    public const string UnsupportedMediaType = "unsupported_media_type";
    public const string TooManyRequests = "too_many_requests";
    public const string InternalError = "internal_error";

    /// <summary>Request header with the client's key for one write (API-12).</summary>
    public const string IdempotencyKeyHeader = "Idempotency-Key";

    /// <summary>Response header set when a write was a retry that returned the first answer.</summary>
    public const string ReplayedHeader = "Idempotent-Replayed";

    /// <summary>Title of an unexpected error; also used for statuses the table does not list.</summary>
    public const string InternalErrorTitle = "Beklenmeyen bir hata oluştu.";

    private static readonly Dictionary<int, (string Code, string Title)> ByStatus = new()
    {
        [StatusCodes.Status400BadRequest] = (ValidationFailed, "İstek geçersiz."),
        [StatusCodes.Status401Unauthorized] = (Unauthorized, "Giriş gerekli."),
        [StatusCodes.Status404NotFound] = (NotFound, "Bulunamadı."),
        [StatusCodes.Status405MethodNotAllowed] = (MethodNotAllowed, "Bu adres bu yöntemi desteklemiyor."),
        [StatusCodes.Status415UnsupportedMediaType] = (UnsupportedMediaType, "Desteklenmeyen içerik türü; JSON gönderin."),
        [StatusCodes.Status429TooManyRequests] = (TooManyRequests, "Çok fazla deneme; biraz sonra tekrar deneyin."),
        [StatusCodes.Status500InternalServerError] = (InternalError, InternalErrorTitle)
    };

    /// <summary>
    /// Runs for every problem body, whoever produced it (MVC, status code pages, the exception handler). A problem
    /// that already carries a <c>code</c> keeps its own title; the others get the code and title of their status.
    /// </summary>
    public static void Customize(ProblemDetailsContext context)
    {
        var problem = context.ProblemDetails;
        if (!problem.Extensions.ContainsKey("code"))
        {
            var status = problem.Status ?? context.HttpContext.Response.StatusCode;
            var (code, title) = ByStatus.TryGetValue(status, out var known) ? known : ("error", InternalErrorTitle);
            problem.Extensions["code"] = code;
            problem.Title = title;
        }
        problem.Extensions.TryAdd("traceId", Activity.Current?.Id ?? context.HttpContext.TraceIdentifier);
    }
}
