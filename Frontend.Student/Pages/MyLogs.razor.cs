using Frontend.Shared.Api;
using Frontend.Shared.Models;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Frontend.Student.Pages;

public partial class MyLogs
{
    [Inject] private MeApi Me { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    private List<SubjectDto> _subjects = [];
    private StudentSubjectResultsDto? _results;
    private bool _loading;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            _subjects = await Me.GetMySubjectsAsync();
        }
        catch (ApiException ex)
        {
            Snackbar.Add(ex.Message, Severity.Error);
        }
    }

    private async Task OnSubjectSelectedAsync(SubjectDto? subject)
    {
        if (subject is null)
            return;

        _loading = true;
        _results = null;

        try
        {
            _results = await Me.GetMyResultsAsync(subject.Id);
        }
        catch (ApiException ex)
        {
            Snackbar.Add($"Не удалось загрузить результаты: {ex.Message}", Severity.Error);
        }
        finally
        {
            _loading = false;
        }
    }

    private static string StatusText(SubmissionStatus status) => status switch
    {
        SubmissionStatus.Rejected => "На доработке",
        SubmissionStatus.Accepted => "Зачтено",
        _ => "Не сдано",
    };

    private static Color StatusColor(SubmissionStatus status) => status switch
    {
        SubmissionStatus.Rejected => Color.Warning,
        SubmissionStatus.Accepted => Color.Success,
        _ => Color.Default,
    };
}
