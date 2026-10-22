using Frontend.Shared.Api;
using Frontend.Shared.Models;
using Frontend.Shared.Resources;
using Frontend.Shared.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using MudBlazor;

namespace Frontend.Shared.Pages;

public class RegisterModel
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string ConfirmPassword { get; set; } = string.Empty;
    public int? GroupId { get; set; }
    public bool AcceptedPrivacyPolicy { get; set; }
    public bool AcceptedTerms { get; set; }
}

public partial class Register
{
    [Inject] private AuthService Auth { get; set; } = null!;
    [Inject] private GroupsApi Groups { get; set; } = null!;
    [Inject] private NavigationManager Nav { get; set; } = null!;
    [Inject] private IStringLocalizer<AppStrings> Loc { get; set; } = null!;
    [Inject] private FeatureFlagsService Flags { get; set; } = null!;

    private RegisterModel _model = new();
    private Frontend.Shared.Components.ServerValidator _serverValidator = null!;
    private List<GroupDto> _groups = [];

    private bool _showPassword;

    private string? _error;
    private bool _busy;

    private string? ValidateConfirm(string value) =>
        value == _model.Password ? null : Loc["Register_PasswordMismatch"].Value;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            _groups = await Groups.GetAllAsync();
        }
        catch (ApiException ex)
        {
            _error = ex.Message;
        }
    }

    private async Task SubmitAsync()
    {
        _error = null;
        if (_model.GroupId is not int groupId || !_groups.Any(g => g.Id == groupId))
        {
            _serverValidator.DisplayErrors(new() { [nameof(RegisterModel.GroupId)] = [Loc["Register_GroupRequired"].Value] });
            return;
        }

        if (Flags.PrivacyPolicy)
        {
            if (!_model.AcceptedPrivacyPolicy)
            {
                _serverValidator.DisplayErrors(new() { [nameof(RegisterModel.AcceptedPrivacyPolicy)] = [Loc["Register_AcceptPrivacyPolicyRequired"].Value] });
                return;
            }
            if (!_model.AcceptedTerms)
            {
                _serverValidator.DisplayErrors(new() { [nameof(RegisterModel.AcceptedTerms)] = [Loc["Register_AcceptTermsRequired"].Value] });
                return;
            }
        }

        _busy = true;

        var request = new RegisterStudentRequest(
            _model.FirstName.Trim(),
            _model.LastName.Trim(),
            _model.Email.Trim(),
            _model.Password,
            groupId,
            _model.AcceptedPrivacyPolicy,
            _model.AcceptedTerms);

        try
        {
            await Auth.RegisterAsync(request);

            // Подтверждение почты обязательно — ведём на ввод кода из письма.
            Nav.NavigateTo($"/confirm-email?email={Uri.EscapeDataString(_model.Email.Trim())}");
        }
        catch (ApiException ex)
        {
            if (ex.ValidationErrors.Any())
            {
                _serverValidator.DisplayErrors(ex.ValidationErrors);
            }
            else
            {
                _error = ex.Message;
            }
        }
        finally
        {
            _busy = false;
        }
    }
}
