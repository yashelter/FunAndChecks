using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;

namespace Frontend.Shared.Services;

/// <summary>
/// «Текущий предмет», общий для всех страниц кабинета (результаты, оценивание, посещаемость, управление,
/// история): выбрал на одной — открывается выбранным на других и после перезагрузки.
/// Хранится в браузере отдельно для каждого аккаунта.
/// </summary>
public sealed class CurrentSubject(IJSRuntime js, AuthenticationStateProvider auth)
{
    private int? _cached;
    private string? _cachedFor;

    /// <summary>
    /// Выбирает предмет из доступного списка: запомненный, если он есть в списке, иначе единственный.
    /// null — выбирать должен пользователь.
    /// </summary>
    public async Task<int?> PickAsync(IEnumerable<int> availableIds)
    {
        var ids = availableIds.ToList();
        var stored = await GetAsync();
        if (stored is int id && ids.Contains(id)) return id;
        return ids.Count == 1 ? ids[0] : null;
    }

    public async Task<int?> GetAsync()
    {
        var key = await KeyAsync();
        if (key is null) return null;
        if (_cachedFor == key) return _cached;
        try
        {
            var raw = await js.InvokeAsync<string?>("localStorage.getItem", key);
            _cached = int.TryParse(raw, out var id) ? id : null;
            _cachedFor = key;
        }
        catch (JSException) { return null; }
        return _cached;
    }

    public async Task SetAsync(int? subjectId)
    {
        var key = await KeyAsync();
        if (key is null || subjectId is null) return;
        _cached = subjectId;
        _cachedFor = key;
        try { await js.InvokeVoidAsync("localStorage.setItem", key, subjectId.Value.ToString()); }
        catch (JSException) { /* Запомним хотя бы до перезагрузки. */ }
    }

    private async Task<string?> KeyAsync()
    {
        var user = (await auth.GetAuthenticationStateAsync()).User;
        var id = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return id is null ? null : $"fc-current-subject:{id}";
    }
}
