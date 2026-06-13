namespace Frontend.Shared.Models;

public record TaskHeaderDto(int TaskId, string TaskName, int MaxPoints);

public record GradeColumnDto(int ComponentId, string Name, int MaxPoints);

public record ResultCellDto(string DisplayValue, string? AdminColor, SubmissionStatus Status);

public record StudentResultRowDto(
    Guid StudentId,
    string FullName,
    string GroupName,
    int TotalPoints,
    Dictionary<int, ResultCellDto> Results,
    Dictionary<int, int> Grades);

public record SubjectResultsDto(
    int SubjectId,
    string SubjectName,
    List<TaskHeaderDto> TaskHeaders,
    List<GradeColumnDto> GradeColumns,
    List<StudentResultRowDto> UserResults);

public record StudentTaskResultDto(
    int TaskId,
    string TaskName,
    SubmissionStatus CurrentStatus,
    int MaxPoints,
    List<SubmissionLogDto>? SubmissionHistory);

public record StudentGradeResultDto(int ComponentId, string Name, int Points, int MaxPoints, string? Comment);

public record StudentSubjectResultsDto(
    int SubjectId,
    string SubjectName,
    int TotalPointsEarned,
    int MaxPointsPossible,
    List<StudentTaskResultDto> TaskResults,
    List<StudentGradeResultDto> Grades);
