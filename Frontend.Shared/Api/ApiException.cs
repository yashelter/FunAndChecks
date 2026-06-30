using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Frontend.Shared.Api;

/// <summary>Ошибка обращения к API с человекочитаемым сообщением из ProblemDetails.</summary>
public class ApiException(HttpStatusCode statusCode, string message, Dictionary<string, string[]>? validationErrors = null) : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
    public Dictionary<string, string[]> ValidationErrors { get; } = validationErrors ?? new();
}

public static class HttpResponseExtensions
{
    private sealed record ProblemDetails(string? Detail, string? Title, Dictionary<string, string[]>? Errors);

    /// <summary>Бросает <see cref="ApiException"/> с сообщением из ProblemDetails, если ответ неуспешен.</summary>
    public static async Task EnsureSuccessAsync(this HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
            return;

        var (message, errors) = await ReadErrorAsync(response);
        throw new ApiException(response.StatusCode, message, errors);
    }

    private static async Task<(string Message, Dictionary<string, string[]> Errors)> ReadErrorAsync(HttpResponseMessage response)
    {
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
            var errors = problem?.Errors ?? new Dictionary<string, string[]>();
            
            if (!string.IsNullOrWhiteSpace(problem?.Detail))
                return (problem!.Detail!, errors);
            if (!string.IsNullOrWhiteSpace(problem?.Title))
                return (problem!.Title!, errors);
                
            return (GetDefaultMessage(response.StatusCode), errors);
        }
        catch (JsonException) { /* тело не ProblemDetails */ }
        catch (NotSupportedException) { /* не JSON */ }

        return (GetDefaultMessage(response.StatusCode), new Dictionary<string, string[]>());
    }

    public static async Task<string> ReadErrorMessageAsync(this HttpResponseMessage response)
    {
        var (message, _) = await ReadErrorAsync(response);
        return message;
    }

    private static string GetDefaultMessage(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.TooManyRequests => "Слишком много запросов. Попробуйте позже.",
        HttpStatusCode.Unauthorized => "Требуется вход.",
        HttpStatusCode.Forbidden => "Недостаточно прав.",
        _ => $"Ошибка запроса ({(int)statusCode}).",
    };
}
