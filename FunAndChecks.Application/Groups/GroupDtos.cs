namespace FunAndChecks.Application.Groups;

public record GroupDto(int Id, string Name);

public record CreateGroupRequest(string Name);

public record UpdateGroupRequest(string Name);

/// <summary>Предметы, доступные группе (для обзора всех доступов разом).</summary>
public record GroupSubjectsDto(int GroupId, List<int> SubjectIds);
