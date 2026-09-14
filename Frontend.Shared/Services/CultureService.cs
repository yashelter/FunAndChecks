using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Frontend.Shared.Services;

/// <summary>
/// Хранит и переключает язык интерфейса, запоминая выбор в localStorage.
/// По образцу <see cref="ThemeService"/>.
/// </summary>
public class CultureService(IJSRuntime js, NavigationManager nav)
{
    public string CurrentCulture { get; private set; } = "en-US";

    /// <summary>
    /// Инициализирует культуру из localStorage; если не сохранена —
    /// определяет по языку браузера (navigator.language).
    /// Вызывается из Program.cs перед RunAsync.
    /// </summary>
    public async Task InitializeAsync()
    {
        try
        {
            CurrentCulture = await js.InvokeAsync<string>("culturePreference.get");
        }
        catch
        {
            CurrentCulture = "en-US";
        }
    }

    /// <summary>
    /// Сохраняет выбранную культуру в localStorage и перезагружает страницу
    /// (forceLoad необходим для применения новой культуры в WASM-рантайме).
    /// </summary>
    public async Task SetCultureAsync(string culture)
    {
        if (culture is not ("en-US" or "ru-RU"))
            throw new ArgumentOutOfRangeException(nameof(culture));
        await js.InvokeVoidAsync("culturePreference.set", culture);
        nav.NavigateTo(nav.Uri, forceLoad: true);
    }

    public async Task SynchronizeAccountCultureAsync(string? culture)
    {
        if (culture is not ("en-US" or "ru-RU") || culture == CurrentCulture)
            return;
        await SetCultureAsync(culture);
    }
}
