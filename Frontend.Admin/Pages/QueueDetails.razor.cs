using System.Text.Json;
using Frontend.Admin.Dialogs;
using Frontend.Shared.Api;
using Frontend.Shared.Models;
using Frontend.Shared.Resources;
using Frontend.Shared.Services;
using Frontend.Shared.UI;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Localization;
using Microsoft.JSInterop;
using MudBlazor;
using System.Security.Claims;

namespace Frontend.Admin.Pages;

public partial class QueueDetails : IAsyncDisposable
{
    /// <summary>Пачка событий очереди склеивается в одно перечитывание.</summary>
    private static readonly TimeSpan ReloadDelay = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    [Parameter] public int EventId { get; set; }

    [Inject] private QueuesApi Queues { get; set; } = null!;
    [Inject] private AuthService Auth { get; set; } = null!;
    [Inject] private AuthenticationStateProvider Authentication { get; set; } = null!;
    [Inject] private NavigationManager Nav { get; set; } = null!;
    [Inject] private IDialogService DialogService { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;
    [Inject] private IStringLocalizer<AppStrings> Loc { get; set; } = null!;
    [Inject] private IJSRuntime JS { get; set; } = null!;

    private QueueDetailsDto? _details;
    private List<QueueParticipantDto> _participants = [];
    private bool _loading = true;
    private HubConnection? _hub;
    private Guid? _currentAdminId;
    private bool _disposed;
    private long _colorRevision;
    private readonly Dictionary<Guid, (long Revision, string? Color)> _latestColorChanges = [];

    private ParticipantSort _sort = ParticipantSort.Points;
    // Группы, которые этот преподаватель смотрит в этой очереди; пусто — все. Хранится в браузере.
    private HashSet<string> _groups = new(StringComparer.Ordinal);
    private bool _dialogOpen, _reloadScheduled;
    private string? _loadError;

    public enum ParticipantSort { Group, Points, JoinedAt }

    private string GroupsKey => $"fc-queue-groups-{EventId}";
    // Прежняя настройка «скрытые группы» — удаляем, чтобы не копилась.
    private string LegacyHiddenGroupsKey => $"fc-queue-hidden-groups-{EventId}";

    protected override async Task OnInitializedAsync()
    {
        var user = (await Authentication.GetAuthenticationStateAsync()).User;
        if (Guid.TryParse(user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user.FindFirst("sub")?.Value ?? user.FindFirst("nameid")?.Value, out var adminId))
            _currentAdminId = adminId;
        await LoadAsync();
        await RestoreGroupsAsync();
        if (_details is not null && !_disposed)
            await InitializeSignalRAsync();
    }

    private List<QueueParticipantDto> Visible => VisibleParticipants();

    /// <summary>Группы, которые есть в этой очереди, с числом участников — варианты фильтра.</summary>
    private List<(string Group, int Count)> Groups => _participants.GroupBy(p => p.GroupName)
        .OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase).Select(g => (g.Key, g.Count())).ToList();

    private IEnumerable<QueueParticipantDto> InVisibleGroups => _groups.Count == 0
        ? _participants
        : _participants.Where(p => _groups.Contains(p.GroupName));

    private List<QueueParticipantDto> VisibleParticipants()
    {
        var items = InVisibleGroups;

        // Статус — всегда первичный ключ сортировки (сдают → в очереди → пропущенные → завершившие),
        // выбранное поле — вторичное, время записи — последний ключ при равенстве.
        var ordered = items.OrderBy(p => QueueStyles.StatusOrder(p.Status));
        ordered = _sort switch
        {
            ParticipantSort.Group => ordered.ThenBy(p => p.GroupName).ThenBy(p => p.LastName),
            ParticipantSort.JoinedAt => ordered.ThenBy(p => p.JoinedAt),
            _ => ordered.ThenByDescending(p => p.TotalPoints).ThenBy(p => p.JoinedAt),
        };

        return ordered.ToList();
    }

    /// <summary>Следующий ожидающий в видимых группах: больше баллов — раньше, при равенстве — кто раньше записался.</summary>
    private QueueParticipantDto? NextWaiting() => InVisibleGroups
        .Where(p => p.Status == QueueEntryStatus.Waiting && p.CanManage)
        .OrderByDescending(p => p.TotalPoints).ThenBy(p => p.JoinedAt)
        .FirstOrDefault();

