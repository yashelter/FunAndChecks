using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Frontend.Shared.Resources;
using Microsoft.Extensions.Localization;

namespace Frontend.Shared.Api;

/// <summary>Ошибка обращения к API с человекочитаемым сообщением из ProblemDetails.</summary>
public class ApiException(HttpStatusCode statusCode, string message, Dictionary<string, string[]>? validationErrors = null) : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
    public Dictionary<string, string[]> ValidationErrors { get; } = validationErrors ?? new();
}

public static class HttpResponseExtensions
{
    private sealed record ErrorDescriptor(string Code, Dictionary<string, JsonElement>? Args);
    private sealed record ProblemDetails(
        string? Detail,
        string? Title,
        string? Code,
        Dictionary<string, JsonElement>? Args,
        Dictionary<string, string[]>? Errors,
        Dictionary<string, ErrorDescriptor[]>? ValidationCodes);

    private static readonly IReadOnlyDictionary<string, (string Resource, string[] Args)> Catalog =
        new Dictionary<string, (string, string[])>(StringComparer.OrdinalIgnoreCase)
        {
            ["auth.unauthorized"] = ("Error_Unauthorized", []),
            ["access.forbidden"] = ("Error_Forbidden", []),
            ["access.subject_restricted"] = ("Error_SubjectRestricted", ["subjectId"]),
            ["access.group_restricted"] = ("Error_GroupRestricted", ["groupId"]),
            ["access.subject_hidden"] = ("Error_SubjectHidden", ["subjectId"]),
            ["access.group_hidden"] = ("Error_GroupHidden", ["groupId"]),
            ["resource.not_found"] = ("Error_NotFound", []),
            ["state.conflict"] = ("Error_Conflict", []),
            ["student.not_enrolled"] = ("Error_StudentNotEnrolled", ["subjectId"]),
            ["queue.autofill_group_not_enrolled"] = ("Error_AutofillGroupNotEnrolled", ["groupIds"]),
            ["queue.already_joined"] = ("Error_AlreadyInQueue", []),
            ["account.email_taken"] = ("Error_EmailTaken", []),
            ["email.rate_limited"] = ("Error_EmailRateLimited", ["retryAfterSeconds"]),
            ["rate_limit.exceeded"] = ("Error_TooManyRequests", []),
            ["request.method_not_allowed"] = ("Error_MethodNotAllowed", []),
            ["server.internal_error"] = ("Error_Internal", []),
            ["validation.NotEmptyValidator"] = ("Error_ValidationRequired", []),
            ["validation.NotNullValidator"] = ("Error_ValidationRequired", []),
            ["validation.EmailValidator"] = ("Error_ValidationEmail", []),
            ["validation.MaximumLengthValidator"] = ("Error_ValidationMaximumLength", ["maxLength"]),
            ["validation.MinimumLengthValidator"] = ("Error_ValidationMinimumLength", ["minLength"]),
            ["validation.GreaterThanValidator"] = ("Error_ValidationGreaterThan", ["comparisonValue"]),
            ["validation.GreaterThanOrEqualValidator"] = ("Error_ValidationGreaterThanOrEqual", ["comparisonValue"]),
            ["validation.RegularExpressionValidator"] = ("Error_ValidationFormat", []),
            ["validation.EnumValidator"] = ("Error_ValidationSelection", []),
            ["validation.SupportedCultureValidator"] = ("Error_ValidationCulture", []),
            ["validation.invalid"] = ("Error_ValidationInvalid", []),
            ["validation.failed"] = ("Error_ValidationFailed", []),
        };

    /// <summary>Бросает <see cref="ApiException"/> с сообщением из ProblemDetails, если ответ неуспешен.</summary>
    public static async Task EnsureSuccessAsync(this HttpResponseMessage response, IStringLocalizer<AppStrings> loc)
    {
        if (response.IsSuccessStatusCode)
            return;

        var (message, errors) = await ReadErrorAsync(response, loc);
        throw new ApiException(response.StatusCode, message, errors);
    }

    public static async Task<string> ReadErrorMessageAsync(this HttpResponseMessage response, IStringLocalizer<AppStrings> loc)
    {
        var (message, _) = await ReadErrorAsync(response, loc);
        return message;
    }

    private static async Task<(string Message, Dictionary<string, string[]> Errors)> ReadErrorAsync(
        HttpResponseMessage response, IStringLocalizer<AppStrings> loc)
    {
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
            var errors = LocalizeValidation(problem, loc);

            if (!string.IsNullOrWhiteSpace(problem?.Code) && Catalog.ContainsKey(problem.Code))
                return (Localize(problem.Code, problem.Args, loc), errors);

            if (!string.IsNullOrWhiteSpace(problem?.Detail))
                return (problem!.Detail!, errors);
            if (!string.IsNullOrWhiteSpace(problem?.Title))
                return (problem!.Title!, errors);

            return (GetDefaultMessage(response.StatusCode, loc), errors);
        }
        catch (JsonException) { /* тело не ProblemDetails */ }
        catch (NotSupportedException) { /* не JSON */ }

        return (GetDefaultMessage(response.StatusCode, loc), new Dictionary<string, string[]>());
    }

    private static Dictionary<string, string[]> LocalizeValidation(ProblemDetails? problem, IStringLocalizer<AppStrings> loc)
    {
        var legacy = problem?.Errors ?? new Dictionary<string, string[]>();
        if (problem?.ValidationCodes is null)
            return legacy;

        return problem.ValidationCodes.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Select((descriptor, index) => Catalog.ContainsKey(descriptor.Code)
                    ? Localize(descriptor.Code, descriptor.Args, loc)
                    : legacy.GetValueOrDefault(pair.Key)?.ElementAtOrDefault(index) ?? loc["Error_ValidationFailed"].Value)
                .ToArray());
    }

    private static string Localize(string code, Dictionary<string, JsonElement>? args, IStringLocalizer<AppStrings> loc)
    {
        var entry = Catalog[code];
        var values = entry.Args.Select(name => ReadScalar(args?.GetValueOrDefault(name))).ToArray();
        return values.Length == 0 ? loc[entry.Resource].Value : string.Format(loc[entry.Resource].Value, values);
    }

    private static object ReadScalar(JsonElement? value) => value?.ValueKind switch
    {
        JsonValueKind.Number when value.Value.TryGetInt64(out var number) => number,
        JsonValueKind.Number when value.Value.TryGetDouble(out var number) => number,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.String => value.Value.GetString() ?? string.Empty,
        _ => string.Empty,
    };

    private static string GetDefaultMessage(HttpStatusCode statusCode, IStringLocalizer<AppStrings> loc) => statusCode switch
    {
        HttpStatusCode.TooManyRequests => loc["Error_TooManyRequests"],
        HttpStatusCode.Unauthorized => loc["Error_Unauthorized"],
        HttpStatusCode.Forbidden => loc["Error_Forbidden"],
        _ => loc["Error_RequestFailed", (int)statusCode],
    };
}
