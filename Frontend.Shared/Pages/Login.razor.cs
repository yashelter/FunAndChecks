using Frontend.Shared.Api;
using Frontend.Shared.Auth;
using Frontend.Shared.Models;
using Frontend.Shared.Resources;
using Frontend.Shared.Services;
using Frontend.Shared.UI;
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
    [Inject] private IDialogService DialogService { get; set; } = null!;
    [SupplyParameterFromQuery(Name = "returnUrl")] public string? ReturnUrl { get; set; }

    private MudForm _form = null!;
    private string _email = string.Empty;
    private string _password = string.Empty;
    private string? _error;
    private bool _emailNotConfirmed;
    private bool _busy, _showPassword, _sharedComputer;

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

            var result = await Auth.LoginAsync(new Models.LoginRequest(_email.Trim(), _password), _sharedComputer);
            if (!result.Success)
            {
                _error = result.Error;
                _emailNotConfirmed = result.Code == "auth.email_not_confirmed";
                return;
            }

            AuthState.NotifyAuthenticationStateChanged();

            // Согласие с правовыми документами спрашивает кабинет (LegalConsentGate в WorkspaceShell):
            // там его увидят и уже вошедшие раньше пользователи.
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
        var home = state.User.IsInRole(Roles.Admin) ? "/admin" : "/student";
        Nav.NavigateTo(SafeReturnUrl(ReturnUrl, home) ?? home);
    }


    /// <summary>
    /// Возврат после входа — только на страницы своего кабинета или мониторинг,
    /// чтобы ссылка вида /login?returnUrl=//evil.example не уводила с сайта.
    /// </summary>
    public static string? SafeReturnUrl(string? returnUrl, string home)
    {
        if (string.IsNullOrEmpty(returnUrl) || !returnUrl.StartsWith('/') || returnUrl.StartsWith("//", StringComparison.Ordinal) ||
            returnUrl.Contains('\\') || returnUrl.Any(char.IsControl))
            return null;
        var path = returnUrl.Split('?', '#')[0];
        return path == "/metrics" || path == home || path.StartsWith(home + "/", StringComparison.Ordinal) ? returnUrl : null;
    }

    private void GoToConfirm() =>
        Nav.NavigateTo($"/confirm-email?email={Uri.EscapeDataString(_email.Trim())}");
}
