using Frontend.Shared.Api;
using Frontend.Shared.Models;
using Frontend.Shared.Resources;
using Frontend.Shared.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using MudBlazor;

namespace Frontend.Admin.Dialogs;

public partial class StudentInteractionDialog : IDisposable
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
    [Parameter] public bool ReadOnly { get; set; }
    [Parameter] public string? StudentColor { get; set; }
    [Parameter] public string? CheckingWarningName { get; set; }

    [Inject] private StudentsApi Students { get; set; } = null!;
    [Inject] private SubjectsApi Subjects { get; set; } = null!;
    [Inject] private SubmissionsApi Submissions { get; set; } = null!;
    [Inject] private GradesApi Grades { get; set; } = null!;
    [Inject] private QueuesApi Queues { get; set; } = null!;
    [Inject] private IDialogService DialogService { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;
    [Inject] private IStringLocalizer<AppStrings> Loc { get; set; } = null!;
    [Inject] private UnsavedChangesTracker Dirty { get; set; } = null!;

    private List<TaskWithStatusDto> _tasks = [];
    private List<GradeComponentDto> _components = [];
    private readonly Dictionary<int, int> _gradeInputs = [];
    private readonly Dictionary<int, int> _currentGrades = [];
    private readonly HashSet<int> _openHistory = [];
    private readonly Dictionary<int, List<SubmissionLogDto>> _history = [];
    private bool _loadingTasks = true;
    private bool _busy;
    private bool _hasChanges;
    private string? _pickerColor;
    private MudBlazor.Utilities.MudColor? _pickerMudColor;
    // Выбран ли цвет (включая сброс в null) — до выбора «Применить» неактивна.
    private bool _pickerTouched;
    private UnsavedChangesTracker.Registration _edits = null!;

    private void SetPickerColor(string? hex)
    {
        _pickerColor = hex;
        _pickerMudColor = hex is null ? null : new MudBlazor.Utilities.MudColor(hex);
        _pickerTouched = true;
    }

    private void OnColorPickerChanged(MudBlazor.Utilities.MudColor? color)
    {
        _pickerMudColor = color;
        _pickerColor = color?.Value;
        _pickerTouched = true;
    }
    protected override async Task OnInitializedAsync()
    {
        _edits = Dirty.Register();
        _pickerColor = StudentColor;
        _pickerMudColor = StudentColor is null ? null : new MudBlazor.Utilities.MudColor(StudentColor);
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
            Snackbar.Add(string.Format(Loc["Dialog_LoadTasksError"], ex.Message), Severity.Error);
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
            Snackbar.Add(string.Format(Loc["Dialog_LoadGradesError"], ex.Message), Severity.Error);
        }
    }

    private async Task ChangeStatusAsync(QueueEntryStatus status)
    {
        if (ReadOnly || _busy || EventId is null) return;
        _busy = true;
        try
        {
            await Queues.UpdateStatusAsync(EventId!.Value, StudentId, new UpdateQueueStatusRequest(status));
            Snackbar.Add(Loc["Dialog_StatusUpdated"], Severity.Success);
            _edits.MarkClean();
            MudDialog.Close(DialogResult.Ok(true));
        }
        catch (ApiException ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
        }
        finally { _busy = false; }
    }

    private async Task ReworkAsync(int taskId)
    {
        if (ReadOnly || _busy) return;
        var dialog = await DialogService.ShowAsync<CommentDialog>(Loc["CommentDialog_Title"]);
        var result = await dialog.Result;
        if (result is { Canceled: false, Data: string comment })
            await SubmitAsync(taskId, SubmissionStatus.Rejected, comment);
    }

    private async Task SubmitAsync(int taskId, SubmissionStatus status, string? comment = null)
    {
        if (ReadOnly || _busy) return;
        _busy = true;
        try
        {
            await Submissions.CreateAsync(new CreateSubmissionRequest(StudentId, taskId, status, comment));
            Snackbar.Add(Loc["Dialog_TaskStatusUpdated"], Severity.Success);
            _hasChanges = true;
            _edits.MarkClean();
            await LoadTasksAsync();
            if (status == SubmissionStatus.Rejected)
                _openHistory.Add(taskId);
            if (_openHistory.Contains(taskId))
                await LoadHistoryAsync(taskId);
        }
        catch (ApiException ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
        }
        finally { _busy = false; }
    }

    private async Task SetGradeAsync(int componentId)
    {
        if (ReadOnly || _busy) return;
        _busy = true;
        try
        {
            await Grades.SetGradeAsync(componentId, StudentId, new SetGradeRequest(_gradeInputs[componentId], null));
            _currentGrades[componentId] = _gradeInputs[componentId];
            _hasChanges = true;
            Snackbar.Add(Loc["Dialog_GradeSaved"], Severity.Success);
            _edits.MarkClean();
        }
        catch (ApiException ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
        }
        finally { _busy = false; }
    }

    private async Task ApplyColorAsync(string? color)
    {
        if (ReadOnly || _busy) return;
        _busy = true;
        try
        {
            await Students.SetColorAsync(StudentId, new SetStudentColorRequest(color));
            _hasChanges = true;
            Snackbar.Add(color is null ? Loc["Dialog_FillRemoved"] : Loc["Dialog_ColorApplied"], Severity.Success);
            _edits.MarkClean();
        }
        catch (ApiException ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
        }
        finally { _busy = false; }
    }

    private void Cancel()
    {
        _edits.MarkClean();
        MudDialog.Close(_hasChanges ? DialogResult.Ok(true) : DialogResult.Cancel());
    }

    public void Dispose() => _edits?.Dispose();

    private string StatusText(SubmissionStatus status) => status switch
    {
        SubmissionStatus.Rejected => Loc["Task_StatusRejected"],
        SubmissionStatus.Accepted => Loc["Task_StatusAccepted"],
        _ => Loc["Task_StatusNotSubmitted"],
    };

    private static Color StatusColor(SubmissionStatus status) => status switch
    {
        SubmissionStatus.Rejected => Color.Warning,
        SubmissionStatus.Accepted => Color.Success,
        _ => Color.Default,
    };
}
