using FunAndChecks.Application.Common.Exceptions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using System.Text.Json;

namespace FunAndChecks.Middleware;

public static class ApiProblemDetails
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task WriteAsync(
        HttpContext context,
        int statusCode,
        string detail,
        string code,
        IReadOnlyDictionary<string, object?>? args = null,
        object? errors = null,
        object? validationCodes = null)
    {
        if (context.Response.HasStarted)
            return;

        var problem = Create(statusCode, detail, code, args, errors, validationCodes);

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json";
        await JsonSerializer.SerializeAsync(context.Response.Body, problem, JsonOptions, context.RequestAborted);
    }

    public static IActionResult ValidationResult(ActionContext context)
    {
        var errors = context.ModelState
            .Where(pair => pair.Value?.ValidationState == ModelValidationState.Invalid)
            .ToDictionary(
                pair => pair.Key,
                pair => pair.Value!.Errors.Select(error =>
                    string.IsNullOrWhiteSpace(error.ErrorMessage) ? "The submitted value is invalid." : error.ErrorMessage).ToArray());
        var validationCodes = errors.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Select(_ => new ErrorDescriptor(
                "validation.invalid", new Dictionary<string, object?>())).ToArray());
        var result = new BadRequestObjectResult(Create(
            StatusCodes.Status400BadRequest,
            "Validation failed.",
            "validation.failed",
            errors: errors,
            validationCodes: validationCodes));
        result.ContentTypes.Add("application/problem+json");
        return result;
    }

    private static ProblemDetails Create(
        int statusCode,
        string detail,
        string code,
        IReadOnlyDictionary<string, object?>? args = null,
        object? errors = null,
        object? validationCodes = null)
    {
        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = Title(statusCode),
            Detail = detail,
        };
        problem.Extensions["code"] = code;
        problem.Extensions["args"] = args ?? new Dictionary<string, object?>();
        if (errors is not null)
            problem.Extensions["errors"] = errors;
        if (validationCodes is not null)
            problem.Extensions["validationCodes"] = validationCodes;
        return problem;
    }

    public static string CodeForStatus(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => "request.invalid",
        StatusCodes.Status401Unauthorized => "auth.unauthorized",
        StatusCodes.Status403Forbidden => "access.forbidden",
        StatusCodes.Status404NotFound => "resource.not_found",
        StatusCodes.Status405MethodNotAllowed => "request.method_not_allowed",
        StatusCodes.Status409Conflict => "state.conflict",
        StatusCodes.Status429TooManyRequests => "rate_limit.exceeded",
        _ => "server.internal_error",
    };

    public static string DetailForStatus(int statusCode) => statusCode switch
    {
        StatusCodes.Status401Unauthorized => "Authentication is required.",
        StatusCodes.Status403Forbidden => "You do not have permission to perform this action.",
        StatusCodes.Status404NotFound => "The requested resource was not found.",
        StatusCodes.Status405MethodNotAllowed => "The HTTP method is not allowed for this resource.",
        StatusCodes.Status429TooManyRequests => "Too many requests. Try again later.",
        _ => "The request could not be completed.",
    };

    private static string Title(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => "Bad Request",
        StatusCodes.Status401Unauthorized => "Unauthorized",
        StatusCodes.Status403Forbidden => "Forbidden",
        StatusCodes.Status404NotFound => "Not Found",
        StatusCodes.Status405MethodNotAllowed => "Method Not Allowed",
        StatusCodes.Status409Conflict => "Conflict",
        StatusCodes.Status429TooManyRequests => "Too Many Requests",
        _ => "Internal Server Error",
    };
}
