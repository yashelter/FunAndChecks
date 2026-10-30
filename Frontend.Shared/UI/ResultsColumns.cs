using Frontend.Shared.Models;

namespace Frontend.Shared.UI;

/// <summary>Отпечаток набора колонок таблицы результатов: предмет, задачи и оценочные колонки по порядку.</summary>
public static class ResultsColumns
{
    /// <summary>
    /// Высота строки таблицы результатов в пикселях. Строки виртуализированы, поэтому она обязана совпадать
    /// с высотой строки в CSS (<c>--fc-results-row</c> в dashboard-content.css), иначе прокрутка «прыгает».
    /// </summary>
    public const int RowHeight = 37;

    /// <summary>С этого числа задач их названия в шапке пишутся вертикально, чтобы колонки были узкими.</summary>
    public const int ManyTasks = 12;

    public static string Key(SubjectResultsDto results) =>
        $"{results.SubjectId}:{string.Join(',', results.TaskHeaders.Select(t => t.TaskId))}|{string.Join(',', results.GradeColumns.Select(c => c.ComponentId))}";

    public static string GridClass(SubjectResultsDto results) =>
        results.TaskHeaders.Count > ManyTasks ? "fc-results-grid fc-results-many-tasks" : "fc-results-grid";
}
