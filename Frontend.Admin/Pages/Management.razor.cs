using Frontend.Shared.Api;
using Frontend.Shared.Models;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Frontend.Admin.Pages;

public partial class Management
{
    [Inject] private SubjectsApi Subjects { get; set; } = null!;
    [Inject] private GroupsApi Groups { get; set; } = null!;
    [Inject] private QueuesApi QueuesApi { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    private List<SubjectDto> _subjects = [];
    private List<GroupDto> _groups = [];
    private bool _loading = true;

    private string _subjectName = string.Empty;
    private string _groupName = string.Empty;

    private int _taskSubjectId;
    private string _taskName = string.Empty;
    private string _taskDescription = string.Empty;
    private int _taskMaxPoints = 1;

    private int _queueSubjectId;
    private string _queueName = string.Empty;
    private DateTime? _queueDate = DateTime.Today;
    private TimeSpan? _queueTime = new(10, 0, 0);

    private int _componentSubjectId;
    private string _componentName = string.Empty;
    private int _componentMaxPoints = 1;

    private int _linkSubjectId;
    private int _linkGroupId;

    protected override async Task OnInitializedAsync()
    {
        await ReloadCatalogAsync();
        _loading = false;
    }

    private async Task ReloadCatalogAsync()
    {
        try
        {
            _subjects = await Subjects.GetAllAsync();
            _groups = await Groups.GetAllAsync();
        }
        catch (ApiException ex)
        {
            Snackbar.Add($"Ошибка загрузки данных: {ex.Message}", Severity.Error);
        }
    }

    private async Task CreateSubjectAsync()
    {
        if (string.IsNullOrWhiteSpace(_subjectName)) { Snackbar.Add("Укажите название предмета.", Severity.Warning); return; }
        await RunAsync(async () =>
        {
            await Subjects.CreateAsync(new CreateSubjectRequest(_subjectName.Trim()));
            _subjectName = string.Empty;
            await ReloadCatalogAsync();
        }, "Предмет создан.");
    }

    private async Task CreateGroupAsync()
    {
        if (string.IsNullOrWhiteSpace(_groupName)) { Snackbar.Add("Укажите название группы.", Severity.Warning); return; }
        await RunAsync(async () =>
        {
            await Groups.CreateAsync(new CreateGroupRequest(_groupName.Trim()));
            _groupName = string.Empty;
            await ReloadCatalogAsync();
        }, "Группа создана.");
    }

    private async Task CreateTaskAsync()
    {
        if (_taskSubjectId == 0 || string.IsNullOrWhiteSpace(_taskName)) { Snackbar.Add("Заполните предмет и название задачи.", Severity.Warning); return; }
        await RunAsync(async () =>
        {
            await Subjects.CreateTaskAsync(_taskSubjectId, new CreateTaskRequest(_taskName.Trim(), _taskDescription?.Trim() ?? string.Empty, _taskMaxPoints));
            _taskName = string.Empty;
            _taskDescription = string.Empty;
            _taskMaxPoints = 1;
        }, "Задача создана.");
    }

    private async Task CreateQueueAsync()
    {
        if (_queueSubjectId == 0 || string.IsNullOrWhiteSpace(_queueName)) { Snackbar.Add("Заполните предмет и название события.", Severity.Warning); return; }
        if (_queueDate is null || _queueTime is null) { Snackbar.Add("Укажите дату и время.", Severity.Warning); return; }

        var when = DateTime.SpecifyKind(_queueDate.Value.Date + _queueTime.Value, DateTimeKind.Local).ToUniversalTime();
        await RunAsync(async () =>
        {
            await QueuesApi.CreateAsync(new CreateQueueEventRequest(_queueName.Trim(), when, _queueSubjectId));
            _queueName = string.Empty;
        }, "Событие создано.");
    }

    private async Task CreateComponentAsync()
    {
        if (_componentSubjectId == 0 || string.IsNullOrWhiteSpace(_componentName)) { Snackbar.Add("Заполните предмет и название колонки.", Severity.Warning); return; }
        await RunAsync(async () =>
        {
            await Subjects.CreateGradeComponentAsync(_componentSubjectId, new CreateGradeComponentRequest(_componentName.Trim(), _componentMaxPoints));
            _componentName = string.Empty;
            _componentMaxPoints = 1;
        }, "Оценочная колонка создана.");
    }

    private async Task LinkAsync()
    {
        if (_linkSubjectId == 0 || _linkGroupId == 0) { Snackbar.Add("Выберите предмет и группу.", Severity.Warning); return; }
        await RunAsync(async () =>
        {
            await Groups.LinkSubjectAsync(_linkGroupId, _linkSubjectId);
        }, "Группа привязана к предмету.");
    }

    private async Task RunAsync(Func<Task> action, string successMessage)
    {
        try
        {
            await action();
            Snackbar.Add(successMessage, Severity.Success);
        }
        catch (ApiException ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
        }
    }
}
