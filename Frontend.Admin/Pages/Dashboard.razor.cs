using System.Text;
using Frontend.Shared.Api;
using Frontend.Shared.Models;
using Frontend.Shared.Services;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Frontend.Admin.Pages;

public partial class Dashboard
{
    [Inject] private SubjectsApi Subjects { get; set; } = null!;
    [Inject] private ResultsApi Results { get; set; } = null!;
    [Inject] private FileDownloader Downloader { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    private List<SubjectDto> _subjects = [];
    private SubjectResultsDto? _results;
    private MudDataGrid<StudentResultRowDto>? _grid;
    private bool _loading;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            _subjects = await Subjects.GetAllAsync();
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

    private async Task ExportCsvAsync()
    {
        if (_results is null)
            return;

        const string delimiter = ";";
        var rows = _grid?.FilteredItems ?? _results.UserResults;

        var sb = new StringBuilder();
        var headers = new List<string> { "ФИО", "Группа" };
        headers.AddRange(_results.TaskHeaders.Select(t => t.TaskName));
        headers.AddRange(_results.GradeColumns.Select(c => c.Name));
        headers.Add("Сумма");
        sb.AppendLine(string.Join(delimiter, headers));

        foreach (var row in rows)
        {
            var cells = new List<string> { row.FullName, row.GroupName };
            cells.AddRange(_results.TaskHeaders.Select(t => CellText(row.Results.GetValueOrDefault(t.TaskId))));
            cells.AddRange(_results.GradeColumns.Select(c =>
                row.Grades.TryGetValue(c.ComponentId, out var p) ? p.ToString() : string.Empty));
            cells.Add(row.TotalPoints.ToString());
            sb.AppendLine(string.Join(delimiter, cells));
        }

        using var stream = new MemoryStream();
        await stream.WriteAsync(Encoding.UTF8.GetPreamble());
        await stream.WriteAsync(Encoding.UTF8.GetBytes(sb.ToString()));
        stream.Position = 0;

        await Downloader.DownloadAsync($"Results_{_results.SubjectName}_{DateTime.Now:yyyy-MM-dd}.csv", stream);
        Snackbar.Add("Экспорт в CSV завершён.", Severity.Success);
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
