using Frontend.Shared.Api;
using Frontend.Shared.Models;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Frontend.Admin.Dialogs;

public partial class StudentInteractionDialog
{
    [CascadingParameter] private IMudDialogInstance MudDialog { get; set; } = null!;

    [Parameter] public QueueParticipantDto Student { get; set; } = null!;
    [Parameter] public int EventId { get; set; }
    [Parameter] public int SubjectId { get; set; }

    [Inject] private StudentsApi Students { get; set; } = null!;
    [Inject] private SubmissionsApi Submissions { get; set; } = null!;
    [Inject] private QueuesApi Queues { get; set; } = null!;
    [Inject] private IDialogService DialogService { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    private List<TaskWithStatusDto> _tasks = [];
    private QueueEntryStatus _status;
    private bool _loadingTasks = true;

    protected override async Task OnInitializedAsync()
    {
        _status = Student.Status;
        await LoadTasksAsync();
    }

    private async Task LoadTasksAsync()
    {
        _loadingTasks = true;
        try
        {
            _tasks = await Students.GetTasksWithStatusAsync(Student.StudentId, SubjectId);
        }
        catch (ApiException ex)
        {
            Snackbar.Add($"Не удалось загрузить задачи: {ex.Message}", Severity.Error);
        }
        finally
        {
            _loadingTasks = false;
        }
    }

    private async Task ChangeStatusAsync(QueueEntryStatus status)
    {
        try
        {
            await Queues.UpdateStatusAsync(EventId, Student.StudentId, new UpdateQueueStatusRequest(status));
            _status = status;
            Snackbar.Add("Статус обновлён.", Severity.Success);

            // Завершение работы со студентом закрывает диалог и обновляет очередь.
            if (status != QueueEntryStatus.Checking)
                MudDialog.Close(DialogResult.Ok(true));
        }
        catch (ApiException ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
        }
    }

    private async Task ReworkAsync(int taskId)
    {
        var dialog = await DialogService.ShowAsync<CommentDialog>("Комментарий");
        var result = await dialog.Result;
        if (result is { Canceled: false, Data: string comment })
            await SubmitAsync(taskId, SubmissionStatus.Rejected, comment);
    }

    private async Task SubmitAsync(int taskId, SubmissionStatus status, string? comment = null)
    {
        try
        {
            await Submissions.CreateAsync(new CreateSubmissionRequest(Student.StudentId, taskId, status, comment));
            Snackbar.Add("Статус задачи обновлён.", Severity.Success);
            await LoadTasksAsync();
        }
        catch (ApiException ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
        }
    }

    private void Cancel() => MudDialog.Close(DialogResult.Cancel());

    private static string StatusText(SubmissionStatus status) => status switch
    {
        SubmissionStatus.Rejected => "На доработке",
        SubmissionStatus.Accepted => "Зачтено",
        _ => "Не сдано",
    };

    private static Color StatusColor(SubmissionStatus status) => status switch
    {
        SubmissionStatus.Rejected => Color.Warning,
        SubmissionStatus.Accepted => Color.Success,
        _ => Color.Default,
    };
}
