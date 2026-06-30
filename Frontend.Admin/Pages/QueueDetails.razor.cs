using Frontend.Admin.Dialogs;
using Frontend.Shared.Api;
using Frontend.Shared.Models;
using Frontend.Shared.Services;
using Frontend.Shared.UI;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using MudBlazor;

namespace Frontend.Admin.Pages;

public partial class QueueDetails : IAsyncDisposable
{
    [Parameter] public int EventId { get; set; }

    [Inject] private QueuesApi Queues { get; set; } = null!;
    [Inject] private AuthService Auth { get; set; } = null!;
    [Inject] private NavigationManager Nav { get; set; } = null!;
    [Inject] private IDialogService DialogService { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    private QueueDetailsDto? _details;
    private List<QueueParticipantDto> _participants = [];
    private bool _loading = true;
    private HubConnection? _hub;

    private string? _filter;
    private ParticipantSort _sort = ParticipantSort.Points;

    public enum ParticipantSort { Group, Points, JoinedAt }

    protected override async Task OnInitializedAsync()
    {
        await LoadAsync();
        await InitializeSignalRAsync();
    }

    private List<QueueParticipantDto> Visible => VisibleParticipants();

    private List<QueueParticipantDto> VisibleParticipants()
    {
        IEnumerable<QueueParticipantDto> items = _participants;

        if (!string.IsNullOrWhiteSpace(_filter))
        {
            var term = _filter.Trim();
            items = items.Where(p =>
                p.FullName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                p.GroupName.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        // Статус — всегда первичный ключ сортировки (сдают → в очереди → пропущенные → завершившие),
        // выбранное поле — вторичное.
        var ordered = items.OrderBy(p => QueueStyles.StatusOrder(p.Status));
        ordered = _sort switch
        {
            ParticipantSort.Group => ordered.ThenBy(p => p.GroupName).ThenBy(p => p.LastName),
            ParticipantSort.JoinedAt => ordered.ThenBy(p => p.JoinedAt),
            _ => ordered.ThenByDescending(p => p.TotalPoints),
        };

        return ordered.ToList();
    }

    private async Task LoadAsync()
    {
        try
        {
            _details = await Queues.GetDetailsAsync(EventId);
            _participants = _details.Participants.ToList();
        }
        catch (ApiException ex)
        {
            Snackbar.Add($"Ошибка загрузки очереди: {ex.Message}", Severity.Error);
        }
        finally
        {
            _loading = false;
            StateHasChanged();
        }
    }

    private async Task InitializeSignalRAsync()
    {
        _hub = new HubConnectionBuilder()
            .WithUrl(Nav.ToAbsoluteUri("/apiHub/queueHub"), options =>
                options.AccessTokenProvider = async () => await Auth.GetTokenAsync())
            .WithAutomaticReconnect()
            .Build();

        _hub.On<QueueEntryUpdateDto>("QueueEntryUpdated", _ => InvokeAsync(LoadAsync));

        // После переподключения переподписываемся, иначе обновления перестают приходить.
        _hub.Reconnected += async _ =>
        {
            try
            {
                await _hub.InvokeAsync("SubscribeToQueue", EventId);
                await InvokeAsync(LoadAsync);
            }
            catch (Exception ex)
            {
                Snackbar.Add($"Ошибка при переподключении: {ex.Message}", Severity.Error);
            }
        };

        try
        {
            await _hub.StartAsync();
            await _hub.InvokeAsync("SubscribeToQueue", EventId);
        }
        catch (Exception ex)
        {
            Snackbar.Add($"Не удалось подключиться к обновлениям: {ex.Message}", Severity.Warning);
        }
    }

    private async Task OpenStudentAsync(QueueParticipantDto participant)
    {
        if (_details is null)
            return;

        // Клик по студенту = начать приём: сразу ставим статус «Сдаёт».
        if (participant.Status != QueueEntryStatus.Checking)
        {
            try
            {
                await Queues.UpdateStatusAsync(EventId, participant.StudentId, new UpdateQueueStatusRequest(QueueEntryStatus.Checking));
                await LoadAsync();
                participant = _participants.FirstOrDefault(p => p.StudentId == participant.StudentId) ?? participant;
            }
            catch (ApiException ex)
            {
                Snackbar.Add(ex.Message, Severity.Error);
            }
        }

        var parameters = new DialogParameters<StudentInteractionDialog>
        {
            { x => x.StudentId, participant.StudentId },
            { x => x.StudentName, $"{participant.LastName} {participant.FirstName}" },
            { x => x.GroupName, participant.GroupName },
            { x => x.EventId, EventId },
            { x => x.SubjectId, _details.SubjectId },
        };

        var dialog = await DialogService.ShowAsync<StudentInteractionDialog>(
            "Работа со студентом",
            parameters,
            new DialogOptions { MaxWidth = MaxWidth.Medium, FullWidth = true });

        var result = await dialog.Result;
        if (result is { Canceled: false })
            await LoadAsync();
    }

    private static Color StatusColor(QueueEntryStatus status) => status switch
    {
        QueueEntryStatus.Checking => Color.Info,
        QueueEntryStatus.Skipped => Color.Error,
        QueueEntryStatus.Waiting => Color.Warning,
        QueueEntryStatus.Finished => Color.Success,
        _ => Color.Default,
    };

    public async ValueTask DisposeAsync()
    {
        if (_hub is not null)
        {
            try
            {
                if (_hub.State == HubConnectionState.Connected)
                    await _hub.InvokeAsync("UnsubscribeFromQueue", EventId);
            }
            catch
            {
                // соединение могло уже закрыться
            }

            await _hub.DisposeAsync();
        }
    }
}
