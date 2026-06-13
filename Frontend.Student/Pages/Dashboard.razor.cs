using Frontend.Shared.Api;
using Frontend.Shared.Models;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Frontend.Student.Pages;

public partial class Dashboard
{
    [Inject] private MeApi Me { get; set; } = null!;
    [Inject] private ResultsApi Results { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    private List<SubjectDto> _subjects = [];
    private SubjectResultsDto? _results;
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
            _results = await Results.GetSubjectResultsAsync(subject.Id);
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

    private static string CellText(ResultCellDto? cell) =>
        cell?.Status == SubmissionStatus.Accepted ? "+" : cell?.DisplayValue ?? string.Empty;

    private static string CellStyle(ResultCellDto? cell)
    {
        var background = cell?.AdminColor ?? "transparent";
        return $"background-color:{background};text-align:center;color:black;font-weight:bold;" +
               "text-shadow:0 0 3px rgba(255,255,255,0.7);";
    }
}
