namespace Frontend.Shared.Models;

public record GradeComponentDto(int Id, int SubjectId, string Name, int MaxPoints);

public record CreateGradeComponentRequest(string Name, int MaxPoints);

public record SetGradeRequest(int Points, string? Comment);

public record StudentGradeDto(
    int ComponentId,
    string ComponentName,
    Guid StudentId,
    int Points,
    int MaxPoints,
    string? Comment,
    DateTime UpdatedAt);

/// <summary>Событие SignalR об изменении балла за колонку.</summary>
public record GradeUpdateDto(Guid StudentId, int ComponentId, int Points);
