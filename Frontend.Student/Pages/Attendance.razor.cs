using Frontend.Shared.Api;
using Frontend.Shared.Models;
using Microsoft.AspNetCore.Components;

namespace Frontend.Student.Pages;

public partial class Attendance
{
    [Inject] private AttendanceApi Api { get; set; } = null!;
    private List<SubjectDto> _subjects = [];
    private List<StudentAttendanceDto> _history = [];
    private int? _subjectId;
    private bool _loading = true;
    private string? _error;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            _subjects = await Api.GetMySubjectsAsync();
            _subjectId = _subjects.FirstOrDefault()?.Id;
            await LoadAsync();
        }
        catch (Exception ex) when (ex is ApiException or HttpRequestException) { _error = ex.Message; }
        finally { _loading = false; }
    }

    private async Task ChangeSubjectAsync(int? id) { _subjectId = id; await LoadAsync(); }
    private async Task LoadAsync()
    {
        _history = [];
        _error = null;
        if (_subjectId is null) { _loading = false; return; }
        _loading = true;
        try { _history = await Api.GetMyHistoryAsync(_subjectId.Value); }
        catch (Exception ex) when (ex is ApiException or HttpRequestException) { _error = ex.Message; }
        finally { _loading = false; }
    }
}
