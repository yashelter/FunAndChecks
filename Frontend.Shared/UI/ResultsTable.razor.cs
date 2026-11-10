using Frontend.Shared.Models;
using Frontend.Shared.Resources;
using Frontend.Shared.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Localization;
using Microsoft.JSInterop;

namespace Frontend.Shared.UI;

public partial class ResultsTable : IAsyncDisposable
{
    [Inject] private IStringLocalizer<AppStrings> Loc { get; set; } = null!;
    [Inject] private ResultsNavigationState ProfileReturn { get; set; } = null!;
    [Inject] private NavigationManager Nav { get; set; } = null!;
    [Inject] private IJSRuntime JS { get; set; } = null!;
    [CascadingParameter] private Task<AuthenticationState>? AuthState { get; set; }

    /// <summary>Снимок предмета: из него берутся колонки задач и оценок.</summary>
    [Parameter, EditorRequired] public SubjectResultsDto Results { get; set; } = null!;
    /// <summary>Строки, если они живут отдельно от снимка (обновление цветов); по умолчанию — строки снимка.</summary>
    [Parameter] public List<StudentResultRowDto>? Rows { get; set; }
    /// <summary>Ключ состояния возврата из профиля — путь страницы с таблицей.</summary>
    [Parameter, EditorRequired] public string StateKey { get; set; } = "";
    /// <summary>Колонка «Место» — в кабинетах; публичная таблица показывается без неё.</summary>
    [Parameter] public bool ShowRank { get; set; }
    /// <summary>Строка текущего студента — подсвечена, её ячейки ведут к истории.</summary>
    [Parameter] public Guid? HighlightStudentId { get; set; }
    /// <summary>Таблица преподавателя: ячейки задач — кнопки, клик по любой сообщается через <see cref="OnCellClick"/>.</summary>
    [Parameter] public bool Interactive { get; set; }
    [Parameter] public EventCallback<ResultCellClick> OnCellClick { get; set; }
    /// <summary>Колонки после «Группы», перед задачами (варианты).</summary>
    [Parameter] public IReadOnlyList<ResultsExtraColumn>? LeadingColumns { get; set; }
    /// <summary>Колонки после оценок (посещаемость).</summary>
    [Parameter] public IReadOnlyList<ResultsExtraColumn>? TrailingColumns { get; set; }

    private ElementReference _host;
    private DotNetObjectReference<ResultsTable>? _self;
    private readonly Guid _instance = Guid.NewGuid();

    private ResultsTableLayout? _layout;
    private List<StudentResultRowDto> _rows = [];
    private List<StudentResultRowDto> _visible = [];
    private List<string> _groups = [];
    private ResultsViewState _view = ResultsViewState.Default;
    private string? _profileBase;
    private string _rankTitle = "", _totalTitle = "";

    // Что было показано: таблица рендерится, только если что-то из этого сменилось.
    private (SubjectResultsDto? Results, List<StudentResultRowDto>? Rows, object? Leading, object? Trailing,
        bool Rank, Guid? Highlight, bool Interactive, string? Culture, string? Key) _shown;
    private bool _dirty = true;
    private bool _attachPending;
    private bool _restoreScroll;

    protected override async Task OnInitializedAsync()
    {
        // Роль зрителя — один раз на таблицу, а не AuthorizeView в каждой строке.
        if (AuthState is null) return;
        var user = (await AuthState).User;
        _profileBase = user.Identity?.IsAuthenticated != true ? null
            : user.IsInRole("Admin") || user.IsInRole("SuperAdmin") ? "/admin" : "/student";
    }

    protected override void OnParametersSet()
    {
        var rows = Rows ?? Results.UserResults;
        var culture = System.Globalization.CultureInfo.CurrentUICulture.Name;
        if (_layout is not null && ReferenceEquals(_shown.Results, Results) && ReferenceEquals(_shown.Rows, rows)
            && ReferenceEquals(_shown.Leading, LeadingColumns) && ReferenceEquals(_shown.Trailing, TrailingColumns)
            && _shown.Rank == ShowRank && _shown.Highlight == HighlightStudentId && _shown.Interactive == Interactive
            && _shown.Culture == culture && _shown.Key == StateKey)
            return;

        // Другой предмет — поиск и сортировка прошлого к нему не относятся. Живое обновление того же предмета их сохраняет.
        if (_shown.Results is not null && _shown.Results.SubjectId != Results.SubjectId)
            _view = ResultsViewState.Default;
        _shown = (Results, rows, LeadingColumns, TrailingColumns, ShowRank, HighlightStudentId, Interactive, culture, StateKey);
        _rows = rows;
        _groups = rows.Select(r => r.GroupName).Where(g => !string.IsNullOrEmpty(g)).Distinct()
            .OrderBy(g => g, StringComparer.CurrentCultureIgnoreCase).ToList();
        if (_view.Group is not null && !_groups.Contains(_view.Group))
            _view = _view with { Group = null };
        _rankTitle = Loc["Results_ColRank"];
        _totalTitle = Loc["Dashboard_TotalPoints"];
        _layout = new ResultsTableLayout
        {
            Tasks = Results.TaskHeaders,
            Grades = Results.GradeColumns,
            Leading = LeadingColumns ?? [],
            Trailing = TrailingColumns ?? [],
            ShowRank = ShowRank,
            Interactive = Interactive,
            HighlightStudentId = HighlightStudentId,
            ProfileBase = _profileBase,
            ReturnUrl = Uri.EscapeDataString("/" + Nav.ToBaseRelativePath(Nav.Uri)),
            YouLabel = Loc["Results_You"],
            TotalStudents = rows.Count,
            RankCounts = ResultsColumns.RankCounts(rows),
            RankTitleFormat = (rank, total, shared) => Loc[shared ? "Results_RankShared" : "Results_MyRank", rank, total],
            OpenProfile = OpenProfileAsync,
        };
        Recompute();
        _attachPending = true;
    }

