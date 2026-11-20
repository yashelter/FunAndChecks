using Frontend.Shared.Api;
using Frontend.Shared.Auth;
using Frontend.Shared.Profiles;
using Frontend.Shared.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.JSInterop;
using System.Text.Json;
using MudBlazor;

namespace Frontend.Admin.Pages;

/// <summary>
/// Страница достижения: основное, конструктор эффектов, свой CSS и живой предпросмотр.
/// CSS для предпросмотра собирает сервер (тот же компилятор, что и при сохранении), поэтому
/// «работает в предпросмотре» значит «сохранится и будет выглядеть так же».
/// </summary>
public partial class AchievementEditor : IAsyncDisposable
{
    private const int PreviewDelayMs = 400;

    [Parameter] public int? Id { get; set; }
    [Inject] private ProfileApi Api { get; set; } = null!;
    [Inject] private AuthenticationStateProvider Authentication { get; set; } = null!;
    [Inject] private NavigationManager Nav { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;
    [Inject] private ConfirmService Confirm { get; set; } = null!;
    [Inject] private DashboardAppearanceService Appearance { get; set; } = null!;
    [Inject] private IJSRuntime JS { get; set; } = null!;
    private IJSObjectReference? _community;
    private IJSObjectReference? _draftMediaModule;
    private Guid _draftId = Guid.NewGuid();
    private readonly Dictionary<string, string> _draftUrls = [];
    private readonly HashSet<string> _expiredSlots = [];
    private bool _importing;
    private string _importText = "";
    private string? _importError;

    private readonly CancellationTokenSource _life = new();
    private CancellationTokenSource? _previewDelay;
    private Dictionary<string, MediaDto> _media = [];
    private AchievementEffects _effects = new();
    private List<EffectCssError> _errors = [];
    private MediaDto? _myAvatar;
    private string _name = "", _description = "", _css = "", _previewCss = "", _myName = "";
    private string? _loadError, _error, _slotEditing;
    private bool _loading = true, _superAdmin, _activeByDefault, _dirty, _busy, _previewing;
    private int? _loadedId;
    private bool _loadedOnce;
    private int _previewVersion;

    private string Title => Id is null
        ? ProfileText.T("Новое достижение", "New achievement")
        : string.IsNullOrWhiteSpace(_name) ? ProfileText.T("Достижение", "Achievement") : _name;
    private static string SuperOnly => ProfileText.T("Недостаточно прав: достижениями управляет супер-администратор",
        "Not enough permissions: achievements are managed by a super administrator");
    private string SampleName => string.IsNullOrWhiteSpace(_myName) ? ProfileText.T("Иванов Иван", "Ivan Ivanov") : _myName;
    private string Initials => ProfileText.Initials(SampleName);
    private static string CssPlaceholder => "name { text-shadow: 0 0 8px gold; }\navatar::after { content: \"\"; position: absolute; inset: -18%; background: image(frame) center/contain no-repeat; }";

    private MediaDto? Slot(string slot) => _media.GetValueOrDefault(slot);
    private string? DraftUrl(string slot) => _draftUrls.GetValueOrDefault(slot);
    private string? BadgeUrl => Id is null ? DraftUrl("badge") :
        Slot("badge") is { } badge ? $"/api/achievements/media/{badge.Id}?poster={IsVideo(badge)}" : null;
    private string PreviewCss => Id is not null || _draftUrls.Count == 0 ? _previewCss :
        _previewCss + "[data-fx=\"preview\"]{" + string.Concat(_draftUrls.Select(
            pair => $"--fx-img-{pair.Key}:url(\"{pair.Value}\");")) + "}";

    private static bool IsVideo(MediaDto media) => media.ContentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase);
    private string GuideUrl => "/admin/achievements/guide?returnUrl=" + Uri.EscapeDataString("/" + Nav.ToBaseRelativePath(Nav.Uri));

    private AchievementDraft CurrentDraft() => new(_name, _description, _activeByDefault, _effects,
        string.IsNullOrWhiteSpace(_css) ? null : _css);

    private async Task CopyJsonAsync()
    {
        _community ??= await JS.InvokeAsync<IJSObjectReference>("import", "./_content/Frontend.Shared/community.js");
        var copied = await _community.InvokeAsync<bool>("copyText", CurrentDraft().ToJson());
        Snackbar.Add(copied ? ProfileText.T("JSON скопирован", "JSON copied") : ProfileText.T("Браузер не дал скопировать", "The browser blocked copying"),
            copied ? Severity.Success : Severity.Warning);
    }

    private void OpenImport() { _importText = ""; _importError = null; _importing = true; }

    /// <summary>Заполняет поля из JSON (формат docs/achievement-effects.md). Значения проверяет сервер — в предпросмотре и при сохранении.</summary>
    private void ApplyImport()
    {
        AchievementDraft draft;
        try { draft = AchievementDraft.Parse(_importText); }
        catch (JsonException ex)
        {
            _importError = ex.LineNumber is long line
                ? ProfileText.T($"JSON не разобран (строка {line + 1}): {ex.Message}", $"Could not read the JSON (line {line + 1}): {ex.Message}")
                : ProfileText.T($"JSON не разобран: {ex.Message}", $"Could not read the JSON: {ex.Message}");
            return;
        }
        _importing = false;
        Set(() => Apply(draft));
        Snackbar.Add(ProfileText.T("Поля заполнены. Проверьте предпросмотр и сохраните.", "Fields filled in. Check the preview and save."), Severity.Success);
    }

