using Frontend.Shared.Api;
using Frontend.Shared.Models;
using Frontend.Shared.Resources;
using Frontend.Shared.Services;
using Frontend.Shared.UI;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using MudBlazor;

namespace Frontend.Admin.Pages;

public partial class Dashboard
{
    [Inject] private MeApi Me { get; set; } = null!;
    [Inject] private ResultsApi Results { get; set; } = null!;
    [Inject] private AttendanceApi Attendance { get; set; } = null!;
    [Inject] private FileDownloader Downloader { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;
    [Inject] private IStringLocalizer<AppStrings> Loc { get; set; } = null!;
    [Inject] private IDialogService Dialogs { get; set; } = null!;

    private List<SubjectDto> _subjects = [];
    private SubjectDto? _selectedSubject;
    private SubjectResultsDto? _results;
    private MudDataGrid<StudentResultRowDto>? _grid;
    private bool _loading;
    private Dictionary<Guid, (int Present, int Absent, int Unmarked)>? _attendance;
    private string AveragePoints => _results?.UserResults is { Count: > 0 } rows
        ? rows.Average(row => row.TotalPoints).ToString("0.##", System.Globalization.CultureInfo.CurrentCulture)
        : "—";

    private string HistoryLabel(StudentResultRowDto row, TaskHeaderDto task) =>
        $"{row.FullName}, {task.TaskName}: {Loc[row.Results.GetValueOrDefault(task.TaskId)?.Status == SubmissionStatus.Accepted ? "Task_StatusAccepted" : "Task_StatusRejected"]}, {Loc["Dialog_History"]}";

    private async Task OpenHistoryAsync(StudentResultRowDto row, TaskHeaderDto task)
    {
        if (_results is null || row.Results.GetValueOrDefault(task.TaskId)?.Status is not
            (SubmissionStatus.Accepted or SubmissionStatus.Rejected))
            return;

        var parameters = new DialogParameters<ResultHistoryDialog>
        {
            { dialog => dialog.StudentId, row.StudentId },
            { dialog => dialog.StudentName, row.FullName },
            { dialog => dialog.TaskId, task.TaskId },
            { dialog => dialog.TaskName, task.TaskName },
        };
        await Dialogs.ShowAsync<ResultHistoryDialog>(Loc["Dialog_History"], parameters,
            new DialogOptions { FullWidth = true, MaxWidth = MaxWidth.Small, CloseButton = true });
    }

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

        _selectedSubject = subject;
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
            Snackbar.Add(string.Format(Loc["Common_LoadResultsError"], ex.Message), Severity.Error);
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
            Snackbar.Add(Loc["Dashboard_ExportDone"], Severity.Success);
        }
        catch (ApiException ex)
        {
            Snackbar.Add(string.Format(Loc["Dashboard_ExportError"], ex.Message), Severity.Error);
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
        if (cell is null || cell.Status == SubmissionStatus.Accepted || string.IsNullOrWhiteSpace(cell.AdminColor))
            return string.Empty;

        // The letter and its teacher's color carry information independently from the theme accent.
        return $"background-color:{cell.AdminColor};color:{Frontend.Shared.UI.ColorUtils.ContrastText(cell.AdminColor)};";
    }

    private static string CellClass(ResultCellDto? cell) => cell?.Status switch
    {
        SubmissionStatus.Accepted => "fc-result-accepted",
        SubmissionStatus.Rejected => "fc-result-rework",
        _ => "fc-result-unsubmitted",
    };
}
