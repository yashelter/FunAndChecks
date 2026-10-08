using Frontend.Shared.Api;
using Frontend.Shared.Models;
using Frontend.Shared.Services;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Frontend.Admin.Pages;

public partial class Dashboard
{
    [Inject] private MeApi Me { get; set; } = null!;
    [Inject] private ResultsApi Results { get; set; } = null!;
    [Inject] private AttendanceApi Attendance { get; set; } = null!;
    [Inject] private FileDownloader Downloader { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    private List<SubjectDto> _subjects = [];
    private SubjectResultsDto? _results;
    private MudDataGrid<StudentResultRowDto>? _grid;
    private bool _loading;
    private Dictionary<Guid, (int Present, int Absent, int Unmarked)>? _attendance;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            _subjects = await Me.GetVisibleSubjectsAsync();
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
        _attendance = null;

        try
        {
            _results = await Results.GetSubjectResultsAsync(subject.Id);
            var journal = await Attendance.GetJournalAsync(subject.Id);
            if (journal.Enabled || journal.Sessions.Count > 0)
                _attendance = journal.Students.GroupBy(s => s.StudentId).ToDictionary(g => g.Key,
                    g => (g.Sum(s => s.Present), g.Sum(s => s.Absent), g.Sum(s => s.Unmarked)));
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

    private async Task ExportXlsxAsync()
    {
        if (_results is null)
            return;

        try
        {
            var bytes = await Results.ExportXlsxAsync(_results.SubjectId);
            using var stream = new MemoryStream(bytes);
            await Downloader.DownloadAsync($"Results_{_results.SubjectName}_{DateTime.Now:yyyy-MM-dd}.xlsx", stream);
            Snackbar.Add("Экспорт в XLSX завершён.", Severity.Success);
        }
        catch (ApiException ex)
        {
            Snackbar.Add($"Не удалось экспортировать: {ex.Message}", Severity.Error);
        }
    }

    private static string FioStyle(string? color)
    {
        if (string.IsNullOrEmpty(color))
            return string.Empty;
        return $"background-color:{color};color:{Frontend.Shared.UI.ColorUtils.ContrastText(color)};" +
               "padding:2px 8px;border-radius:6px;display:inline-block;";
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
