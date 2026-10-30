using System.Security.Claims;
using Frontend.Shared.Models;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;
using MudBlazor;

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
    public async Task CaptureAsync(string page, MudDataGrid<StudentResultRowDto>? grid, Guid viewInstance)
    {
        if (grid is null || !_pages.TryGetValue(page, out var state)) return;
        state.Filters = grid.FilterDefinitions.Select(filter => new SavedFilter(
            filter.Column is null ? -1 : grid.RenderedColumns.IndexOf(filter.Column), filter.Operator, filter.Value)).Where(f => f.Column >= 0).ToList();
        state.Sorts = grid.SortDefinitions.Values.OrderBy(s => s.Index).Select(sort => new SavedSort(
            grid.RenderedColumns.FindIndex(c => c.PropertyName == sort.SortBy), sort)).Where(s => s.Column >= 0).ToList();
        ReturnUrl = page;
        state.SourceInstance = viewInstance;
        state.RestorePending = true;
        try { await js.InvokeVoidAsync("profileResultNavigation.capture", page); } catch (JSException) { }
    }
    /// <summary>
    /// Возвращает фильтры, сортировку и прокрутку таблицы после возврата из профиля. true — состояние применено:
    /// таблица, которая перерисовывается только при смене данных, должна перерисоваться, чтобы показать новый порядок строк.
    /// </summary>
    public async Task<bool> RestoreAsync(string page, MudDataGrid<StudentResultRowDto>? grid, Guid viewInstance)
    {
        if (grid is null || !_pages.TryGetValue(page, out var state) || !state.RestorePending || state.SourceInstance == viewInstance || grid.RenderedColumns.Count == 0) return false;
        state.RestorePending = false;
        foreach (var filter in state.Filters.Where(f => f.Column < grid.RenderedColumns.Count))
            await grid.AddFilterAsync(new FilterDefinition<StudentResultRowDto> {
                Column = grid.RenderedColumns[filter.Column], Operator = filter.Operator, Value = filter.Value });
        if (state.Filters.Count > 0) await grid.CloseFilterAsync();
        foreach (var sort in state.Sorts.Where(s => s.Column < grid.RenderedColumns.Count))
            await grid.ExtendSortAsync(grid.RenderedColumns[sort.Column].PropertyName,
                sort.Definition.Descending ? SortDirection.Descending : SortDirection.Ascending,
                sort.Definition.SortFunc, sort.Definition.Comparer);
        try { await js.InvokeVoidAsync("profileResultNavigation.restore", page); } catch (JSException) { }
        return true;
    }
    public void Dispose() { if (_subscribed) auth.AuthenticationStateChanged -= Changed; }
    private sealed class Snapshot
    {
        public int SubjectId { get; init; }
        public bool RestorePending { get; set; }
        public Guid SourceInstance { get; set; }
        public List<SavedFilter> Filters { get; set; } = [];
        public List<SavedSort> Sorts { get; set; } = [];
    }
    private record SavedFilter(int Column, string? Operator, object? Value);
    private record SavedSort(int Column, SortDefinition<StudentResultRowDto> Definition);
}
