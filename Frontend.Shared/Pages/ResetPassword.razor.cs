using Frontend.Shared.Models;
using Frontend.Shared.Services;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Frontend.Shared.Pages;

public partial class ResetPassword
{
    [Inject] private AuthService Auth { get; set; } = null!;
    [Inject] private NavigationManager Nav { get; set; } = null!;

    [Parameter, SupplyParameterFromQuery(Name = "email")]
    public string? EmailFromQuery { get; set; }

    private MudForm _form = null!;
    private string _email = string.Empty;
    private string _code = string.Empty;
    private string _newPassword = string.Empty;
    private string? _error;
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

        var result = await Auth.ResetPasswordAsync(new ResetPasswordRequest(_email.Trim(), _code.Trim(), _newPassword));
        _busy = false;

        if (!result.Success)
        {
            _error = result.Error;
            return;
        }

        Nav.NavigateTo("/login");
    }
}
