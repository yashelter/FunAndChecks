using Frontend.Shared.Auth;
using Frontend.Shared.Models;
using Frontend.Shared.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;

namespace Frontend.Shared.Pages;

public partial class Login
{
    [Inject] private AuthService Auth { get; set; } = null!;
    [Inject] private JwtAuthenticationStateProvider AuthState { get; set; } = null!;
    [Inject] private NavigationManager Nav { get; set; } = null!;

    private MudForm _form = null!;
    private string _email = string.Empty;
    private string _password = string.Empty;
    private string? _error;
    private bool _emailNotConfirmed;
    private bool _busy;

    private async Task SubmitAsync()
    {
        await _form.Validate();
        if (!_form.IsValid)
            return;

        _busy = true;
        _error = null;
        _emailNotConfirmed = false;

        var result = await Auth.LoginAsync(new Models.LoginRequest(_email.Trim(), _password));
        if (!result.Success)
        {
            _error = result.Error;
            _emailNotConfirmed = result.Error?.Contains("not confirmed", StringComparison.OrdinalIgnoreCase) == true
                                 || result.Error?.Contains("подтвержд", StringComparison.OrdinalIgnoreCase) == true;
            _busy = false;
            return;
        }

        AuthState.NotifyAuthenticationStateChanged();
        await RedirectByRoleAsync();
    }

    private async Task RedirectByRoleAsync()
    {
        var state = await ((AuthenticationStateProvider)AuthState).GetAuthenticationStateAsync();
        var target = state.User.IsInRole(Roles.Admin) ? "/admin" : "/student";
        Nav.NavigateTo(target);
    }

    private void GoToConfirm() =>
        Nav.NavigateTo($"/confirm-email?email={Uri.EscapeDataString(_email.Trim())}");
}
