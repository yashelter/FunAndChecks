using Frontend.Shared.Api;
using Frontend.Shared.Models;
using Frontend.Shared.Services;
using Frontend.Shared.UI;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Frontend.Admin.Pages;

public partial class Attendance
{
    [Inject] private AttendanceApi Api { get; set; } = null!;
    [Inject] private MeApi Me { get; set; } = null!;
    [Inject] private FileDownloader Downloader { get; set; } = null!;
    [Inject] private NavigationManager Nav { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;
    [SupplyParameterFromQuery(Name = "subjectId")] public int? SubjectId { get; set; }
    [SupplyParameterFromQuery(Name = "studentId")] public Guid? StudentId { get; set; }

    private List<SubjectDto> _subjects = [];
    private List<GroupDto> _groups = [];
    private List<GroupDto> _historicalGroups = [];
    private AttendanceJournalDto? _journal;
    private AttendanceStudentRowDto? _selectedStudent;
    private int? _subjectId, _groupId, _loadedGroup;
    private DateTime? _from, _to;
    private DateOnly? _loadedFrom, _loadedTo;
    private bool _loading = true, _working, _creating, _onlyMissing, _sortAbsences;
    private string _search = "", _name = "";
    private string _view = "events";
    private string? _error;
    private DateTime? _date = AttendanceStyles.Moscow(DateTime.UtcNow).Date;
    private TimeSpan? _time = new(10, 0, 0);
    private IReadOnlyCollection<int> _createGroups = [];
    private bool Busy => _loading || _working;
    private IEnumerable<GroupDto> JournalGroups => _groups.Concat(_historicalGroups)
        .DistinctBy(g => g.Id).OrderBy(g => g.Name);
    private IEnumerable<AttendanceStudentRowDto> VisibleStudents
    {
        get
        {
            var rows = (_journal?.Students ?? []).Where(s => string.IsNullOrWhiteSpace(_search)
                || s.FullName.Contains(_search, StringComparison.OrdinalIgnoreCase) || s.GroupName.Contains(_search, StringComparison.OrdinalIgnoreCase))
                .Where(s => !_onlyMissing || s.Unmarked > 0);
            return _sortAbsences ? rows.OrderByDescending(s => s.Absent).ThenBy(s => s.FullName) : rows;
        }
    }
    private string LoadedPeriod => _loadedFrom is null && _loadedTo is null ? "весь период"
        : $"{(_loadedFrom?.ToString("dd.MM.yyyy") ?? "начало")} — {(_loadedTo?.ToString("dd.MM.yyyy") ?? "сегодня и далее")}";

    protected override async Task OnInitializedAsync()
    {
        try
        {
            _subjects = await Me.GetVisibleSubjectsAsync();
            _subjectId = _subjects.Any(s => s.Id == SubjectId) ? SubjectId : _subjects.FirstOrDefault()?.Id;
            if (_subjectId.HasValue) _groups = await Api.GetGroupsAsync(_subjectId.Value);
            await LoadAsync();
        }
        catch (Exception ex) when (ex is ApiException or HttpRequestException)
        {
            _error = ex.Message;
        }
        finally { _loading = false; }
    }

    private async Task ChangeSubjectAsync(int? id)
    {
        _subjectId = id;
        _groupId = null;
        _creating = false;
        _selectedStudent = null;
        _groups = [];
        _historicalGroups = [];
        _journal = null;
        _loading = true;
        try
        {
            if (id.HasValue) _groups = await Api.GetGroupsAsync(id.Value);
            await LoadAsync();
        }
        catch (Exception ex) when (ex is ApiException or HttpRequestException) { _error = ex.Message; }
        finally { _loading = false; }
    }

    private async Task LoadAsync()
    {
        if (!_subjectId.HasValue) { _loading = false; return; }
        _loading = true;
        _error = null;
        _selectedStudent = null;
        _journal = null;
        try
        {
            _loadedGroup = _groupId;
            _loadedFrom = _from.HasValue ? DateOnly.FromDateTime(_from.Value) : null;
            _loadedTo = _to.HasValue ? DateOnly.FromDateTime(_to.Value) : null;
            _journal = await Api.GetJournalAsync(_subjectId.Value, _loadedGroup, _loadedFrom, _loadedTo);
            _historicalGroups = _historicalGroups.Concat(_journal.Students.Select(s => new GroupDto(s.GroupId, s.GroupName))).DistinctBy(g => g.Id).ToList();
            if (StudentId.HasValue)
            {
                _selectedStudent = _journal.Students.FirstOrDefault(s => s.StudentId == StudentId);
                _search = _selectedStudent?.FullName ?? "";
                if (_selectedStudent is not null) _view = "journal";
            }
        }
        catch (Exception ex) when (ex is ApiException or HttpRequestException) { _error = ex.Message; }
        finally { _loading = false; }
    }

    private async Task SetEnabledAsync(bool enabled)
    {
        if (_journal is null) return;
        _working = true;
        try
        {
            await Api.SetEnabledAsync(_journal.SubjectId, enabled);
            _journal = _journal with { Enabled = enabled };
            if (!enabled) _creating = false;
            Snackbar.Add(enabled ? "Учёт посещаемости включён." : "Учёт выключен, история сохранена.", Severity.Success);
        }
        catch (Exception ex) when (ex is ApiException or HttpRequestException) { Snackbar.Add(ex.Message, Severity.Error); }
        finally { _working = false; }
    }

    private void ToggleCreate()
    {
        _creating = !_creating;
        if (_creating) _createGroups = _groups.Where(g => g.Id == _groupId).Select(g => g.Id).ToList();
    }

    private async Task CreateAsync()
    {
        if (_subjectId is null || _date is null || _time is null || _createGroups.Count == 0) return;
        _working = true;
        try
        {
            var session = await Api.CreateSessionAsync(_subjectId.Value,
                new(_name, AttendanceStyles.ToUtc(_date.Value, _time.Value), _createGroups.ToList()));
            Open(session.Id);
        }
        catch (Exception ex) when (ex is ApiException or HttpRequestException) { Snackbar.Add(ex.Message, Severity.Error); }
        finally { _working = false; }
    }

    private async Task ExportAsync()
    {
        if (_journal is null) return;
        _working = true;
        try
        {
            var bytes = await Api.ExportAsync(_journal.SubjectId, _loadedGroup, _loadedFrom, _loadedTo);
            using var stream = new MemoryStream(bytes);
            await Downloader.DownloadAsync($"Attendance_{_journal.SubjectId}_{DateTime.UtcNow:yyyy-MM-dd}.xlsx", stream);
        }
        catch (Exception ex) when (ex is ApiException or HttpRequestException) { Snackbar.Add(ex.Message, Severity.Error); }
        finally { _working = false; }
    }

    private void Open(int id) => Nav.NavigateTo($"/admin/attendance/sessions/{id}");
}
