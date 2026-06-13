using Frontend.Shared.Auth;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace Frontend.Pages;

/// <summary>Точка входа «/»: ведёт в нужную область по роли или на вход.</summary>
public partial class Home
{
    [Inject] private AuthenticationStateProvider AuthState { get; set; } = null!;
    [Inject] private NavigationManager Nav { get; set; } = null!;

    protected override async Task OnInitializedAsync()
    {
        var state = await AuthState.GetAuthenticationStateAsync();

        if (state.User.Identity?.IsAuthenticated != true)
        {
            Nav.NavigateTo("/login");
            return;
        }

        Nav.NavigateTo(state.User.IsInRole(Roles.Admin) ? "/admin" : "/student");
    }
}
