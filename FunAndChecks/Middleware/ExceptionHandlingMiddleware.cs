using FluentValidation;
using FunAndChecks.Application.Common.Exceptions;

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

            var validationCodes = ex.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(ToDescriptor).ToArray());

            await ApiProblemDetails.WriteAsync(context, StatusCodes.Status400BadRequest, "Validation failed.",
                "validation.failed", errors: errors, validationCodes: validationCodes);
        }
        catch (NotFoundException ex)
        {
            await WriteApplicationProblemAsync(context, StatusCodes.Status404NotFound, ex);
        }
        catch (ConflictException ex)
        {
            await WriteApplicationProblemAsync(context, StatusCodes.Status409Conflict, ex);
        }
        catch (ForbiddenException ex)
        {
            await WriteApplicationProblemAsync(context, StatusCodes.Status403Forbidden, ex);
        }
        catch (RateLimitException ex)
        {
            if (ex.Arguments.TryGetValue("retryAfterSeconds", out var seconds))
                context.Response.Headers.RetryAfter = Convert.ToInt64(seconds).ToString();
            await WriteApplicationProblemAsync(context, StatusCodes.Status429TooManyRequests, ex);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // Клиент оборвал соединение — это не ошибка сервера: не пишем 500 в мёртвый
            // сокет и не засоряем error-логи стектрейсами.
            logger.LogDebug("Request {Method} {Path} was cancelled by the client.",
                context.Request.Method, context.Request.Path);
            throw;
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
            await ApiProblemDetails.WriteAsync(context, StatusCodes.Status500InternalServerError,
                "An unexpected error occurred.", "server.internal_error");
        }
    }

    private static Task WriteApplicationProblemAsync(HttpContext context, int statusCode, ApplicationExceptionBase exception) =>
        ApiProblemDetails.WriteAsync(context, statusCode, exception.Message, exception.Code, exception.Arguments);

    private static ErrorDescriptor ToDescriptor(FluentValidation.Results.ValidationFailure failure)
    {
        var arguments = new Dictionary<string, object?>();
        if (failure.FormattedMessagePlaceholderValues is not null)
        {
            foreach (var (name, value) in failure.FormattedMessagePlaceholderValues)
            {
                if (name.Length == 0)
                    continue;
                if (value is null or string or bool or byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal)
                    arguments[char.ToLowerInvariant(name[0]) + name[1..]] = value;
                else if (value is Enum enumValue)
                    // enum-плейсхолдеры сериализуем строкой — JSON-число ничего не скажет UI.
                    arguments[char.ToLowerInvariant(name[0]) + name[1..]] = enumValue.ToString();
            }
        }

        var code = string.IsNullOrWhiteSpace(failure.ErrorCode)
            ? "validation.invalid"
            : $"validation.{failure.ErrorCode}";
        return new ErrorDescriptor(code, arguments);
    }
}
