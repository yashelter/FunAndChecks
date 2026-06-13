namespace FunAndChecks.Application.Grades;

/// <summary>Оценочная колонка предмета («Билет», «Курсовая» и т.п.).</summary>
public record GradeComponentDto(int Id, int SubjectId, string Name, int MaxPoints);

public record CreateGradeComponentRequest(string Name, int MaxPoints);

/// <summary>Выставление (или обновление) баллов студенту за колонку.</summary>
public record SetGradeRequest(int Points, string? Comment);

/// <summary>Оценка студента за колонку.</summary>
public record StudentGradeDto(
    int ComponentId,
    string ComponentName,
    Guid StudentId,
    int Points,
    int MaxPoints,
    string? Comment,
    DateTime UpdatedAt);

/// <summary>Событие для подписчиков SignalR об изменении оценки.</summary>
public record GradeUpdateDto(Guid StudentId, int ComponentId, int Points);
