using FunAndChecks.Domain.Enums;
using FunAndChecks.Application.Groups;

namespace FunAndChecks.Application.Attendance;

public record AttendanceSettingsDto(bool Enabled);
public record CreateAttendanceSessionRequest(string? Name, DateTime StartsAt, List<int> GroupIds);
public record AttendanceSessionDto(int Id, string Name, DateTime StartsAt);
public record SetAttendanceRequest(AttendanceStatus Status, Guid Version);
public record RemainingAttendanceRequest(List<RemainingAttendanceStudent> Students);
public record RemainingAttendanceStudent(Guid StudentId, Guid Version);
public record AttendanceParticipantDto(Guid StudentId, string FullName, int GroupId, string GroupName,
    AttendanceStatus Status, string? MarkedBy, DateTime? UpdatedAt, Guid Version, bool CanManage);
public record AttendanceSessionDetailsDto(int Id, int SubjectId, string SubjectName, string Name,
    DateTime StartsAt, bool Enabled, List<GroupDto> Groups, List<AttendanceParticipantDto> Participants);
public record AttendanceStudentRowDto(Guid StudentId, string FullName, int GroupId, string GroupName,
    Dictionary<int, AttendanceStatus> Marks, int Present, int Absent, int Unmarked);
public record AttendanceJournalDto(int SubjectId, string SubjectName, bool Enabled,
    List<AttendanceSessionDto> Sessions, List<AttendanceStudentRowDto> Students);
public record StudentAttendanceDto(int SessionId, string Name, DateTime StartsAt, AttendanceStatus Status,
    string? MarkedBy, DateTime? UpdatedAt);
