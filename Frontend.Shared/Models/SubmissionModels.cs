namespace Frontend.Shared.Models;

public record CreateSubmissionRequest(Guid StudentId, int TaskId, SubmissionStatus Status, string? Comment);

public record SubmissionLogDto(SubmissionStatus Status, string? Comment, DateTime SubmittedAt, AdminDto Admin);

/// <summary>Событие SignalR об изменении статуса задачи студента.</summary>
public record ResultUpdateDto(Guid StudentId, int TaskId, string NewStatus);
