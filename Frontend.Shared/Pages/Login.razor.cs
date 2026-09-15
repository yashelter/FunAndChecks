using Frontend.Shared.Auth;
using Frontend.Shared.Models;
using Frontend.Shared.Resources;
using Frontend.Shared.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Localization;
using MudBlazor;

namespace Frontend.Shared.Pages;

public partial class Login
{
    [Inject] private AuthService Auth { get; set; } = null!;
    [Inject] private JwtAuthenticationStateProvider AuthState { get; set; } = null!;
    [Inject] private NavigationManager Nav { get; set; } = null!;
    [Inject] private IStringLocalizer<AppStrings> Loc { get; set; } = null!;

    private MudForm _form = null!;
    private string _email = string.Empty;
    private string _password = string.Empty;
    private string? _error;
    private bool _emailNotConfirmed;
    private bool _busy;

    private async Task HandleKeyDownAsync(KeyboardEventArgs e)
    {
        if (e.Key is "Enter" or "NumpadEnter")
            await SubmitAsync();
    }

    private async Task SubmitAsync()
    {
        if (_busy) return; // защита от двойного Enter/клика, пока идёт запрос

        _busy = true;
        try
        {
            await _form.ValidateAsync();
            if (!_form.IsValid)
                return;

            _error = null;
            _emailNotConfirmed = false;

            var result = await Auth.LoginAsync(new Models.LoginRequest(_email.Trim(), _password));
            if (!result.Success)
            {
                _error = result.Error;
                _emailNotConfirmed = result.Code == "auth.email_not_confirmed";
                return;
            }

            AuthState.NotifyAuthenticationStateChanged();
            await RedirectByRoleAsync();
        }
        finally
        {
            _busy = false;
        }
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
