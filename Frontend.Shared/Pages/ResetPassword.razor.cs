using Frontend.Shared.Models;
using Frontend.Shared.Resources;
using Frontend.Shared.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using MudBlazor;

namespace Frontend.Shared.Pages;

public partial class ResetPassword
{
    [Inject] private AuthService Auth { get; set; } = null!;
    [Inject] private NavigationManager Nav { get; set; } = null!;
    [Inject] private IStringLocalizer<AppStrings> Loc { get; set; } = null!;

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
        if (_busy) return; // защита от двойного Enter/клика, пока идёт запрос

        _busy = true;
        try
        {
            await _form.ValidateAsync();
            if (!_form.IsValid)
                return;

            _error = null;
            var result = await Auth.ResetPasswordAsync(new ResetPasswordRequest(_email.Trim(), _code.Trim(), _newPassword));
            if (!result.Success)
            {
                _error = result.Error;
                return;
            }

            Nav.NavigateTo("/login");
        }
        finally
        {
            _busy = false;
        }
    }
}
