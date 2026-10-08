using System.Net;
using Frontend.Shared.Api;
using Frontend.Shared.Models;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace Frontend.Admin.Pages;

public partial class AttendanceSession
{
    [Parameter] public int SessionId { get; set; }
    [Inject] private AttendanceApi Api { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;
    [Inject] private IDialogService Dialog { get; set; } = null!;
    private AttendanceSessionDetailsDto? _details;
    private bool _loading = true, _busy, _onlyUnmarked, _adding, _saveError;
    private string _search = "";
    private string? _error;
    private List<StudentDto> _candidates = [];
    private Guid? _addStudentId;
    private IEnumerable<AttendanceParticipantDto> Visible => (_details?.Participants ?? [])
        .Where(p => !_onlyUnmarked || p.Status == AttendanceStatus.Unmarked)
        .Where(p => string.IsNullOrWhiteSpace(_search) || p.FullName.Contains(_search, StringComparison.OrdinalIgnoreCase)
            || p.GroupName.Contains(_search, StringComparison.OrdinalIgnoreCase));
    private List<AttendanceParticipantDto> Remaining => (_details?.Participants ?? [])
        .Where(p => p.CanManage && p.Status == AttendanceStatus.Unmarked).ToList();

    protected override Task OnParametersSetAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        _loading = true;
        _error = null;
        try { _details = await Api.GetSessionAsync(SessionId); }
        catch (Exception ex) when (ex is ApiException or HttpRequestException) { _details = null; _error = ex.Message; }
        finally { _loading = false; }
    }

    private async Task MarkAsync(AttendanceParticipantDto participant, AttendanceStatus status)
    {
        if (_busy || _details is null) return;
        _busy = true;
        _saveError = false;
        try
        {
            var updated = await Api.SetMarkAsync(SessionId, participant.StudentId, new(status, participant.Version));
            var index = _details.Participants.FindIndex(p => p.StudentId == participant.StudentId);
            if (index >= 0) _details.Participants[index] = updated;
        }
        catch (Exception ex) when (ex is ApiException or HttpRequestException)
        {
            _saveError = true;
            Snackbar.Add(ex.Message, Severity.Error);
            if (ex is ApiException { StatusCode: HttpStatusCode.Conflict }) await LoadAsync();
        }
        finally { _busy = false; }
    }

    private async Task MarkRemainingAsync()
    {
        var remaining = Remaining;
        if (_busy || remaining.Count == 0) return;
        _busy = true;
        try
        {
            var confirmed = await Dialog.ShowMessageBoxAsync("Завершить перекличку?",
                $"Отметить отсутствующими {remaining.Count} студентов? Уже выставленные отметки сохранятся. Поиск и фильтры не ограничивают список.",
                yesText: "Отметить отсутствующими", cancelText: "Отмена");
            if (confirmed != true) return;
            await Api.MarkRemainingAbsentAsync(SessionId, new(remaining.Select(p => new RemainingAttendanceStudent(p.StudentId, p.Version)).ToList()));
            _saveError = false;
            await LoadAsync();
            Snackbar.Add($"Выставлено отметок: {remaining.Count}.", Severity.Success);
        }
        catch (Exception ex) when (ex is ApiException or HttpRequestException)
        {
            _saveError = true;
            Snackbar.Add(ex.Message, Severity.Error);
            if (ex is ApiException { StatusCode: HttpStatusCode.Conflict }) await LoadAsync();
        }
        finally { _busy = false; }
    }

    private async Task ToggleAddAsync()
    {
        if (_details is null) return;
        _adding = !_adding;
        if (!_adding) return;
        _busy = true;
        try
        {
            _candidates = await Api.GetAvailableStudentsAsync(SessionId);
        }
        catch (Exception ex) when (ex is ApiException or HttpRequestException) { Snackbar.Add(ex.Message, Severity.Error); }
        finally { _busy = false; }
    }

    private async Task AddAsync()
    {
        if (_addStudentId is null || _busy) return;
        _busy = true;
        try
        {
            await Api.AddStudentAsync(SessionId, _addStudentId.Value);
            _candidates.RemoveAll(s => s.Id == _addStudentId);
            _addStudentId = null;
            await LoadAsync();
            Snackbar.Add("Студент добавлен. Отметка пока не выставлена.", Severity.Success);
        }
        catch (Exception ex) when (ex is ApiException or HttpRequestException) { Snackbar.Add(ex.Message, Severity.Error); }
        finally { _busy = false; }
    }
}
