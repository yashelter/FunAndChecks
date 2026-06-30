using FluentValidation;
using FunAndChecks.Application.Common.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace FunAndChecks.Middleware;

/// <summary>
/// Переводит исключения прикладного слоя в HTTP-ответы (ProblemDetails).
/// </summary>
public class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (ValidationException ex)
        {
            var errors = ex.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());

            await WriteProblemAsync(context, StatusCodes.Status400BadRequest, "Validation failed.", extensions: new()
            {
                ["errors"] = errors,
            });
        }
        catch (NotFoundException ex)
        {
            await WriteProblemAsync(context, StatusCodes.Status404NotFound, ex.Message);
        }
        catch (ConflictException ex)
        {
            await WriteProblemAsync(context, StatusCodes.Status409Conflict, ex.Message);
        }
        catch (ForbiddenException ex)
        {
            await WriteProblemAsync(context, StatusCodes.Status403Forbidden, ex.Message);
        }
        catch (RateLimitException ex)
        {
            await WriteProblemAsync(context, StatusCodes.Status429TooManyRequests, ex.Message);
        }
        catch (Exception ex)
        {
            if (context.Response.HasStarted)
            {
                logger.LogError(ex, "An exception occurred, but response has already started.");
                throw; // Rethrow so the server forcefully aborts the connection instead of sending a malformed 200 OK.
            }

            logger.LogError(ex, "Unhandled exception while processing {Method} {Path}.",
                context.Request.Method, context.Request.Path);
            await WriteProblemAsync(context, StatusCodes.Status500InternalServerError, "An unexpected error occurred.");
        }
    }

    private static async Task WriteProblemAsync(
        HttpContext context, int statusCode, string detail, Dictionary<string, object?>? extensions = null)
    {
        if (context.Response.HasStarted)
            return;

        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = ReasonPhrase(statusCode),
            Detail = detail,
        };

        if (extensions != null)
        {
            foreach (var (key, value) in extensions)
                problem.Extensions[key] = value;
        }

        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsJsonAsync(problem);
    }

    private static string ReasonPhrase(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => "Bad Request",
        StatusCodes.Status403Forbidden => "Forbidden",
        StatusCodes.Status404NotFound => "Not Found",
        StatusCodes.Status409Conflict => "Conflict",
        StatusCodes.Status429TooManyRequests => "Too Many Requests",
        _ => "Internal Server Error",
    };
}
