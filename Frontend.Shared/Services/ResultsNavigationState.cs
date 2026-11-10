using System.Security.Claims;
using Frontend.Shared.UI;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;

namespace Frontend.Shared.Services;

/// <summary>Remembers result-table state for profile visits within the current signed-in session.</summary>
public sealed class ResultsNavigationState(AuthenticationStateProvider auth, IJSRuntime js) : IDisposable
{
    private readonly Dictionary<string, Snapshot> _pages = new();
    private string? _account;
    private bool _subscribed;
    public string ReturnUrl { get; private set; } = "/results";

    public async Task InitializeAsync()
    {
        if (!_subscribed) { auth.AuthenticationStateChanged += Changed; _subscribed = true; }
        CheckAccount(await auth.GetAuthenticationStateAsync());
    }
    private async void Changed(Task<AuthenticationState> state)
    {
        try { CheckAccount(await state); } catch { _pages.Clear(); }
    }
    private void CheckAccount(AuthenticationState state)
    {
        var id = state.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (id != _account) { _pages.Clear(); ReturnUrl = "/results"; _account = id; }
    }
    public int? Subject(string page) => _pages.GetValueOrDefault(page)?.SubjectId;
    public void SelectSubject(string page, int subjectId)
    {
        if (!_pages.TryGetValue(page, out var state) || state.SubjectId != subjectId)
            _pages[page] = new Snapshot { SubjectId = subjectId };
    }
    /// <summary>Запомнить поиск, фильтр, сортировку и прокрутку таблицы перед переходом в профиль.</summary>
    public async Task CaptureAsync(string page, ResultsViewState view, Guid viewInstance)
    {
        if (!_pages.TryGetValue(page, out var state)) return;
        state.View = view;
        ReturnUrl = page;
        state.SourceInstance = viewInstance;
        state.RestorePending = true;
        try { await js.InvokeVoidAsync("profileResultNavigation.capture", page); } catch (JSException) { }
    }
    /// <summary>
    /// Состояние таблицы после возврата из профиля — один раз и только новому экземпляру таблицы.
    /// null — возвращать нечего. Таблица применяет его, перерисовывается и затем зовёт <see cref="RestoreScrollAsync"/>.
    /// </summary>
    public ResultsViewState? TakeRestore(string page, Guid viewInstance)
    {
        if (!_pages.TryGetValue(page, out var state) || !state.RestorePending || state.SourceInstance == viewInstance) return null;
        state.RestorePending = false;
        return state.View;
    }
    /// <summary>Вернуть прокрутку страницы и таблицы — когда строки в восстановленном порядке уже отрисованы.</summary>
    public async Task RestoreScrollAsync(string page)
    {
        try { await js.InvokeVoidAsync("profileResultNavigation.restore", page); } catch (JSException) { }
    }
    public void Dispose() { if (_subscribed) auth.AuthenticationStateChanged -= Changed; }
    private sealed class Snapshot
    {
        public int SubjectId { get; init; }
        public bool RestorePending { get; set; }
        public Guid SourceInstance { get; set; }
        public ResultsViewState View { get; set; } = ResultsViewState.Default;
    }
}
