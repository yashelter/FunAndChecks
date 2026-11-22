namespace FunAndChecks.Application.Statistics;

/// <summary>
/// Статистика одного проверяющего: принятые и непринятые попытки (все, а не только последние) и сколько разных
/// студентов он завершил в очереди (как последний, кто их вёл).
/// </summary>
public record ReviewerStatsDto(
    Guid AdminId,
    string FullName,
    string? Color,
    string? Letter,
    int Accepted,
    int Rejected,
    int StudentsFinished);

/// <summary>
/// Расширенная сводка активности одного администратора за выбранный период и предмет.
/// </summary>
public record AdminActivityStatsDto(
    Guid AdminId,
    string FullName,
    string? Color,
    string? Letter,
    int TasksAccepted,
    int TasksRejected,
    int QueueStudentsFinished,
    int GradesUpdated,
    int AttendanceSessionsCreated,
    int AttendanceMarksSet,
    int VariantsAssigned,
    int AchievementsGranted,
    DateTime? LastActivityAt)
{
    public int TotalActions =>
        TasksAccepted + TasksRejected + QueueStudentsFinished + GradesUpdated +
        AttendanceSessionsCreated + AttendanceMarksSet + VariantsAssigned + AchievementsGranted;
}
