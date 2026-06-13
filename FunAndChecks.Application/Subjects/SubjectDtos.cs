namespace FunAndChecks.Application.Subjects;

public record SubjectDto(int Id, string Name);

public record CreateSubjectRequest(string Name);

public record UpdateSubjectRequest(string Name);