    private async Task LoadAsync()
    {
        if (_disposed)
            return;
        try
        {
            var eventId = EventId;
            var colorRevision = _colorRevision;
            var details = await Queues.GetDetailsAsync(eventId);
            if (_disposed || EventId != eventId)
                return;
            _participants = details.Participants.Select(participant =>
                _latestColorChanges.TryGetValue(participant.StudentId, out var change) && change.Revision > colorRevision
                    ? participant with { StudentColor = change.Color }
                    : participant).ToList();
            _details = details with { Participants = _participants };
            _loadError = null;
        }
        catch (ApiException ex)
        {
            if (_details is null)
            {
                _participants = [];
                Snackbar.Add(string.Format(Loc["QueueDetails_LoadErrorPrefix"], ex.Message), Severity.Error);
            }
            else
            {
                // Не стираем очередь из-за одной ошибки: показываем последний список и пробуем снова.
                _loadError = ex.Message;
                _ = RetryLaterAsync();
            }
        }
        finally
        {
            if (!_disposed)
            {
                _loading = false;
                StateHasChanged();
            }
        }
    }

    // Одна цепочка повторов на страницу: ошибки от событий и от самих повторов её не множат.
    private bool _retryScheduled;

    private async Task RetryLaterAsync()
    {
        if (_retryScheduled) return;
        _retryScheduled = true;
        try { await Task.Delay(RetryDelay); }
        finally { _retryScheduled = false; }
        if (!_disposed && _loadError is not null && !_dialogOpen)
            await InvokeAsync(LoadAsync);
    }

    /// <summary>
    /// Реакция на событие очереди. Пока открыт диалог студента, список под ним не трогаем —
    /// он перечитается целиком при возврате; иначе склеиваем пачку событий в одно перечитывание.
    /// </summary>
    private async Task OnQueueChangedAsync()
    {
        if (_disposed) return;
        if (_dialogOpen || _reloadScheduled) return;
        _reloadScheduled = true;
        try
        {
            await Task.Delay(ReloadDelay);
            if (!_dialogOpen) await LoadAsync();
        }
        finally { _reloadScheduled = false; }
    }

    private async Task InitializeSignalRAsync()
    {
        if (_disposed)
            return;
        _hub = new HubConnectionBuilder()
            .WithUrl(Nav.ToAbsoluteUri("/apiHub/queueHub"), options =>
                options.AccessTokenProvider = async () => await Auth.GetTokenAsync())
            .WithAutomaticReconnect()
            .Build();

        var hub = _hub;
        var eventId = EventId;

        _hub.On<QueueEntryUpdateDto>("QueueEntryUpdated", _ => InvokeAsync(OnQueueChangedAsync));
        _hub.On<StudentColorUpdateDto>("StudentColorUpdated", update => InvokeAsync(() => UpdateStudentColor(update)));
        _hub.Reconnecting += _ => InvokeAsync(() => { if (!_disposed) StateHasChanged(); });
        _hub.Closed += _ => InvokeAsync(() => { if (!_disposed) StateHasChanged(); });

        // После переподключения переподписываемся, иначе обновления перестают приходить.
        _hub.Reconnected += async _ =>
        {
            try
            {
                if (_disposed || !ReferenceEquals(_hub, hub) || EventId != eventId)
                    return;
                await hub.InvokeAsync("SubscribeToQueue", eventId);
                await InvokeAsync(OnQueueChangedAsync);
            }
            catch (Exception ex)
            {
                if (!_disposed)
                    Snackbar.Add(string.Format(Loc["Common_SignalRReconnectError"], ex.Message), Severity.Error);
            }
        };

        try
        {
            await hub.StartAsync();
            if (_disposed)
                return;
            await hub.InvokeAsync("SubscribeToQueue", eventId);
            // Изменения между первой загрузкой и подпиской не должны потеряться.
            await LoadAsync();
        }
        catch (Exception ex)
        {
            if (!_disposed)
                Snackbar.Add(string.Format(Loc["Common_SignalRConnectError"], ex.Message), Severity.Warning);
        }
    }

    private async Task TakeNextAsync()
    {
        // Перед выбором — свежий список: «следующий» должен быть актуальным, а не из устаревшей картинки.
        await LoadAsync();
        var next = NextWaiting();
        if (next is null)
        {
            Snackbar.Add(Loc["QueueDetails_NoNext"], Severity.Info);
            return;
        }
        await OpenStudentAsync(next);
    }

