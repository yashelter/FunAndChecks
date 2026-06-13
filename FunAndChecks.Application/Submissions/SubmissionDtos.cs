using FunAndChecks.Application.Students;
using FunAndChecks.Domain.Enums;

namespace FunAndChecks.Application.Submissions;

public record CreateSubmissionRequest(Guid StudentId, int TaskId, SubmissionStatus Status, string? Comment);

/// <summary>Одна попытка сдачи в истории.</summary>
public record SubmissionLogDto(SubmissionStatus Status, string? Comment, DateTime SubmittedAt, AdminDto Admin);

/// <summary>Событие для подписчиков SignalR об изменении результата.</summary>
public record ResultUpdateDto(Guid StudentId, int TaskId, string NewStatus);
