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

    private List<StudentDetailsDto> Visible
    {
        get
        {
            IEnumerable<StudentDetailsDto> items = _students;
            if (_groups.Count > 0) items = items.Where(s => s.GroupId is int group && _groups.Contains(group));
            var query = _query.Trim();
            // Имя ищем в обоих порядках: в списке «Фамилия Имя», а набирают часто «Имя Фамилия».
            if (query.Length > 0)
                items = items.Where(s => $"{s.LastName} {s.FirstName}".Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                                         $"{s.FirstName} {s.LastName}".Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                                         (s.Email?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false));
            return items.ToList();
        }
    }

    /// <summary>Группы студентов предмета с числом студентов — для фильтра.</summary>
    private List<(int Id, string Name, int Count)> StudentGroups => _students.Where(s => s.GroupId.HasValue)
        .GroupBy(s => s.GroupId!.Value)
        .Select(g => (g.Key, g.First().GroupName ?? "—", g.Count()))
        .OrderBy(g => g.Item2, StringComparer.CurrentCultureIgnoreCase).ToList();

    private string GroupsText(IReadOnlyList<string> _) => _groups.Count == 0
        ? Loc["AttendanceJournal_AllGroups"]
        : string.Join(", ", StudentGroups.Where(g => _groups.Contains(g.Id)).Select(g => g.Name));

    private string GroupsKey => $"fc-grading-groups:{_subjectId}";

    private async Task OnGroupsChangedAsync(IEnumerable<int>? groups)
    {
        _groups = [.. groups ?? []];
        try
        {
            if (_groups.Count == 0) await JS.InvokeVoidAsync("localStorage.removeItem", GroupsKey);
            else await JS.InvokeVoidAsync("localStorage.setItem", GroupsKey, JsonSerializer.Serialize(_groups));
        }
        catch (JSException) { /* Выбор останется до перезагрузки страницы. */ }
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
        await CurrentSubject.SetAsync(_subjectId);
        await LoadStudentsAsync();
    }

    /// <summary>Студенты предмета и их прогресс (из таблицы результатов) — параллельно, одним заходом.</summary>
    private async Task LoadStudentsAsync(bool showSpinner = true)
    {
        if (_subjectId is not int subjectId)
        {
            _students = [];
            return;
        }
        _loading = showSpinner;
        try
        {
            var accessTask = Me.GetMyAccessAsync();
            var studentsTask = Subjects.GetStudentsAsync(subjectId);
            var resultsTask = Results.GetSubjectResultsAsync(subjectId);
            _restrictedGroups = [.. (await accessTask).RestrictedGroupIds];
            _students = (await studentsTask).OrderBy(s => s.LastName).ThenBy(s => s.FirstName).ToList();
            if (showSpinner) await RestoreGroupsAsync();
            // Прогресс — подсказка в строке; если таблица результатов не загрузилась, список всё равно показываем.
            try
            {
                var results = await resultsTask;
                _taskCount = results.TaskHeaders.Count;
                _progress = results.UserResults.ToDictionary(r => r.StudentId, r => (
                    r.Results.Values.Count(c => c.Status == SubmissionStatus.Accepted),
                    r.Results.Values.Count(c => c.Status == SubmissionStatus.Rejected)));
            }
            catch (ApiException)
            {
                _taskCount = 0;
                _progress = [];
            }
        }
        catch (ApiException ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
        }
        finally
        {
            _loading = false;
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

    private string ProgressText(StudentDetailsDto student)
    {
        if (_taskCount == 0) return string.Empty;
        var (accepted, rework) = _progress.GetValueOrDefault(student.Id);
        return rework > 0
            ? Loc["Grading_ProgressWithRework", accepted, _taskCount, rework]
            : Loc["Grading_Progress", accepted, _taskCount];
    }

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
