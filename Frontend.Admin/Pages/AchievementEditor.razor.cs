using Frontend.Shared.Api;
using Frontend.Shared.Auth;
using Frontend.Shared.Profiles;
using Frontend.Shared.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Routing;
using MudBlazor;

namespace Frontend.Admin.Pages;

/// <summary>
/// Страница достижения: основное, конструктор эффектов, свой CSS и живой предпросмотр.
/// CSS для предпросмотра собирает сервер (тот же компилятор, что и при сохранении), поэтому
/// «работает в предпросмотре» значит «сохранится и будет выглядеть так же».
/// </summary>
public partial class AchievementEditor : IDisposable
{
    private const int PreviewDelayMs = 400;

    [Parameter] public int? Id { get; set; }
    [Inject] private ProfileApi Api { get; set; } = null!;
    [Inject] private AuthenticationStateProvider Authentication { get; set; } = null!;
    [Inject] private NavigationManager Nav { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;
    [Inject] private ConfirmService Confirm { get; set; } = null!;
    [Inject] private DashboardAppearanceService Appearance { get; set; } = null!;

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

    protected override async Task OnParametersSetAsync()
    {
        if (_loadedOnce && _loadedId == Id) return;
        _loadedOnce = true; _loadedId = Id;
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
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
            var request = new SaveAchievementRequest(_name.Trim(), _description.Trim(), null, null, false, _activeByDefault, _effects, _css);
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
                Snackbar.Add(ProfileText.T("Достижение создано. Теперь можно загрузить картинки.", "Achievement created. You can upload pictures now."), Severity.Success);
                Nav.NavigateTo($"/admin/achievements/{created.Id}");
            }
        }
        catch (ApiException ex) { _error = ex.Message; }
        catch (OperationCanceledException) when (_life.IsCancellationRequested) { }
        finally { _busy = false; }
    }

    /// <summary>Картинка сохраняется сразу, отдельно от остальных полей: у неё свой файл на сервере.</summary>
    private async Task<bool> SaveSlotAsync(MediaSelection selection)
    {
        if (Id is not int id || _slotEditing is not { } slot) return false;
        var media = await Api.SetAchievementMediaAsync(id, slot, selection, _life.Token);
        _media[slot] = media;
        await Appearance.ReloadEffectsAsync();
        await RefreshPreviewAsync();
        Snackbar.Add(ProfileText.T("Картинка сохранена", "Picture saved"), Severity.Success);
        return true;
    }

    private async Task RemoveSlotAsync(string slot)
    {
        if (Id is not int id || !_superAdmin) return;
        _busy = true; _error = null;
        try
        {
            await Api.RemoveAchievementMediaAsync(id, slot, _life.Token);
            _media.Remove(slot);
            await Appearance.ReloadEffectsAsync();
            await RefreshPreviewAsync();
        }
        catch (ApiException ex) { _error = ex.Message; }
        finally { _busy = false; }
    }

    private async Task ConfirmLeaveAsync(LocationChangingContext context)
    {
        if (!_dirty) return;
        if (!await Confirm.ConfirmAsync(ProfileText.T("Уйти без сохранения?", "Leave without saving?"),
                ProfileText.T("Изменения достижения не сохранены и пропадут.", "Your changes to the achievement are not saved and will be lost."),
                ProfileText.T("Уйти", "Leave")))
            context.PreventNavigation();
    }

    public void Dispose()
    {
        _previewDelay?.Cancel();
        _life.Cancel();
        _life.Dispose();
    }
}
