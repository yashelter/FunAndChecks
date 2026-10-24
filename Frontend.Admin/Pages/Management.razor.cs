using System.Globalization;
using Frontend.Admin.Dialogs;
using Frontend.Shared.Api;
using Frontend.Shared.Auth;
using Frontend.Shared.Models;
using Frontend.Shared.Resources;
using Frontend.Shared.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Localization;
using Microsoft.JSInterop;
using MudBlazor;

namespace Frontend.Admin.Pages;

public partial class Management
{
    [Inject] private SubjectsApi Subjects { get; set; } = null!;
    [Inject] private GroupsApi Groups { get; set; } = null!;
    [Inject] private GradesApi Grades { get; set; } = null!;
    [Inject] private QueuesApi QueuesApi { get; set; } = null!;
    [Inject] private MeApi Me { get; set; } = null!;
    [Inject] private AuthenticationStateProvider Authentication { get; set; } = null!;
    [Inject] private IDialogService DialogService { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;
    [Inject] private AttendanceApi Attendance { get; set; } = null!;
    [Inject] private IStringLocalizer<AppStrings> Loc { get; set; } = null!;
    [Inject] private UnsavedChangesTracker Dirty { get; set; } = null!;
    [Inject] private ConfirmService Confirm { get; set; } = null!;
    [Inject] private CurrentSubject CurrentSubject { get; set; } = null!;
    [Inject] private IJSRuntime JS { get; set; } = null!;

    // ----- Мутабельные модели строк для inline-редактирования таблиц -----
    private sealed class SubjectRow { public int Id; public string Name = ""; public bool AttendanceEnabled; }
    private sealed class GroupRow { public int Id; public string Name = ""; }
    private sealed class TaskRow
    {
        public int Id; public string Name = ""; public string Description = ""; public int MaxPoints; public int? TaskGroupId;
        /// <summary>Значение для MudSelect: 0 — «без группы».</summary>
        public int GroupSelection { get => TaskGroupId ?? 0; set => TaskGroupId = value == 0 ? null : value; }
    }
    private sealed class TaskGroupRow { public int Id; public string Name = ""; public int TaskCount; }
    private sealed class ComponentRow { public int Id; public string Name = ""; public int MinPoints; public int MaxPoints; }

    private sealed class QueueRow
    {
        public int Id;
        public string Name = "";
        public DateTime EventDateTime;
        public bool AllowSelfJoin;

        /// <summary>Введённая дата не разобралась — сохранять строку нельзя.</summary>
        public bool InvalidDate;

        /// <summary>Редактируемое локальное представление даты/времени.</summary>
        public string LocalDateTimeText
        {
            get => EventDateTime.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
            set
            {
                InvalidDate = !DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out var parsed);
                if (!InvalidDate) EventDateTime = parsed.ToUniversalTime();
            }
        }
    }

    private List<SubjectRow> _subjects = [];
    private List<GroupRow> _groups = [];
    private List<TaskRow> _tasks = [];
    private List<TaskGroupRow> _taskGroups = [];
    private List<ComponentRow> _components = [];
    private List<QueueRow> _queues = [];
    private bool _loading = true;
    private bool _isSuperAdmin;
    private string _section = "queues";
    private static readonly (string Id, string Label, string Icon)[] ManagementSections =
    [
        ("queues", "Management_TabQueues", Icons.Material.Filled.Queue),
        ("subjects", "Management_TabSubjects", Icons.Material.Filled.Layers),
        ("groups", "Management_TabGroups", Icons.Material.Filled.Groups),
        ("tasks", "Management_TabTasks", Icons.Material.Filled.Code),
        ("grades", "Management_TabGradeComponents", Icons.Material.Filled.Grading),
        ("visibility", "Management_VisibilityTab", Icons.Material.Filled.Visibility)
    ];

    // Видимость/архив: скрытые самим админом и запрещённые супер-админом.
    private HashSet<int> _hiddenSubjects = [];
    private HashSet<int> _hiddenGroups = [];
    private HashSet<int> _restrictedSubjects = [];
    private HashSet<int> _restrictedGroups = [];

