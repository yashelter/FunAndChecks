using Frontend.Admin.Dialogs;
using Frontend.Shared.Api;
using Frontend.Shared.Models;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Frontend.Admin.Pages;

public partial class StudentGrading
{
    [Inject] private MeApi Me { get; set; } = null!;
    [Inject] private StudentsApi Students { get; set; } = null!;
    [Inject] private IDialogService DialogService { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    private List<SubjectDto> _subjects = [];
    private List<StudentDetailsDto> _results = [];
    private int? _subjectId;
    private string _query = "";
    private bool _searching;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            _subjects = await Me.GetVisibleSubjectsAsync();
            await SearchAsync(); // пустой запрос → весь список по алфавиту
        }
        catch (ApiException ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
        }
    }

    private async Task SearchAsync()
    {
        _searching = true;
        try
        {
            _results = await Students.SearchAsync(_query.Trim());
        }
        catch (ApiException ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
        }
        finally
        {
            _searching = false;
        }
    }

    private async Task OpenAsync(StudentDetailsDto student)
    {
        if (_subjectId is null)
        {
            Snackbar.Add("Сначала выберите предмет.", Severity.Warning);
            return;
        }

        // Тот же интерфейс, что и из очереди, но без queue-статусов (EventId = null).
        var parameters = new DialogParameters<StudentInteractionDialog>
        {
            { x => x.StudentId, student.Id },
            { x => x.StudentName, $"{student.LastName} {student.FirstName}" },
            { x => x.GroupName, null },
            { x => x.EventId, (int?)null },
            { x => x.SubjectId, _subjectId.Value },
        };

        await DialogService.ShowAsync<StudentInteractionDialog>(
            "Оценивание",
            parameters,
            new DialogOptions { MaxWidth = MaxWidth.Medium, FullWidth = true });
    }
}
