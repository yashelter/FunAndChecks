using System.Globalization;
using Frontend.Shared.Api;
using Frontend.Shared.Models;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Frontend.Admin.Pages;

public partial class Management
{
    [Inject] private SubjectsApi Subjects { get; set; } = null!;
    [Inject] private GroupsApi Groups { get; set; } = null!;
    [Inject] private GradesApi Grades { get; set; } = null!;
    [Inject] private QueuesApi QueuesApi { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    // ----- Мутабельные модели строк для inline-редактирования таблиц -----
    private sealed class SubjectRow { public int Id; public string Name = ""; }
    private sealed class GroupRow { public int Id; public string Name = ""; }
    private sealed class TaskRow { public int Id; public string Name = ""; public string Description = ""; public int MaxPoints; }
    private sealed class ComponentRow { public int Id; public string Name = ""; public int MinPoints; public int MaxPoints; }

    private sealed class QueueRow
    {
        public int Id;
        public string Name = "";
        public DateTime EventDateTime;
        public bool AllowSelfJoin;

        /// <summary>Редактируемое локальное представление даты/времени.</summary>
        public string LocalDateTimeText
        {
            get => EventDateTime.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
            set
            {
                if (DateTime.TryParseExact(value, "dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeLocal, out var parsed))
                    EventDateTime = parsed.ToUniversalTime();
            }
        }
    }

    private List<SubjectRow> _subjects = [];
    private List<GroupRow> _groups = [];
    private List<TaskRow> _tasks = [];
    private List<ComponentRow> _components = [];
    private List<QueueRow> _queues = [];
    private bool _loading = true;

    // Поля форм добавления.
    private string _newSubjectName = "";
    private string _newGroupName = "";
    private string _newTaskName = "";
    private string _newTaskDescription = "";
    private int _newTaskMaxPoints = 1;
    private string _newComponentName = "";
    private int _newComponentMin;
    private int _newComponentMax = 5;

    private int _taskSubjectId;
    private int _componentSubjectId;
    private int _linkSubjectId;
    private int _linkGroupId;

    // Очередь — форма создания.
    private int _queueSubjectId;
    private string _queueName = "";
    private DateTime? _queueDate = DateTime.Today;
    private TimeSpan? _queueTime = new(10, 0, 0);
    private int? _autoFillGroupId;
    private bool _allowSelfJoin = true;

    protected override async Task OnInitializedAsync()
    {
        await ReloadAsync();
        _loading = false;
    }

    private async Task ReloadAsync()
    {
        try
        {
            _subjects = (await Subjects.GetAllAsync()).Select(s => new SubjectRow { Id = s.Id, Name = s.Name }).ToList();
            _groups = (await Groups.GetAllAsync()).Select(g => new GroupRow { Id = g.Id, Name = g.Name }).ToList();
            await ReloadQueuesAsync();
        }
        catch (ApiException ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
        }
    }

    private async Task ReloadQueuesAsync()
    {
        _queues = (await QueuesApi.GetAllAsync())
            .Select(q => new QueueRow { Id = q.Id, Name = q.Name, EventDateTime = q.EventDateTime, AllowSelfJoin = q.AllowSelfJoin })
            .ToList();
    }

    // ----- Очереди -----
    private void OnQueueSubjectChanged(int subjectId)
    {
        _queueSubjectId = subjectId;
        var name = _subjects.FirstOrDefault(s => s.Id == subjectId)?.Name;
        if (name is not null)
            _queueName = $"{name} {(_queueDate ?? DateTime.Today):dd.MM}";
    }

    private async Task CreateQueueAsync()
    {
        if (_queueSubjectId == 0) { Snackbar.Add("Выберите предмет.", Severity.Warning); return; }
        if (_queueDate is null || _queueTime is null) { Snackbar.Add("Укажите дату и время.", Severity.Warning); return; }

        var when = DateTime.SpecifyKind(_queueDate.Value.Date + _queueTime.Value, DateTimeKind.Local).ToUniversalTime();
        var name = string.IsNullOrWhiteSpace(_queueName)
            ? $"{_subjects.FirstOrDefault(s => s.Id == _queueSubjectId)?.Name} {_queueDate.Value:dd.MM}"
            : _queueName.Trim();

        try
        {
            await QueuesApi.CreateAsync(new CreateQueueEventRequest(name, when, _queueSubjectId, _allowSelfJoin, _autoFillGroupId));
            Snackbar.Add("Очередь создана.", Severity.Success);
            await ReloadQueuesAsync();
        }
        catch (ApiException ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
        }
    }

    // RowEditCommit в MudTable — синхронный Action<object>; запускаем сохранение фоном.
    private void OnQueueCommit(object element) => _ = RunAsync(
        () => QueuesApi.UpdateAsync(((QueueRow)element).Id, new UpdateQueueEventRequest(((QueueRow)element).Name, ((QueueRow)element).EventDateTime)),
        "Очередь обновлена.", () => Task.CompletedTask);

    private async Task DeleteQueueAsync(QueueRow row) =>
        await RunAsync(() => QueuesApi.DeleteAsync(row.Id), "Очередь удалена.", ReloadQueuesAsync);

    // ----- Предметы -----
    private async Task CreateSubjectAsync()
    {
        if (string.IsNullOrWhiteSpace(_newSubjectName)) return;
        await RunAsync(() => Subjects.CreateAsync(new CreateSubjectRequest(_newSubjectName.Trim())),
            "Предмет создан.", async () => { _newSubjectName = ""; await ReloadAsync(); });
    }

    private void OnSubjectCommit(object element) => _ = RunAsync(
        () => Subjects.UpdateAsync(((SubjectRow)element).Id, new UpdateSubjectRequest(((SubjectRow)element).Name)),
        "Сохранено.", () => Task.CompletedTask);

    private async Task DeleteSubjectAsync(SubjectRow row) =>
        await RunAsync(() => Subjects.DeleteAsync(row.Id), "Предмет удалён.", ReloadAsync);

    // ----- Группы -----
    private async Task CreateGroupAsync()
    {
        if (string.IsNullOrWhiteSpace(_newGroupName)) return;
        await RunAsync(() => Groups.CreateAsync(new CreateGroupRequest(_newGroupName.Trim())),
            "Группа создана.", async () => { _newGroupName = ""; await ReloadAsync(); });
    }

    private void OnGroupCommit(object element) => _ = RunAsync(
        () => Groups.UpdateAsync(((GroupRow)element).Id, new UpdateGroupRequest(((GroupRow)element).Name)),
        "Сохранено.", () => Task.CompletedTask);

    private async Task DeleteGroupAsync(GroupRow row) =>
        await RunAsync(() => Groups.DeleteAsync(row.Id), "Группа удалена.", ReloadAsync);

    // ----- Задачи -----
    private async Task OnTaskSubjectChanged(int subjectId)
    {
        _taskSubjectId = subjectId;
        _tasks = subjectId == 0
            ? []
            : (await Subjects.GetTasksAsync(subjectId))
                .Select(t => new TaskRow { Id = t.Id, Name = t.Name, Description = t.Description, MaxPoints = t.MaxPoints })
                .ToList();
    }

    private async Task CreateTaskAsync()
    {
        if (_taskSubjectId == 0 || string.IsNullOrWhiteSpace(_newTaskName)) return;
        await RunAsync(
            () => Subjects.CreateTaskAsync(_taskSubjectId, new CreateTaskRequest(_newTaskName.Trim(), _newTaskDescription?.Trim() ?? "", _newTaskMaxPoints)),
            "Задача создана.",
            async () => { _newTaskName = ""; _newTaskDescription = ""; _newTaskMaxPoints = 1; await OnTaskSubjectChanged(_taskSubjectId); });
    }

    private void OnTaskCommit(object element)
    {
        var row = (TaskRow)element;
        _ = RunAsync(() => Subjects.UpdateTaskAsync(row.Id, new UpdateTaskRequest(row.Name, row.Description, row.MaxPoints)),
            "Сохранено.", () => Task.CompletedTask);
    }

    private async Task DeleteTaskAsync(TaskRow row) =>
        await RunAsync(() => Subjects.DeleteTaskAsync(row.Id), "Задача удалена.", () => OnTaskSubjectChanged(_taskSubjectId));

    // ----- Оценочные колонки -----
    private async Task OnComponentSubjectChanged(int subjectId)
    {
        _componentSubjectId = subjectId;
        _components = subjectId == 0
            ? []
            : (await Subjects.GetGradeComponentsAsync(subjectId))
                .Select(c => new ComponentRow { Id = c.Id, Name = c.Name, MinPoints = c.MinPoints, MaxPoints = c.MaxPoints })
                .ToList();
    }

    private async Task CreateComponentAsync()
    {
        if (_componentSubjectId == 0 || string.IsNullOrWhiteSpace(_newComponentName)) return;
        await RunAsync(
            () => Subjects.CreateGradeComponentAsync(_componentSubjectId, new CreateGradeComponentRequest(_newComponentName.Trim(), _newComponentMin, _newComponentMax)),
            "Колонка создана.",
            async () => { _newComponentName = ""; _newComponentMin = 0; _newComponentMax = 5; await OnComponentSubjectChanged(_componentSubjectId); });
    }

    private void OnComponentCommit(object element)
    {
        var row = (ComponentRow)element;
        _ = RunAsync(() => Subjects.UpdateGradeComponentAsync(row.Id, new UpdateGradeComponentRequest(row.Name, row.MinPoints, row.MaxPoints)),
            "Сохранено.", () => Task.CompletedTask);
    }

    private async Task DeleteComponentAsync(ComponentRow row) =>
        await RunAsync(() => Grades.DeleteComponentAsync(row.Id), "Колонка удалена.", () => OnComponentSubjectChanged(_componentSubjectId));

    // ----- Связи -----
    private async Task LinkAsync()
    {
        if (_linkSubjectId == 0 || _linkGroupId == 0) { Snackbar.Add("Выберите предмет и группу.", Severity.Warning); return; }
        await RunAsync(() => Groups.LinkSubjectAsync(_linkGroupId, _linkSubjectId), "Связано.", () => Task.CompletedTask);
    }

    private async Task UnlinkAsync()
    {
        if (_linkSubjectId == 0 || _linkGroupId == 0) { Snackbar.Add("Выберите предмет и группу.", Severity.Warning); return; }
        await RunAsync(() => Groups.UnlinkSubjectAsync(_linkGroupId, _linkSubjectId), "Связь снята.", () => Task.CompletedTask);
    }

    private async Task RunAsync(Func<Task> action, string success, Func<Task> after)
    {
        try
        {
            await action();
            await after();
            Snackbar.Add(success, Severity.Success);
        }
        catch (ApiException ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
        }
    }
}
