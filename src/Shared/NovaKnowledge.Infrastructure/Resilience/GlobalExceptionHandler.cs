using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NovaKnowledge.Contracts.Common;

namespace NovaKnowledge.Infrastructure.Resilience;

/// <summary>
/// Converts unhandled exceptions into RFC 7807 ProblemDetails responses and logs them,
/// so services return consistent error payloads instead of raw exception messages.
/// </summary>
public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var status = exception switch
        {
            ArgumentException => StatusCodes.Status400BadRequest,
            KeyNotFoundException => StatusCodes.Status404NotFound,
            InvalidOperationException => StatusCodes.Status409Conflict,
            NotSupportedException => StatusCodes.Status415UnsupportedMediaType,
            _ => StatusCodes.Status500InternalServerError
        };

        logger.LogError(exception, "Unhandled exception for {Method} {Path} -> {Status}",
            httpContext.Request.Method, httpContext.Request.Path, status);

        var problem = new ProblemDetails
        {
            Status = status,
            Title = status == StatusCodes.Status500InternalServerError
                ? "An unexpected error occurred."
                : exception.Message,
            Type = $"https://httpstatuses.io/{status}",
            Instance = httpContext.Request.Path
        };
        problem.Extensions["correlationId"] = httpContext.TraceIdentifier;

        httpContext.Response.StatusCode = status;
        httpContext.Response.ContentType = "application/problem+json";

        if (status == StatusCodes.Status500InternalServerError)
        {
            // Surface a consistent shape for 500s without leaking internals.
            var error = new ErrorResponse("An unexpected error occurred.", httpContext.TraceIdentifier, "internal_error");
            await httpContext.Response.WriteAsJsonAsync(error, cancellationToken);
            return true;
        }

        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
        return true;
    }
}
