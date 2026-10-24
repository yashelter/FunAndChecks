using System.Net;
using Frontend.Shared.Api;
using Frontend.Shared.Models;
using Frontend.Shared.Resources;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using MudBlazor;

namespace Frontend.Admin.Pages;

public partial class AttendanceSession
{
    [Parameter] public int SessionId { get; set; }
    [Inject] private AttendanceApi Api { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;
    [Inject] private IDialogService Dialog { get; set; } = null!;
    [Inject] private IStringLocalizer<AppStrings> Loc { get; set; } = null!;
    private AttendanceSessionDetailsDto? _details;
    private bool _loading = true, _refreshing, _bulkBusy, _onlyUnmarked, _adding, _saveError;
    // Блокируется только строка, которая сейчас сохраняется: по списку можно идти подряд, не дожидаясь сервера.
    private readonly HashSet<Guid> _saving = [];
    private string _search = "";
    private string? _error;
    private List<StudentDto> _candidates = [];
    private Guid? _addStudentId;
    private int? _loadedSession;
    private bool Busy => _bulkBusy || _refreshing;
    private IEnumerable<AttendanceParticipantDto> Visible => (_details?.Participants ?? [])
        .Where(p => !_onlyUnmarked || p.Status == AttendanceStatus.Unmarked)
        .Where(p => string.IsNullOrWhiteSpace(_search) || p.FullName.Contains(_search, StringComparison.OrdinalIgnoreCase)
            || p.GroupName.Contains(_search, StringComparison.OrdinalIgnoreCase));
    private List<AttendanceParticipantDto> Remaining => (_details?.Participants ?? [])
        .Where(p => p.CanManage && p.Status == AttendanceStatus.Unmarked && !_saving.Contains(p.StudentId)).ToList();

    protected override async Task OnParametersSetAsync()
    {
        if (_loadedSession == SessionId) return;
        _loadedSession = SessionId;
        _loading = true;
        await LoadAsync();
        _loading = false;
    }

    /// <summary>Перечитывает занятие. После первой загрузки — в фоне, без полностраничного спиннера.</summary>
    private async Task LoadAsync()
    {
        _refreshing = !_loading;
        _error = null;
        try { _details = await Api.GetSessionAsync(SessionId); }
        catch (Exception ex) when (ex is ApiException or HttpRequestException)
        {
            if (_details is null) _error = ex.Message;
            else Snackbar.Add(ex.Message, Severity.Error);
        }
        finally { _refreshing = false; }
    }

    private async Task MarkAsync(AttendanceParticipantDto participant, AttendanceStatus status)
    {
        if (_details is null || !_saving.Add(participant.StudentId)) return;
        _saveError = false;
        // Отметка видна сразу; если сервер откажет — вернём прежнюю.
        Replace(participant with { Status = status });
        try
        {
            Replace(await Api.SetMarkAsync(SessionId, participant.StudentId, new(status, participant.Version)));
        }
        catch (Exception ex) when (ex is ApiException or HttpRequestException)
        {
            _saveError = true;
            Replace(participant);
            Snackbar.Add(ex.Message, Severity.Error);
            if (ex is ApiException { StatusCode: HttpStatusCode.Conflict }) await LoadAsync();
        }
        finally { _saving.Remove(participant.StudentId); }
    }

    private void Replace(AttendanceParticipantDto participant)
    {
        if (_details is null) return;
        var index = _details.Participants.FindIndex(p => p.StudentId == participant.StudentId);
        if (index >= 0) _details.Participants[index] = participant;
    }

    private async Task MarkRemainingAsync(AttendanceStatus status)
    {
        var remaining = Remaining;
        if (Busy || remaining.Count == 0) return;
        _bulkBusy = true;
        try
        {
            var present = status == AttendanceStatus.Present;
            var confirmed = await Dialog.ShowMessageBoxAsync(Loc["Attendance_FinishTitle"],
                Loc[present ? "Attendance_RemainingPresentConfirm" : "Attendance_RemainingAbsentConfirm", remaining.Count].Value,
                yesText: Loc[present ? "Attendance_MarkRemainingPresent" : "Attendance_MarkRemainingAbsent", remaining.Count],
                cancelText: Loc["Common_Cancel"]);
            if (confirmed != true) return;
            await Api.MarkRemainingAbsentAsync(SessionId,
                new(remaining.Select(p => new RemainingAttendanceStudent(p.StudentId, p.Version)).ToList(), status));
            _saveError = false;
            await LoadAsync();
            Snackbar.Add(Loc["Attendance_MarksSet", remaining.Count], Severity.Success);
        }
        catch (Exception ex) when (ex is ApiException or HttpRequestException)
        {
            _saveError = true;
            Snackbar.Add(ex.Message, Severity.Error);
            if (ex is ApiException { StatusCode: HttpStatusCode.Conflict }) await LoadAsync();
        }
        finally { _bulkBusy = false; }
    }

    private async Task ToggleAddAsync()
    {
        if (_details is null) return;
        _adding = !_adding;
        if (!_adding) return;
        _bulkBusy = true;
        try
        {
            _candidates = await Api.GetAvailableStudentsAsync(SessionId);
        }
        catch (Exception ex) when (ex is ApiException or HttpRequestException) { Snackbar.Add(ex.Message, Severity.Error); }
        finally { _bulkBusy = false; }
    }

    private async Task AddAsync()
    {
        if (_addStudentId is null || Busy) return;
        _bulkBusy = true;
        try
        {
            await Api.AddStudentAsync(SessionId, _addStudentId.Value);
            _candidates.RemoveAll(s => s.Id == _addStudentId);
            _addStudentId = null;
            await LoadAsync();
            Snackbar.Add(Loc["Attendance_StudentAdded"], Severity.Success);
        }
        catch (Exception ex) when (ex is ApiException or HttpRequestException) { Snackbar.Add(ex.Message, Severity.Error); }
        finally { _bulkBusy = false; }
    }
}
