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
    private bool _busy;

    private async Task SubmitAsync()
    {
        await _form.ValidateAsync();
        if (!_form.IsValid)
            return;

        _busy = true;
        await Auth.ForgotPasswordAsync(_email.Trim());
        _busy = false;

        // Не раскрываем существование почты; сразу ведём на ввод кода.
        _info = Loc["ForgotPassword_SentInfo"];
        Nav.NavigateTo($"/reset-password?email={Uri.EscapeDataString(_email.Trim())}");
    }
}
