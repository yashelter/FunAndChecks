using Frontend.Shared.Services;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Frontend.Shared.Pages;

public partial class ForgotPassword
{
    [Inject] private AuthService Auth { get; set; } = null!;
    [Inject] private NavigationManager Nav { get; set; } = null!;

    private MudForm _form = null!;
    private string _email = string.Empty;
    private string? _info;
    private bool _busy;

    private async Task SubmitAsync()
    {
        await _form.Validate();
        if (!_form.IsValid)
            return;

        _busy = true;
        await Auth.ForgotPasswordAsync(_email.Trim());
        _busy = false;

        // Не раскрываем существование почты; сразу ведём на ввод кода.
        _info = "Если аккаунт с такой почтой существует, код отправлен.";
        Nav.NavigateTo($"/reset-password?email={Uri.EscapeDataString(_email.Trim())}");
    }
}
