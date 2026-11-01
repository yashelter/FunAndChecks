using Frontend.Shared.UI;
using Frontend.Shared.Api;
using Frontend.Shared.Models;
using Frontend.Shared.Resources;
using Frontend.Shared.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Localization;
using MudBlazor;

namespace Frontend.Student.Pages;

public partial class Queues : IAsyncDisposable
{
    [Inject] private MeApi Me { get; set; } = null!;
    [Inject] private QueuesApi QueuesApi { get; set; } = null!;
    [Inject] private AuthService Auth { get; set; } = null!;
    [Inject] private NavigationManager Nav { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;
    [Inject] private ConfirmService Confirm { get; set; } = null!;
    [Inject] private IStringLocalizer<AppStrings> Loc { get; set; } = null!;
    [Inject] private Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider Authentication { get; set; } = null!;
    [Inject] private Frontend.Shared.Seasons.SeasonalThemes Seasons { get; set; } = null!;

    /// <summary>Открытая очередь — в адресе: «назад» в браузере и обновление страницы её не теряют.</summary>
    [Parameter] public int? EventId { get; set; }
    private Guid? _myId;

    /// <summary>Своя запись — последний круг; прошлые круги остаются в списке историей.</summary>
    private QueueParticipantDto? MyEntry => _participants.Where(p => p.StudentId == _myId).MaxBy(p => p.Round);

    /// <summary>
    /// Ожидающие в порядке вызова: первый круг — больше баллов раньше, при равенстве — кто раньше записался;
    /// повторно вставшие — после всех, по времени повторного вступления.
    /// </summary>
    private List<QueueParticipantDto> Waiting => QueueStyles.ByStatusThenRejoin(_participants.Where(p => p.Status == QueueEntryStatus.Waiting))
        .ThenByDescending(p => p.TotalPoints).ThenBy(p => p.JoinedAt).ToList();

    private int? MyPosition => Waiting.FindIndex(p => p.StudentId == _myId) is var index and >= 0 ? index + 1 : null;

    private IEnumerable<QueueParticipantDto> Ordered => QueueStyles.ByStatusThenRejoin(_participants)
        .ThenByDescending(x => x.TotalPoints).ThenBy(x => x.JoinedAt).ThenBy(x => x.Round);

    private List<QueueEventDto> _myEvents = [];
    private List<QueueEventDto> _availableEvents = [];
    private QueueDetailsDto? _details;
    private List<QueueParticipantDto> _participants = [];
    private bool _loading = true;
    private bool _showPast;

    // Single persistent hub for both list page and detail view
    private HubConnection? _listHub;
    private readonly List<IDisposable> _listHubHandlers = [];
    private readonly SemaphoreSlim _listHubLock = new(1, 1);
    private int? _viewedEventId;
    private bool _disposed;
    private long _colorRevision;
    private readonly Dictionary<Guid, (long Revision, string? Color)> _latestColorChanges = [];

    protected override async Task OnInitializedAsync()
    {
        var user = (await Authentication.GetAuthenticationStateAsync()).User;
        if (Guid.TryParse(user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var id)) _myId = id;
        await LoadAllAsync();
        await InitializeListHubAsync();
    }

    private int? _shownEvent = -1;

    protected override async Task OnParametersSetAsync()
    {
        if (_shownEvent == EventId) return;
        _shownEvent = EventId;
        if (EventId is int eventId)
        {
            _loading = true;
            await ViewQueueAsync(eventId);
            // Очередь по ссылке не открылась (удалена, нет доступа) — показываем список, а не пустую страницу.
            if (_details is null) await LoadAllAsync();
            _loading = false;
        }
        else
        {
            await CloseDetailsAsync();
            await LoadAllAsync();
        }
    }

    private async Task OnShowPastChangedAsync(bool value)
    {
        _showPast = value;
        await LoadAllAsync();
        if (_listHub?.State == HubConnectionState.Connected)
        {
            await SubscribeToAllQueueGroupsAsync();
        }
    }

    private async Task LoadAllAsync()
    {
        _loading = true;
        try
        {
            var mine = await Me.GetMyQueueEventsAsync(_showPast);
            var available = await Me.GetAvailableQueueEventsAsync(_showPast);

            _myEvents = mine;
            var joinedIds = mine.Select(e => e.Id).ToHashSet();
            _availableEvents = available.Where(e => !joinedIds.Contains(e.Id)).ToList();
        }
        catch (ApiException ex)
        {
            Snackbar.Add(string.Format(Loc["StudentQueues_LoadError"], ex.Message), Severity.Error);
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
            Snackbar.Add(Loc["StudentQueues_Joined"], Severity.Success);
            // Сразу в очередь: студенту важно видеть своё место, а не список событий.
            Nav.NavigateTo($"/student/queues/{eventId}");
        }
        catch (ApiException ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
        }
    }

    /// <summary>Сдавший или пропущенный встаёт снова — новым кругом в конец ожидающих.</summary>
    private async Task RejoinAsync()
    {
        if (_viewedEventId is not int eventId) return;
        if (!await Confirm.ConfirmAsync(Loc["StudentQueues_RejoinTitle"], Loc["StudentQueues_RejoinText"], Loc["StudentQueues_Rejoin"])) return;
        try
        {
            await QueuesApi.JoinAsync(eventId);
            Snackbar.Add(Loc["StudentQueues_Joined"], Severity.Success);
            await LoadDetailsAsync(eventId);
        }
        catch (ApiException ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
            await LoadDetailsAsync(eventId);
        }
    }

    private async Task ViewQueueAsync(int eventId)
    {
        if (_disposed)
            return;
        if (_viewedEventId != eventId)
            _latestColorChanges.Clear();
        _viewedEventId = eventId;

        // Add detail-view handlers to the shared hub
        if (_listHub?.State == HubConnectionState.Connected)
        {
            var handler = _listHub.On<QueueEntryUpdateDto>("QueueEntryUpdated", _ => InvokeAsync(() => LoadDetailsAsync(eventId)));
            _listHubHandlers.Add(handler);
            await _listHub.InvokeAsync("SubscribeToQueue", eventId);
        }

        try
        {
            await LoadDetailsAsync(eventId);
        }
        catch (ApiException ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
        }
    }

    // Вызывается в том числе из SignalR-колбэков — любые сбои гасим здесь,
    // иначе необработанное исключение уронит весь компонент.
    private async Task LoadDetailsAsync(int eventId)
    {
        try
        {
            if (_disposed || _viewedEventId != eventId)
                return;
            var colorRevision = _colorRevision;
            var details = await QueuesApi.GetDetailsAsync(eventId);
            if (_disposed || _viewedEventId != eventId)
                return;
            _participants = details.Participants
                .Select(participant => _latestColorChanges.TryGetValue(participant.StudentId, out var change) && change.Revision > colorRevision
                    ? participant with { StudentColor = change.Color }
                    : participant)
                .ToList();
            _details = details with { Participants = _participants };
            StateHasChanged();
        }
        catch (ApiException ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
        }
    }

    // ========== Persistent hub for list page + detail view ==========

    private async Task InitializeListHubAsync()
    {
        await _listHubLock.WaitAsync();
        try
        {
            await DisposeListHubAsync();

            _listHub = new HubConnectionBuilder()
                .WithUrl(Nav.ToAbsoluteUri("/apiHub/queueHub"), options =>
                    options.AccessTokenProvider = async () => await Auth.GetTokenAsync())
                .WithAutomaticReconnect()
                .Build();

            // Handler for settings changes (AllowSelfJoin toggle) - always active
            _listHubHandlers.Add(_listHub.On<QueueSettingsUpdateDto>("QueueSettingsChanged", dto =>
                InvokeAsync(() => OnQueueSettingsChangedAsync(dto))));

            _listHub.Reconnected += async _ =>
            {
                try
                {
                    await SubscribeToAllQueueGroupsAsync();
                    // Jitter to prevent thundering herd on server restart
                    await Task.Delay(Random.Shared.Next(500, 3000));
                    await InvokeAsync(LoadAllAsync);
                }
                catch (Exception ex)
                {
                    Snackbar.Add(string.Format(Loc["Common_SignalRReconnectError"], ex.Message), Severity.Error);
                }
            };

            await _listHub.StartAsync();
            await SubscribeToAllQueueGroupsAsync();
        }
        catch (Exception ex)
        {
            Snackbar.Add(string.Format(Loc["Common_SignalRConnectError"], ex.Message), Severity.Warning);
        }
        finally
        {
            _listHubLock.Release();
        }
    }

    private async Task SubscribeToAllQueueGroupsAsync()
    {
        if (_listHub?.State != HubConnectionState.Connected) return;

        var allQueueIds = _myEvents.Select(e => e.Id)
            .Concat(_availableEvents.Select(e => e.Id))
            .Distinct()
            .ToList();

        foreach (var eventId in allQueueIds)
        {
            try
            {
                await _listHub.InvokeAsync("SubscribeToQueue", eventId);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to subscribe to queue {eventId}: {ex.Message}");
            }
        }
    }

    private async Task OnQueueSettingsChangedAsync(QueueSettingsUpdateDto dto)
    {
        bool updated = false;
        string? queueName = null;

        var myQueue = _myEvents.FirstOrDefault(q => q.Id == dto.EventId);
        if (myQueue is not null)
        {
            var index = _myEvents.IndexOf(myQueue);
            _myEvents[index] = myQueue with { AllowSelfJoin = dto.AllowSelfJoin };
            queueName = myQueue.Name;
            updated = true;
        }

        var availQueue = _availableEvents.FirstOrDefault(q => q.Id == dto.EventId);
        if (availQueue is not null)
        {
            var index = _availableEvents.IndexOf(availQueue);
            _availableEvents[index] = availQueue with { AllowSelfJoin = dto.AllowSelfJoin };
            queueName ??= availQueue.Name;
            updated = true;
        }

        if (updated)
        {
            StateHasChanged();

            var message = dto.AllowSelfJoin
                ? string.Format(Loc["StudentQueues_RegistrationNowOpen"], queueName)
                : string.Format(Loc["StudentQueues_RegistrationNowClosed"], queueName);

            Snackbar.Add(message, Severity.Warning, config =>
            {
                config.ShowCloseIcon = true;
                config.VisibleStateDuration = 10000;
                config.SnackbarVariant = Variant.Filled;
            });
        }
    }

    private async Task DisposeListHubAsync()
    {
        if (_listHub is not null)
        {
            foreach (var handler in _listHubHandlers)
                handler.Dispose();
            _listHubHandlers.Clear();

            await _listHub.DisposeAsync();
            _listHub = null;
        }
    }

    private void BackToList() => Nav.NavigateTo("/student/queues");

    private void Open(int eventId) => Nav.NavigateTo($"/student/queues/{eventId}");

    private async Task LeaveAsync()
    {
        if (_viewedEventId is not int eventId) return;
        // Место в очереди при повторной записи не возвращается — уточняем, чтобы случайный клик его не стоил.
        if (!await Confirm.ConfirmAsync(Loc["StudentQueues_LeaveTitle"], Loc["StudentQueues_LeaveText"], Loc["StudentQueues_Leave"])) return;
        try
        {
            await QueuesApi.LeaveAsync(eventId);
            Snackbar.Add(Loc["StudentQueues_Left"], Severity.Success);
            BackToList();
        }
        catch (ApiException ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
        }
    }

    private async Task CloseDetailsAsync()
    {
        if (_viewedEventId is not int eventId) return;
        
        // Remove detail-view handlers but keep the shared hub alive
        var handlersToRemove = _listHubHandlers.Where(h => 
            h.GetType().Name.Contains("QueueEntryUpdated")).ToList();
        foreach (var handler in handlersToRemove)
        {
            handler.Dispose();
            _listHubHandlers.Remove(handler);
        }
        
        // Unsubscribe from the specific queue group
        if (_listHub?.State == HubConnectionState.Connected)
        {
            try { await _listHub.InvokeAsync("UnsubscribeFromQueue", eventId); }
            catch { }
        }

        _viewedEventId = null;
        _details = null;
        _latestColorChanges.Clear();
    }

    private void UpdateStudentColor(int eventId, StudentColorUpdateDto update)
    {
        if (_disposed || _viewedEventId != eventId)
            return;
        _latestColorChanges[update.StudentId] = (++_colorRevision, update.Color);
        if (!_participants.Any(participant => participant.StudentId == update.StudentId && participant.StudentColor != update.Color))
            return;

        _participants = _participants.Select(participant => participant.StudentId == update.StudentId
            ? participant with { StudentColor = update.Color }
            : participant).ToList();
        if (_details is not null)
            _details = _details with { Participants = _participants };
        StateHasChanged();
    }

    private static Color StatusColor(QueueEntryStatus status) => status switch
    {
        QueueEntryStatus.Checking => Color.Info,
        QueueEntryStatus.Skipped => Color.Error,
        QueueEntryStatus.Waiting => Color.Warning,
        QueueEntryStatus.Finished => Color.Success,
        _ => Color.Error,
    };

    /// <summary>Тыквенное оформление кнопки записи: событие включено и режим выбран пользователем.</summary>
    private bool HalloweenActive => Seasons.IsOn(Frontend.Shared.Seasons.HalloweenTheme.Key);

    private string? SignUpButtonClass => HalloweenActive ? "fc-halloween-btn" : null;

    private string? SignUpButtonIcon => HalloweenActive ? null : Icons.Material.Filled.Add;

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        _viewedEventId = null;

        // Cleanup color changes older than 24h (queues auto-close after event)
        var cutoff = DateTime.UtcNow.AddDays(-1);
        // Since we only track revision numbers, we can't do time-based cleanup directly.
        // Instead, clear on detail view close (CloseDetailsAsync) and on full dispose.
        // For long-running list page, the dict stays small (only students in viewed queues).

        await _listHubLock.WaitAsync();
        try { await DisposeListHubAsync(); }
        finally { _listHubLock.Release(); _listHubLock.Dispose(); }
    }
}
