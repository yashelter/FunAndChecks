using System.Globalization;
using Frontend.Admin.Dialogs;
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
    [Inject] private MeApi Me { get; set; } = null!;
    [Inject] private IDialogService DialogService { get; set; } = null!;
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

    // Очередь — форма создания.
    private int? _queueSubjectId;
    private string _queueName = "";
    private DateTime? _queueDate = DateTime.Today;
    private TimeSpan? _queueTime = new(10, 0, 0);
    private IReadOnlyCollection<int> _autoFillGroupIds = new HashSet<int>();
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

            var access = await Me.GetMyAccessAsync();
            _hiddenSubjects = [.. access.HiddenSubjectIds];
            _hiddenGroups = [.. access.HiddenGroupIds];
            _restrictedSubjects = [.. access.RestrictedSubjectIds];
            _restrictedGroups = [.. access.RestrictedGroupIds];

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
    private void OnQueueSubjectChanged(int? subjectId)
    {
        _queueSubjectId = subjectId;
        var name = _subjects.FirstOrDefault(s => s.Id == subjectId)?.Name;
        if (name is not null)
            _queueName = $"{name} {(_queueDate ?? DateTime.Today):dd.MM}";
    }

    private async Task CreateQueueAsync()
    {
        if (_queueSubjectId is null) { Snackbar.Add("Выберите предмет.", Severity.Warning); return; }
        if (_queueDate is null || _queueTime is null) { Snackbar.Add("Укажите дату и время.", Severity.Warning); return; }

        var when = DateTime.SpecifyKind(_queueDate.Value.Date + _queueTime.Value, DateTimeKind.Local).ToUniversalTime();
        var name = string.IsNullOrWhiteSpace(_queueName)
            ? $"{_subjects.FirstOrDefault(s => s.Id == _queueSubjectId)?.Name} {_queueDate.Value:dd.MM}"
            : _queueName.Trim();

        var autoFill = _autoFillGroupIds.Count > 0 ? _autoFillGroupIds.ToList() : null;

        try
        {
            await QueuesApi.CreateAsync(new CreateQueueEventRequest(name, when, _queueSubjectId.Value, _allowSelfJoin, autoFill));
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

        await RunAsync(
            async () =>
            {
                var subject = await Subjects.CreateAsync(new CreateSubjectRequest(_newSubjectName.Trim()));
                foreach (var groupId in _newSubjectGroupIds)
                    await Groups.LinkSubjectAsync(groupId, subject.Id);
            },
            "Предмет создан.",
            async () => { _newSubjectName = ""; _newSubjectGroupIds = new HashSet<int>(); await ReloadAsync(); });
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

        await RunAsync(
            async () =>
            {
                var group = await Groups.CreateAsync(new CreateGroupRequest(_newGroupName.Trim()));
                foreach (var subjectId in _newGroupSubjectIds)
                    await Groups.LinkSubjectAsync(group.Id, subjectId);
            },
            "Группа создана.",
            async () => { _newGroupName = ""; _newGroupSubjectIds = new HashSet<int>(); await ReloadAsync(); });
    }

    private void OnGroupCommit(object element) => _ = RunAsync(
        () => Groups.UpdateAsync(((GroupRow)element).Id, new UpdateGroupRequest(((GroupRow)element).Name)),
        "Сохранено.", () => Task.CompletedTask);

    private async Task DeleteGroupAsync(GroupRow row) =>
        await RunAsync(() => Groups.DeleteAsync(row.Id), "Группа удалена.", ReloadAsync);

    // ----- Задачи -----
    private async Task OnTaskSubjectChanged(int? subjectId)
    {
        _taskSubjectId = subjectId;
        _tasks = subjectId is null
            ? []
            : (await Subjects.GetTasksAsync(subjectId.Value))
                .Select(t => new TaskRow { Id = t.Id, Name = t.Name, Description = t.Description, MaxPoints = t.MaxPoints })
                .ToList();
    }

    private async Task CreateTaskAsync()
    {
        if (_taskSubjectId is null || string.IsNullOrWhiteSpace(_newTaskName)) return;
        await RunAsync(
            () => Subjects.CreateTaskAsync(_taskSubjectId.Value, new CreateTaskRequest(_newTaskName.Trim(), _newTaskDescription?.Trim() ?? "", _newTaskMaxPoints)),
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
    private async Task OnComponentSubjectChanged(int? subjectId)
    {
        _componentSubjectId = subjectId;
        _components = subjectId is null
            ? []
            : (await Subjects.GetGradeComponentsAsync(subjectId.Value))
                .Select(c => new ComponentRow { Id = c.Id, Name = c.Name, MinPoints = c.MinPoints, MaxPoints = c.MaxPoints })
                .ToList();
    }

    private async Task CreateComponentAsync()
    {
        if (_componentSubjectId is null || string.IsNullOrWhiteSpace(_newComponentName)) return;
        await RunAsync(
            () => Subjects.CreateGradeComponentAsync(_componentSubjectId.Value, new CreateGradeComponentRequest(_newComponentName.Trim(), _newComponentMin, _newComponentMax)),
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

    // ----- Доступ (связи группа↔предмет) -----
    private async Task OpenSubjectAccessAsync(SubjectRow subject)
    {
        var current = await Groups.GetGroupIdsForSubjectAsync(subject.Id);
        var options = _groups.Select(g => new EntityLinkDialog.LinkOption(g.Id, g.Name)).ToList();
        var selected = await ShowAccessDialogAsync($"Доступ к предмету «{subject.Name}»", options, current);
        if (selected is null)
            return;

        await ApplyAccessDiffAsync(current, selected,
            groupId => Groups.LinkSubjectAsync(groupId, subject.Id),
            groupId => Groups.UnlinkSubjectAsync(groupId, subject.Id));
    }

    private async Task OpenGroupAccessAsync(GroupRow group)
    {
        var current = await Groups.GetSubjectIdsAsync(group.Id);
        var options = _subjects.Select(s => new EntityLinkDialog.LinkOption(s.Id, s.Name)).ToList();
        var selected = await ShowAccessDialogAsync($"Предметы группы «{group.Name}»", options, current);
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
            Snackbar.Add("Доступ обновлён.", Severity.Success);
        }
        catch (ApiException ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
        }
    }

    // ----- Видимость / архив (скрытие самим админом) -----
    private bool IsSubjectArchived(int id) => _hiddenSubjects.Contains(id);
    private bool IsSubjectRestricted(int id) => _restrictedSubjects.Contains(id);
    private bool IsGroupArchived(int id) => _hiddenGroups.Contains(id);
    private bool IsGroupRestricted(int id) => _restrictedGroups.Contains(id);

    private async Task SetSubjectArchivedAsync(int subjectId, bool hidden)
    {
        await RunAsync(
            () => Me.SetSubjectHiddenAsync(subjectId, hidden),
            hidden ? "Предмет в архиве." : "Предмет восстановлен.",
            () => { if (hidden) _hiddenSubjects.Add(subjectId); else _hiddenSubjects.Remove(subjectId); return Task.CompletedTask; });
    }

    private async Task SetGroupArchivedAsync(int groupId, bool hidden)
    {
        await RunAsync(
            () => Me.SetGroupHiddenAsync(groupId, hidden),
            hidden ? "Группа в архиве." : "Группа восстановлена.",
            () => { if (hidden) _hiddenGroups.Add(groupId); else _hiddenGroups.Remove(groupId); return Task.CompletedTask; });
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
