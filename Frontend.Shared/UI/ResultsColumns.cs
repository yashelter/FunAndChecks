using Frontend.Shared.Models;

namespace Frontend.Shared.UI;

/// <summary>Отпечаток набора колонок таблицы результатов: предмет, задачи и оценочные колонки по порядку.</summary>
public static class ResultsColumns
{
    public static string Key(SubjectResultsDto results) =>
        $"{results.SubjectId}:{string.Join(',', results.TaskHeaders.Select(t => t.TaskId))}|{string.Join(',', results.GradeColumns.Select(c => c.ComponentId))}";
}
