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
    [Inject] private ISnackbar Snackbar { get; set; } = null!;
    [Inject] private IStringLocalizer<AppStrings> Loc { get; set; } = null!;

    private List<SubjectDto> _subjects = [];
    private SubjectDto? _selectedSubject;
    private StudentSubjectResultsDto? _results;
    private bool _loading = true;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            _subjects = await Me.GetMySubjectsAsync();
            if (_subjects.FirstOrDefault() is { } subject)
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
