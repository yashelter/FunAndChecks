using Frontend.Shared.Api;
using Frontend.Shared.Models;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Frontend.Admin.Dialogs;

public partial class StudentInteractionDialog
{
    private const string GreenColor = "#43A047";
    private const string BrownColor = "#8D6E63";

    [CascadingParameter] private IMudDialogInstance MudDialog { get; set; } = null!;

    [Parameter] public QueueParticipantDto Student { get; set; } = null!;
    [Parameter] public int EventId { get; set; }
    [Parameter] public int SubjectId { get; set; }

    [Inject] private StudentsApi Students { get; set; } = null!;
    [Inject] private SubjectsApi Subjects { get; set; } = null!;
    [Inject] private SubmissionsApi Submissions { get; set; } = null!;
    [Inject] private GradesApi Grades { get; set; } = null!;
    [Inject] private QueuesApi Queues { get; set; } = null!;
    [Inject] private IDialogService DialogService { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    private List<TaskWithStatusDto> _tasks = [];
    private List<GradeComponentDto> _components = [];
    private readonly Dictionary<int, int> _gradeInputs = [];
    private QueueEntryStatus _status;
    private bool _loadingTasks = true;
    private string? _pickerColor;

    protected override async Task OnInitializedAsync()
    {
        _status = Student.Status;
        await LoadTasksAsync();
        await LoadGradesAsync();
    }

    private async Task LoadTasksAsync()
    {
        _loadingTasks = true;
        try
        {
            _tasks = await Students.GetTasksWithStatusAsync(Student.StudentId, SubjectId);
        }
        catch (ApiException ex)
        {
            Snackbar.Add($"Не удалось загрузить задачи: {ex.Message}", Severity.Error);
        }
        finally
        {
            _loadingTasks = false;
        }
    }

    private async Task LoadGradesAsync()
    {
        try
        {
            _components = await Subjects.GetGradeComponentsAsync(SubjectId);
            var grades = await Students.GetGradesAsync(Student.StudentId, SubjectId);
            _gradeInputs.Clear();
            foreach (var c in _components)
                _gradeInputs[c.Id] = grades.FirstOrDefault(g => g.ComponentId == c.Id)?.Points ?? c.MinPoints;
        }
        catch (ApiException ex)
        {
            Snackbar.Add($"Не удалось загрузить оценки: {ex.Message}", Severity.Error);
        }
    }

    private async Task ChangeStatusAsync(QueueEntryStatus status)
    {
        try
        {
            await Queues.UpdateStatusAsync(EventId, Student.StudentId, new UpdateQueueStatusRequest(status));
            _status = status;
            Snackbar.Add("Статус обновлён.", Severity.Success);

            if (status != QueueEntryStatus.Checking)
                MudDialog.Close(DialogResult.Ok(true));
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
        try
        {
            await Submissions.CreateAsync(new CreateSubmissionRequest(Student.StudentId, taskId, status, comment));
            Snackbar.Add("Статус задачи обновлён.", Severity.Success);
            await LoadTasksAsync();
        }
        catch (ApiException ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
        }
    }

    private async Task SetGradeAsync(int componentId)
    {
        try
        {
            await Grades.SetGradeAsync(componentId, Student.StudentId, new SetGradeRequest(_gradeInputs[componentId], null));
            Snackbar.Add("Оценка сохранена.", Severity.Success);
        }
        catch (ApiException ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
        }
    }

    private async Task ApplyColorAsync(string? color)
    {
        try
        {
            await Students.SetColorAsync(Student.StudentId, new SetStudentColorRequest(color));
            Snackbar.Add(color is null ? "Заливка убрана." : "Цвет применён.", Severity.Success);
        }
        catch (ApiException ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
        }
    }

    private void Cancel() => MudDialog.Close(DialogResult.Cancel());

    private static string StatusText(SubmissionStatus status) => status switch
    {
        SubmissionStatus.Rejected => "На доработке",
        SubmissionStatus.Accepted => "Зачтено",
        _ => "Не сдано",
    };

    private static Color StatusColor(SubmissionStatus status) => status switch
    {
        SubmissionStatus.Rejected => Color.Warning,
        SubmissionStatus.Accepted => Color.Success,
        _ => Color.Default,
    };
}