    private void Apply(AchievementDraft draft)
    {
        if (draft.Name is { } name) _name = name.Length > 100 ? name[..100] : name;
        if (draft.Description is { } description) _description = description;
        if (draft.ActiveByDefault is { } byDefault) _activeByDefault = byDefault;
        if (draft.Effects is { } effects) _effects = Normalize(effects);
        if (draft.CustomCss is { } css) _css = css;
    }

    protected override async Task OnParametersSetAsync()
    {
        if (_loadedOnce && _loadedId == Id) return;
        _loadedOnce = true; _loadedId = Id;
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        await ReleaseDraftUrlsAsync();
        _expiredSlots.Clear();
        _draftId = Guid.NewGuid();
        _loading = true; _loadError = _error = null; _dirty = false; _slotEditing = null;
        try
        {
            var state = await Authentication.GetAuthenticationStateAsync();
            _superAdmin = state.User.IsInRole(Roles.SuperAdmin);
            if (Id is int id)
            {
                var edit = await Api.AchievementForEditAsync(id, _life.Token);
                _name = edit.Achievement.Name;
                _description = edit.Achievement.Description;
                _activeByDefault = edit.Achievement.ActiveByDefault;
                _css = edit.Achievement.CustomCss ?? "";
                _effects = Normalize(edit.Achievement.Effects);
                _media = edit.Media;
            }
            else
            {
                _name = _description = _css = ""; _activeByDefault = false; _effects = new(); _media = [];
            }
            // Предпросмотр на своём аватаре и имени нагляднее, чем на заглушке; без них тоже работает.
            try { var mine = await Api.MineAsync(_life.Token); _myAvatar = mine.Avatar; _myName = mine.FullName; }
            catch (ApiException) { }
        }
        catch (ApiException ex) { _loadError = ex.Message; }
        catch (OperationCanceledException) when (_life.IsCancellationRequested) { return; }
        finally { _loading = false; }
        if (_loadError is null) await RefreshPreviewAsync();
    }

    /// <summary>Разделы, которых нет в сохранённых настройках, приходят как null — редактору нужны все.</summary>
    private static AchievementEffects Normalize(AchievementEffects? effects)
    {
        effects ??= new();
        effects.Avatar ??= new(); effects.Name ??= new(); effects.Row ??= new(); effects.Profile ??= new(); effects.Site ??= new();
        return effects;
    }

    /// <summary>Любая правка: отметить несохранённое и обновить предпросмотр, когда автор остановится.</summary>
    private void Set(Action change)
    {
        change();
        _dirty = true;
        _previewDelay?.Cancel();
        _previewDelay = new CancellationTokenSource();
        _ = PreviewLaterAsync(_previewDelay.Token);
    }

    private async Task PreviewLaterAsync(CancellationToken token)
    {
        try { await Task.Delay(PreviewDelayMs, token); }
        catch (OperationCanceledException) { return; }
        await RefreshPreviewAsync();
        await InvokeAsync(StateHasChanged);
    }

    private async Task RefreshPreviewAsync()
    {
        var version = ++_previewVersion;
        _previewing = true;
        try
        {
            var preview = await Api.PreviewEffectsAsync(new(_effects, _css, Id), _life.Token);
            if (version != _previewVersion) return;
            _errors = preview.Errors;
            // При ошибке оставляем прошлый вид: предпросмотр не должен мигать, пока автор печатает.
            if (preview.Errors.Count == 0) _previewCss = preview.Css;
        }
        catch (ApiException ex) { if (version == _previewVersion) _errors = [new(0, ex.Message)]; }
        catch (OperationCanceledException) when (_life.IsCancellationRequested) { }
        finally { if (version == _previewVersion) _previewing = false; }
    }

    private async Task SaveAsync()
    {
        if (!_superAdmin || string.IsNullOrWhiteSpace(_name)) return;
        _busy = true; _error = null;
        try
        {
            SaveAchievementRequest request = new(_name.Trim(), _description.Trim(), null, null, false, _activeByDefault,
                _effects, _css, Id is null ? _draftId : null,
                Id is null ? _media.ToDictionary(pair => pair.Key, pair => pair.Value.Id) : null);
            if (Id is int id)
            {
                await Api.UpdateAchievementAsync(id, request, _life.Token);
                _dirty = false;
                await Appearance.ReloadEffectsAsync();
                Snackbar.Add(ProfileText.T("Достижение сохранено", "Achievement saved"), Severity.Success);
            }
            else
            {
                var created = await Api.CreateAchievementAsync(request, _life.Token);
                _dirty = false;
                await Appearance.ReloadEffectsAsync();
                Snackbar.Add(ProfileText.T("Достижение создано", "Achievement created"), Severity.Success);
                Nav.NavigateTo($"/admin/achievements/{created.Id}");
            }
        }
        catch (ApiException ex)
        {
            _error = ex.Message;
            if (Id is null && ex.Code == "achievement.draft_media_expired")
            {
                try
                {
                    await CheckDraftSlotsAsync();
                    _error = "Срок хранения картинок истёк. Загрузите отмеченные картинки повторно или уберите их.";
                }
                catch (ApiException checkError)
                {
                    _error = checkError.Message;
                }
            }
        }
        catch (OperationCanceledException) when (_life.IsCancellationRequested) { }
        finally { _busy = false; }
    }

