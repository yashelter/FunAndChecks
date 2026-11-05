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

    private HubConnection? _listHub;
    private readonly List<IDisposable> _listHubHandlers = [];
    // Обработчики открытой очереди: снимаются при уходе к списку или в другую очередь.
    private readonly List<IDisposable> _detailHandlers = [];
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
            CloseDetails();
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
        await AttachDetailsAsync(eventId);

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


    /// <summary>
    /// Живые обновления открытой очереди. Обработчики прошлой открытой очереди снимаются, а события других очередей
    /// пропускаются: страница подписана на все очереди списка. Обработчики можно навесить и до подключения —
    /// подписку на группу тогда сделает переподключение.
    /// </summary>
    private async Task AttachDetailsAsync(int eventId)
    {
        DetachDetails();
        if (_listHub is not { } hub)
            return;
        _detailHandlers.Add(hub.On<QueueEntryUpdateDto>("QueueEntryUpdated", update =>
            update.EventId == eventId ? InvokeAsync(() => LoadDetailsAsync(eventId)) : Task.CompletedTask));
        // Изменилось само событие (название, время, порог опоздания) — порядок мог поменяться.
        _detailHandlers.Add(hub.On<int>("QueueChanged", changed =>
            changed == eventId ? InvokeAsync(() => LoadDetailsAsync(eventId)) : Task.CompletedTask));
        _detailHandlers.Add(hub.On<StudentColorUpdateDto>("StudentColorUpdated", update =>
            InvokeAsync(() => UpdateStudentColor(eventId, update))));
        await SubscribeAsync(eventId);
    }

    private void DetachDetails()
    {
        foreach (var handler in _detailHandlers)
            handler.Dispose();
        _detailHandlers.Clear();
    }

    /// <summary>Подписка на группу очереди; сбой не страшен — переподключение подпишет заново.</summary>
    private async Task SubscribeAsync(int eventId)
    {
        if (_listHub?.State != HubConnectionState.Connected)
            return;
        try
        {
            await _listHub.InvokeAsync("SubscribeToQueue", eventId);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to subscribe to queue {eventId}: {ex.Message}");
        }
    }

    private async Task InitializeListHubAsync()
    {
        if (_disposed)
            return;
        await _listHubLock.WaitAsync();
        try
        {
            // Страницу закрыли, пока грузился список: соединение уже не нужно.
            if (_disposed)
                return;
            await DisposeListHubAsync();

            _listHub = new HubConnectionBuilder()
                .WithUrl(Nav.ToAbsoluteUri("/apiHub/queueHub"), options =>
                    options.AccessTokenProvider = async () => await Auth.GetTokenAsync())
                .WithAutomaticReconnect()
                .Build();

            _listHubHandlers.Add(_listHub.On<QueueSettingsUpdateDto>("QueueSettingsChanged", dto =>
                InvokeAsync(() => OnQueueSettingsChanged(dto))));

            // После переподключения подписки на группы теряются: подписываемся заново (и на открытую очередь,
            // даже если её нет в списке) и перечитываем то, что на экране, иначе очередь «зависает».
            _listHub.Reconnected += async _ =>
            {
                try
                {
                    if (_disposed)
                        return;
                    await SubscribeToAllQueueGroupsAsync();
                    if (_viewedEventId is int viewed)
                        await SubscribeAsync(viewed);
                    // Разброс, чтобы после сбоя сети все студенты не перечитывали очередь одновременно.
                    await Task.Delay(Random.Shared.Next(500, 3000));
                    if (_disposed)
                        return;
                    await InvokeAsync(() => _viewedEventId is int id ? LoadDetailsAsync(id) : LoadAllAsync());
                }
                catch (Exception ex)
                {
                    if (!_disposed)
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
            await SubscribeAsync(eventId);
    }

    /// <summary>Запись в очередь открыли или закрыли: обновляем кнопки и сообщаем, только если флаг правда сменился.</summary>
    private void OnQueueSettingsChanged(QueueSettingsUpdateDto dto)
    {
        if (_disposed)
            return;
        var known = _myEvents.Concat(_availableEvents).FirstOrDefault(q => q.Id == dto.EventId);
        var listChanged = known is not null && known.AllowSelfJoin != dto.AllowSelfJoin;
        var detailsChanged = _details is { } shown && shown.EventId == dto.EventId && shown.AllowSelfJoin != dto.AllowSelfJoin;
        if (!listChanged && !detailsChanged)
            return;

        _myEvents = _myEvents.Select(q => q.Id == dto.EventId ? q with { AllowSelfJoin = dto.AllowSelfJoin } : q).ToList();
        _availableEvents = _availableEvents.Select(q => q.Id == dto.EventId ? q with { AllowSelfJoin = dto.AllowSelfJoin } : q).ToList();
        if (detailsChanged)
            _details = _details! with { AllowSelfJoin = dto.AllowSelfJoin };
        StateHasChanged();

        // Открытая очередь перечитывается при переименовании, а список — нет: её название свежее.
        var queueName = _details?.EventId == dto.EventId ? _details.EventName : known?.Name;
        Snackbar.Add(string.Format(Loc[dto.AllowSelfJoin ? "StudentQueues_RegistrationNowOpen" : "StudentQueues_RegistrationNowClosed"], queueName),
            Severity.Warning, config =>
            {
                config.ShowCloseIcon = true;
                config.VisibleStateDuration = 10000;
                config.SnackbarVariant = Variant.Filled;
            });
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

    /// <summary>
    /// Назад к списку. От группы очереди не отписываемся: список подписан на все свои очереди и ждёт от неё
    /// уведомлений о записи. Снимаются только обработчики открытой очереди.
    /// </summary>
    private void CloseDetails()
    {
        DetachDetails();
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
        DetachDetails();

        // Семафор не уничтожаем: InitializeListHubAsync может ещё ждать его и сам выйдет по _disposed.
        // SemaphoreSlim без AvailableWaitHandle неуправляемых ресурсов не держит.
        await _listHubLock.WaitAsync();
        try { await DisposeListHubAsync(); }
        finally { _listHubLock.Release(); }
    }
}
