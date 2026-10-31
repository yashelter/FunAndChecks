using System.Globalization;
using Frontend.Admin.Dialogs;
using Frontend.Shared.Api;
using Frontend.Shared.Models;
using Frontend.Shared.Resources;
using Frontend.Shared.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using MudBlazor;

namespace Frontend.Admin.Pages;

public partial class Management
{
    [Inject] private SubjectsApi Subjects { get; set; } = null!;
    [Inject] private GroupsApi Groups { get; set; } = null!;
    [Inject] private GradesApi Grades { get; set; } = null!;
    [Inject] private QueuesApi QueuesApi { get; set; } = null!;
    [Inject] private MeApi Me { get; set; } = null!;
    [Inject] private IDialogService DialogService { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;
    [Inject] private IStringLocalizer<AppStrings> Loc { get; set; } = null!;
    [Inject] private UnsavedChangesTracker Dirty { get; set; } = null!;
    [Inject] private AttendanceApi Attendance { get; set; } = null!;
    [Inject] private IDialogService Dialog { get; set; } = null!;

    // ----- Мутабельные модели строк для inline-редактирования таблиц -----
    private sealed class SubjectRow
    {
        public int Id;
        public string Name = "";
        public bool AttendanceEnabled;
    }

    private sealed class GroupRow { public int Id; public string Name = ""; }

    private sealed class TaskRow { public int Id; public string Name = ""; public string Description = ""; public int MaxPoints; }

    private sealed class ComponentRow { public int Id; public string Name = ""; public int MinPoints; public int MaxPoints; }

    private sealed class QueueRow
    {
        public int Id;
        public string Name = "";
        public DateTime EventDateTime;
        public bool AllowSelfJoin;
        public bool InvalidDate = false;

        /// <summary>Редактируемое локальное представление даты/времени.</summary>
        public string LocalDateTimeText
        {
            get => EventDateTime.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
            set
            {
                if (DateTime.TryParse(value, CultureInfo.CurrentCulture,
                        DateTimeStyles.AssumeLocal, out var parsed))
                    EventDateTime = parsed.ToUniversalTime();
                else
                    InvalidDate = true;
            }
        }
    }

    // ----- Данные -----
    private List<SubjectRow> _subjects = [];
    private List<GroupRow> _groups = [];
    private List<TaskRow> _tasks = [];
    private List<ComponentRow> _components = [];
    private List<QueueRow> _queues = [];
    private Dictionary<int, List<int>> _groupSubjects = new();
    private bool _loading = true;
    private bool _accessLinksFailed = false;

    // ----- UI state -----
    private string _newSubjectName = "";
    private HashSet<int> _newSubjectGroupIds = [];
    private string _newGroupName = "";
    private string _newTaskName = "";
    private string _newTaskDescription = "";
    private int _newTaskMaxPoints = 100;
    private int _newTaskSubjectId;
    private string _newComponentName = "";
    private int _newComponentMinPoints;
    private int _newComponentMaxPoints;
    private int _newComponentSubjectId;
    private string _newQueueName = "";
    private DateTime _newQueueDateTime = DateTime.Now.AddDays(1);
    private int _queueSubjectId;
    private bool _allowSelfJoin = true;
    private HashSet<int> _autoFillGroupIds = [];

    protected override async Task OnInitializedAsync()
    {
        await ReloadAsync();
        await LoadAccessLinksAsync();
    }

    private async Task ReloadAsync()
    {
        _loading = true;
        try
        {
            _subjects = (await Subjects.GetAllAsync())
                .Select(s => new SubjectRow { Id = s.Id, Name = s.Name, AttendanceEnabled = s.AttendanceEnabled })
                .ToList();
            _groups = (await Groups.GetAllAsync())
                .Select(g => new GroupRow { Id = g.Id, Name = g.Name })
                .ToList();
            _tasks = (await Grades.GetTasksAsync())
                .Select(t => new TaskRow { Id = t.Id, Name = t.Name, Description = t.Description, MaxPoints = t.MaxPoints })
                .ToList();
            _components = (await Grades.GetComponentsAsync())
                .Select(c => new ComponentRow { Id = c.Id, Name = c.Name, MinPoints = c.MinPoints, MaxPoints = c.MaxPoints })
                .ToList();
            await ReloadQueuesAsync();
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

    private async Task LoadAccessLinksAsync()
    {
        try
        {
            var links = await Me.GetVisibleGroupsAsync();
            _groupSubjects = (await Groups.GetSubjectLinksAsync()).ToDictionary(l => l.GroupId, l => l.SubjectIds);
            _accessLinksFailed = false;
        }
        catch (ApiException)
        {
            // Обзор — подсказка: без него таблицы и кнопки «Доступ» работают как раньше.
            _accessLinksFailed = true;
        }
    }

    private IEnumerable<string> SubjectNamesOf(int groupId) =>
        _groupSubjects.GetValueOrDefault(groupId, [])
            .Select(id => _subjects.FirstOrDefault(s => s.Id == id)?.Name).OfType<string>()
            .Order(StringComparer.CurrentCultureIgnoreCase);

    private IEnumerable<string> GroupNamesOf(int subjectId) =>
        _groups.Where(g => _groupSubjects.TryGetValue(g.Id, out var ids) && ids.Contains(subjectId))
            .Select(g => g.Name).Order(StringComparer.CurrentCultureIgnoreCase);

    private async Task ReloadQueuesAsync()
    {
        try
        {
            _queues = (await QueuesApi.GetAllAsync())
                .Select(q => new QueueRow { Id = q.Id, Name = q.Name, EventDateTime = q.EventDateTime, AllowSelfJoin = q.AllowSelfJoin })
                .ToList();
        }
        catch (ApiException ex)
        {
            // Вызывается и как after() успешных операций — сбой перечитки не должен
            // выглядеть ошибкой самой операции.
            Snackbar.Add(ex.Message, Severity.Error);
        }
    }

    // ----- Очереди -----
    private async Task OnQueueCreatedAsync(QueueEventDto _)
    {
        await ReloadQueuesAsync();
        Dirty.MarkClean();
    }

    // RowEditCommit в MudTable — синхронный Action<object>; запускаем сохранение фоном.
    private void OnQueueCommit(object element)
    {
        if (element is QueueRow { InvalidDate: true } invalid)
        {
            invalid.InvalidDate = false;
            Snackbar.Add(Loc["Management_InvalidDate", CultureInfo.CurrentCulture.DateTimeFormat.ShortDatePattern + " " +
                CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern], Severity.Warning);
            _ = ReloadQueuesAsync();
            return;
        }
        var row = (QueueRow)element;
        _ = RunAsync(
            () => QueuesApi.UpdateAsync(row.Id, new UpdateQueueEventRequest(row.Name, row.EventDateTime, row.AllowSelfJoin)),
            Loc["Management_QueueUpdated"], () => Task.CompletedTask, ReloadQueuesAsync);
    }

    /// <summary>Самозапись переключается прямо в строке, без режима правки — для любой очереди, в том числе авто.</summary>
    private Task SetQueueSelfJoinAsync(QueueRow row, bool allow) => RunAsync(
        () => QueuesApi.UpdateAsync(row.Id, new UpdateQueueEventRequest(row.Name, row.EventDateTime, allow)),
        Loc["Management_QueueUpdated"], () => { row.AllowSelfJoin = allow; return Task.CompletedTask; }, ReloadQueuesAsync);

    private async Task DeleteQueueAsync(QueueRow row)
    {
        if (await Confirm.ConfirmDeleteAsync(row.Name)) await RunAsync(() => QueuesApi.DeleteAsync(row.Id), Loc["Management_QueueDeleted"], ReloadQueuesAsync);
    }

    // ----- Предметы -----
    private async Task SetAttendanceEnabledAsync(SubjectRow row, bool enabled)
    {
        try
        {
            await Attendance.SetEnabledAsync(row.Id, enabled);
            row.AttendanceEnabled = enabled;
            Snackbar.Add(enabled ? "Учёт посещаемости включён." : "Учёт выключен, история сохранена.", Severity.Success);
        }
        catch (ApiException ex) { Snackbar.Add(ex.Message, Severity.Error); }
    }

    private async Task CreateSubjectAsync()
    {
        if (string.IsNullOrWhiteSpace(_newSubjectName)) return;

        await RunAsync(
            async () =>
            {
                var subject = await Subjects.CreateAsync(new CreateSubjectRequest(_newSubjectName.Trim()));
                foreach (var groupId in _newSubjectGroupIds)
                    await Groups.LinkSubjectAsync(groupId, subject.Id);
            },
            Loc["Management_SubjectCreated"],
            async () => { _newSubjectName = ""; _newSubjectGroupIds = new HashSet<int>(); await ReloadAsync(); });
    }

    private void OnSubjectCommit(object element) => _ = RunAsync(
        () => Subjects.UpdateAsync(((SubjectRow)element).Id, new UpdateSubjectRequest(((SubjectRow)element).Name)),
        Loc["Common_Saved"], () => Task.CompletedTask, ReloadAsync);

    private async Task DeleteSubjectAsync(SubjectRow row)
    {
        if (await Confirm.ConfirmDeleteAsync(row.Name)) await RunAsync(() => Subjects.DeleteAsync(row.Id), Loc["Management_SubjectDeleted"], ReloadAsync);
    }

    // ----- Группы -----
    private async Task CreateGroupAsync()
    {
        if (string.IsNullOrWhiteSpace(_newGroupName)) return;

        await RunAsync(
            async () =>
            {
                var group = await Groups.CreateAsync(new CreateGroupRequest(_newGroupName.Trim()));
                foreach (var subjectId in _newSubjectGroupIds)
                    await Groups.LinkSubjectAsync(group.Id, subjectId);
            },
            Loc["Management_GroupCreated"],
            async () => { _newGroupName = ""; _newSubjectGroupIds = new HashSet<int>(); await ReloadAsync(); });
    }

    private void OnGroupCommit(object element) => _ = RunAsync(
        () => Groups.UpdateAsync(((GroupRow)element).Id, new UpdateGroupRequest(((GroupRow)element).Name)),
        Loc["Common_Saved"], () => Task.CompletedTask, ReloadAsync);

    private async Task DeleteGroupAsync(GroupRow row)
    {
        if (await Confirm.ConfirmDeleteAsync(row.Name)) await RunAsync(() => Groups.DeleteAsync(row.Id), Loc["Management_GroupDeleted"], ReloadAsync);
    }

    // ----- Задачи -----
    private async Task CreateTaskAsync()
    {
        if (string.IsNullOrWhiteSpace(_newTaskName) || _newTaskSubjectId == 0) return;

        await RunAsync(
            async () =>
            {
                await Grades.CreateTaskAsync(new CreateTaskRequest(_newTaskName.Trim(), _newTaskDescription, _newTaskMaxPoints, _newTaskSubjectId));
            },
            Loc["Management_TaskCreated"],
            async () => { _newTaskName = ""; _newTaskDescription = ""; _newTaskMaxPoints = 100; _newTaskSubjectId = 0; await ReloadAsync(); });
    }

    private void OnTaskCommit(object element) => _ = RunAsync(
        () => Grades.UpdateTaskAsync(((TaskRow)element).Id, new UpdateTaskRequest(((TaskRow)element).Name, ((TaskRow)element).Description, ((TaskRow)element).MaxPoints)),
        Loc["Common_Saved"], () => Task.CompletedTask, ReloadAsync);

    private async Task DeleteTaskAsync(TaskRow row)
    {
        if (await Confirm.ConfirmDeleteAsync(row.Name)) await RunAsync(() => Grades.DeleteTaskAsync(row.Id), Loc["Management_TaskDeleted"], ReloadAsync);
    }

    // ----- Компоненты оценки -----
    private async Task CreateComponentAsync()
    {
        if (string.IsNullOrWhiteSpace(_newComponentName) || _newComponentSubjectId == 0) return;

        await RunAsync(
            async () =>
            {
                await Grades.CreateComponentAsync(new CreateComponentRequest(_newComponentName.Trim(), _newComponentMinPoints, _newComponentMaxPoints, _newComponentSubjectId));
            },
            Loc["Management_ComponentCreated"],
            async () => { _newComponentName = ""; _newComponentMinPoints = 0; _newComponentMaxPoints = 0; _newComponentSubjectId = 0; await ReloadAsync(); });
    }

    private void OnComponentCommit(object element) => _ = RunAsync(
        () => Grades.UpdateComponentAsync(((ComponentRow)element).Id, new UpdateComponentRequest(((ComponentRow)element).Name, ((ComponentRow)element).MinPoints, ((ComponentRow)element).MaxPoints)),
        Loc["Common_Saved"], () => Task.CompletedTask, ReloadAsync);

    private async Task DeleteComponentAsync(ComponentRow row)
    {
        if (await Confirm.ConfirmDeleteAsync(row.Name)) await RunAsync(() => Grades.DeleteComponentAsync(row.Id), Loc["Management_ComponentDeleted"], ReloadAsync);
    }

    // ----- Очередь: создание -----
    private async Task CreateQueueAsync()
    {
        if (string.IsNullOrWhiteSpace(_newQueueName) || _queueSubjectId == 0) return;

        var autoFill = _autoFillGroupIds.Count > 0 ? _autoFillGroupIds.ToList() : null;

        await RunAsync(
            () => QueuesApi.CreateAsync(new CreateQueueEventRequest(_newQueueName.Trim(), _newQueueDateTime, _queueSubjectId, _allowSelfJoin, autoFill)),
            Loc["Management_QueueCreated"],
            async () => { _newQueueName = ""; _newQueueDateTime = DateTime.Now.AddDays(1); _queueSubjectId = 0; _allowSelfJoin = true; _autoFillGroupIds = new HashSet<int>(); await ReloadAsync(); });
    }

    // ----- Очередь: автозаполнение по группам (SetQueueSelfJoinAsync выше) -----

    // ----- Helpers -----
    private async Task RunAsync(Func<Task> action, string successMessage, Func<Task> afterSuccess = null, Func<Task> finallyAction = null)
    {
        try
        {
            await action();
            Snackbar.Add(successMessage, Severity.Success);
            if (afterSuccess != null) await afterSuccess();
        }
        catch (ApiException ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
        }
        finally
        {
            if (finallyAction != null) await finallyAction();
        }
    }
}
