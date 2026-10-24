using FunAndChecks.Domain.Enums;

namespace FunAndChecks.Application.Tasks;

public record TaskDto(int Id, string Name, string Description, int MaxPoints, int? TaskGroupId = null, string? TaskGroupName = null);

/// <summary>Задание со статусом последней сдачи конкретного студента.</summary>
public record TaskWithStatusDto(int Id, string Name, string Description, int MaxPoints, SubmissionStatus Status,
    int? TaskGroupId = null, string? TaskGroupName = null);

/// <param name="TaskGroupId">Группа заданий того же предмета; null — без группы.</param>
public record CreateTaskRequest(string Name, string Description, int MaxPoints, int? TaskGroupId = null);

/// <param name="TaskGroupId">Полная замена: null снимает группу.</param>
public record UpdateTaskRequest(string Name, string Description, int MaxPoints, int? TaskGroupId = null);
