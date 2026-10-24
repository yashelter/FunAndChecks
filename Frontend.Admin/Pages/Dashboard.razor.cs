using Frontend.Shared.Api;
using Frontend.Shared.Models;
using Frontend.Shared.Resources;
using Frontend.Shared.Services;
using Frontend.Shared.UI;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using MudBlazor;

namespace Frontend.Admin.Pages;

public partial class Dashboard
{
    [Inject] private MeApi Me { get; set; } = null!;
    [Inject] private ResultsApi Results { get; set; } = null!;
    [Inject] private AttendanceApi Attendance { get; set; } = null!;
    [Inject] private FileDownloader Downloader { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;
    [Inject] private IStringLocalizer<AppStrings> Loc { get; set; } = null!;
    [Inject] private IDialogService Dialogs { get; set; } = null!;
    [Inject] private ResultsNavigationState ProfileReturn { get; set; } = null!;
    [Inject] private CurrentSubject CurrentSubject { get; set; } = null!;

    private List<SubjectDto> _subjects = [];
    private SubjectDto? _selectedSubject;
    private SubjectResultsDto? _results;
    private MudDataGrid<StudentResultRowDto>? _grid;
    private readonly Guid _profileViewInstance = Guid.NewGuid();
    private bool _loading;
    private bool _showAcceptedTeacherColors;
    private Dictionary<Guid, (int Present, int Absent, int Unmarked)>? _attendance;
    private string AveragePoints => _results?.UserResults is { Count: > 0 } rows
        ? rows.Average(row => row.TotalPoints).ToString("0.##", System.Globalization.CultureInfo.CurrentCulture)
        : "—";

    private int _acceptedCount, _reworkCount;

    /// <summary>
    /// MudDataGrid регистрирует колонки в порядке создания: колонка, появившаяся при живом обновлении
    /// (новая задача или оценка), встала бы в конец, после Σ. Другой набор колонок — новая таблица.
    /// </summary>
    private string ColumnsKey => _results is null ? "" : ResultsColumns.Key(_results);

    private string CellLabel(StudentResultRowDto row, TaskHeaderDto task)
    {
        var status = row.Results.GetValueOrDefault(task.TaskId)?.Status switch
        {
            SubmissionStatus.Accepted => "Task_StatusAccepted",
            SubmissionStatus.Rejected => "Task_StatusRejected",
            _ => "Task_StatusNotSubmitted",
        };
        return $"{row.FullName}, {task.TaskName}: {Loc[status]}. {Loc["Dashboard_CellAction"]}";
    }

    /// <summary>
    /// Клик по ячейке открывает студента сразу на этой задаче (с историей): зачесть или вернуть
    /// можно здесь же, без перехода в «Оценивание» и повторного выбора предмета.
    /// </summary>
    private async Task OpenStudentAsync(StudentResultRowDto row, TaskHeaderDto task)
    {
        if (_results is null)
            return;

        var parameters = new DialogParameters<Frontend.Admin.Dialogs.StudentInteractionDialog>
        {
            { x => x.StudentId, row.StudentId },
            { x => x.StudentName, row.FullName },
            { x => x.GroupName, row.GroupName },
            { x => x.SubjectId, _results.SubjectId },
            { x => x.FocusTaskId, task.TaskId },
            { x => x.StudentColor, row.StudentColor },
        };
        var dialog = await Dialogs.ShowAsync<Frontend.Admin.Dialogs.StudentInteractionDialog>(row.FullName, parameters,
            new DialogOptions { MaxWidth = MaxWidth.Medium, FullWidth = true });
        if (await dialog.Result is { Canceled: false })
            await RefreshResultsAsync();
    }

    private CancellationTokenSource? _liveRefresh;

    /// <summary>Отметки других преподавателей появляются без ручного обновления; пачка событий — один запрос.</summary>
    private async Task OnResultUpdatedAsync(ResultUpdateDto update)
    {
        _liveRefresh?.Cancel();
        var refresh = _liveRefresh = new CancellationTokenSource();
        try
        {
            await Task.Delay(400, refresh.Token);
            await RefreshResultsAsync(quiet: true);
            StateHasChanged();
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>
    /// Перечитывает таблицу без спиннера: сортировка, фильтры и прокрутка остаются на месте.
    /// Ответ применяется, только если предмет за это время не сменили. Фоновое обновление ошибку не показывает:
    /// следующее событие или смена предмета подтянут данные.
    /// </summary>
    private async Task RefreshResultsAsync(bool quiet = false)
    {
        if (_results is null) return;
        var subjectId = _results.SubjectId;
        try
        {
            var fresh = await Results.GetSubjectResultsAsync(subjectId);
            if (_results?.SubjectId == subjectId && _selectedSubject?.Id == subjectId) SetResults(fresh);
        }
        catch (ApiException ex) when (!quiet) { Snackbar.Add(string.Format(Loc["Common_LoadResultsError"], ex.Message), Severity.Error); }
        catch (ApiException) { }
    }

    private void SetResults(SubjectResultsDto? results)
    {
        _results = results;
        _acceptedCount = results?.UserResults.Sum(r => r.Results.Values.Count(c => c.Status == SubmissionStatus.Accepted)) ?? 0;
        _reworkCount = results?.UserResults.Sum(r => r.Results.Values.Count(c => c.Status == SubmissionStatus.Rejected)) ?? 0;
    }

    protected override async Task OnInitializedAsync()
    {
        try
        {
            await ProfileReturn.InitializeAsync();
            _subjects = await Me.GetVisibleSubjectsAsync();
            // Возврат из профиля важнее общего выбора: он восстанавливает и фильтры таблицы.
            var id = ProfileReturn.Subject("/admin") ?? await CurrentSubject.PickAsync(_subjects.Select(s => s.Id));
            if (_subjects.FirstOrDefault(s => s.Id == id) is { } subject)
                await OnSubjectSelectedAsync(subject);
        }
        catch (ApiException ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
        }
    }

    private async Task OnSubjectSelectedAsync(SubjectDto? subject)
    {
        if (subject is null)
            return;

        _selectedSubject = subject;
        _liveRefresh?.Cancel();
        ProfileReturn.SelectSubject("/admin", subject.Id);
        await CurrentSubject.SetAsync(subject.Id);
        _loading = true;
        SetResults(null);
        _attendance = null;

        try
        {
            // Таблица и журнал независимы — запрашиваем параллельно.
            var resultsTask = Results.GetSubjectResultsAsync(subject.Id);
            var journalTask = Attendance.GetJournalAsync(subject.Id);
            var results = await resultsTask;
            // Пока грузили, могли выбрать другой предмет — его загрузка и покажет свою таблицу.
            if (_selectedSubject?.Id != subject.Id) return;
            // Таблица видна, даже если журнал посещаемости не загрузится.
            SetResults(results);
            var journal = await journalTask;
            if (_selectedSubject?.Id != subject.Id) return;
            if (journal.Enabled || journal.Sessions.Count > 0)
                _attendance = journal.Students.GroupBy(s => s.StudentId).ToDictionary(g => g.Key,
                    g => (g.Sum(s => s.Present), g.Sum(s => s.Absent), g.Sum(s => s.Unmarked)));
        }
        catch (ApiException ex) when (_selectedSubject?.Id == subject.Id)
        {
            Snackbar.Add(string.Format(Loc["Common_LoadResultsError"], ex.Message), Severity.Error);
        }
        catch (ApiException) { /* Ответ по уже не выбранному предмету. */ }
        finally
        {
            if (_selectedSubject?.Id == subject.Id) _loading = false;
        }
    }

    private async Task ExportXlsxAsync()
    {
        if (_results is null)
            return;

        try
        {
            var bytes = await Results.ExportXlsxAsync(_results.SubjectId);
            using var stream = new MemoryStream(bytes);
            await Downloader.DownloadAsync($"Results_{_results.SubjectName}_{DateTime.Now:yyyy-MM-dd}.xlsx", stream);
            Snackbar.Add(Loc["Dashboard_ExportDone"], Severity.Success);
        }
        catch (ApiException ex)
        {
            Snackbar.Add(string.Format(Loc["Dashboard_ExportError"], ex.Message), Severity.Error);
        }
    }

    private Task RememberResultsAsync() => ProfileReturn.CaptureAsync("/admin", _grid, _profileViewInstance);
    protected override Task OnAfterRenderAsync(bool firstRender) => ProfileReturn.RestoreAsync("/admin", _grid, _profileViewInstance);

    private void UpdateStudentColors(IReadOnlyList<StudentColorUpdateDto> updates)
    {
        if (_results is not null)
            SetResults(_results with { UserResults = StudentColorRows.Apply(_results.UserResults, updates) });
    }

    private static string FioStyle(string? color)
    {
        if (string.IsNullOrEmpty(color))
            return string.Empty;
        return $"background-color:{color};color:{Frontend.Shared.UI.ColorUtils.ContrastText(color)};" +
               "padding:2px 8px;border-radius:6px;display:inline-block;";
    }

    private static string CellText(ResultCellDto? cell) =>
        cell?.Status == SubmissionStatus.Accepted ? "+" : cell?.DisplayValue ?? string.Empty;

    private string CellStyle(ResultCellDto? cell)
    {
        if (cell is null || string.IsNullOrWhiteSpace(cell.AdminColor))
            return string.Empty;

        if (cell.Status == SubmissionStatus.Accepted)
            return _showAcceptedTeacherColors ? $"background-color:{cell.AdminColor};color:#000000;border-color:{cell.AdminColor};text-shadow:0 0 3px rgba(255,255,255,.7);" : string.Empty;

        // The letter and its teacher's color carry information independently from the theme accent.
        return $"background-color:{cell.AdminColor};color:{Frontend.Shared.UI.ColorUtils.ContrastText(cell.AdminColor)};";
    }

    private static string CellClass(ResultCellDto? cell) => cell?.Status switch
    {
        SubmissionStatus.Accepted => "fc-result-accepted",
        SubmissionStatus.Rejected => "fc-result-rework",
        _ => "fc-result-unsubmitted",
    };
}
