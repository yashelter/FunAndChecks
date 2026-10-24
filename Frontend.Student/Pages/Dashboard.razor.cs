using Frontend.Shared.Services;
using Frontend.Shared.Api;
using Frontend.Shared.Models;
using Frontend.Shared.Resources;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using MudBlazor;

namespace Frontend.Student.Pages;

public partial class Dashboard
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
    private StudentSubjectResultsDto? _mine;
    private List<QueueEventDto> _queues = [];
    private CancellationTokenSource? _refresh;

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
        var tableTask = Results.GetSubjectResultsAsync(subjectId);
        var mineTask = includeMine ? Me.GetMyResultsAsync(subjectId) : null;
        var queuesTask = includeMine ? Me.GetMyQueueEventsAsync() : null;
        var table = await tableTask;
        if (_selectedSubject?.Id != subjectId) return;
        _results = table;
        if (mineTask is not null)
        {
            try { _mine = await mineTask; } catch (ApiException) { _mine = null; }
        }
        if (queuesTask is not null)
        {
            try { _queues = await queuesTask; } catch (ApiException) { _queues = []; }
        }
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
    }
}
