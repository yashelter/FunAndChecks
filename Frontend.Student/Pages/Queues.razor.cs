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

    private QueueParticipantDto? MyEntry => _participants.FirstOrDefault(p => p.StudentId == _myId);

    /// <summary>Ожидающие в порядке вызова: больше баллов — раньше, при равенстве — кто раньше записался.</summary>
    private List<QueueParticipantDto> Waiting => _participants.Where(p => p.Status == QueueEntryStatus.Waiting)
        .OrderByDescending(p => p.TotalPoints).ThenBy(p => p.JoinedAt).ToList();

    private int? MyPosition => Waiting.FindIndex(p => p.StudentId == _myId) is var index and >= 0 ? index + 1 : null;

    private IEnumerable<QueueParticipantDto> Ordered => _participants.OrderBy(x => QueueStyles.StatusOrder(x.Status))
        .ThenByDescending(x => x.TotalPoints).ThenBy(x => x.JoinedAt);

    private List<QueueEventDto> _myEvents = [];
    private List<QueueEventDto> _availableEvents = [];
    private QueueDetailsDto? _details;
    private List<QueueParticipantDto> _participants = [];
    private bool _loading = true;
    private bool _showPast;

    private HubConnection? _hub;
    private readonly SemaphoreSlim _hubLock = new(1, 1);
    private int? _viewedEventId;
    private bool _disposed;
    private long _colorRevision;
    private readonly Dictionary<Guid, (long Revision, string? Color)> _latestColorChanges = [];

    protected override async Task OnInitializedAsync()
    {
        var user = (await Authentication.GetAuthenticationStateAsync()).User;
        if (Guid.TryParse(user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var id)) _myId = id;
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

    private async Task ViewQueueAsync(int eventId)
    {
        if (_disposed)
            return;
        if (_viewedEventId != eventId)
            _latestColorChanges.Clear();
        _viewedEventId = eventId;
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
                .OrderBy(p => p.Status)
                .ThenByDescending(p => p.TotalPoints)
                .ToList();
            _details = details with { Participants = _participants };
            StateHasChanged();
        }
        catch (ApiException ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
        }
    }

    private async Task InitializeSignalRAsync(int eventId)
    {
        await _hubLock.WaitAsync();
        try
        {
            if (_disposed || _viewedEventId != eventId)
                return;
            await DisposeHubAsync();

            _hub = new HubConnectionBuilder()
                .WithUrl(Nav.ToAbsoluteUri("/apiHub/queueHub"), options =>
                    options.AccessTokenProvider = async () => await Auth.GetTokenAsync())
                .WithAutomaticReconnect()
                .Build();

            // На любое обновление очереди перечитываем её состав.
            _hub.On<QueueEntryUpdateDto>("QueueEntryUpdated", _ => InvokeAsync(() => LoadDetailsAsync(eventId)));
            _hub.On<StudentColorUpdateDto>("StudentColorUpdated", update => InvokeAsync(() => UpdateStudentColor(eventId, update)));

            var hub = _hub;

            // После переподключения подписка на группу теряется — переподписываемся и обновляем данные,
            // иначе очередь «зависает» и перестаёт обновляться.
            _hub.Reconnected += async _ =>
            {
                try
                {
                    if (_disposed || _viewedEventId != eventId || !ReferenceEquals(_hub, hub))
                        return;
                    await hub.InvokeAsync("SubscribeToQueue", eventId);
                    await InvokeAsync(() => LoadDetailsAsync(eventId));
                }
                catch (Exception ex)
                {
                    if (!_disposed && _viewedEventId == eventId)
                        Snackbar.Add(string.Format(Loc["Common_SignalRReconnectError"], ex.Message), Severity.Error);
                }
            };

            try
            {
                await hub.StartAsync();
                if (_disposed || _viewedEventId != eventId)
                    return;
                await hub.InvokeAsync("SubscribeToQueue", eventId);
                await LoadDetailsAsync(eventId);
            }
            catch (Exception ex)
            {
                if (!_disposed && _viewedEventId == eventId)
                    Snackbar.Add(string.Format(Loc["Common_SignalRConnectError"], ex.Message), Severity.Warning);
            }
        }
        finally
        {
            _hubLock.Release();
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
        _viewedEventId = null;
        _details = null;
        await _hubLock.WaitAsync();
        try { await DisposeHubAsync(); }
        finally { _hubLock.Release(); }
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
        // Под замком, чтобы не пересечься с идущей (пере)инициализацией хаба.
        await _hubLock.WaitAsync();
        try
        {
            await DisposeHubAsync();
        }
        finally
        {
            _hubLock.Release();
        }
    }

    private async Task DisposeHubAsync()
    {
        if (_hub is not null)
        {
            await _hub.DisposeAsync();
            _hub = null;
        }
    }
}
