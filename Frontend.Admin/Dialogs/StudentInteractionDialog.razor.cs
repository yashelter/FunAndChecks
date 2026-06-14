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

    [Parameter] public Guid StudentId { get; set; }
    [Parameter] public string StudentName { get; set; } = "";
    [Parameter] public string? GroupName { get; set; }
    /// <summary>Id события очереди; null — оценивание вне очереди (без статус-кнопок).</summary>
    [Parameter] public int? EventId { get; set; }
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
    private readonly Dictionary<int, int> _currentGrades = [];
    private readonly HashSet<int> _openHistory = [];
    private readonly Dictionary<int, List<SubmissionLogDto>> _history = [];
    private bool _loadingTasks = true;
    private string? _pickerColor;

    protected override async Task OnInitializedAsync()
    {
        await LoadTasksAsync();
        await LoadGradesAsync();

        // История несданных задач (статус не «Зачтено») раскрыта по умолчанию.
        foreach (var task in _tasks.Where(t => t.Status != SubmissionStatus.Accepted))
        {
            _openHistory.Add(task.Id);
            await LoadHistoryAsync(task.Id);
        }
    }

    private async Task ToggleHistoryAsync(int taskId)
    {
        if (!_openHistory.Add(taskId))
        {
            _openHistory.Remove(taskId);
            return;
        }

        await LoadHistoryAsync(taskId);
    }

    private async Task LoadHistoryAsync(int taskId)
    {
        try
        {
            _history[taskId] = await Submissions.GetLogAsync(StudentId, taskId);
        }
        catch (ApiException)
        {
            // Нет сдач — пустая история.
            _history[taskId] = [];
        }
    }

    private async Task LoadTasksAsync()
    {
        _loadingTasks = true;
        try
        {
            _tasks = await Students.GetTasksWithStatusAsync(StudentId, SubjectId);
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
            var grades = await Students.GetGradesAsync(StudentId, SubjectId);
            _gradeInputs.Clear();
            _currentGrades.Clear();
            foreach (var c in _components)
            {
                var existing = grades.FirstOrDefault(g => g.ComponentId == c.Id);
                _gradeInputs[c.Id] = existing?.Points ?? c.MinPoints;
                if (existing is not null)
                    _currentGrades[c.Id] = existing.Points;
            }
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
            await Queues.UpdateStatusAsync(EventId!.Value, StudentId, new UpdateQueueStatusRequest(status));
            Snackbar.Add("Статус обновлён.", Severity.Success);
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
            await Submissions.CreateAsync(new CreateSubmissionRequest(StudentId, taskId, status, comment));
            Snackbar.Add("Статус задачи обновлён.", Severity.Success);
            await LoadTasksAsync();
            if (_openHistory.Contains(taskId))
                await LoadHistoryAsync(taskId);
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
            await Grades.SetGradeAsync(componentId, StudentId, new SetGradeRequest(_gradeInputs[componentId], null));
            _currentGrades[componentId] = _gradeInputs[componentId];
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
            await Students.SetColorAsync(StudentId, new SetStudentColorRequest(color));
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
