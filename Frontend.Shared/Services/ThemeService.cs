using Microsoft.JSInterop;

namespace Frontend.Shared.Services;

/// <summary>Хранит и переключает тёмную/светлую тему, запоминая выбор в localStorage.</summary>
public class ThemeService(IJSRuntime js)
{
    private const string ThemePreferenceKey = "theme_preference";

    public bool IsDarkMode { get; private set; }

    public event Action? OnThemeChanged;

    public async Task InitializeAsync()
    {
        try
        {
            var preference = await js.InvokeAsync<string?>("localStorage.getItem", ThemePreferenceKey);
            IsDarkMode = !string.IsNullOrEmpty(preference) && bool.Parse(preference);
        }
        catch
        {
            IsDarkMode = false;
        }

        OnThemeChanged?.Invoke();
    }

    public async Task ToggleThemeAsync()
    {
        IsDarkMode = !IsDarkMode;
        await js.InvokeVoidAsync("localStorage.setItem", ThemePreferenceKey, IsDarkMode.ToString());
        OnThemeChanged?.Invoke();
    }
}
