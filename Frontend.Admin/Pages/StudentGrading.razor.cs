using Frontend.Admin.Dialogs;
using Frontend.Shared.Api;
using Frontend.Shared.Models;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Frontend.Admin.Pages;

public partial class StudentGrading
{
    [Inject] private SubjectsApi Subjects { get; set; } = null!;
    [Inject] private StudentsApi Students { get; set; } = null!;
    [Inject] private SubmissionsApi Submissions { get; set; } = null!;
    [Inject] private GradesApi Grades { get; set; } = null!;
    [Inject] private IDialogService DialogService { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    private List<SubjectDto> _subjects = [];
    private List<GradeComponentDto> _components = [];
    private List<TaskWithStatusDto> _tasks = [];
    private readonly Dictionary<int, int> _gradeInputs = [];

    private SubjectDto? _subject;
    private StudentDetailsDto? _student;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            _subjects = await Subjects.GetAllAsync();
        }
        catch (ApiException ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
        }
    }

    private async Task OnSubjectChangedAsync(SubjectDto? subject)
    {
        _subject = subject;
        _tasks = [];
        _components = [];

        if (subject is null)
            return;

        try
        {
            _components = await Subjects.GetGradeComponentsAsync(subject.Id);
            if (_student is not null)
                await LoadStudentDataAsync();
        }
        catch (ApiException ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
        }
    }

    /// <summary>Глобальный поиск по фамилии — не ограничен группой/предметом.</summary>
    private async Task<IEnumerable<StudentDetailsDto>> SearchStudents(string? value, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(value))
            return [];

        try
        {
            return await Students.SearchAsync(value, token);
        }
        catch (ApiException)
        {
            return [];
        }
    }

    private async Task OnStudentChangedAsync(StudentDetailsDto? student)
    {
        _student = student;
        if (student is null || _subject is null)
            return;

        await LoadStudentDataAsync();
    }

    private async Task LoadStudentDataAsync()
    {
        if (_student is null || _subject is null)
            return;

        try
        {
            _tasks = await Students.GetTasksWithStatusAsync(_student.Id, _subject.Id);

            var grades = await Students.GetGradesAsync(_student.Id, _subject.Id);
            _gradeInputs.Clear();
            foreach (var component in _components)
                _gradeInputs[component.Id] = grades.FirstOrDefault(g => g.ComponentId == component.Id)?.Points ?? component.MinPoints;

            StateHasChanged();
        }
        catch (ApiException ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
        }
    }

    private async Task ReworkAsync(int taskId)
    {
        var dialog = await DialogService.ShowAsync<CommentDialog>("Комментарий");
        var result = await dialog.Result;
        if (result is { Canceled: false, Data: string comment })
            await SubmitAsync(taskId, SubmissionStatus.Rejected, comment);
    }

    private async Task SubmitAsync(int taskId, SubmissionStatus status, string? comment = null)
    {
        if (_student is null)
            return;

        try
        {
            await Submissions.CreateAsync(new CreateSubmissionRequest(_student.Id, taskId, status, comment));
            Snackbar.Add("Статус задачи обновлён.", Severity.Success);
            await LoadStudentDataAsync();
        }
        catch (ApiException ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
        }
    }

    private async Task SetGradeAsync(int componentId)
    {
        if (_student is null)
            return;

        try
        {
            await Grades.SetGradeAsync(componentId, _student.Id, new SetGradeRequest(_gradeInputs[componentId], null));
            Snackbar.Add("Оценка сохранена.", Severity.Success);
        }
        catch (ApiException ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
        }
    }

    private static string TaskText(SubmissionStatus status) => status switch
    {
        SubmissionStatus.Rejected => "На доработке",
        SubmissionStatus.Accepted => "Зачтено",
        _ => "Не сдано",
    };

    private static Color TaskColor(SubmissionStatus status) => status switch
    {
        SubmissionStatus.Rejected => Color.Warning,
        SubmissionStatus.Accepted => Color.Success,
        _ => Color.Default,
    };
}
