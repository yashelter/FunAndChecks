using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Frontend.Shared.Api;

/// <summary>Ошибка обращения к API с человекочитаемым сообщением из ProblemDetails.</summary>
public class ApiException(HttpStatusCode statusCode, string message) : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}

public static class HttpResponseExtensions
{
    private sealed record ProblemDetails(string? Detail, string? Title);

    /// <summary>Бросает <see cref="ApiException"/> с сообщением из ProblemDetails, если ответ неуспешен.</summary>
    public static async Task EnsureSuccessAsync(this HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
            return;

        throw new ApiException(response.StatusCode, await ReadErrorMessageAsync(response));
    }

    public static async Task<string> ReadErrorMessageAsync(this HttpResponseMessage response)
    {
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
            if (!string.IsNullOrWhiteSpace(problem?.Detail))
                return problem!.Detail!;
            if (!string.IsNullOrWhiteSpace(problem?.Title))
                return problem!.Title!;
        }
        catch (JsonException) { /* тело не ProblemDetails */ }
        catch (NotSupportedException) { /* не JSON */ }

        return response.StatusCode switch
        {
            HttpStatusCode.TooManyRequests => "Слишком много запросов. Попробуйте позже.",
            HttpStatusCode.Unauthorized => "Требуется вход.",
            HttpStatusCode.Forbidden => "Недостаточно прав.",
            _ => $"Ошибка запроса ({(int)response.StatusCode}).",
        };
    }
}
