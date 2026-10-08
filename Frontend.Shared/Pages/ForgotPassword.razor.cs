using Frontend.Shared.Resources;
using Frontend.Shared.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using MudBlazor;

namespace Frontend.Shared.Pages;

public partial class ForgotPassword
{
    [Inject] private AuthService Auth { get; set; } = null!;
    [Inject] private NavigationManager Nav { get; set; } = null!;
    [Inject] private IStringLocalizer<AppStrings> Loc { get; set; } = null!;

    private MudForm _form = null!;
    private string _email = string.Empty;
    private string? _info;
    private string? _error;
    private bool _busy;

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
            var result = await Auth.ForgotPasswordAsync(_email.Trim());
            if (!result.Success)
            {
                _error = result.Error;
                return;
            }

            // Не раскрываем существование почты; сразу ведём на ввод кода.
            _info = Loc["ForgotPassword_SentInfo"];
            Nav.NavigateTo($"/reset-password?email={Uri.EscapeDataString(_email.Trim())}");
        }
        finally
        {
            _busy = false;
        }
    }
}
