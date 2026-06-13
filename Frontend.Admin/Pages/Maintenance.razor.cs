using Frontend.Shared.Api;
using Frontend.Shared.Models;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Frontend.Admin.Pages;

public partial class Maintenance
{
    [Inject] private SubjectsApi Subjects { get; set; } = null!;
    [Inject] private GroupsApi Groups { get; set; } = null!;
    [Inject] private BackupApi Backup { get; set; } = null!;
    [Inject] private IDialogService DialogService { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    private List<SubjectDto> _subjects = [];
    private List<GroupDto> _groups = [];
    private List<TaskDto> _tasks = [];
    private bool _loading = true;

    private int _subjectToDelete;
    private int _groupToDelete;
    private int _taskToDelete;
    private int _unlinkSubjectId;
    private int _unlinkGroupId;

    private bool _backupRunning;
    private string? _lastBackupPath;

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
            Snackbar.Add(ex.Message, Severity.Error);
        }
    }

    private async Task OnTaskSubjectChangedAsync(int subjectId)
    {
        _taskToDelete = 0;
        _tasks = [];
        if (subjectId == 0)
            return;

        try
        {
            _tasks = await Subjects.GetTasksAsync(subjectId);
        }
        catch (ApiException ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
        }
    }

    private async Task DeleteSubjectAsync()
    {
        var name = _subjects.FirstOrDefault(s => s.Id == _subjectToDelete)?.Name;
        if (!await ConfirmAsync($"Удалить предмет «{name}» со всеми задачами и историей?"))
            return;

        await RunAsync(async () =>
        {
            await Subjects.DeleteAsync(_subjectToDelete);
            _subjectToDelete = 0;
            await ReloadCatalogAsync();
        }, "Предмет удалён.");
    }

    private async Task DeleteGroupAsync()
    {
        var name = _groups.FirstOrDefault(g => g.Id == _groupToDelete)?.Name;
        if (!await ConfirmAsync($"Удалить группу «{name}»?"))
            return;

        await RunAsync(async () =>
        {
            await Groups.DeleteAsync(_groupToDelete);
            _groupToDelete = 0;
            await ReloadCatalogAsync();
        }, "Группа удалена.");
    }

    private async Task DeleteTaskAsync()
    {
        var name = _tasks.FirstOrDefault(t => t.Id == _taskToDelete)?.Name;
        if (!await ConfirmAsync($"Удалить задачу «{name}» со всей историей сдач?"))
            return;

        await RunAsync(async () =>
        {
            await Subjects.DeleteTaskAsync(_taskToDelete);
            _tasks.RemoveAll(t => t.Id == _taskToDelete);
            _taskToDelete = 0;
        }, "Задача удалена.");
    }

    private async Task UnlinkAsync()
    {
        if (!await ConfirmAsync("Отозвать доступ группы к предмету?"))
            return;

        await RunAsync(async () =>
        {
            await Groups.UnlinkSubjectAsync(_unlinkGroupId, _unlinkSubjectId);
            _unlinkGroupId = 0;
            _unlinkSubjectId = 0;
        }, "Доступ отозван.");
    }

    private async Task BackupAsync()
    {
        _backupRunning = true;
        try
        {
            var result = await Backup.CreateAsync();
            _lastBackupPath = result.Path;
            Snackbar.Add("Резервная копия создана.", Severity.Success);
        }
        catch (ApiException ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
        }
        finally
        {
            _backupRunning = false;
        }
    }

    private async Task<bool> ConfirmAsync(string message)
    {
        var result = await DialogService.ShowMessageBox("Подтверждение", message, yesText: "Да", cancelText: "Отмена");
        return result == true;
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
