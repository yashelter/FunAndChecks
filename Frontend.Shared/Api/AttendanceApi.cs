using Frontend.Shared.Models;
using Frontend.Shared.Resources;
using Microsoft.Extensions.Localization;

namespace Frontend.Shared.Api;

public class AttendanceApi(HttpClient http, IStringLocalizer<AppStrings> loc) : ApiClientBase(http, loc)
{
    public Task SetEnabledAsync(int subjectId, bool enabled) =>
        PutAsync($"api/attendance/subjects/{subjectId}/settings", new AttendanceSettingsDto(enabled));
    public Task<List<GroupDto>> GetGroupsAsync(int subjectId) =>
        GetAsync<List<GroupDto>>($"api/attendance/subjects/{subjectId}/groups");
    public Task<AttendanceJournalDto> GetJournalAsync(int subjectId, int? groupId = null, DateOnly? from = null, DateOnly? to = null) =>
        GetAsync<AttendanceJournalDto>($"api/attendance/subjects/{subjectId}/journal{Query(groupId, from, to)}");
    public Task<AttendanceSessionDto> CreateSessionAsync(int subjectId, CreateAttendanceSessionRequest request) =>
        PostAsync<AttendanceSessionDto>($"api/attendance/subjects/{subjectId}/sessions", request);
    public Task<AttendanceSessionDetailsDto> GetSessionAsync(int sessionId) =>
        GetAsync<AttendanceSessionDetailsDto>($"api/attendance/sessions/{sessionId}");
    public Task<AttendanceParticipantDto> SetMarkAsync(int sessionId, Guid studentId, SetAttendanceRequest request) =>
        PutAsync<AttendanceParticipantDto>($"api/attendance/sessions/{sessionId}/students/{studentId}", request);
    public Task MarkRemainingAbsentAsync(int sessionId, RemainingAttendanceRequest request) =>
        PostAsync($"api/attendance/sessions/{sessionId}/remaining-absent", request);
    public Task AddStudentAsync(int sessionId, Guid studentId) =>
        PostAsync($"api/attendance/sessions/{sessionId}/students/{studentId}");
    public Task<List<StudentDto>> GetAvailableStudentsAsync(int sessionId) =>
        GetAsync<List<StudentDto>>($"api/attendance/sessions/{sessionId}/available-students");
    public Task<List<SubjectDto>> GetMySubjectsAsync() =>
        GetAsync<List<SubjectDto>>("api/me/attendance/subjects");
    public Task<List<StudentAttendanceDto>> GetMyHistoryAsync(int subjectId) =>
        GetAsync<List<StudentAttendanceDto>>($"api/me/attendance/subjects/{subjectId}");
    public async Task<byte[]> ExportAsync(int subjectId, int? groupId, DateOnly? from, DateOnly? to)
    {
        using var response = await Http.GetAsync($"api/attendance/subjects/{subjectId}/export{Query(groupId, from, to)}");
        await response.EnsureSuccessAsync(Loc);
        return await response.Content.ReadAsByteArrayAsync();
    }
    private static string Query(int? groupId, DateOnly? from, DateOnly? to) =>
        $"?groupId={groupId}&from={from?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)}&to={to?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)}";
}
