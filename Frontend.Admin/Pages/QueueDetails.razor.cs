using Frontend.Admin.Dialogs;
using Frontend.Shared.Api;
using Frontend.Shared.Models;
using Frontend.Shared.Services;
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
    private ParticipantSort _sort = ParticipantSort.Status;

    public enum ParticipantSort { Status, Group, Points, JoinedAt }

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

        items = _sort switch
        {
            ParticipantSort.Group => items.OrderBy(p => p.GroupName).ThenBy(p => p.LastName),
            ParticipantSort.Points => items.OrderByDescending(p => p.TotalPoints),
            ParticipantSort.JoinedAt => items.OrderBy(p => p.JoinedAt),
            _ => items.OrderBy(p => p.Status).ThenByDescending(p => p.TotalPoints),
        };

        return items.ToList();
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
            await _hub.InvokeAsync("SubscribeToQueue", EventId);
            await InvokeAsync(LoadAsync);
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

        var parameters = new DialogParameters<StudentInteractionDialog>
        {
            { x => x.Student, participant },
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

    private static string StatusText(QueueEntryStatus status, string? adminName) => status switch
    {
        QueueEntryStatus.Waiting => "В очереди",
        QueueEntryStatus.Skipped => "Пропущен",
        QueueEntryStatus.Checking => $"Сдаёт ({adminName ?? "админ"})",
        QueueEntryStatus.Finished => "Завершил",
        _ => "Неизвестно",
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
