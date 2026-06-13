using Frontend.Shared.Api;
using Frontend.Shared.Models;
using Frontend.Shared.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using MudBlazor;

namespace Frontend.Student.Pages;

public partial class Queues : IAsyncDisposable
{
    [Inject] private MeApi Me { get; set; } = null!;
    [Inject] private QueuesApi QueuesApi { get; set; } = null!;
    [Inject] private AuthService Auth { get; set; } = null!;
    [Inject] private NavigationManager Nav { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    private List<QueueEventDto> _myEvents = [];
    private List<QueueEventDto> _availableEvents = [];
    private QueueDetailsDto? _details;
    private List<QueueParticipantDto> _participants = [];
    private bool _loading = true;

    private HubConnection? _hub;

    protected override async Task OnInitializedAsync() => await LoadAllAsync();

    private async Task LoadAllAsync()
    {
        _loading = true;
        try
        {
            var mine = await Me.GetMyQueueEventsAsync();
            var available = await Me.GetAvailableQueueEventsAsync();

            _myEvents = mine;
            var joinedIds = mine.Select(e => e.Id).ToHashSet();
            _availableEvents = available.Where(e => !joinedIds.Contains(e.Id)).ToList();
        }
        catch (ApiException ex)
        {
            Snackbar.Add($"Не удалось загрузить очереди: {ex.Message}", Severity.Error);
        }
        finally
        {
            _loading = false;
        }
    }

    private async Task JoinAsync(int eventId)
    {
        try
        {
            await QueuesApi.JoinAsync(eventId);
            Snackbar.Add("Вы записались в очередь.", Severity.Success);
            await LoadAllAsync();
        }
        catch (ApiException ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
        }
    }

    private async Task ViewQueueAsync(int eventId)
    {
        try
        {
            await LoadDetailsAsync(eventId);
            await InitializeSignalRAsync(eventId);
        }
        catch (ApiException ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
        }
    }

    private async Task LoadDetailsAsync(int eventId)
    {
        _details = await QueuesApi.GetDetailsAsync(eventId);
        _participants = _details.Participants
            .OrderBy(p => p.Status)
            .ThenByDescending(p => p.TotalPoints)
            .ToList();
        StateHasChanged();
    }

    private async Task InitializeSignalRAsync(int eventId)
    {
        await DisposeHubAsync();

        _hub = new HubConnectionBuilder()
            .WithUrl(Nav.ToAbsoluteUri("/apiHub/queueHub"), options =>
                options.AccessTokenProvider = async () => await Auth.GetTokenAsync())
            .WithAutomaticReconnect()
            .Build();

        // На любое обновление очереди перечитываем её состав.
        _hub.On<QueueEntryUpdateDto>("QueueEntryUpdated", _ => InvokeAsync(() => LoadDetailsAsync(eventId)));

        // После переподключения подписка на группу теряется — переподписываемся и обновляем данные,
        // иначе очередь «зависает» и перестаёт обновляться.
        _hub.Reconnected += async _ =>
        {
            await _hub.InvokeAsync("SubscribeToQueue", eventId);
            await InvokeAsync(() => LoadDetailsAsync(eventId));
        };

        try
        {
            await _hub.StartAsync();
            await _hub.InvokeAsync("SubscribeToQueue", eventId);
        }
        catch (Exception ex)
        {
            Snackbar.Add($"Не удалось подключиться к обновлениям: {ex.Message}", Severity.Warning);
        }
    }

    private void BackToList()
    {
        _details = null;
        _ = DisposeHubAsync();
    }

    private static Color StatusColor(QueueEntryStatus status) => status switch
    {
        QueueEntryStatus.Checking => Color.Default,
        QueueEntryStatus.Skipped => Color.Warning,
        QueueEntryStatus.Waiting => Color.Info,
        QueueEntryStatus.Finished => Color.Success,
        _ => Color.Error,
    };

    private static string StatusText(QueueEntryStatus status, string? adminName) => status switch
    {
        QueueEntryStatus.Waiting => "В очереди",
        QueueEntryStatus.Skipped => "Пропущен",
        QueueEntryStatus.Checking => $"Сдаёт ({adminName ?? "админ"})",
        QueueEntryStatus.Finished => "Завершил",
        _ => "Неизвестно",
    };

    public async ValueTask DisposeAsync() => await DisposeHubAsync();

    private async Task DisposeHubAsync()
    {
        if (_hub is not null)
        {
            await _hub.DisposeAsync();
            _hub = null;
        }
    }
}
