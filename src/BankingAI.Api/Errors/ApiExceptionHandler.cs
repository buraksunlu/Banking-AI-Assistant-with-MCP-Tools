using BankingAI.Api.Assistant;
using BankingAI.Application;
using BankingAI.Data.Writing;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace BankingAI.Api.Errors;

/// <summary>
/// The single place that turns an exception from a service into an HTTP answer, so controllers carry no
/// <c>try/catch</c>. Anything it does not recognise is a 500 whose details stay in the server log.
/// </summary>
public sealed class ApiExceptionHandler(IProblemDetailsService problems, ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    /// <inheritdoc />
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        // The client went away: there is nobody to answer and nothing went wrong on our side.
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
            return true;

        var problem = exception switch
        {
            // Checked before KeyNotFoundException, which it derives from: a stale token means "sign in again".
            CustomerNotFoundException => Coded(StatusCodes.Status401Unauthorized, ApiProblems.SessionInvalid,
                "Oturum geçersiz; yeniden giriş yapın."),
            // No detail: a foreign resource must look exactly like a missing one.
            KeyNotFoundException => new ProblemDetails { Status = StatusCodes.Status404NotFound },
            BalanceLimitException error => Coded(StatusCodes.Status409Conflict, ApiProblems.BalanceLimit, "Bakiye sınırı", error.Message),
            IdempotencyConflictException error => Coded(StatusCodes.Status409Conflict, ApiProblems.IdempotencyConflict,
                "Bu anahtar farklı bir istek için kullanılmış", error.Message),
            ArgumentException error => Validation(error),
            // Grouped by what the client should do next (AI-14b K5-A); the AI service's own code is already in the log.
            AssistantException { Failure: AssistantFailure.Unavailable } => Coded(StatusCodes.Status503ServiceUnavailable,
                ApiProblems.AssistantUnavailable, "Kılavuz şu an kullanılamıyor."),
            AssistantException { Failure: AssistantFailure.Timeout } => Coded(StatusCodes.Status504GatewayTimeout,
                ApiProblems.AssistantTimeout, "Kılavuz zamanında cevap veremedi; tekrar deneyin."),
            AssistantException => Coded(StatusCodes.Status502BadGateway,
                ApiProblems.AssistantFailed, "Kılavuz bu soruya cevap üretemedi; farklı sormayı deneyin."),
            BadHttpRequestException error => new ProblemDetails { Status = error.StatusCode },
            _ => null
        };
        if (problem is null)
        {
            logger.LogError(exception, "Unhandled exception for {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
            problem = Coded(StatusCodes.Status500InternalServerError, ApiProblems.InternalError, ApiProblems.InternalErrorTitle);
        }

        httpContext.Response.StatusCode = problem.Status!.Value;
        // The exception itself is deliberately not passed on: no writer may copy its message or stack into the body.
        await problems.WriteAsync(new ProblemDetailsContext { HttpContext = httpContext, ProblemDetails = problem });
        return true;
    }

    private static ProblemDetails Coded(int status, string code, string title, string? detail = null) =>
        new() { Status = status, Title = title, Detail = detail, Extensions = { ["code"] = code } };

    // Service input rules are ArgumentExceptions whose messages are written for the client. The field is named
    // the way the client names it.
    private static HttpValidationProblemDetails Validation(ArgumentException error) =>
        new(new Dictionary<string, string[]> { [ApiValidation.FieldName(error.ParamName ?? "request")] = [ApiValidation.Message(error)] })
        {
            Status = StatusCodes.Status400BadRequest
        };
}
