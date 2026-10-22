namespace FunAndChecks.Application.Tasks;

/// <summary>Группа заданий предмета и число заданий в ней.</summary>
public record TaskGroupDto(int Id, string Name, int TaskCount);

public record CreateTaskGroupRequest(string Name);

public record UpdateTaskGroupRequest(string Name);
