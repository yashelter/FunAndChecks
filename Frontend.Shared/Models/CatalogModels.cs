namespace Frontend.Shared.Models;

public record SubjectDto(int Id, string Name);

public record CreateSubjectRequest(string Name);

public record UpdateSubjectRequest(string Name);

public record TaskDto(int Id, string Name, string Description, int MaxPoints);

public record TaskWithStatusDto(int Id, string Name, string Description, int MaxPoints, SubmissionStatus Status);

public record CreateTaskRequest(string Name, string Description, int MaxPoints);

public record UpdateTaskRequest(string Name, string Description, int MaxPoints);

public record GroupDto(int Id, string Name);

public record CreateGroupRequest(string Name);

public record UpdateGroupRequest(string Name);
