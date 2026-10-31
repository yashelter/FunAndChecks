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
