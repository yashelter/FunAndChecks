namespace Frontend.Shared.Models;

public record SubjectDto(int Id, string Name, bool AttendanceEnabled = false);

public record CreateSubjectRequest(string Name);

public record UpdateSubjectRequest(string Name);

public record TaskDto(int Id, string Name, string Description, int MaxPoints, int? TaskGroupId = null, string? TaskGroupName = null);

public record TaskWithStatusDto(int Id, string Name, string Description, int MaxPoints, SubmissionStatus Status,
    int? TaskGroupId = null, string? TaskGroupName = null);

public record CreateTaskRequest(string Name, string Description, int MaxPoints, int? TaskGroupId = null);

/// <summary>TaskGroupId — полная замена: null снимает группу.</summary>
public record UpdateTaskRequest(string Name, string Description, int MaxPoints, int? TaskGroupId = null);

/// <summary>Группа заданий предмета (не путать с группой студентов <see cref="GroupDto"/>).</summary>
public record TaskGroupDto(int Id, string Name, int TaskCount);

public record CreateTaskGroupRequest(string Name);

public record UpdateTaskGroupRequest(string Name);

public record GroupDto(int Id, string Name);

public record CreateGroupRequest(string Name);

public record UpdateGroupRequest(string Name);

public record GroupSubjectsDto(int GroupId, List<int> SubjectIds);
