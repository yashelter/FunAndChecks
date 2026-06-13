using FunAndChecks.Domain.Enums;

namespace FunAndChecks.Application.Tasks;

public record TaskDto(int Id, string Name, string Description, int MaxPoints);

/// <summary>Задание со статусом последней сдачи конкретного студента.</summary>
public record TaskWithStatusDto(int Id, string Name, string Description, int MaxPoints, SubmissionStatus Status);

public record CreateTaskRequest(string Name, string Description, int MaxPoints);

public record UpdateTaskRequest(string Name, string Description, int MaxPoints);
