using Frontend.Shared.Api;
using Frontend.Shared.Services;
using Microsoft.AspNetCore.Components;

namespace Frontend.Admin.Layout;

public partial class AdminLayout : IDisposable
{
    [Inject] private MeApi Me { get; set; } = null!;
    [Inject] private AuthService Auth { get; set; } = null!;
    [Inject] private JwtAuthenticationStateProvider AuthState { get; set; } = null!;
    [Inject] private ThemeService Theme { get; set; } = null!;
    [Inject] private NavigationManager Nav { get; set; } = null!;

    private bool _drawerOpen = true;
    private string? _userName;

    protected override void OnInitialized() => Theme.OnThemeChanged += OnThemeChanged;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            var me = await Me.GetMeAsync();
            _userName = me.FullName;
        }
        catch
        {
            // имя в шапке некритично
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
            await Theme.InitializeAsync();
    }

    private void OnThemeChanged() => InvokeAsync(StateHasChanged);

    private Task ToggleThemeAsync() => Theme.ToggleThemeAsync();

    private void ToggleDrawer() => _drawerOpen = !_drawerOpen;

    public void Dispose() => Theme.OnThemeChanged -= OnThemeChanged;

    private async Task LogoutAsync()
    {
        await Auth.LogoutAsync();
        AuthState.NotifyAuthenticationStateChanged();
        Nav.NavigateTo("/login");
    }
}
