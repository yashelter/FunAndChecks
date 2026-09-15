using Frontend.Admin.Dialogs;
using Frontend.Shared.Api;
using Frontend.Shared.Models;
using Frontend.Shared.Resources;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using MudBlazor;

namespace Frontend.Admin.Pages;

public partial class StudentGrading
{
    [Inject] private MeApi Me { get; set; } = null!;
    [Inject] private SubjectsApi Subjects { get; set; } = null!;
    [Inject] private IDialogService DialogService { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;
    [Inject] private IStringLocalizer<AppStrings> Loc { get; set; } = null!;

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
            _results = [];
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
            if (_subjectId is null)
            {
                _results = [];
                return;
            }

            var students = await Subjects.GetStudentsAsync(_subjectId.Value);
            var query = _query.Trim();
            _results = string.IsNullOrEmpty(query)
                ? students
                : students.Where(s => s.FullName.Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToList();
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
            Snackbar.Add(Loc["Grading_SelectSubjectFirst"], Severity.Warning);
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
            Loc["Grading_DialogTitle"],
            parameters,
            new DialogOptions { MaxWidth = MaxWidth.Medium, FullWidth = true });
    }

    private async Task EditAsync(StudentDetailsDto student)
    {
        var parameters = new DialogParameters<EditStudentDialog>
        {
            { x => x.StudentId, student.Id },
            { x => x.CurrentDetails, student }
        };

        var dialog = await DialogService.ShowAsync<EditStudentDialog>(
            Loc["StudentGrading_EditProfileTitle"],
            parameters,
            new DialogOptions { MaxWidth = MaxWidth.Small, FullWidth = true });

        var result = await dialog.Result;
        if (result is { Canceled: false })
        {
            await SearchAsync();
            Snackbar.Add(Loc["StudentGrading_ProfileUpdated"], Severity.Success);
        }
    }
}
