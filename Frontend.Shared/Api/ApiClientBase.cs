using System.Net.Http.Json;

namespace Frontend.Shared.Api;

/// <summary>Базовые помощники для типизированных клиентов: вызов + разбор ошибок ProblemDetails.</summary>
public abstract class ApiClientBase(HttpClient http)
{
    protected HttpClient Http { get; } = http;

    protected Task<T> GetAsync<T>(string url, CancellationToken ct = default) =>
        ExecuteAsync(async () =>
        {
            var response = await Http.GetAsync(url, ct);
            await response.EnsureSuccessAsync();
            return (await response.Content.ReadFromJsonAsync<T>(ct))!;
        });

    protected Task<TRes> PostAsync<TRes>(string url, object body, CancellationToken ct = default) =>
        ExecuteAsync(async () =>
        {
            var response = await Http.PostAsJsonAsync(url, body, ct);
            await response.EnsureSuccessAsync();
            return (await response.Content.ReadFromJsonAsync<TRes>(ct))!;
        });

    protected Task PostAsync(string url, object? body = null, CancellationToken ct = default) =>
        ExecuteAsync(async () =>
        {
            using var response = body is null
                ? await Http.PostAsync(url, content: null, ct)
                : await Http.PostAsJsonAsync(url, body, ct);
            await response.EnsureSuccessAsync();
        });

    protected Task<TRes> PutAsync<TRes>(string url, object body, CancellationToken ct = default) =>
        ExecuteAsync(async () =>
        {
            var response = await Http.PutAsJsonAsync(url, body, ct);
            await response.EnsureSuccessAsync();
            return (await response.Content.ReadFromJsonAsync<TRes>(ct))!;
        });

    protected Task PutAsync(string url, object? body = null, CancellationToken ct = default) =>
        ExecuteAsync(async () =>
        {
            using var response = body is null
                ? await Http.PutAsync(url, content: null, ct)
                : await Http.PutAsJsonAsync(url, body, ct);
            await response.EnsureSuccessAsync();
        });

    protected Task DeleteAsync(string url, CancellationToken ct = default) =>
        ExecuteAsync(async () =>
        {
            using var response = await Http.DeleteAsync(url, ct);
            await response.EnsureSuccessAsync();
        });

    private async Task<T> ExecuteAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (ApiException)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            throw new ApiException(System.Net.HttpStatusCode.ServiceUnavailable, "Ошибка сети. Проверьте подключение.");
        }
        catch (Exception ex)
        {
            throw new ApiException(System.Net.HttpStatusCode.InternalServerError, $"Неизвестная ошибка: {ex.Message}");
        }
    }

    private async Task ExecuteAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (ApiException)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            throw new ApiException(System.Net.HttpStatusCode.ServiceUnavailable, "Ошибка сети. Проверьте подключение.");
        }
        catch (Exception ex)
        {
            throw new ApiException(System.Net.HttpStatusCode.InternalServerError, $"Неизвестная ошибка: {ex.Message}");
        }
    }
}