    /// <summary>Картинка сохраняется сразу, отдельно от остальных полей: у неё свой файл на сервере.</summary>
    private async Task<bool> SaveSlotAsync(MediaSelection selection)
    {
        if (!_superAdmin || _slotEditing is not { } slot)
        {
            return false;
        }
        if (Id is not int id)
        {
            MediaDto staged = await Api.StageAchievementMediaAsync(_draftId, slot, selection, _life.Token);
            _media[slot] = staged;
            _expiredSlots.Remove(slot);
            _dirty = true;
            await ReleaseDraftUrlAsync(slot);
            try
            {
                await LoadDraftUrlAsync(slot, staged);
            }
            catch (ApiException ex)
            {
                _error = ex.Message;
            }
            catch (JSException)
            {
                _error = "Картинка подготовлена, но браузеру не удалось показать предпросмотр.";
            }
            return true;
        }
        MediaDto media = await Api.SetAchievementMediaAsync(id, slot, selection, _life.Token);
        _media[slot] = media;
        await Appearance.ReloadEffectsAsync();
        await RefreshPreviewAsync();
        Snackbar.Add(ProfileText.T("Картинка сохранена", "Picture saved"), Severity.Success);
        return true;
    }

    private async Task RemoveSlotAsync(string slot)
    {
        if (!_superAdmin)
        {
            return;
        }
        _busy = true; _error = null;
        try
        {
            if (Id is not int id)
            {
                if (!_expiredSlots.Contains(slot))
                {
                    await Api.RemoveDraftAchievementMediaAsync(_draftId, slot, _life.Token);
                }
                _expiredSlots.Remove(slot);
                _media.Remove(slot);
                _dirty = true;
                await ReleaseDraftUrlAsync(slot);
                return;
            }
            await Api.RemoveAchievementMediaAsync(id, slot, _life.Token);
            _media.Remove(slot);
            await Appearance.ReloadEffectsAsync();
            await RefreshPreviewAsync();
        }
        catch (ApiException ex) { _error = ex.Message; }
        finally { _busy = false; }
    }

    private async Task LoadDraftUrlAsync(string slot, MediaDto media)
    {
        _draftMediaModule ??= await JS.InvokeAsync<IJSObjectReference>("import", _life.Token, ProfileMediaModule.Url);
        await Api.ConsumeDraftAchievementMediaAsync(_draftId, slot, media.Id, IsVideo(media), async stream =>
        {
            using DotNetStreamReference reference = new(stream, leaveOpen: true);
            _draftUrls[slot] = await _draftMediaModule.InvokeAsync<string>("objectUrl", _life.Token,
                reference, IsVideo(media) ? "image/webp" : media.ContentType);
        }, _life.Token);
    }

    private async Task CheckDraftSlotsAsync()
    {
        foreach ((string slot, MediaDto media) in _media)
        {
            try
            {
                await Api.ConsumeDraftAchievementMediaAsync(_draftId, slot, media.Id, IsVideo(media),
                    _ => Task.CompletedTask, _life.Token);
            }
            catch (ApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _expiredSlots.Add(slot);
                await ReleaseDraftUrlAsync(slot);
            }
        }
    }

    private async Task ReleaseDraftUrlAsync(string slot)
    {
        if (_draftUrls.Remove(slot, out string? url) && _draftMediaModule is not null)
        {
            await _draftMediaModule.InvokeVoidAsync("release", url);
        }
    }

    private async Task ReleaseDraftUrlsAsync()
    {
        foreach (string slot in _draftUrls.Keys.ToArray())
        {
            await ReleaseDraftUrlAsync(slot);
        }
    }

    private async Task ConfirmLeaveAsync(LocationChangingContext context)
    {
        if (!_dirty) return;
        if (!await Confirm.ConfirmAsync(ProfileText.T("Уйти без сохранения?", "Leave without saving?"),
                ProfileText.T("Изменения достижения не сохранены и пропадут.", "Your changes to the achievement are not saved and will be lost."),
                ProfileText.T("Уйти", "Leave")))
            context.PreventNavigation();
    }

    public async ValueTask DisposeAsync()
    {
        _previewDelay?.Cancel();
        _life.Cancel();
        _life.Dispose();
        try
        {
            await ReleaseDraftUrlsAsync();
            if (_draftMediaModule is not null)
            {
                await _draftMediaModule.DisposeAsync();
            }
            if (_community is not null)
            {
                await _community.DisposeAsync();
            }
        }
        catch (JSDisconnectedException) { }
    }
}