    /// <summary>
    /// Страница перерисовывается часто (статистика, уведомления, события SignalR) — таблица только при смене данных,
    /// поиска или сортировки. Клик по ячейке её не перерисовывает: диалог открывает страница. Переключатель цветов
    /// преподавателей меняет лишь класс панели (<c>fc-results-show-teacher</c>), а цвет уже лежит в ячейке.
    /// </summary>
    protected override bool ShouldRender() => _dirty;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        _dirty = false;
        if (_layout is null) return;
        // Возврат из профиля: сначала показать строки в сохранённом порядке, прокрутку вернуть следующим рендером.
        if (ProfileReturn.TakeRestore(StateKey, _instance) is { } saved)
        {
            _view = saved.Group is not null && !_groups.Contains(saved.Group) ? saved with { Group = null } : saved;
            Recompute();
            _restoreScroll = true;
            StateHasChanged();
            return;
        }
        if (_attachPending)
        {
            _attachPending = false;
            _self ??= DotNetObjectReference.Create(this);
            try
            {
                await JS.InvokeVoidAsync("resultsScroll.attach", _host, new
                {
                    dotnet = Interactive ? _self : null,
                    accepted = Loc["Task_StatusAccepted"].Value,
                    rejected = Loc["Task_StatusRejected"].Value,
                    none = Loc["Task_StatusNotSubmitted"].Value,
                    action = Interactive ? Loc["Dashboard_CellAction"].Value : null,
                });
            }
            catch (JSException) { }
        }
        if (_restoreScroll)
        {
            _restoreScroll = false;
            await ProfileReturn.RestoreScrollAsync(StateKey);
        }
    }

    private void Recompute()
    {
        _visible = ResultsRowLocator.Apply(_rows, _view, ExtraSortKey);
        _dirty = true;
    }

    private Func<StudentResultRowDto, IComparable>? ExtraSortKey(string key) =>
        (LeadingColumns ?? []).Concat(TrailingColumns ?? []).FirstOrDefault(c => c.Key == key)?.SortKey;

    private void OnSearchChanged(string? value)
    {
        _view = _view with { Search = value ?? "" };
        Recompute();
    }

    private void OnGroupChanged(string? value)
    {
        _view = _view with { Group = string.IsNullOrEmpty(value) ? null : value };
        Recompute();
    }

    private void ToggleSort(string key)
    {
        _view = _view.ToggleSort(key);
        Recompute();
    }

    private string SortClass(string key) =>
        _view.SortKey != key ? "fc-results-sort" : _view.Descending ? "fc-results-sort desc" : "fc-results-sort asc";

    private string? AriaSort(string key) =>
        _view.SortKey != key ? null : _view.Descending ? "descending" : "ascending";

    private async Task OpenProfileAsync(Guid studentId)
    {
        if (_layout?.ProfileUrl(studentId) is not { } url) return;
        await ProfileReturn.CaptureAsync(StateKey, _view, _instance);
        Nav.NavigateTo(url);
    }

    /// <summary>Клик по ячейке задачи из results-scroll.js: один обработчик на всю таблицу.</summary>
    [JSInvokable]
    public Task OnCellClickFromJs(string studentId, int taskId)
    {
        if (!Guid.TryParse(studentId, out var id)
            || _rows.FirstOrDefault(r => r.StudentId == id) is not { } row
            || Results.TaskHeaders.FirstOrDefault(t => t.TaskId == taskId) is not { } task)
            return Task.CompletedTask;
        return OnCellClick.InvokeAsync(new ResultCellClick(row, task));
    }

    /// <summary>
    /// Прокрутить таблицу к строке студента. Строки виртуализированы, поэтому позиция считается по порядку
    /// видимых строк (поиск + группа + сортировка), а не ищется в DOM. clearFilters — сначала сбросить поиск и группу.
    /// </summary>
    public async Task<ResultsRowJump> ScrollToStudentAsync(Guid studentId, bool clearFilters = false)
    {
        if (_layout is null || !_rows.Any(r => r.StudentId == studentId))
            return ResultsRowJump.Missing;
        if (clearFilters && _view.HasFilter)
        {
            _view = _view.WithoutFilter();
            Recompute();
            StateHasChanged();
            await Task.Yield();
        }
        var index = ResultsRowLocator.VisibleIndex(_visible, studentId);
        if (index < 0)
            return ResultsRowJump.HiddenByFilter;
        try { await JS.InvokeVoidAsync("resultsScroll.scrollToRow", _host, index, ResultsColumns.RowHeight); }
        catch (JSException) { }
        return ResultsRowJump.Shown;
    }

    public async ValueTask DisposeAsync()
    {
        try { await JS.InvokeVoidAsync("resultsScroll.detach", _host); }
        catch (JSException) { }
        catch (JSDisconnectedException) { }
        _self?.Dispose();
    }
}
