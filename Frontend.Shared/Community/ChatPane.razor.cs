using System.Globalization;
using Frontend.Shared.Api;
using Frontend.Shared.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using MudBlazor;

namespace Frontend.Shared.Community;

public partial class ChatPane : IAsyncDisposable
{
    private const int PageSize = 30;

    [Inject] private ForumApi Api { get; set; } = default!;
    [Inject] private ForumCatalog Catalog { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private ISnackbar Snackbar { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;
    [Inject] private ConfirmService Confirm { get; set; } = default!;

    [Parameter, EditorRequired] public int TopicId { get; set; }
    [Parameter] public long? PostId { get; set; }
    [Parameter] public bool IsAdmin { get; set; }
    [Parameter] public Guid ViewerId { get; set; }
    [Parameter] public bool Live { get; set; } = true;
    /// <summary>Чат изменился так, что список слева стоит перечитать (отправка, переименование, удаление).</summary>
    [Parameter] public EventCallback ListChanged { get; set; }

    private string BasePath => IsAdmin ? "/admin" : "/student";
    private ForumTopicDto? _topic;
    private List<ForumPostDto>? _items;
    private IReadOnlyList<ForumReactionTypeDto> _reactions = [];
    private List<ForumSectionDto> _sections = [];
    private ForumQuoteDto? _replyTo;
    private PostComposer? _composer;
    private ElementReference _feed;
    private IJSObjectReference? _module;
    private DotNetObjectReference<ChatPane>? _self;
    private readonly CancellationTokenSource _life = new();
    private string? _error, _title, _moveTarget;
    private bool _renaming, _moving, _newPosts, _watching;
    private int _firstPage = 1, _lastPage = 1, _total, _pinIndex = int.MaxValue;
    private long _readMark;
    private long? _loadedPost;
    /// <summary>Куда прокрутить после отрисовки: id сообщения, «непрочитанное» или низ ленты.</summary>
    private string? _scrollTarget;
    private (double Height, double Top)? _keepScroll;

    private int PageCount => Math.Max(1, (int)Math.Ceiling(_total / (double)PageSize));

    /// <summary>Первое чужое непрочитанное сообщение на момент открытия — над ним разделитель.</summary>
    private long? UnreadAnchor => _items?.FirstOrDefault(p => p.Id > _readMark && p.Author.UserId != ViewerId && !p.IsDeleted)?.Id;

    private string Title => _topic is null ? CommunityText.T("Чат", "Chat") : ChatAvatar.TitleOf(_topic.Topic);

    private string Subtitle
    {
        get
        {
            var topic = _topic!.Topic;
            return topic.Kind switch
            {
                ForumTopicKind.Saved => CommunityText.T("Заметки и сохранённые сообщения — видите только вы", "Notes and saved messages — only you see them"),
                ForumTopicKind.Direct => topic.Peer is null ? CommunityText.T("Аккаунт удалён", "Account deleted")
                    : topic.Peer.IsAdmin ? CommunityText.T("Преподаватель · личный чат", "Teacher · private chat")
                    : CommunityText.T("Личный чат", "Private chat"),
                _ => $"{topic.SubjectName ?? CommunityText.T("Общее", "General")} · {CommunityText.Posts(topic.PostCount)}" +
                     (topic.IsAnnouncement ? CommunityText.T(" · объявления", " · announcements") : ""),
            };
        }
    }

    protected override async Task OnParametersSetAsync()
    {
        if (_topic is not null && _topic.Topic.Id == TopicId)
        {
            // Тот же чат, другая ссылка на сообщение (например, из уведомления).
            if (PostId is long post && post != _loadedPost) { _loadedPost = post; await JumpToAsync(post); }
            return;
        }
        _loadedPost = PostId;
        _reactions = await Catalog.ReactionsAsync();
        if (!await LoadTopicAsync()) return;
        _readMark = _topic!.LastReadPostId;
        var around = PostId ?? (_readMark > 0 ? _readMark + 1 : null);
        await LoadAroundAsync(around);
        _scrollTarget = PostId is long id ? $"post-{id}" : UnreadAnchor is not null ? "fc-unread" : "bottom";
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_items is null) return;
        _module ??= await JS.InvokeAsync<IJSObjectReference>("import", "./_content/Frontend.Shared/community.js");
        if (!_watching)
        {
            _watching = true;
            _self ??= DotNetObjectReference.Create(this);
            await _module.InvokeVoidAsync("watchFeed", _feed, _self);
        }
        if (_keepScroll is { } keep)
        {
            _keepScroll = null;
            await _module.InvokeVoidAsync("restoreScroll", _feed, keep.Height, keep.Top);
        }
        if (_scrollTarget is { } target)
        {
            _scrollTarget = null;
            if (target == "bottom") await _module.InvokeVoidAsync("scrollToBottom", _feed);
            else if (!await _module.InvokeAsync<bool>("scrollToElement", target)) await _module.InvokeVoidAsync("scrollToBottom", _feed);
        }
    }

    /// <summary>Ленту докрутили до края — подгружаем соседнюю страницу.</summary>
    [JSInvokable]
    public Task FeedEdge(bool atTop) => InvokeAsync(async () =>
    {
        if (_items is null) return;
        if (atTop && _firstPage > 1) await LoadOlderAsync();
        else if (!atTop && _lastPage < PageCount) await LoadNewerAsync();
    });

    /// <summary>Событие живого канала по этому чату.</summary>
    public async Task OnTopicEventAsync(ForumTopicEventDto update)
    {
        if (update.TopicId != TopicId || _items is null) return;
        if (update.Kind == "post-created")
        {
            if (_items.Any(p => p.Id == update.PostId)) return;
            var atEnd = _lastPage >= PageCount;
            var nearBottom = _module is null || await _module.InvokeAsync<bool>("isNearBottom", _feed);
            await LoadTopicAsync();
            if (atEnd)
            {
                await RefreshPagesAsync(true);
                if (nearBottom) _scrollTarget = "bottom"; else _newPosts = true;
            }
            else _newPosts = true;
        }
        else await RefreshAsync();
        StateHasChanged();
    }

    public async Task RefreshAsync()
    {
        if (!await LoadTopicAsync()) return;
        await RefreshPagesAsync(false);
    }

    private async Task<bool> LoadTopicAsync()
    {
        try
        {
            _topic = await Api.TopicAsync(TopicId, _life.Token);
            _error = null;
            return true;
        }
        catch (ApiException ex) { _error = CommunityText.Error(ex); _topic = null; return false; }
        catch (OperationCanceledException) { return false; }
    }

    private async Task LoadAroundAsync(long? around)
    {
        try
        {
            var page = await Api.PostsAsync(TopicId, 1, around, _life.Token);
            _items = page.Items.ToList();
            _firstPage = _lastPage = page.Page;
            _total = page.TotalCount;
            _newPosts = false;
            await MarkReadAsync();
        }
        catch (ApiException ex) { Snackbar.Add(CommunityText.Error(ex), Severity.Error); _items ??= []; }
        catch (OperationCanceledException) { }
    }

    /// <summary>Перечитывает загруженные страницы; followNew — дочитать и появившиеся в конце.</summary>
    private async Task RefreshPagesAsync(bool followNew)
    {
        try
        {
            var items = new List<ForumPostDto>();
            var page = _firstPage;
            while (true)
            {
                var result = await Api.PostsAsync(TopicId, page, null, _life.Token);
                items.AddRange(result.Items);
                _total = result.TotalCount;
                var pages = PageCount;
                if (page >= pages || (page >= _lastPage && !followNew)) break;
                page++;
            }
            _lastPage = Math.Max(_firstPage, Math.Min(page, PageCount));
            _items = items.DistinctBy(p => p.Id).OrderBy(p => p.Id).ToList();
            await MarkReadAsync();
        }
        catch (ApiException ex) { Snackbar.Add(CommunityText.Error(ex), Severity.Error); }
        catch (OperationCanceledException) { }
    }

    private async Task LoadOlderAsync()
    {
        if (_items is null || _firstPage <= 1) return;
        try
        {
            var page = await Api.PostsAsync(TopicId, _firstPage - 1, null, _life.Token);
            if (_module is not null)
            {
                var state = await _module.InvokeAsync<double[]>("scrollState", _feed);
                _keepScroll = (state[0], state[1]);
            }
            _firstPage--;
            _total = page.TotalCount;
            _items = page.Items.Concat(_items).DistinctBy(p => p.Id).ToList();
            StateHasChanged();
        }
        catch (ApiException ex) { Snackbar.Add(CommunityText.Error(ex), Severity.Error); }
        catch (OperationCanceledException) { }
    }

    private async Task LoadNewerAsync()
    {
        if (_items is null || _lastPage >= PageCount) return;
        try
        {
            var page = await Api.PostsAsync(TopicId, _lastPage + 1, null, _life.Token);
            _lastPage++;
            _total = page.TotalCount;
            _items = _items.Concat(page.Items).DistinctBy(p => p.Id).ToList();
            if (_lastPage >= PageCount) _newPosts = false;
            await MarkReadAsync();
            StateHasChanged();
        }
        catch (ApiException ex) { Snackbar.Add(CommunityText.Error(ex), Severity.Error); }
        catch (OperationCanceledException) { }
    }

    private async Task MarkReadAsync()
    {
        var last = _items is { Count: > 0 } ? _items.Max(p => p.Id) : 0;
        if (_topic is null || last <= _topic.LastReadPostId) return;
        _topic = _topic with { LastReadPostId = last };
        try
        {
            await Api.MarkReadAsync(TopicId, last, _life.Token);
            await ListChanged.InvokeAsync();
        }
        catch (ApiException) { }
        catch (OperationCanceledException) { }
    }

    private async Task ToLatestAsync()
    {
        _newPosts = false;
        if (_lastPage < PageCount) await LoadAroundAsync(long.MaxValue / 2);
        else await RefreshPagesAsync(true);
        _scrollTarget = "bottom";
    }

    private async Task JumpToAsync(long postId)
    {
        if (_items?.Any(p => p.Id == postId) != true) await LoadAroundAsync(postId);
        _scrollTarget = $"post-{postId}";
        StateHasChanged();
    }

    private async Task NextPinnedAsync()
    {
        if (_topic is not { PinnedPosts.Count: > 0 } topic) return;
        var index = Math.Clamp(_pinIndex, 0, topic.PinnedPosts.Count - 1);
        await JumpToAsync(topic.PinnedPosts[index].Id);
        // Как в мессенджере: каждое нажатие ведёт к предыдущему закреплённому, по кругу.
        _pinIndex = index == 0 ? topic.PinnedPosts.Count - 1 : index - 1;
    }

    private async Task StartReply(ForumPostDto post)
    {
        _replyTo = new ForumQuoteDto(post.Id, post.Author.Name, ForumMarkdown.ToPlainText(post.Body, 140), false, post.Author.UserId);
        StateHasChanged();
        await Task.Yield();
        if (_composer is not null) await _composer.FocusAsync();
    }

    private async Task<bool> SendAsync(string body, List<Guid> files) =>
        await PostAsync(new CreateForumPostRequest(body, _replyTo?.Id, files));

    private async Task SendStickerAsync(ForumStickerDto sticker) =>
        await PostAsync(new CreateForumPostRequest(null, _replyTo?.Id, [], sticker.Id));

    private async Task<bool> PostAsync(CreateForumPostRequest request)
    {
        try
        {
            var created = await Api.CreatePostAsync(TopicId, request);
            _replyTo = null;
            await LoadTopicAsync();
            if (_lastPage >= PageCount - 1) await RefreshPagesAsync(true);
            if (_items?.Any(p => p.Id == created.Id) != true) await LoadAroundAsync(created.Id);
            _readMark = Math.Max(_readMark, created.Id);
            _scrollTarget = "bottom";
            await ListChanged.InvokeAsync();
            return true;
        }
        catch (ApiException ex) { Snackbar.Add(CommunityText.Error(ex), Severity.Error); return false; }
    }

    private async Task WriteToAsync(Guid userId)
    {
        try
        {
            var chat = await Api.OpenDirectAsync(userId);
            Nav.NavigateTo($"{BasePath}/forum/{chat.Topic.Id}");
        }
        catch (ApiException ex) { Snackbar.Add(CommunityText.Error(ex), Severity.Error); }
    }

    private async Task UpdateAsync(UpdateForumTopicRequest request)
    {
        try
        {
            _topic = await Api.UpdateTopicAsync(TopicId, request);
            await ListChanged.InvokeAsync();
        }
        catch (ApiException ex) { Snackbar.Add(CommunityText.Error(ex), Severity.Error); }
    }

    private void StartRename() { _title = _topic?.Topic.Title; _renaming = true; }

    private async Task RenameAsync()
    {
        await UpdateAsync(new(Title: _title));
        _renaming = false;
    }

    private async Task OpenMoveAsync()
    {
        try { _sections = await Api.SectionsAsync(); }
        catch (ApiException ex) { Snackbar.Add(CommunityText.Error(ex), Severity.Error); return; }
        _moveTarget = _topic?.Topic.SubjectId?.ToString() ?? "general";
        _moving = true;
    }

    private async Task MoveAsync()
    {
        _moving = false;
        await UpdateAsync(new(ChangeSubject: true, SubjectId: _moveTarget == "general" ? null : int.Parse(_moveTarget!, CultureInfo.InvariantCulture)));
    }

    private async Task ToggleSubscriptionAsync()
    {
        if (_topic is null) return;
        var subscribe = !_topic.Permissions.IsSubscribed;
        try
        {
            await Api.SubscribeAsync(TopicId, subscribe);
            _topic = _topic with { Permissions = _topic.Permissions with { IsSubscribed = subscribe } };
        }
        catch (ApiException ex) { Snackbar.Add(CommunityText.Error(ex), Severity.Error); }
    }

    private async Task DeleteTopicAsync()
    {
        if (_topic is null || !await Confirm.ConfirmDeleteAsync(_topic.Topic.Title)) return;
        try
        {
            await Api.DeleteTopicAsync(TopicId);
            await ListChanged.InvokeAsync();
            Nav.NavigateTo($"{BasePath}/forum");
        }
        catch (ApiException ex) { Snackbar.Add(CommunityText.Error(ex), Severity.Error); }
    }

    private bool SameSeries(ForumPostDto? first, ForumPostDto? second) =>
        first is not null && second is not null && first.Author.UserId == second.Author.UserId && first.Author.Name == second.Author.Name &&
        (second.CreatedAtUtc - first.CreatedAtUtc).Duration() < TimeSpan.FromMinutes(5);

    private static bool NewDay(ForumPostDto post, ForumPostDto? next) =>
        next is not null && post.CreatedAtUtc.ToLocalTime().Date != next.CreatedAtUtc.ToLocalTime().Date;

    private static string DayLabel(DateTime utc)
    {
        var day = utc.ToLocalTime().Date;
        var today = DateTime.Now.Date;
        if (day == today) return CommunityText.T("Сегодня", "Today");
        if (day == today.AddDays(-1)) return CommunityText.T("Вчера", "Yesterday");
        return day.ToString(day.Year == today.Year ? "d MMMM" : "d MMMM yyyy", CultureInfo.CurrentCulture);
    }

    private static string Excerpt(ForumPostDto post) =>
        post.Sticker is not null ? CommunityText.T("Стикер", "Sticker")
        : string.IsNullOrWhiteSpace(post.Body) ? CommunityText.T("Вложение", "Attachment")
        : ForumMarkdown.ToPlainText(post.Body, 140);

    public async ValueTask DisposeAsync()
    {
        _life.Cancel();
        if (_module is not null)
        {
            try
            {
                await _module.InvokeVoidAsync("unwatchFeed", _feed);
                await _module.DisposeAsync();
            }
            catch (JSDisconnectedException) { }
            catch (ObjectDisposedException) { }
        }
        _self?.Dispose();
        _life.Dispose();
    }
}
