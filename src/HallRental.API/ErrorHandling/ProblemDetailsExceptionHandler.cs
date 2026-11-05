using HallRental.API.Domain.Errors;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace HallRental.API.ErrorHandling;

/// <summary>
/// Maps exceptions to RFC 7807 responses: domain errors to 400/404/409 with their (client-safe) message,
/// anything else to a generic 500 so internal details such as SQL errors never reach the client.
/// </summary>
public sealed class ProblemDetailsExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<ProblemDetailsExceptionHandler> logger) : IExceptionHandler
{
    private const string UnexpectedErrorDetail = "The server could not process the request. Please try again later.";

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (statusCode, title, detail) = Map(exception);

        if (statusCode == StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Unhandled exception while processing {Method} {Path}",
                httpContext.Request.Method, httpContext.Request.Path);
        }

        httpContext.Response.StatusCode = statusCode;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = statusCode,
                Title = title,
                Detail = detail
            }
        });
    }

    private static (int StatusCode, string Title, string Detail) Map(Exception exception) => exception switch
    {
        ValidationException => (StatusCodes.Status400BadRequest, ProblemTitles.Validation, exception.Message),
        HallNotFoundException => (StatusCodes.Status404NotFound, ProblemTitles.HallNotFound, exception.Message),
        BookingConflictException => (StatusCodes.Status409Conflict, ProblemTitles.BookingConflict, exception.Message),
        // Thrown by Minimal API binding (malformed JSON, missing body) when ThrowOnBadRequest is on, i.e. in Development.
        BadHttpRequestException badRequest => (badRequest.StatusCode, ProblemTitles.BadRequest, badRequest.Message),
        _ => (StatusCodes.Status500InternalServerError, ProblemTitles.Unexpected, UnexpectedErrorDetail)
    };
}
