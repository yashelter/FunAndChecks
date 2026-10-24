using System.Net;
using Frontend.Shared.Api;
using Frontend.Shared.Models;
using Frontend.Shared.Resources;
using Frontend.Shared.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Microsoft.JSInterop;
using MudBlazor;

namespace Frontend.Admin.Dialogs;

/// <summary>Что попросили сделать при закрытии диалога из очереди.</summary>
public static class StudentDialogResults
{
    /// <summary>Завершить и сразу открыть следующего ожидающего.</summary>
    public const string Next = "next";
}

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
    /// <summary>Задача, по ячейке которой открыли диалог, — подсвечивается и прокручивается в видимую область.</summary>
    [Parameter] public int? FocusTaskId { get; set; }
    [Parameter] public string? StudentColor { get; set; }
    [Parameter] public string? CheckingWarningName { get; set; }
    [Parameter] public EventCallback<StudentColorUpdateDto> OnColorApplied { get; set; }

    [Inject] private StudentsApi Students { get; set; } = null!;
    [Inject] private SubjectsApi Subjects { get; set; } = null!;
    [Inject] private SubmissionsApi Submissions { get; set; } = null!;
    [Inject] private GradesApi Grades { get; set; } = null!;
    [Inject] private QueuesApi Queues { get; set; } = null!;
    [Inject] private IDialogService DialogService { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;
    [Inject] private IJSRuntime JS { get; set; } = null!;
    [Inject] private IStringLocalizer<AppStrings> Loc { get; set; } = null!;
    [Inject] private UnsavedChangesTracker Dirty { get; set; } = null!;

    private List<TaskWithStatusDto> _tasks = [];
    private List<GradeComponentDto> _components = [];
    // null — оценка не выставлена: поле пустое, а не показывает минимум как будто это оценка.
    private readonly Dictionary<int, int?> _gradeInputs = [];
    private readonly Dictionary<int, int> _currentGrades = [];
    private readonly HashSet<int> _openHistory = [];
    private readonly HashSet<int> _openGroups = [];
    private readonly Dictionary<int, List<SubmissionLogDto>> _history = [];
    private readonly HashSet<int> _historyErrors = [];
    private readonly HashSet<int> _savingTasks = [];
    private bool _loadingTasks = true;
    private bool _busy;
    private bool _hasChanges;
    private bool _disposed;
    private readonly CancellationTokenSource _cts = new();
    private bool _focusPending;
    private int _activePanelIndex;
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
        _pickerColor = color?.ToString(MudBlazor.Utilities.MudColorOutputFormats.Hex);
        _pickerTouched = true;
    }

    protected override async Task OnInitializedAsync()
    {
        _edits = Dirty.Register();
        _pickerColor = StudentColor;
        _pickerMudColor = StudentColor is null ? null : new MudBlazor.Utilities.MudColor(StudentColor);
        // Задачи и оценки независимы — грузим параллельно, а не цепочкой.
        await Task.WhenAll(LoadTasksAsync(), LoadGradesAsync());

        // Группы задач свёрнуты; раскрываем только группу задачи, к которой нужно прокрутить.
        if (FocusTaskId is int focusTask && _tasks.FirstOrDefault(t => t.Id == focusTask)?.TaskGroupId is int focusGroup)
            _openGroups.Add(focusGroup);

        // Видимые несданные и задачи на доработке раскрыты сразу: история нужна преподавателю перед приёмом.
        var open = _tasks.Where(t => IsTaskVisible(t) && t.Status != SubmissionStatus.Accepted).Select(t => t.Id).ToList();
        if (FocusTaskId is int focus && !open.Contains(focus)) open.Add(focus);
        foreach (var id in open) _openHistory.Add(id);
        _focusPending = FocusTaskId is not null;
        await Task.WhenAll(open.Select(LoadHistoryAsync));
    }

    private bool IsTaskVisible(TaskWithStatusDto task) => task.TaskGroupId is not int group || _openGroups.Contains(group);

    private async Task ToggleGroupAsync(int groupId)
    {
        if (!_openGroups.Add(groupId))
        {
            _openGroups.Remove(groupId);
            return;
        }

        // Как при открытии окна: у несданных задач раскрытой группы сразу видна история.
        var load = new List<int>();
        foreach (var task in _tasks.Where(t => t.TaskGroupId == groupId && t.Status != SubmissionStatus.Accepted))
        {
            if (_openHistory.Add(task.Id)) load.Add(task.Id);
        }
        await Task.WhenAll(load.Select(LoadHistoryAsync));
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!_focusPending || _loadingTasks || FocusTaskId is not int focus) return;
        _focusPending = false;
        try
        {
            await using var module = await JS.InvokeAsync<IJSObjectReference>("import", "./_content/Frontend.Shared/community.js");
            await module.InvokeAsync<bool>("scrollToElement", $"task-{focus}");
        }
        catch (JSException) { /* Прокрутка — удобство, не ошибка. */ }
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
        _historyErrors.Remove(taskId);
        try
        {
            _history[taskId] = await Submissions.GetLogAsync(StudentId, taskId);
        }
        catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // Сервер отвечает 404, когда попыток ещё не было, — это пустая история, а не ошибка.
            _history[taskId] = [];
        }
        catch (ApiException)
        {
            _history.Remove(taskId);
            _historyErrors.Add(taskId);
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
            var componentsTask = Subjects.GetGradeComponentsAsync(SubjectId);
            var gradesTask = Students.GetGradesAsync(StudentId, SubjectId);
            _components = await componentsTask;
            var grades = await gradesTask;
            _gradeInputs.Clear();
            _currentGrades.Clear();
            foreach (var c in _components)
            {
                var existing = grades.FirstOrDefault(g => g.ComponentId == c.Id);
                _gradeInputs[c.Id] = existing?.Points;
                if (existing is not null)
                    _currentGrades[c.Id] = existing.Points;
            }
        }
        catch (ApiException ex)
        {
            Snackbar.Add(string.Format(Loc["Dialog_LoadGradesError"], ex.Message), Severity.Error);
        }
    }

    private async Task ChangeStatusAsync(QueueEntryStatus status, string? next = null)
    {
        if (ReadOnly || _busy || EventId is null) return;
        if (!await SaveDirtyGradesAsync()) return;
        _busy = true;
        try
        {
            await Queues.UpdateStatusAsync(EventId!.Value, StudentId, new UpdateQueueStatusRequest(status));
            Snackbar.Add(Loc["Dialog_StatusUpdated"], Severity.Success);
            _edits.MarkClean();
            MudDialog.Close(DialogResult.Ok<object>(next ?? (object)true));
        }
        catch (ApiException ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
        }
        finally { _busy = false; }
    }

    private Task FinishAndNextAsync() => ChangeStatusAsync(QueueEntryStatus.Finished, StudentDialogResults.Next);


    private async Task ReworkAsync(TaskWithStatusDto task)
    {
        if (ReadOnly || _savingTasks.Contains(task.Id)) return;
        var dialog = await DialogService.ShowAsync<CommentDialog>(Loc["CommentDialog_Title"]);
        var result = await dialog.Result;
        if (result is { Canceled: false, Data: string comment })
            await SubmitAsync(task, SubmissionStatus.Rejected, comment);
    }

    /// <summary>
    /// Отметка задачи. Статус меняется сразу в карточке — без перезагрузки списка и прыжка вверх;
    /// блокируется только эта задача, остальные можно отмечать подряд.
    /// </summary>
    private async Task SubmitAsync(TaskWithStatusDto task, SubmissionStatus status, string? comment = null)
    {
        if (ReadOnly || !_savingTasks.Add(task.Id)) return;
        try
        {
            await Submissions.CreateAsync(new CreateSubmissionRequest(StudentId, task.Id, status, comment));
            ReplaceStatus(task.Id, status);
            _hasChanges = true;
            _edits.MarkClean();
            if (status == SubmissionStatus.Rejected) _openHistory.Add(task.Id);
            if (_openHistory.Contains(task.Id)) await LoadHistoryAsync(task.Id);
            Snackbar.Add(string.Format(Loc[status == SubmissionStatus.Accepted ? "Dialog_TaskAccepted" : "Dialog_TaskRework"], task.Name),
                Severity.Success, config =>
                {
                    config.Action = Loc["Dialog_Undo"];
                    config.ActionColor = Color.Primary;
                    config.ShowCloseIcon = true;
                    config.OnClick = _ => UndoAsync(task.Id);
                });
            var uid = Guid.NewGuid().ToString("N");
            const int fadeDuration = 600;
            const int totalDuration = 7000;
            await JS.InvokeAsync<bool>("snackbarTagLast", _cts.Token, uid);
            _ = Task.Delay(totalDuration - fadeDuration, _cts.Token).ContinueWith(async t =>
            {
                if (t.IsCanceled) return;
                try { await JS.InvokeVoidAsync("snackbarFadeDismissByUid", _cts.Token, uid, fadeDuration); }
                catch (OperationCanceledException) { }
            }, TaskScheduler.Default);
        }
        catch (ApiException ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
        }
        finally { _savingTasks.Remove(task.Id); }
    }

    private async Task UndoAsync(int taskId)
    {
        try
        {
            var status = await Submissions.UndoLatestAsync(StudentId, taskId);
            _hasChanges = true;
            if (_disposed) return;
            ReplaceStatus(taskId, status);
            if (_openHistory.Contains(taskId)) await LoadHistoryAsync(taskId);
            Snackbar.Add(Loc["Dialog_MarkUndone"], Severity.Info);
            await InvokeAsync(StateHasChanged);
        }
        catch (ApiException ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
        }
    }

    private void ReplaceStatus(int taskId, SubmissionStatus status) =>
        _tasks = _tasks.Select(t => t.Id == taskId ? t with { Status = status } : t).ToList();

    // Сохранения оценок идут цепочкой и не занимают _busy: кнопки «Завершить»/«Закрыть» остаются живыми
    // и просто дожидаются цепочки, а не теряют клик, пока идёт автосохранение после выхода из поля.
    private Task _gradeSaves = Task.CompletedTask;

    private Task SetGradeAsync(int componentId)
    {
        if (ReadOnly) return Task.CompletedTask;
        return _gradeSaves = SaveGradeAfterAsync(_gradeSaves, componentId);
    }

    private async Task SaveGradeAfterAsync(Task previous, int componentId)
    {
        try { await previous; } catch (Exception) { /* Ошибку прошлого сохранения уже показали; цепочка не должна встать. */ }
        // Значение берём на момент своей очереди: повторный клик или blur после сохранения — ничего не делают.
        if (!IsGradeDirty(componentId) || _gradeInputs.GetValueOrDefault(componentId) is not int points) return;
        try
        {
            await Grades.SetGradeAsync(componentId, StudentId, new SetGradeRequest(points, null));
            _currentGrades[componentId] = points;
            _hasChanges = true;
            Snackbar.Add(Loc["Dialog_GradeSaved"], Severity.Success);
            _edits.MarkClean();
        }
        catch (ApiException ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
        }
    }

    /// <summary>Автосохранение при выходе из поля: введённая оценка не теряется при закрытии диалога.</summary>
    private async Task GradeBlurAsync(int componentId)
    {
        if (IsGradeDirty(componentId)) await SetGradeAsync(componentId);
    }

    private bool IsGradeDirty(int componentId) =>
        _gradeInputs.GetValueOrDefault(componentId) is int value &&
        (!_currentGrades.TryGetValue(componentId, out var current) || current != value);

    /// <summary>Сохраняет все изменённые, но не сохранённые оценки. false — если что-то не сохранилось.</summary>
    private async Task<bool> SaveDirtyGradesAsync()
    {
        if (ReadOnly) return true;
        try { await _gradeSaves; } catch (Exception) { /* Не сохранившиеся оценки останутся «грязными» и будут сохранены ниже. */ }
        foreach (var id in _components.Select(c => c.Id).Where(IsGradeDirty).ToList())
        {
            await SetGradeAsync(id);
            if (IsGradeDirty(id))
            {
                Snackbar.Add(Loc["Dialog_GradeNotSaved"], Severity.Warning);
                return false;
            }
        }
        return true;
    }

    private async Task ApplyColorAsync(string? color)
    {
        if (ReadOnly || _busy) return;
        _busy = true;
        try
        {
            await Students.SetColorAsync(StudentId, new SetStudentColorRequest(color));
            StudentColor = color;
            _hasChanges = true;
            _pickerTouched = false;
            await OnColorApplied.InvokeAsync(new StudentColorUpdateDto(StudentId, color));
            Snackbar.Add(color is null ? Loc["Dialog_FillRemoved"] : Loc["Dialog_ColorApplied"], Severity.Success);
            _edits.MarkClean();
        }
        catch (ApiException ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
        }
        finally { _busy = false; }
    }

    private async Task Cancel()
    {
        if (!await SaveDirtyGradesAsync()) return;
        _edits.MarkClean();
        MudDialog.Close(_hasChanges ? DialogResult.Ok(true) : DialogResult.Cancel());
    }

    public void Dispose()
    {
        _disposed = true;
        _cts.Cancel();
        _cts.Dispose();
        _edits?.Dispose();
    }

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
        _ => Color.Error,
    };

    private static string TaskStatusClass(SubmissionStatus status) => status switch
    {
        SubmissionStatus.Accepted => "fc-task-accepted",
        SubmissionStatus.Rejected => "fc-task-rework",
        _ => "fc-task-unsubmitted",
    };
}
