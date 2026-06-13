namespace FunAndChecks.Application.Groups;

public record GroupDto(int Id, string Name);

public record CreateGroupRequest(string Name);

public record UpdateGroupRequest(string Name);
