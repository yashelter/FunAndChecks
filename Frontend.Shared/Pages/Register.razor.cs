using Frontend.Shared.Api;
using Frontend.Shared.Models;
using Frontend.Shared.Services;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Frontend.Shared.Pages;

public partial class Register
{
    [Inject] private AuthService Auth { get; set; } = null!;
    [Inject] private GroupsApi Groups { get; set; } = null!;
    [Inject] private NavigationManager Nav { get; set; } = null!;

    private MudForm _form = null!;
    private List<GroupDto> _groups = [];

    private string _firstName = string.Empty;
    private string _lastName = string.Empty;
    private string _email = string.Empty;
    private string _password = string.Empty;
    private int _groupId;
    private string? _gitHubUrl;
    private string? _color;

    private string? _error;
    private bool _busy;

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
        await _form.Validate();
        if (!_form.IsValid)
            return;

        _busy = true;
        _error = null;

        var request = new RegisterStudentRequest(
            _firstName.Trim(),
            _lastName.Trim(),
            _email.Trim(),
            _password,
            _groupId,
            string.IsNullOrWhiteSpace(_gitHubUrl) ? null : _gitHubUrl.Trim(),
            string.IsNullOrWhiteSpace(_color) ? null : _color.Trim());

        var result = await Auth.RegisterAsync(request);
        _busy = false;

        if (!result.Success)
        {
            _error = result.Error;
            return;
        }

        // Подтверждение почты обязательно — ведём на ввод кода из письма.
        Nav.NavigateTo($"/confirm-email?email={Uri.EscapeDataString(_email.Trim())}");
    }
}