    // Поля форм добавления.
    private string _newSubjectName = "";
    private IReadOnlyCollection<int> _newSubjectGroupIds = new HashSet<int>();
    private string _newGroupName = "";
    private IReadOnlyCollection<int> _newGroupSubjectIds = new HashSet<int>();
    private string _newTaskName = "";
    private string _newTaskDescription = "";
    private int _newTaskMaxPoints = 1;
    private string _newComponentName = "";
    private int _newComponentMin;
    private int _newComponentMax = 5;

    private int? _taskSubjectId;
    private int? _componentSubjectId;

    // Очередь — предмет, с которым открывается форма создания (сама форма — общий QueueCreateForm).
    private int? _queueSubjectId;
    private List<(int Id, string Name)> QueueSubjects => AvailableSubjects.Select(s => (s.Id, s.Name)).ToList();
    private List<(int Id, string Name)> QueueGroups => _groups.Select(g => (g.Id, g.Name)).ToList();

    protected override async Task OnInitializedAsync()
    {
        _isSuperAdmin = (await Authentication.GetAuthenticationStateAsync()).User.IsInRole(Roles.SuperAdmin);
        await ReloadAsync();
        if (await CurrentSubject.PickAsync(AvailableSubjects.Select(s => s.Id)) is int current)
        {
            _queueSubjectId = current;
            await OnTaskSubjectChanged(current);
            await OnComponentSubjectChanged(current);
        }
        _loading = false;
    }

    private async Task ReloadAsync()
    {
        try
        {
            _subjects = (await Subjects.GetAllAsync()).Select(s => new SubjectRow { Id = s.Id, Name = s.Name, AttendanceEnabled = s.AttendanceEnabled }).ToList();
            _groups = (await Groups.GetAllAsync()).Select(g => new GroupRow { Id = g.Id, Name = g.Name }).ToList();

            var access = await Me.GetMyAccessAsync();
            _hiddenSubjects = [.. access.HiddenSubjectIds];
            _hiddenGroups = [.. access.HiddenGroupIds];
            _restrictedSubjects = [.. access.RestrictedSubjectIds];
            _restrictedGroups = [.. access.RestrictedGroupIds];

            await ReloadQueuesAsync();
            await LoadAccessLinksAsync();
        }
        catch (ApiException ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
        }
    }

    // ----- Обзор доступов: какие предметы открыты каждой группе (и наоборот) -----
    private Dictionary<int, List<int>> _groupSubjects = [];
    private bool _accessLinksFailed;

