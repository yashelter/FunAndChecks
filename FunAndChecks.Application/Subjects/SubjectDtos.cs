namespace FunAndChecks.Application.Subjects;

public record SubjectDto(int Id, string Name, bool AttendanceEnabled = false);

public record CreateSubjectRequest(string Name);

public record UpdateSubjectRequest(string Name);
