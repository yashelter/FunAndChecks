using Frontend.Shared.Api;
using Frontend.Shared.Models;
using Frontend.Shared.Services;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Frontend.Shared.Pages;

public class RegisterModel
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string ConfirmPassword { get; set; } = string.Empty;
    public int GroupId { get; set; }
}

public partial class Register
{
    [Inject] private AuthService Auth { get; set; } = null!;
    [Inject] private GroupsApi Groups { get; set; } = null!;
    [Inject] private NavigationManager Nav { get; set; } = null!;

    private RegisterModel _model = new();
    private Frontend.Shared.Components.ServerValidator _serverValidator = null!;
    private List<GroupDto> _groups = [];

    private bool _showPassword;

    private string? _error;
    private bool _busy;

    private string? ValidateConfirm(string value) =>
        value == _model.Password ? null : "Пароли не совпадают";

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
        _busy = true;
        _error = null;

        var request = new RegisterStudentRequest(
            _model.FirstName.Trim(),
            _model.LastName.Trim(),
            _model.Email.Trim(),
            _model.Password,
            _model.GroupId);

        try
        {
            var result = await Auth.RegisterAsync(request);
            if (!result.Success)
            {
                _error = result.Error;
                return;
            }

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
