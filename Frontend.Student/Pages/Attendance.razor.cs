using Frontend.Shared.Services;
using Frontend.Shared.Api;
using Frontend.Shared.Models;
using Microsoft.AspNetCore.Components;

namespace Frontend.Student.Pages;

public partial class Attendance
{
    [Inject] private AttendanceApi Api { get; set; } = null!;
    [Inject] private Microsoft.Extensions.Localization.IStringLocalizer<Frontend.Shared.Resources.AppStrings> Loc { get; set; } = null!;
    [Inject] private CurrentSubject CurrentSubject { get; set; } = null!;
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
            _subjectId = await CurrentSubject.PickAsync(_subjects.Select(s => s.Id)) ?? _subjects.FirstOrDefault()?.Id;
            await LoadAsync();
        }
        catch (Exception ex) when (ex is ApiException or HttpRequestException) { _error = ex.Message; }
        finally { _loading = false; }
    }

    private async Task ChangeSubjectAsync(int? id) { _subjectId = id; await CurrentSubject.SetAsync(id); await LoadAsync(); }
    private int _loadVersion;

    private async Task LoadAsync()
    {
        var version = ++_loadVersion;
        _history = [];
        _error = null;
        if (_subjectId is null) { _loading = false; return; }
        _loading = true;
        try
        {
            var history = await Api.GetMyHistoryAsync(_subjectId.Value);
            if (version == _loadVersion) _history = history;
        }
        catch (Exception ex) when ((ex is ApiException or HttpRequestException) && version == _loadVersion) { _error = ex.Message; }
        catch (Exception ex) when (ex is ApiException or HttpRequestException) { }
        finally { if (version == _loadVersion) _loading = false; }
    }
}
