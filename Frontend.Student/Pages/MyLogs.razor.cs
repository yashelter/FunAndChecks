using Microsoft.JSInterop;
using Frontend.Shared.Services;
using Frontend.Shared.Api;
using Frontend.Shared.Models;
using Frontend.Shared.Resources;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using MudBlazor;

namespace Frontend.Student.Pages;

public partial class MyLogs
{
    [Inject] private MeApi Me { get; set; } = null!;
    [Inject] private CurrentSubject CurrentSubject { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;
    [Inject] private IStringLocalizer<AppStrings> Loc { get; set; } = null!;
    [Inject] private Microsoft.JSInterop.IJSRuntime JS { get; set; } = null!;

    /// <summary>Задача из ссылки (из таблицы, уведомления или карточки статуса) — раскрыта и прокручена.</summary>
    [SupplyParameterFromQuery(Name = "task")] public int? FocusTask { get; set; }
    private bool _focusPending = true;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!_focusPending || FocusTask is not int task || _results is null) return;
        _focusPending = false;
        try
        {
            await using var module = await JS.InvokeAsync<Microsoft.JSInterop.IJSObjectReference>("import", "./_content/Frontend.Shared/community.js");
            await module.InvokeAsync<bool>("scrollToElement", $"task-{task}");
        }
        catch (Microsoft.JSInterop.JSException) { }
    }

    private List<SubjectDto> _subjects = [];
    private SubjectDto? _selectedSubject;
    private StudentSubjectResultsDto? _results;
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
        await CurrentSubject.SetAsync(subject.Id);
        _loading = true;
        _results = null;

        try
        {
            _results = await Me.GetMyResultsAsync(subject.Id);
        }
        catch (ApiException ex)
        {
            Snackbar.Add(string.Format(Loc["Common_LoadResultsError"], ex.Message), Severity.Error);
        }
        finally
        {
            _loading = false;
        }
    }

    private string StatusText(SubmissionStatus status) => status switch
    {
        SubmissionStatus.Rejected => Loc["Task_StatusRejected"],
        SubmissionStatus.Accepted => Loc["Task_StatusAccepted"],
        _ => Loc["Task_StatusNotSubmitted"],
    };

    private static Color StatusColor(SubmissionStatus status) => status switch
    {
        SubmissionStatus.Rejected => Color.Warning,
        SubmissionStatus.Accepted => Color.Success,
        _ => Color.Default,
    };
}
