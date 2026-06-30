using Frontend.Shared.Api;
using Frontend.Shared.Components;
using Frontend.Shared.Models;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Frontend.Admin.Dialogs;

public partial class EditStudentDialog : ComponentBase
{
    [CascadingParameter] IMudDialogInstance MudDialog { get; set; } = null!;
    [Parameter] public Guid StudentId { get; set; }
    [Parameter] public StudentDetailsDto CurrentDetails { get; set; } = null!;

    private ServerValidator _serverValidator = null!;
    private EditStudentModel _model = new();
    private bool _busy;
    private List<GroupDto> _groups = new();

    protected override async Task OnInitializedAsync()
    {
        _model.FirstName = CurrentDetails.FirstName;
        _model.LastName = CurrentDetails.LastName;
        _model.Email = CurrentDetails.Email ?? "";
        _model.GroupId = CurrentDetails.GroupId;

        _groups = await Groups.GetAllAsync();
    }

    private async Task SubmitAsync()
    {
        _serverValidator.ClearErrors();
        _busy = true;

        try
        {
            var request = new UpdateStudentAccountRequest(
                _model.FirstName,
                _model.LastName,
                _model.GroupId,
                _model.Email,
                _model.NewPassword
            );

            await Students.UpdateAccountAsync(StudentId, request);
            MudDialog.Close(DialogResult.Ok(true));
        }
        catch (ApiException ex)
        {
            _serverValidator.DisplayErrors(ex.ValidationErrors);
            if (ex.ValidationErrors.Count == 0)
                _serverValidator.DisplayErrors(new Dictionary<string, string[]> { [""] = new[] { ex.Message } });
        }
        finally
        {
            _busy = false;
        }
    }

    private void Cancel() => MudDialog.Cancel();

    private class EditStudentModel
    {
        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
        public string Email { get; set; } = "";
        public string? NewPassword { get; set; }
        public int? GroupId { get; set; }
    }
}
