using Frontend.Shared.Services;
using Frontend.Shared.Api;
using Frontend.Shared.Models;
using Frontend.Shared.Resources;
using Frontend.Shared.UI;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using MudBlazor;

namespace Frontend.Student.Pages;

public partial class Dashboard : IDisposable
{
    [Inject] private MeApi Me { get; set; } = null!;
    [Inject] private CurrentSubject CurrentSubject { get; set; } = null!;
    [Inject] private ResultsApi Results { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;
    [Inject] private IStringLocalizer<AppStrings> Loc { get; set; } = null!;
    [Inject] private NavigationManager Nav { get; set; } = null!;
    [Inject] private Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider Authentication { get; set; } = null!;

    private List<SubjectDto> _subjects = [];
    private SubjectDto? _selectedSubject;
    private SubjectResultsDto? _results;
    private bool _loading = true;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            _subjects = await Me.GetMySubjectsAsync();
            var id = await CurrentSubject.PickAsync(_subjects.Select(s => s.Id));
            if ((_subjects.FirstOrDefault(s => s.Id == id) ?? _subjects.FirstOrDefault()) is { } subject)
                await OnSubjectSelectedAsync(subject);
        }
        catch (ApiException ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
        }
        finally
        {
            _loading = false;
        }
    }

    private async Task OnSubjectSelectedAsync(SubjectDto? subject)
    {
        if (subject is null)
            return;

        _selectedSubject = subject;
        _refresh?.Cancel();
        _refreshMine = false;
        // Карточка прошлого предмета не должна остаться под новым, пока не пришли свои результаты.
        _mine = null;
        await CurrentSubject.SetAsync(subject.Id);
        _loading = true;
        _results = null;

        try
        {
            await LoadAsync(subject.Id);
        }
        catch (ApiException ex) when (_selectedSubject?.Id == subject.Id)
        {
            Snackbar.Add(string.Format(Loc["Common_LoadResultsError"], ex.Message), Severity.Error);
        }
        catch (ApiException) { /* Ответ по уже не выбранному предмету. */ }
        finally
        {
            if (_selectedSubject?.Id == subject.Id) _loading = false;
        }
    }

    private Guid? _myId;
    private SubjectResultsView? _view;
    private StudentSubjectResultsDto? _mine;

    /// <summary>Своя строка в том же снимке, что и таблица: место и размер рейтинга обновляются вместе с ней.</summary>
    private StudentResultRowDto? MyRow => _myId is { } id ? _results?.UserResults.FirstOrDefault(r => r.StudentId == id) : null;

    /// <summary>Очередь из полосы изменили или архивировали: перечитать только свои текущие очереди.</summary>
    private async Task ReloadQueuesAsync()
    {
        try
        {
            _queues = await Me.GetMyQueueEventsAsync(ct: _life.Token);
            StateHasChanged();
        }
        catch (ApiException) { /* Следующая загрузка страницы подтянет список. */ }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
    }

    /// <summary>«К моей строке»; если строку скрывает фильтр таблицы — предложить его сбросить.</summary>
    private async Task JumpToMyRowAsync()
    {
        if (_view is null || _myId is not { } id) return;
        if (await _view.ScrollToStudentAsync(id) != ResultsRowJump.HiddenByFilter) return;
        Snackbar.Add(Loc["Results_RowHiddenByFilter"], Severity.Info, config =>
        {
            config.Action = Loc["Results_ResetFilter"];
            config.OnClick = async _ => { if (_view is not null) await _view.ScrollToStudentAsync(id, clearFilters: true); };
        });
    }
    private List<QueueEventDto> _queues = [];
    private CancellationTokenSource? _refresh;
    private readonly CancellationTokenSource _life = new();
    // Версии загрузок: ответ применяется, только если после него не стартовала более новая загрузка.
    // Проверка одного id предмета не спасает при быстром A → B → A.
    private int _tableVersion, _mineVersion;

    // Своя отметка пришла в окне склейки событий: при обновлении перечитать и карточку статуса.
    private bool _refreshMine;

    /// <summary>
    /// Таблица группы и (если нужно) свои результаты и очереди — параллельно. Ответ применяется,
    /// только если предмет за это время не сменили.
    /// </summary>
    private async Task LoadAsync(int subjectId, bool includeMine = true)
    {
        if (_myId is null)
        {
            var user = (await Authentication.GetAuthenticationStateAsync()).User;
            if (Guid.TryParse(user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var id)) _myId = id;
        }
        var ct = _life.Token;
        var tableVersion = ++_tableVersion;
        var mineVersion = includeMine ? ++_mineVersion : -1;
        var tableTask = Results.GetSubjectResultsAsync(subjectId, ct);
        var mineTask = includeMine ? Me.GetMyResultsAsync(subjectId, ct) : null;
        var queuesTask = includeMine ? Me.GetMyQueueEventsAsync(ct: ct) : null;
        var table = await tableTask;
        if (tableVersion == _tableVersion && _selectedSubject?.Id == subjectId)
            _results = table;
        if (mineTask is null || queuesTask is null) return;

        StudentSubjectResultsDto? mine;
        try { mine = await mineTask; } catch (ApiException) { mine = null; }
        List<QueueEventDto> queues;
        try { queues = await queuesTask; } catch (ApiException) { queues = []; }
        if (mineVersion != _mineVersion || _selectedSubject?.Id != subjectId) return;
        _mine = mine;
        _queues = queues;
    }

    public void Dispose()
    {
        _refresh?.Cancel();
        _life.Cancel();
        _life.Dispose();
    }

    /// <summary>
    /// Живое обновление: таблица перечитывается тихо (пачка событий — одним запросом),
    /// а про свою задачу студент получает уведомление со ссылкой на комментарий.
    /// </summary>
    private async Task OnResultUpdatedAsync(ResultUpdateDto update)
    {
        if (_selectedSubject is null) return;
        if (update.StudentId == _myId)
        {
            _refreshMine = true;
            var name = _mine?.TaskResults.FirstOrDefault(t => t.TaskId == update.TaskId)?.TaskName ?? "";
            var accepted = update.NewStatus == nameof(SubmissionStatus.Accepted);
            if (update.NewStatus is nameof(SubmissionStatus.Accepted) or nameof(SubmissionStatus.Rejected))
                Snackbar.Add(Loc[accepted ? "Results_ToastAccepted" : "Results_ToastRework", name], accepted ? Severity.Success : Severity.Warning, config =>
                {
                    config.Action = Loc["Results_ToastOpen"];
                    config.OnClick = _ => { Nav.NavigateTo($"/student/logs?task={update.TaskId}"); return Task.CompletedTask; };
                });
        }
        _refresh?.Cancel();
        var refresh = _refresh = new CancellationTokenSource();
        try
        {
            await Task.Delay(400, refresh.Token);
            // Чужие отметки меняют только таблицу (она кешируется на сервере); своё — ещё и карточку статуса.
            var includeMine = _refreshMine;
            _refreshMine = false;
            await LoadAsync(_selectedSubject.Id, includeMine);
            StateHasChanged();
        }
        catch (OperationCanceledException) { }
        catch (ApiException) { /* Следующее событие или ручное обновление подтянут данные. */ }
        catch (ObjectDisposedException) { /* Страницу уже закрыли. */ }
    }
}
