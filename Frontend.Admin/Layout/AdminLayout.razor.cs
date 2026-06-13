using Frontend.Shared.Api;
using Frontend.Shared.Services;
using Microsoft.AspNetCore.Components;

namespace Frontend.Admin.Layout;

public partial class AdminLayout
{
    [Inject] private MeApi Me { get; set; } = null!;
    [Inject] private AuthService Auth { get; set; } = null!;
    [Inject] private JwtAuthenticationStateProvider AuthState { get; set; } = null!;
    [Inject] private NavigationManager Nav { get; set; } = null!;

    private bool _drawerOpen = true;
    private string? _userName;

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

    private void ToggleDrawer() => _drawerOpen = !_drawerOpen;

    private async Task LogoutAsync()
    {
        await Auth.LogoutAsync();
        AuthState.NotifyAuthenticationStateChanged();
        Nav.NavigateTo("/login");
    }
}
