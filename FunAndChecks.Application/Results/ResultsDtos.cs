using FunAndChecks.Application.Submissions;
using FunAndChecks.Domain.Enums;

namespace FunAndChecks.Application.Results;

public record TaskHeaderDto(int TaskId, string TaskName, int MaxPoints);

/// <summary>Заголовок колонки глобальной оценки (билет, курсовая и т.п.).</summary>
public record GradeColumnDto(int ComponentId, string Name, int MaxPoints);

/// <summary>
/// Одна ячейка в таблице результатов:
/// что написано ("+", буква админа, ""), цвет фона и статус.
/// </summary>
public record ResultCellDto(string DisplayValue, string? AdminColor, SubmissionStatus Status);

/// <summary>Строка таблицы результатов: студент, ячейки по TaskId и баллы по колонкам оценок.</summary>
public record StudentResultRowDto(
    Guid StudentId,
    string FullName,
    string GroupName,
    int TotalPoints,
    IReadOnlyDictionary<int, ResultCellDto> Results,
    IReadOnlyDictionary<int, int> Grades,
    string? StudentColor);

/// <summary>Полная таблица результатов по предмету (кэшируется).</summary>
public record SubjectResultsDto(
    int SubjectId,
    string SubjectName,
    IReadOnlyList<TaskHeaderDto> TaskHeaders,
    IReadOnlyList<GradeColumnDto> GradeColumns,
    IReadOnlyList<StudentResultRowDto> UserResults);

/// <summary>Результат студента по одному заданию; история заполняется только для Rejected.</summary>
public record StudentTaskResultDto(
    int TaskId,
    string TaskName,
    SubmissionStatus CurrentStatus,
    int MaxPoints,
    IReadOnlyList<SubmissionLogDto>? SubmissionHistory);

/// <summary>Балл студента за глобальную оценочную колонку.</summary>
public record StudentGradeResultDto(int ComponentId, string Name, int Points, int MaxPoints, string? Comment);

/// <summary>Итоги студента по предмету.</summary>
public record StudentSubjectResultsDto(
    int SubjectId,
    string SubjectName,
    int TotalPointsEarned,
    int MaxPointsPossible,
    IReadOnlyList<StudentTaskResultDto> TaskResults,
    IReadOnlyList<StudentGradeResultDto> Grades);