    private async Task LoadAccessLinksAsync()
    {
        try
        {
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
        _ = RunAsync(
        () => QueuesApi.UpdateAsync(((QueueRow)element).Id, new UpdateQueueEventRequest(((QueueRow)element).Name, ((QueueRow)element).EventDateTime)),
        Loc["Management_QueueUpdated"], () => Task.CompletedTask, ReloadQueuesAsync);
    }

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
                if (_isSuperAdmin)
                    foreach (var groupId in _newSubjectGroupIds)
                        await Groups.LinkSubjectAsync(groupId, subject.Id);
            },
            Loc[_isSuperAdmin ? "Management_SubjectCreated" : "Management_SubjectCreatedPendingAccess"],
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
                foreach (var subjectId in _newGroupSubjectIds)
                    await Groups.LinkSubjectAsync(group.Id, subjectId);
            },
            Loc["Management_GroupCreated"],
            async () => { _newGroupName = ""; _newGroupSubjectIds = new HashSet<int>(); await ReloadAsync(); });
    }

    private void OnGroupCommit(object element) => _ = RunAsync(
        () => Groups.UpdateAsync(((GroupRow)element).Id, new UpdateGroupRequest(((GroupRow)element).Name)),
        Loc["Common_Saved"], () => Task.CompletedTask, ReloadAsync);

    private async Task DeleteGroupAsync(GroupRow row)
    {
        if (await Confirm.ConfirmDeleteAsync(row.Name)) await RunAsync(() => Groups.DeleteAsync(row.Id), Loc["Management_GroupDeleted"], ReloadAsync);
    }

    // ----- Задачи -----
    private enum TaskCreateMode { Single, List, Set, Copy }
    private static readonly (TaskCreateMode Id, string Label)[] TaskCreateModes =
    [
        (TaskCreateMode.Single, "Management_TaskModeSingle"),
        (TaskCreateMode.List, "Management_TaskModeList"),
        (TaskCreateMode.Set, "Management_TaskModeSet"),
        (TaskCreateMode.Copy, "Management_TaskModeCopy"),
    ];
    private TaskCreateMode _taskCreateMode = TaskCreateMode.Single;

    private async Task OnTaskSubjectChanged(int? subjectId)
    {
        // Отмеченные задачи переживают перечитку того же предмета (строки пересоздаются — сверяем по Id).
        var keepSelected = subjectId == _taskSubjectId ? _selectedTasks.Select(t => t.Id).ToHashSet() : [];
        _taskSubjectId = subjectId;
        await CurrentSubject.SetAsync(subjectId);
        if (subjectId is null)
        {
            _tasks = [];
            _taskGroups = [];
            _selectedTasks = [];
            _newTaskGroupId = 0;
            return;
        }

        try
        {
            var groupsTask = Subjects.GetTaskGroupsAsync(subjectId.Value);
            _tasks = (await Subjects.GetTasksAsync(subjectId.Value))
                .Select(t => new TaskRow { Id = t.Id, Name = t.Name, Description = t.Description, MaxPoints = t.MaxPoints, TaskGroupId = t.TaskGroupId })
                .ToList();
            _taskGroups = (await groupsTask).Select(g => new TaskGroupRow { Id = g.Id, Name = g.Name, TaskCount = g.TaskCount }).ToList();
            if (_taskGroups.All(g => g.Id != _newTaskGroupId)) _newTaskGroupId = 0;
            if (_bulkGroupSelection > 0 && _taskGroups.All(g => g.Id != _bulkGroupSelection)) _bulkGroupSelection = KeepValue;
            _selectedTasks = _tasks.Where(t => keepSelected.Contains(t.Id)).ToHashSet();
        }
        catch (ApiException ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
        }
    }

    private MudTextField<string>? _taskNameField;
    /// <summary>Группа для новых задач (одиночных, пачкой и набором); 0 — без группы.</summary>
    private int _newTaskGroupId;
    private int? NewTaskGroupId => _newTaskGroupId == 0 ? null : _newTaskGroupId;
    private string _newTaskGroupName = "";

    private string TaskGroupName(int? taskGroupId) =>
        _taskGroups.FirstOrDefault(g => g.Id == taskGroupId)?.Name ?? Loc["TaskGroup_None"];
    private string? _bulkTasks;
    private int? _copyFromSubjectId;
    private bool _bulkBusy;

    private async Task CreateTaskAsync()
    {
        if (_taskSubjectId is null || string.IsNullOrWhiteSpace(_newTaskName)) return;
        await RunAsync(
            () => Subjects.CreateTaskAsync(_taskSubjectId.Value, new CreateTaskRequest(_newTaskName.Trim(), _newTaskDescription?.Trim() ?? "", _newTaskMaxPoints, NewTaskGroupId)),
            Loc["Management_TaskCreated"],
            async () => { _newTaskName = ""; _newTaskDescription = ""; await OnTaskSubjectChanged(_taskSubjectId); });
        // Баллы оставляем прежними: соседние задачи обычно стоят одинаково.
        if (_taskNameField is not null) await _taskNameField.FocusAsync();
    }

    /// <summary>Строки вида «название;баллы[;описание]» — сразу пачка задач. Ошибка в строке останавливает ввод до записи.</summary>
    private async Task AddBulkTasksAsync()
    {
        if (_taskSubjectId is not int subjectId || string.IsNullOrWhiteSpace(_bulkTasks)) return;
        var requests = new List<CreateTaskRequest>();
        var lines = _bulkTasks.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < lines.Length; i++)
        {
            var parts = lines[i].Split(lines[i].Contains('\t') ? '\t' : ';', StringSplitOptions.TrimEntries);
            if (parts.Length < 2 || parts[0].Length == 0 || !int.TryParse(parts[1], out var points) || points is < 0 or > 1000)
            {
                Snackbar.Add(Loc["Management_TasksBulkInvalid", i + 1], Severity.Warning);
                return;
            }
            requests.Add(new CreateTaskRequest(parts[0], parts.Length > 2 ? parts[2] : "", points, NewTaskGroupId));
        }
        if (await CreateTasksAsync(subjectId, requests) is int created && created > 0)
        {
            _bulkTasks = null;
            Snackbar.Add(Loc["Management_TasksBulkAdded", created], Severity.Success);
        }
    }

    /// <summary>
    /// Копирует задачи другого предмета, пропуская совпадающие по названию. Группы переносятся по названию:
    /// недостающие создаются в текущем предмете.
    /// </summary>
    private async Task CopyTasksFromAsync(int? sourceId)
    {
        _copyFromSubjectId = null;
        if (_taskSubjectId is not int subjectId || sourceId is not int source) return;
        try
        {
            var existing = _tasks.Select(t => t.Name.Trim()).ToHashSet(StringComparer.CurrentCultureIgnoreCase);
            var sourceTasks = (await Subjects.GetTasksAsync(source))
                .Where(t => !existing.Contains(t.Name.Trim())).ToList();
            var groupIds = _taskGroups.ToDictionary(g => g.Name, g => g.Id, StringComparer.Ordinal);
            foreach (var groupName in sourceTasks.Select(t => t.TaskGroupName).OfType<string>().Distinct(StringComparer.Ordinal))
            {
                if (!groupIds.ContainsKey(groupName))
                    groupIds[groupName] = (await Subjects.CreateTaskGroupAsync(subjectId, new CreateTaskGroupRequest(groupName))).Id;
            }
            var requests = sourceTasks
                .Select(t => new CreateTaskRequest(t.Name, t.Description, t.MaxPoints,
                    t.TaskGroupName is string g ? groupIds[g] : null)).ToList();
            var created = await CreateTasksAsync(subjectId, requests) ?? 0;
            Snackbar.Add(Loc["Management_TasksCopied", created], Severity.Success);
        }
        catch (ApiException ex) { Snackbar.Add(ex.Message, Severity.Error); }
    }

    private string _setPrefix = "";
    private int _setCount = 10;
    private int _setPoints = 1;

    /// <summary>Имена набора «префикс.NN»: ведущие нули до общей длины, минимум две цифры.</summary>
    private static List<string> BuildSetNames(string prefix, int count)
    {
        var width = Math.Max(2, count.ToString(CultureInfo.InvariantCulture).Length);
        return Enumerable.Range(1, count)
            .Select(i => $"{prefix}.{i.ToString(CultureInfo.InvariantCulture).PadLeft(width, '0')}")
            .ToList();
    }

    private string? TaskSetPreview
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_setPrefix) || _setCount is < 1 or > 99) return null;
            var names = BuildSetNames(_setPrefix.Trim(), _setCount);
            return Loc["Management_TaskSetPreview", names[0], names[^1]];
        }
    }

    /// <summary>Создаёт набор задач, пропуская уже существующие имена — повтор дозаполняет пропуски.</summary>
    private async Task CreateTaskSetAsync()
    {
        if (_taskSubjectId is not int subjectId || string.IsNullOrWhiteSpace(_setPrefix) || _setCount is < 1 or > 99) return;
        var existing = _tasks.Select(t => t.Name.Trim()).ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        var names = BuildSetNames(_setPrefix.Trim(), _setCount);
        var requests = names.Where(n => !existing.Contains(n))
            .Select(n => new CreateTaskRequest(n, "", _setPoints, NewTaskGroupId)).ToList();
        var created = await CreateTasksAsync(subjectId, requests) ?? 0;
        Snackbar.Add(Loc["Management_TaskSetCreated", created, names.Count - requests.Count], Severity.Success);
    }

    private async Task<int?> CreateTasksAsync(int subjectId, IReadOnlyList<CreateTaskRequest> requests)
    {
        _bulkBusy = true;
        var created = 0;
        try
        {
            foreach (var request in requests)
            {
                await Subjects.CreateTaskAsync(subjectId, request);
                created++;
            }
            return created;
        }
        catch (ApiException ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
            return created;
        }
        finally
        {
            _bulkBusy = false;
            await OnTaskSubjectChanged(_taskSubjectId);
        }
    }

    private void OnTaskCommit(object element)
    {
        var row = (TaskRow)element;
        _ = RunAsync(() => Subjects.UpdateTaskAsync(row.Id, new UpdateTaskRequest(row.Name, row.Description, row.MaxPoints, row.TaskGroupId)),
            Loc["Common_Saved"], () => Task.CompletedTask, () => OnTaskSubjectChanged(_taskSubjectId));
    }

    // ----- Массовое изменение задач -----
    private const int KeepValue = -1;
    private HashSet<TaskRow> _selectedTasks = [];
    /// <summary>Группа для отмеченных: <see cref="KeepValue"/> — не менять, 0 — без группы.</summary>
    private int _bulkGroupSelection = KeepValue;
    /// <summary>Баллы для отмеченных; null — не менять.</summary>
    private int? _bulkPoints;
    private bool BulkHasChanges => _bulkGroupSelection != KeepValue || _bulkPoints is not null;

    private void ClearTaskSelection() => _selectedTasks = [];

    private ElementReference _tasksTableHost;
    private IJSObjectReference? _dragSelectModule;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // Таблица задач появляется и исчезает со сменой раздела/предмета; init повторно не навешивает обработчики.
        if (_section != "tasks" || _taskSubjectId is null || _tasksTableHost.Context is null) return;
        try
        {
            _dragSelectModule ??= await JS.InvokeAsync<IJSObjectReference>("import", "./_content/Frontend.Admin/table-drag-select.js?v=2");
            await _dragSelectModule.InvokeVoidAsync("init", _tasksTableHost);
        }
        catch (JSException) { /* без протяжки остаются обычные галочки */ }
        catch (JSDisconnectedException) { }
    }

    private async Task ApplyBulkEditAsync()
    {
        if (!BulkHasChanges || _bulkPoints is < 0 or > 1000) return;
        var changes = _selectedTasks
            .Select(t => (Task: t,
                Points: _bulkPoints ?? t.MaxPoints,
                GroupId: _bulkGroupSelection == KeepValue ? t.TaskGroupId : _bulkGroupSelection == 0 ? null : _bulkGroupSelection))
            .Where(c => c.Points != c.Task.MaxPoints || c.GroupId != c.Task.TaskGroupId)
            .ToList();
        _bulkBusy = true;
        var changed = 0;
        try
        {
            foreach (var (task, points, groupId) in changes)
            {
                await Subjects.UpdateTaskAsync(task.Id, new UpdateTaskRequest(task.Name, task.Description, points, groupId));
                changed++;
            }
            _bulkGroupSelection = KeepValue;
            _bulkPoints = null;
            Dirty.MarkClean();
            Snackbar.Add(Loc["Management_TasksUpdated", changed], Severity.Success);
        }
        catch (ApiException ex)
        {
            // Часть задач уже могла сохраниться — перечитка ниже покажет, какие.
            Snackbar.Add(ex.Message, Severity.Error);
        }
        finally
        {
            _bulkBusy = false;
            await OnTaskSubjectChanged(_taskSubjectId);
        }
    }

    private async Task DeleteSelectedTasksAsync()
    {
        var selected = _selectedTasks.ToList();
        if (selected.Count == 0 ||
            !await Confirm.ConfirmAsync(Loc["Management_BulkDeleteTitle"], Loc["Management_BulkDeleteText", selected.Count], Loc["Common_Delete"]))
            return;
        _bulkBusy = true;
        var deleted = 0;
        try
        {
            foreach (var task in selected)
            {
                await Subjects.DeleteTaskAsync(task.Id);
                deleted++;
            }
            Snackbar.Add(Loc["Management_TasksDeleted", deleted], Severity.Success);
        }
        catch (ApiException ex) { Snackbar.Add(ex.Message, Severity.Error); }
        finally
        {
            _bulkBusy = false;
            await OnTaskSubjectChanged(_taskSubjectId);
        }
    }

    private async Task DeleteTaskAsync(TaskRow row)
    {
        if (await Confirm.ConfirmDeleteAsync(row.Name)) await RunAsync(() => Subjects.DeleteTaskAsync(row.Id), Loc["Management_TaskDeleted"], () => OnTaskSubjectChanged(_taskSubjectId));
    }

    // ----- Группы задач -----
    private async Task CreateTaskGroupAsync()
    {
        if (_taskSubjectId is not int subjectId || string.IsNullOrWhiteSpace(_newTaskGroupName)) return;
        await RunAsync(
            () => Subjects.CreateTaskGroupAsync(subjectId, new CreateTaskGroupRequest(_newTaskGroupName.Trim())),
            Loc["Management_TaskGroupCreated"],
            async () => { _newTaskGroupName = ""; await OnTaskSubjectChanged(_taskSubjectId); });
    }

    private void OnTaskGroupCommit(object element)
    {
        var row = (TaskGroupRow)element;
        _ = RunAsync(() => Subjects.UpdateTaskGroupAsync(row.Id, new UpdateTaskGroupRequest(row.Name)),
            Loc["Common_Saved"], () => OnTaskSubjectChanged(_taskSubjectId), () => OnTaskSubjectChanged(_taskSubjectId));
    }

    /// <summary>Удаляет группу; задачи остаются без группы.</summary>
    private async Task DeleteTaskGroupAsync(TaskGroupRow row)
    {
        if (await Confirm.ConfirmDeleteAsync(row.Name))
            await RunAsync(() => Subjects.DeleteTaskGroupAsync(row.Id), Loc["Management_TaskGroupDeleted"], () => OnTaskSubjectChanged(_taskSubjectId));
    }

    // ----- Оценочные колонки -----
    private async Task OnComponentSubjectChanged(int? subjectId)
    {
        _componentSubjectId = subjectId;
        await CurrentSubject.SetAsync(subjectId);
        if (subjectId is null)
        {
            _components = [];
            return;
        }

        try
        {
            _components = (await Subjects.GetGradeComponentsAsync(subjectId.Value))
                .Select(c => new ComponentRow { Id = c.Id, Name = c.Name, MinPoints = c.MinPoints, MaxPoints = c.MaxPoints })
                .ToList();
        }
        catch (ApiException ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
        }
    }

    private async Task CreateComponentAsync()
    {
        if (_componentSubjectId is null || string.IsNullOrWhiteSpace(_newComponentName)) return;
        await RunAsync(
            () => Subjects.CreateGradeComponentAsync(_componentSubjectId.Value, new CreateGradeComponentRequest(_newComponentName.Trim(), _newComponentMin, _newComponentMax)),
            Loc["Management_ComponentCreated"],
            async () => { _newComponentName = ""; _newComponentMin = 0; _newComponentMax = 5; await OnComponentSubjectChanged(_componentSubjectId); });
    }

    private void OnComponentCommit(object element)
    {
        var row = (ComponentRow)element;
        _ = RunAsync(() => Subjects.UpdateGradeComponentAsync(row.Id, new UpdateGradeComponentRequest(row.Name, row.MinPoints, row.MaxPoints)),
            Loc["Common_Saved"], () => Task.CompletedTask, () => OnComponentSubjectChanged(_componentSubjectId));
    }

    private async Task DeleteComponentAsync(ComponentRow row)
    {
        if (await Confirm.ConfirmDeleteAsync(row.Name)) await RunAsync(() => Grades.DeleteComponentAsync(row.Id), Loc["Management_ComponentDeleted"], () => OnComponentSubjectChanged(_componentSubjectId));
    }

    // ----- Доступ (связи группа↔предмет) -----
    private async Task OpenSubjectAccessAsync(SubjectRow subject)
    {
        List<int> current;
        try
        {
            current = await Groups.GetGroupIdsForSubjectAsync(subject.Id);
        }
        catch (ApiException ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
            return;
        }

        var options = _groups.Select(g => new EntityLinkDialog.LinkOption(g.Id, g.Name)).ToList();
        var selected = await ShowAccessDialogAsync(string.Format(Loc["Management_SubjectAccessTitle"], subject.Name), options, current);
        if (selected is null)
            return;

        await ApplyAccessDiffAsync(current, selected,
            groupId => Groups.LinkSubjectAsync(groupId, subject.Id),
            groupId => Groups.UnlinkSubjectAsync(groupId, subject.Id));
    }

    private async Task OpenGroupAccessAsync(GroupRow group)
    {
        List<int> current;
        try
        {
            current = await Groups.GetSubjectIdsAsync(group.Id);
        }
        catch (ApiException ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
            return;
        }

        var options = AvailableSubjects.Select(s => new EntityLinkDialog.LinkOption(s.Id, s.Name)).ToList();
        var editableIds = options.Select(option => option.Id).ToHashSet();
        current = current.Where(editableIds.Contains).ToList();
        var selected = await ShowAccessDialogAsync(string.Format(Loc["Management_GroupAccessTitle"], group.Name), options, current);
        if (selected is null)
            return;

        await ApplyAccessDiffAsync(current, selected,
            subjectId => Groups.LinkSubjectAsync(group.Id, subjectId),
            subjectId => Groups.UnlinkSubjectAsync(group.Id, subjectId));
    }

    private async Task<HashSet<int>?> ShowAccessDialogAsync(string title, List<EntityLinkDialog.LinkOption> options, List<int> current)
    {
        var parameters = new DialogParameters<EntityLinkDialog>
        {
            { x => x.Title, title },
            { x => x.Options, options },
            { x => x.SelectedIds, current },
        };
        var dialog = await DialogService.ShowAsync<EntityLinkDialog>(title, parameters);
        var result = await dialog.Result;
        return result is { Canceled: false, Data: HashSet<int> set } ? set : null;
    }

    private async Task ApplyAccessDiffAsync(List<int> current, HashSet<int> selected, Func<int, Task> link, Func<int, Task> unlink)
    {
        try
        {
            foreach (var id in selected.Where(id => !current.Contains(id)))
                await link(id);
            foreach (var id in current.Where(id => !selected.Contains(id)))
                await unlink(id);
            Snackbar.Add(Loc["Management_AccessUpdated"], Severity.Success);
            Dirty.MarkClean();
        }
        catch (ApiException ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
        }
        // И после частичной ошибки: обзор показывает то, что реально сохранено.
        await LoadAccessLinksAsync();
    }

    // ----- Видимость / архив (скрытие самим админом) -----
    private bool IsSubjectArchived(int id) => _hiddenSubjects.Contains(id);
    private bool IsSubjectRestricted(int id) => _restrictedSubjects.Contains(id);
    private bool CanUseSubject(int id) => !IsSubjectRestricted(id) && !IsSubjectArchived(id);
    private IEnumerable<SubjectRow> AvailableSubjects => _subjects.Where(subject => CanUseSubject(subject.Id));
    private bool IsGroupArchived(int id) => _hiddenGroups.Contains(id);
    private bool IsGroupRestricted(int id) => _restrictedGroups.Contains(id);

    private async Task SetSubjectArchivedAsync(int subjectId, bool hidden)
    {
        await RunAsync(
            () => Me.SetSubjectHiddenAsync(subjectId, hidden),
            hidden ? Loc["Management_SubjectArchived"] : Loc["Management_SubjectRestored"],
            () => { if (hidden) _hiddenSubjects.Add(subjectId); else _hiddenSubjects.Remove(subjectId); return Task.CompletedTask; });
    }

    private async Task SetGroupArchivedAsync(int groupId, bool hidden)
    {
        await RunAsync(
            () => Me.SetGroupHiddenAsync(groupId, hidden),
            hidden ? Loc["Management_GroupArchived"] : Loc["Management_GroupRestored"],
            () => { if (hidden) _hiddenGroups.Add(groupId); else _hiddenGroups.Remove(groupId); return Task.CompletedTask; });
    }

    private async Task RunAsync(Func<Task> action, string success, Func<Task> after, Func<Task>? onError = null)
    {
        try
        {
            await action();
            await after();
            Dirty.MarkClean();
            Snackbar.Add(success, Severity.Success);
        }
        catch (ApiException ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
            if (onError != null)
                await onError();
        }
        finally
        {
            StateHasChanged();
        }
    }
}
