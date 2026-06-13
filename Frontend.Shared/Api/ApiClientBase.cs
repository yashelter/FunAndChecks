using System.Net.Http.Json;

namespace Frontend.Shared.Api;

/// <summary>Базовые помощники для типизированных клиентов: вызов + разбор ошибок ProblemDetails.</summary>
public abstract class ApiClientBase(HttpClient http)
{
    protected HttpClient Http { get; } = http;

    protected async Task<T> GetAsync<T>(string url, CancellationToken ct = default)
    {
        var response = await Http.GetAsync(url, ct);
        await response.EnsureSuccessAsync();
        return (await response.Content.ReadFromJsonAsync<T>(ct))!;
    }

    protected async Task<TRes> PostAsync<TRes>(string url, object body, CancellationToken ct = default)
    {
        var response = await Http.PostAsJsonAsync(url, body, ct);
        await response.EnsureSuccessAsync();
        return (await response.Content.ReadFromJsonAsync<TRes>(ct))!;
    }

    protected async Task PostAsync(string url, object? body = null, CancellationToken ct = default)
    {
        using var response = body is null
            ? await Http.PostAsync(url, content: null, ct)
            : await Http.PostAsJsonAsync(url, body, ct);
        await response.EnsureSuccessAsync();
    }

    protected async Task<TRes> PutAsync<TRes>(string url, object body, CancellationToken ct = default)
    {
        var response = await Http.PutAsJsonAsync(url, body, ct);
        await response.EnsureSuccessAsync();
        return (await response.Content.ReadFromJsonAsync<TRes>(ct))!;
    }

    protected async Task PutAsync(string url, object? body = null, CancellationToken ct = default)
    {
        using var response = body is null
            ? await Http.PutAsync(url, content: null, ct)
            : await Http.PutAsJsonAsync(url, body, ct);
        await response.EnsureSuccessAsync();
    }

    protected async Task DeleteAsync(string url, CancellationToken ct = default)
    {
        using var response = await Http.DeleteAsync(url, ct);
        await response.EnsureSuccessAsync();
    }
}
