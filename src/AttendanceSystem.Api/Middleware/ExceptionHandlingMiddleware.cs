using AttendanceSystem.Api.Domain.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace AttendanceSystem.Api.Middleware;

public sealed class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception exception)
        {
            if (context.Response.HasStarted)
            {
                throw;
            }

            await WriteProblemAsync(context, exception);
        }
    }

    private async Task WriteProblemAsync(HttpContext context, Exception exception)
    {
        var problem = CreateProblem(exception);
        context.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync((object)problem, problem.GetType());
    }

    private ProblemDetails CreateProblem(Exception exception)
    {
        switch (exception)
        {
            case ValidationFailedException validation:
                return new ValidationProblemDetails(validation.Errors)
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "One or more validation errors occurred.",
                    Type = "https://tools.ietf.org/html/rfc9110#section-15.5.1"
                };
            case NotFoundException notFound:
                return new ProblemDetails
                {
                    Status = StatusCodes.Status404NotFound,
                    Title = "The requested resource was not found.",
                    Detail = notFound.Message,
                    Type = "https://tools.ietf.org/html/rfc9110#section-15.5.5"
                };
            case ConflictException conflict:
                var conflictProblem = new ProblemDetails
                {
                    Status = StatusCodes.Status409Conflict,
                    Title = "Attendance already exists for the date.",
                    Detail = conflict.Message,
                    Type = "https://tools.ietf.org/html/rfc9110#section-15.5.10"
                };
                conflictProblem.Extensions["studentIds"] = conflict.StudentIds;
                return conflictProblem;
            default:
                _logger.LogError(exception, "Unhandled exception.");
                return new ProblemDetails
                {
                    Status = StatusCodes.Status500InternalServerError,
                    Title = "An unexpected error occurred.",
                    Detail = "An unexpected error occurred.",
                    Type = "https://tools.ietf.org/html/rfc9110#section-15.6.1"
                };
        }
    }
}
