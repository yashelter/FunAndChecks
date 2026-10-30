using Frontend.Shared.Api;
using Frontend.Shared.Models;
using Frontend.Shared.Resources;
using Frontend.Shared.Services;
using Frontend.Shared.UI;
using Frontend.Admin.UI;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using MudBlazor;

namespace Frontend.Admin.Pages;

public partial class Dashboard
{
    [Inject] private MeApi Me { get; set; } = null!;
    [Inject] private ResultsApi Results { get; set; } = null!;
    [Inject] private AttendanceApi Attendance { get; set; } = null!;
    [Inject] private VariantsApi Variants { get; set; } = null!;
    [Inject] private FileDownloader Downloader { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;
    [Inject] private IStringLocalizer<AppStrings> Loc { get; set; } = null!;
    [Inject] private IDialogService Dialogs { get; set; } = null!;
    [Inject] private ResultsNavigationState ProfileReturn { get; set; } = null!;
    [Inject] private CurrentSubject CurrentSubject { get; set; } = null!;

    private List<SubjectDto> _subjects = [];
    private SubjectDto? _selectedSubject;
    private SubjectResultsDto? _results;
    private bool _loading;
    private bool _showAcceptedTeacherColors;
    private Dictionary<Guid, (int Present, int Absent, int Unmarked)>? _attendance;
    private VariantBoardDto? _variants;
    private string AveragePoints => _results?.UserResults is { Count: > 0 } rows
        ? rows.Average(row => row.TotalPoints).ToString("0.##", System.Globalization.CultureInfo.CurrentCulture)
        : "—";

    private int _acceptedCount, _reworkCount;

    private void SetVariants(VariantBoardDto? board) => _variants = board is { Sets.Count: > 0 } ? board : null;

    /// <summary>Варианты — дополнительная колонка: без них таблица всё равно показывается.</summary>
    private async Task RefreshVariantsAsync(int subjectId)
    {
        try
        {
            var board = await Variants.GetBoardAsync(subjectId);
            if (_selectedSubject?.Id == subjectId) SetVariants(board);
        }
        catch (ApiException) { }
    }

    /// <summary>Посещаемость — дополнительная колонка: если журнал не загрузился, таблица показывается без неё.</summary>
    private async Task<Dictionary<Guid, (int Present, int Absent, int Unmarked)>?> LoadAttendanceAsync(int subjectId)
    {
        try
        {
            var journal = await Attendance.GetJournalAsync(subjectId);
            if (!journal.Enabled && journal.Sessions.Count == 0)
                return null;
            return journal.Students.GroupBy(s => s.StudentId).ToDictionary(g => g.Key,
                g => (g.Sum(s => s.Present), g.Sum(s => s.Absent), g.Sum(s => s.Unmarked)));
        }
        catch (ApiException) { return null; }
    }

    /// <summary>
    /// Клик по ячейке открывает студента сразу на этой задаче (с историей): зачесть или вернуть
    /// можно здесь же, без перехода в «Оценивание» и повторного выбора предмета.
    /// </summary>
    private async Task OpenStudentAsync(ResultCellClick click)
    {
        if (_results is null)
            return;

        var (row, task) = click;
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
            await Task.WhenAll(RefreshResultsAsync(), RefreshVariantsAsync(parameters.Get(x => x.SubjectId)));
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
        SetVariants(null);

        try
        {
            // Таблица, варианты и журнал независимы — запрашиваем параллельно.
            var resultsTask = Results.GetSubjectResultsAsync(subject.Id);
            var attendanceTask = LoadAttendanceAsync(subject.Id);
            var variantsTask = RefreshVariantsAsync(subject.Id);
            var results = await resultsTask;
            // Колонки вариантов и посещаемости дожидаемся до показа: пришедшая позже колонка перерисовала бы
            // большую таблицу ещё раз. Без них таблица всё равно показывается — обе загрузки ошибок не бросают.
            await variantsTask;
            var attendance = await attendanceTask;
            // Пока грузили, могли выбрать другой предмет — его загрузка и покажет свою таблицу.
            if (_selectedSubject?.Id != subject.Id) return;
            SetResults(results);
            _attendance = attendance;
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

    private void UpdateStudentColors(IReadOnlyList<StudentColorUpdateDto> updates)
    {
        // Apply возвращает тот же список, если цвета не изменились: тогда и таблица не перерисовывается.
        if (_results is not null && StudentColorRows.Apply(_results.UserResults, updates) is var rows && !ReferenceEquals(rows, _results.UserResults))
            SetResults(_results with { UserResults = rows });
    }
}
