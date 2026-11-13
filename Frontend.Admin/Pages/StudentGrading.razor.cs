using Frontend.Admin.Dialogs;
using Frontend.Shared.Api;
using Frontend.Shared.Models;
using Frontend.Shared.Resources;
using System.Text.Json;
using Frontend.Shared.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Microsoft.JSInterop;
using MudBlazor;

namespace Frontend.Admin.Pages;

public partial class StudentGrading
{
    [Inject] private MeApi Me { get; set; } = null!;
    [Inject] private SubjectsApi Subjects { get; set; } = null!;
    [Inject] private ResultsApi Results { get; set; } = null!;
    [Inject] private CurrentSubject CurrentSubject { get; set; } = null!;
    [Inject] private IDialogService DialogService { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;
    [Inject] private IStringLocalizer<AppStrings> Loc { get; set; } = null!;
    [Inject] private IJSRuntime JS { get; set; } = null!;

    private List<SubjectDto> _subjects = [];
    // Список студентов предмета грузится один раз; поиск и фильтр по группе работают на клиенте.
    private List<StudentDetailsDto> _students = [];
    private Dictionary<Guid, (int Accepted, int Rework)> _progress = [];
    private int _taskCount;
    private HashSet<int> _restrictedGroups = [];
    private int? _subjectId;
    // Выбранные группы предмета; пусто — все. Запоминается в браузере отдельно для каждого предмета.
    private HashSet<int> _groups = [];
    private string _query = "";
    private bool _loading;
    private bool _reworkOnly;
    private bool _sortByProgress;
    private bool _descending;

    private int ReworkStudentCount => _students.Count(student => _progress.GetValueOrDefault(student.Id).Rework > 0);

    private List<StudentDetailsDto> Visible
    {
        get
        {
            IEnumerable<StudentDetailsDto> items = _students;
            if (_groups.Count > 0)
            {
                items = items.Where(s => s.GroupId is int group && _groups.Contains(group));
            }
            if (_reworkOnly && _taskCount > 0)
            {
                items = items.Where(s => _progress.GetValueOrDefault(s.Id).Rework > 0);
            }
            string query = _query?.Trim() ?? string.Empty;
            // Имя ищем в обоих порядках: в списке «Фамилия Имя», а набирают часто «Имя Фамилия».
            if (query.Length > 0)
            {
                items = items.Where(s => $"{s.LastName} {s.FirstName}".Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                                         $"{s.FirstName} {s.LastName}".Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                                         (s.Email?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false));
            }
            if (_sortByProgress && _taskCount > 0)
            {
                return (_descending
                    ? items.OrderByDescending(s => _progress.GetValueOrDefault(s.Id).Accepted)
                    : items.OrderBy(s => _progress.GetValueOrDefault(s.Id).Accepted)).ToList();
            }
            if (!_sortByProgress && _descending)
            {
                items = items.Reverse();
            }
            return items.ToList();
        }
    }

    private bool IsSortedBy(bool progress) => progress == (_sortByProgress && _taskCount > 0);

    private void SortBy(bool progress)
    {
        _descending = IsSortedBy(progress) ? !_descending : progress;
        _sortByProgress = progress;
    }

    private string? SortState(bool progress) => IsSortedBy(progress)
        ? (_descending && (!_sortByProgress || progress) ? "descending" : "ascending")
        : null;

    private string SortArrow(bool progress) => SortState(progress) switch
    {
        "ascending" => "↑",
        "descending" => "↓",
        _ => "↕"
    };

    /// <summary>Группы студентов предмета с числом студентов — для фильтра.</summary>
    private List<(int Id, string Name, int Count)> StudentGroups => _students.Where(s => s.GroupId.HasValue)
        .GroupBy(s => s.GroupId!.Value)
        .Select(g => (g.Key, g.First().GroupName ?? "—", g.Count()))
        .OrderBy(g => g.Item2, StringComparer.CurrentCultureIgnoreCase).ToList();

    private string GroupsKey => $"fc-grading-groups:{_subjectId}";

    private async Task SelectGroupAsync(int? groupId, CancellationToken cancellationToken = default)
    {
        if (groupId is not int id)
        {
            _groups.Clear();
        }
        else if (!_groups.Add(id))
        {
            _groups.Remove(id);
        }

        try
        {
            if (_groups.Count == 0)
            {
                await JS.InvokeVoidAsync("localStorage.removeItem", cancellationToken, GroupsKey);
            }
            else
            {
                await JS.InvokeVoidAsync("localStorage.setItem", cancellationToken, GroupsKey, JsonSerializer.Serialize(_groups));
            }
        }
        catch (JSException)
        {
            // The in-memory selection still applies when storage is unavailable.
        }
    }

    /// <summary>Восстанавливает выбор групп для предмета, отбрасывая группы, которых в нём больше нет.</summary>
    private async Task RestoreGroupsAsync()
    {
        _groups = [];
        try
        {
            var json = await JS.InvokeAsync<string?>("localStorage.getItem", GroupsKey);
            if (!string.IsNullOrEmpty(json) && JsonSerializer.Deserialize<int[]>(json) is { } saved)
                _groups = [.. saved.Where(id => StudentGroups.Any(g => g.Id == id))];
        }
        catch (Exception ex) when (ex is JSException or JsonException) { _groups = []; }
    }

    protected override async Task OnInitializedAsync()
    {
        try
        {
            _subjects = await Me.GetVisibleSubjectsAsync();
            _subjectId = await CurrentSubject.PickAsync(_subjects.Select(s => s.Id));
            if (_subjectId is not null) await LoadStudentsAsync();
        }
        catch (ApiException ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
        }
    }

    private async Task OnSubjectChangedAsync()
    {
        _reworkOnly = false;
        await CurrentSubject.SetAsync(_subjectId);
        await LoadStudentsAsync();
    }

    private int _loadVersion;

    /// <summary>Студенты предмета и их прогресс (из таблицы результатов) — параллельно, одним заходом.</summary>
    private async Task LoadStudentsAsync(bool showSpinner = true)
    {
        if (_subjectId is not int subjectId)
        {
            ++_loadVersion;
            _students = [];
            return;
        }
        _loading = showSpinner;
        // Ответы прежнего предмета не должны лечь под новый: применяет только последняя загрузка.
        var version = ++_loadVersion;
        try
        {
            var accessTask = Me.GetMyAccessAsync();
            var studentsTask = Subjects.GetStudentsAsync(subjectId);
            var resultsTask = Results.GetSubjectResultsAsync(subjectId);
            var restricted = (await accessTask).RestrictedGroupIds;
            var students = await studentsTask;
            // Прогресс — подсказка в строке; если таблица результатов не загрузилась, список всё равно показываем.
            SubjectResultsDto? results;
            try { results = await resultsTask; }
            catch (ApiException) { results = null; }
            if (version != _loadVersion) return;

            _restrictedGroups = [.. restricted];
            _students = students.OrderBy(s => s.LastName).ThenBy(s => s.FirstName).ToList();
            _taskCount = results?.TaskHeaders.Count ?? 0;
            _progress = results?.UserResults.ToDictionary(r => r.StudentId, r => (
                r.Results.Values.Count(c => c.Status == SubmissionStatus.Accepted),
                r.Results.Values.Count(c => c.Status == SubmissionStatus.Rejected))) ?? [];
            if (showSpinner) await RestoreGroupsAsync();
        }
        catch (ApiException ex) when (version == _loadVersion)
        {
            Snackbar.Add(ex.Message, Severity.Error);
        }
        catch (ApiException) { /* Ответ по уже не выбранному предмету. */ }
        finally
        {
            if (version == _loadVersion) _loading = false;
        }
    }

    private async Task OpenAsync(StudentDetailsDto student)
    {
        if (IsGroupRestricted(student))
            return;

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
            { x => x.GroupName, student.GroupName },
            { x => x.EventId, (int?)null },
            { x => x.SubjectId, _subjectId.Value },
            { x => x.StudentColor, student.Color },
            { x => x.OnColorApplied, EventCallback.Factory.Create<StudentColorUpdateDto>(this, UpdateStudentColor) },
        };

        var dialog = await DialogService.ShowAsync<StudentInteractionDialog>(
            Loc["Grading_DialogTitle"],
            parameters,
            new DialogOptions { MaxWidth = MaxWidth.Medium, FullWidth = true });
        // Обновляем прогресс тихо: список, поиск и прокрутка остаются на месте.
        if (await dialog.Result is { Canceled: false })
            await LoadStudentsAsync(showSpinner: false);
    }

    private bool IsGroupRestricted(StudentDetailsDto student) =>
        student.GroupId is int groupId && _restrictedGroups.Contains(groupId);

    private void UpdateStudentColor(StudentColorUpdateDto update) =>
        _students = _students.Select(student => student.Id == update.StudentId
            ? student with { Color = update.Color }
            : student).ToList();

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
            await LoadStudentsAsync(showSpinner: false);
            Snackbar.Add(Loc["StudentGrading_ProfileUpdated"], Severity.Success);
        }
    }
}
