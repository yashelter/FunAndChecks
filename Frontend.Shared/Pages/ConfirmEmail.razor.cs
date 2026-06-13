using Frontend.Shared.Models;
using Frontend.Shared.Services;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Frontend.Shared.Pages;

public partial class ConfirmEmail
{
    [Inject] private AuthService Auth { get; set; } = null!;
    [Inject] private NavigationManager Nav { get; set; } = null!;

    /// <summary>Email можно передать в query (?email=...) — подставляется после регистрации.</summary>
    [Parameter, SupplyParameterFromQuery(Name = "email")]
    public string? EmailFromQuery { get; set; }

    private MudForm _form = null!;
    private string _email = string.Empty;
    private string _code = string.Empty;
    private string? _error;
    private string? _info;
    private bool _busy;

    protected override void OnInitialized()
    {
        if (!string.IsNullOrWhiteSpace(EmailFromQuery))
            _email = EmailFromQuery;
    }

    private async Task SubmitAsync()
    {
        await _form.Validate();
        if (!_form.IsValid)
            return;

        _busy = true;
        _error = null;
        _info = null;

        var result = await Auth.ConfirmEmailAsync(new ConfirmEmailRequest(_email.Trim(), _code.Trim()));
        _busy = false;

        if (!result.Success)
        {
            _error = result.Error;
            return;
        }

        Nav.NavigateTo("/login");
    }

    private async Task ResendAsync()
    {
        _error = null;
        _info = null;

        if (string.IsNullOrWhiteSpace(_email))
        {
            _error = "Укажите email, чтобы отправить код повторно.";
            return;
        }

        await Auth.ResendConfirmationAsync(_email.Trim());
        _info = "Если аккаунт существует и не подтверждён, код отправлен повторно.";
    }
}
