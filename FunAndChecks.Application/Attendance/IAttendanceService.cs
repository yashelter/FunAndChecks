using FunAndChecks.Application.Groups;
using FunAndChecks.Application.Subjects;
using FunAndChecks.Application.Students;

namespace FunAndChecks.Application.Attendance;

public interface IAttendanceService
{
    Task SetEnabledAsync(Guid adminId, int subjectId, bool enabled, CancellationToken ct = default);
    Task<List<GroupDto>> GetGroupsAsync(Guid adminId, int subjectId, CancellationToken ct = default);
    Task<AttendanceSessionDto> CreateSessionAsync(Guid adminId, int subjectId, CreateAttendanceSessionRequest request, CancellationToken ct = default);
    Task<AttendanceSessionDetailsDto> GetSessionAsync(Guid adminId, int sessionId, CancellationToken ct = default);
    Task<AttendanceParticipantDto> SetMarkAsync(Guid adminId, int sessionId, Guid studentId, SetAttendanceRequest request, CancellationToken ct = default);
    Task MarkRemainingAbsentAsync(Guid adminId, int sessionId, RemainingAttendanceRequest request, CancellationToken ct = default);
    Task AddStudentAsync(Guid adminId, int sessionId, Guid studentId, CancellationToken ct = default);
    Task<List<StudentDto>> GetAvailableStudentsAsync(Guid adminId, int sessionId, CancellationToken ct = default);
    Task<AttendanceJournalDto> GetJournalAsync(Guid adminId, int subjectId, int? groupId = null, DateOnly? from = null, DateOnly? to = null, CancellationToken ct = default);
    Task<List<StudentAttendanceDto>> GetStudentHistoryAsync(Guid studentId, int subjectId, CancellationToken ct = default);
    Task<List<SubjectDto>> GetStudentSubjectsAsync(Guid studentId, CancellationToken ct = default);
}
