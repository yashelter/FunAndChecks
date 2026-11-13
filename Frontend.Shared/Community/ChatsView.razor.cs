using System.Globalization;
using System.Security.Claims;
using Frontend.Shared.Api;
using Frontend.Shared.Auth;
using Frontend.Shared.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.SignalR.Client;
using MudBlazor;

namespace Frontend.Shared.Community;

public partial class ChatsView : IAsyncDisposable
{
    private const int ListPageSize = 40;

    [Inject] private ForumApi Api { get; set; } = default!;
    [Inject] private AuthService Auth { get; set; } = default!;
    [Inject] private AuthenticationStateProvider Authentication { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;

    [Parameter] public bool IsAdmin { get; set; }
    [Parameter] public int? TopicId { get; set; }
    [Parameter] public long? PostId { get; set; }

    private string BasePath => IsAdmin ? "/admin" : "/student";
    private ForumFoldersDto? _folders;
    private ForumTopicPageDto? _page;
    private ChatPane? _pane;
    private HubConnection? _hub;
    private Guid _viewerId;
    private bool _isSuper, _creating, _live = true;
    private bool _sendingAnnouncement;
    private string _folder = "all";
    private string? _search, _error;
    private int _listSize = ListPageSize;
    private int? _subscribedTopic;
    private CancellationTokenSource? _reload;
    private readonly CancellationTokenSource _life = new();

    private sealed record Folder(string Key, string Name, int Unread);

    /// <summary>Папки без чатов не показываем: пустых вкладок по предметам больше нет.</summary>
    private List<Folder> Folders
    {
        get
        {
            if (_folders is null) return [];
            var list = new List<Folder> { new("all", CommunityText.T("Все", "All"), _folders.PersonalUnreadCount + _folders.Sections.Sum(s => s.UnreadTopicCount)) };
            if (_folders.PersonalCount > 0 || _folders.CanHaveSaved)
                list.Add(new("personal", CommunityText.T("Личные", "Personal"), _folders.PersonalUnreadCount));
            foreach (var section in _folders.Sections.Where(s => s.TopicCount > 0 || FolderKey(s) == _folder))
                list.Add(new(FolderKey(section), section.SubjectName ?? CommunityText.T("Общее", "General"), section.UnreadTopicCount));
            return list;
        }
    }

    private List<ForumSectionDto> CreatableSections => _folders?.Sections.Where(s => s.CanCreateTopics).ToList() ?? [];

    private bool ShowSavedShortcut => _folders is { CanHaveSaved: true, SavedTopicId: null } && _folder is "all" or "personal" &&
        string.IsNullOrWhiteSpace(_search);

    protected override async Task OnInitializedAsync()
    {
        var state = await Authentication.GetAuthenticationStateAsync();
        Guid.TryParse(state.User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out _viewerId);
        _isSuper = state.User.IsInRole(Roles.SuperAdmin);
        await Task.WhenAll(LoadFoldersAsync(), LoadListAsync());
        await ConnectAsync();
    }

    protected override async Task OnParametersSetAsync()
    {
        if (_hub is not null) await SubscribeTopicAsync();
    }

    private static string FolderKey(ForumSectionDto section) => section.SubjectId?.ToString(CultureInfo.InvariantCulture) ?? "general";

    private async Task LoadFoldersAsync()
    {
        try { _folders = await Api.FoldersAsync(_life.Token); }
        catch (ApiException ex) { _error = CommunityText.Error(ex); }
        catch (OperationCanceledException) { }
    }

    private async Task LoadListAsync()
    {
        try
        {
            var personal = _folder == "personal";
            var general = _folder == "general";
            int? subject = int.TryParse(_folder, out var id) ? id : null;
            _page = await Api.TopicsAsync(subject, general, _search, 1, _life.Token, personal, Math.Min(_listSize, 100));
            _error = null;
        }
        catch (ApiException ex) { _error = CommunityText.Error(ex); }
        catch (OperationCanceledException) { }
    }

    private async Task SelectFolderAsync(string key)
    {
        _folder = key;
        _listSize = ListPageSize;
        await LoadListAsync();
    }

    private async Task SearchChangedAsync()
    {
        _reload?.Cancel();
        var token = (_reload = new CancellationTokenSource()).Token;
        try { await Task.Delay(300, token); } catch (TaskCanceledException) { return; }
        await LoadListAsync();
    }

    private async Task MoreAsync()
    {
        _listSize = Math.Min(_listSize + ListPageSize, 100);
        await LoadListAsync();
    }

    /// <summary>Перечитать список и папки чуть позже: события приходят пачками.</summary>
    private Task ScheduleReload()
    {
        _reload?.Cancel();
        var token = (_reload = new CancellationTokenSource()).Token;
        _ = InvokeAsync(async () =>
        {
            try { await Task.Delay(400, token); } catch (TaskCanceledException) { return; }
            await Task.WhenAll(LoadFoldersAsync(), LoadListAsync());
            StateHasChanged();
        });
        return Task.CompletedTask;
    }

    private async Task OpenSavedAsync()
    {
        try
        {
            var saved = await Api.OpenSavedAsync();
            await ScheduleReload();
            Nav.NavigateTo($"{BasePath}/forum/{saved.Topic.Id}");
        }
        catch (ApiException ex) { Snackbar.Add(CommunityText.Error(ex), Severity.Error); }
    }

    private async Task ChatOpened(int topicId)
    {
        _creating = false;
        await ScheduleReload();
        Nav.NavigateTo($"{BasePath}/forum/{topicId}");
    }

    private async Task ConnectAsync()
    {
        try
        {
            var hub = new HubConnectionBuilder()
                .WithUrl(Nav.ToAbsoluteUri("/apiHub/forumHub"), options => options.AccessTokenProvider = () => Auth.GetTokenAsync())
                .WithAutomaticReconnect().Build();
            hub.On("ChatsChanged", () => InvokeAsync(ScheduleReload));
            hub.On<ForumTopicEventDto>("TopicChanged", update => InvokeAsync(async () =>
            {
                if (_pane is not null) await _pane.OnTopicEventAsync(update);
            }));
            hub.Reconnecting += _ => InvokeAsync(() => { _live = false; StateHasChanged(); });
            hub.Closed += _ => InvokeAsync(() => { _live = false; StateHasChanged(); });
            hub.Reconnected += _ => InvokeAsync(async () =>
            {
                _live = true;
                _subscribedTopic = null;
                await SubscribeTopicAsync();
                await ScheduleReload();
                if (_pane is not null) await _pane.RefreshAsync();
                StateHasChanged();
            });
            _hub = hub;
            await hub.StartAsync(_life.Token);
            _live = true;
            await SubscribeTopicAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Без живого канала чаты работают по запросу — честно показываем это в шапке чата.
            _live = false;
        }
    }

    private async Task SubscribeTopicAsync()
    {
        if (_hub is not { State: HubConnectionState.Connected } hub || _subscribedTopic == TopicId) return;
        try
        {
            if (_subscribedTopic is int previous) await hub.InvokeAsync("UnsubscribeFromTopic", previous, _life.Token);
            _subscribedTopic = null;
            if (TopicId is int topic)
            {
                await hub.InvokeAsync("SubscribeToTopic", topic, _life.Token);
                _subscribedTopic = topic;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { /* Чужой или удалённый чат: панель покажет ошибку сама. */ }
    }

    private string Preview(ForumTopicSummaryDto chat)
    {
        if (chat.LastPost is not { } last) return CommunityText.T("Нет сообщений", "No messages");
        var who = last.AuthorId == _viewerId ? CommunityText.T("Вы: ", "You: ")
            : chat.Kind == ForumTopicKind.Section ? $"{FirstName(last.AuthorName)}: " : "";
        var text = last.IsSticker ? CommunityText.T("Стикер", "Sticker")
            : !string.IsNullOrWhiteSpace(last.Text) ? ForumMarkdown.ToPlainText(last.Text, 90)
            : last.HasFiles ? CommunityText.T("📎 Файл", "📎 File") : "";
        return chat.Kind == ForumTopicKind.Saved ? text : who + text;
    }

    /// <summary>Имя без фамилии: в имени аккаунта сначала фамилия.</summary>
    private static string FirstName(string fullName)
    {
        var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 1 ? parts[1] : fullName;
    }

    private static string ListTime(DateTime utc)
    {
        var local = utc.ToLocalTime();
        var today = DateTime.Now.Date;
        if (local.Date == today) return local.ToString("HH:mm", CultureInfo.CurrentCulture);
        if (local.Date > today.AddDays(-7)) return local.ToString("ddd", CultureInfo.CurrentCulture);
        return local.ToString(local.Year == today.Year ? "d MMM" : "dd.MM.yy", CultureInfo.CurrentCulture);
    }

    public async ValueTask DisposeAsync()
    {
        _life.Cancel();
        _reload?.Cancel();
        if (_hub is not null)
        {
            try { await _hub.DisposeAsync(); } catch (Exception) { }
        }
        _life.Dispose();
    }
}