    /// <summary>
    /// Открывает студента и сразу берёт его на проверку — в любом статусе, одним действием.
    /// Один преподаватель может проверять нескольких студентов одновременно.
    /// </summary>
    private async Task OpenStudentAsync(QueueParticipantDto participant)
    {
        if (_details is null || !participant.CanManage)
            return;

        var checkingWarning = IsOtherTeacherChecking(participant) ? participant.CheckingByAdminName : null;
        // Другой преподаватель — предупреждение, а не запрет на приём.
        if (!IsReadOnly(participant) &&
            (participant.Status != QueueEntryStatus.Checking || participant.CheckingByAdminId != _currentAdminId))
        {
            try
            {
                await Queues.UpdateStatusAsync(EventId, participant.StudentId, new UpdateQueueStatusRequest(QueueEntryStatus.Checking));
                participant = participant with { Status = QueueEntryStatus.Checking, CheckingByAdminId = _currentAdminId };
            }
            catch (ApiException ex)
            {
                Snackbar.Add(ex.Message, Severity.Error);
                await LoadAsync();
                return;
            }
        }

        var parameters = new DialogParameters<StudentInteractionDialog>
        {
            { x => x.StudentId, participant.StudentId },
            { x => x.StudentName, $"{participant.LastName} {participant.FirstName}" },
            { x => x.GroupName, participant.GroupName },
            { x => x.EventId, EventId },
            { x => x.SubjectId, _details.SubjectId },
            { x => x.ReadOnly, IsReadOnly(participant) },
            { x => x.StudentColor, participant.StudentColor },
            { x => x.CheckingWarningName, checkingWarning },
            { x => x.OnColorApplied, EventCallback.Factory.Create<StudentColorUpdateDto>(this, UpdateStudentColor) },
        };

        _dialogOpen = true;
        DialogResult? result;
        try
        {
            var dialog = await DialogService.ShowAsync<StudentInteractionDialog>(
                Loc["QueueDetails_WorkTitle"],
                parameters,
                new DialogOptions { MaxWidth = MaxWidth.Medium, FullWidth = true });
            result = await dialog.Result;
        }
        finally
        {
            _dialogOpen = false;
        }

        // Возврат к списку — всегда полное перечитывание: так надёжнее, чем собирать картину из событий.
        await LoadAsync();
        if (result is { Canceled: false, Data: StudentDialogResults.Next } && !_disposed)
        {
            var next = NextWaiting();
            if (next is null) Snackbar.Add(Loc["QueueDetails_NoNext"], Severity.Info);
            else await OpenStudentAsync(next);
        }
    }

    /// <summary>Счётчик статуса: при выбранных группах — «в выбранных / всего».</summary>
    private string StatCount(QueueEntryStatus status)
    {
        var total = _participants.Count(p => p.Status == status);
        var visible = InVisibleGroups.Count(p => p.Status == status);
        return visible == total ? total.ToString() : $"{visible} / {total}";
    }

    private string GroupsText(IReadOnlyList<string> _) =>
        _groups.Count == 0 ? Loc["AttendanceJournal_AllGroups"] : string.Join(", ", _groups.Order());

    private async Task OnGroupsChangedAsync(IEnumerable<string>? groups)
    {
        _groups = new HashSet<string>(groups ?? [], StringComparer.Ordinal);
        try
        {
            if (_groups.Count == 0) await JS.InvokeVoidAsync("localStorage.removeItem", GroupsKey);
            else await JS.InvokeVoidAsync("localStorage.setItem", GroupsKey, JsonSerializer.Serialize(_groups));
        }
        catch (JSException) { /* Выбор останется до перезагрузки страницы. */ }
    }

    /// <summary>Восстанавливает выбор групп после первой загрузки — только из групп, что есть в очереди.</summary>
    private async Task RestoreGroupsAsync()
    {
        try
        {
            await JS.InvokeVoidAsync("localStorage.removeItem", LegacyHiddenGroupsKey);
            var json = await JS.InvokeAsync<string?>("localStorage.getItem", GroupsKey);
            if (!string.IsNullOrEmpty(json) && JsonSerializer.Deserialize<string[]>(json) is { } saved)
            {
                var existing = Groups.Select(g => g.Group).ToHashSet(StringComparer.Ordinal);
                _groups = new HashSet<string>(saved.Where(existing.Contains), StringComparer.Ordinal);
            }
        }
        catch (Exception ex) when (ex is JSException or JsonException) { _groups = new(StringComparer.Ordinal); }
    }

    private static Color StatusColor(QueueEntryStatus status) => status switch
    {
        QueueEntryStatus.Checking => Color.Info,
        QueueEntryStatus.Skipped => Color.Error,
        QueueEntryStatus.Waiting => Color.Warning,
        QueueEntryStatus.Finished => Color.Success,
        _ => Color.Default,
    };

    private void UpdateStudentColor(StudentColorUpdateDto update)
    {
        if (_disposed)
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

    private bool IsReadOnly(QueueParticipantDto participant) => _currentAdminId is null;

    private bool IsOtherTeacherChecking(QueueParticipantDto participant) =>
        participant.Status == QueueEntryStatus.Checking &&
        participant.CheckingByAdminId is not null && participant.CheckingByAdminId != _currentAdminId;

    private static string QueueClass(QueueEntryStatus status) => status switch
    {
        QueueEntryStatus.Checking => "fc-queue-checking",
        QueueEntryStatus.Skipped => "fc-queue-skipped",
        QueueEntryStatus.Finished => "fc-queue-finished",
        _ => "fc-queue-waiting",
    };

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
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
