using System.Net;
using System.Net.Http.Json;
using Frontend.Shared.Resources;
using Microsoft.Extensions.Localization;

namespace Frontend.Shared.Api;

/// <summary>Базовые помощники для типизированных клиентов: вызов + разбор ошибок ProblemDetails.</summary>
public abstract class ApiClientBase(HttpClient http, IStringLocalizer<AppStrings> loc)
{
    protected HttpClient Http { get; } = http;
    protected IStringLocalizer<AppStrings> Loc { get; } = loc;

    protected Task<T> GetAsync<T>(string url, CancellationToken ct = default) =>
        ExecuteAsync(async () =>
        {
            var response = await Http.GetAsync(url, ct);
            await response.EnsureSuccessAsync(Loc);
            return (await response.Content.ReadFromJsonAsync<T>(ct))!;
        });

    protected Task<TRes> PostAsync<TRes>(string url, object body, CancellationToken ct = default) =>
        ExecuteAsync(async () =>
        {
            var response = await Http.PostAsJsonAsync(url, body, ct);
            await response.EnsureSuccessAsync(Loc);
            return (await response.Content.ReadFromJsonAsync<TRes>(ct))!;
        });

    protected Task PostAsync(string url, object? body = null, CancellationToken ct = default) =>
        ExecuteAsync(async () =>
        {
            using var response = body is null
                ? await Http.PostAsync(url, content: null, ct)
                : await Http.PostAsJsonAsync(url, body, ct);
            await response.EnsureSuccessAsync(Loc);
        });

    protected Task<TRes> PutAsync<TRes>(string url, object body, CancellationToken ct = default) =>
        ExecuteAsync(async () =>
        {
            var response = await Http.PutAsJsonAsync(url, body, ct);
            await response.EnsureSuccessAsync(Loc);
            return (await response.Content.ReadFromJsonAsync<TRes>(ct))!;
        });

    protected Task PutAsync(string url, object? body = null, CancellationToken ct = default) =>
        ExecuteAsync(async () =>
        {
            using var response = body is null
                ? await Http.PutAsync(url, content: null, ct)
                : await Http.PutAsJsonAsync(url, body, ct);
            await response.EnsureSuccessAsync(Loc);
        });

    protected Task DeleteAsync(string url, CancellationToken ct = default) =>
        ExecuteAsync(async () =>
        {
            using var response = await Http.DeleteAsync(url, ct);
            await response.EnsureSuccessAsync(Loc);
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
            throw new ApiException(HttpStatusCode.ServiceUnavailable, Loc["Common_NetworkError"].Value);
        }
        catch (Exception ex)
        {
            throw new ApiException(HttpStatusCode.InternalServerError, string.Format(Loc["Common_UnknownError"].Value, ex.Message));
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
            throw new ApiException(HttpStatusCode.ServiceUnavailable, Loc["Common_NetworkError"].Value);
        }
        catch (Exception ex)
        {
            throw new ApiException(HttpStatusCode.InternalServerError, string.Format(Loc["Common_UnknownError"].Value, ex.Message));
        }
    }
}
