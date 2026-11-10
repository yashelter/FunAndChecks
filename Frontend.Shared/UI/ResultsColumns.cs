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

    /// <param name="ranked">Есть закреплённая колонка «Место»: класс сдвигает закреплённую Σ на её ширину.</param>
    public static string GridClass(SubjectResultsDto results, bool ranked = false) =>
        (results.TaskHeaders.Count > ManyTasks ? "fc-results-grid fc-results-many-tasks" : "fc-results-grid")
        + (ranked ? " fc-results-ranked" : "");

    /// <summary>Места 1…N выделены цветом (градация от 1 к N); у первого — корона.</summary>
    public const int HighlightedRanks = 7;

    /// <summary>Строки первых мест подсвечены целиком.</summary>
    public const int PodiumRanks = 3;

    /// <summary>
    /// Место выделяется, только если у студента есть баллы: в новом предмете у всех Σ = 0 и общее 1-е место,
    /// корона у всех сразу ничего бы не значила.
    /// </summary>
    public static bool IsRanked(int rank, int points) => rank > 0 && points > 0;

    /// <summary>Класс бейджа места: fc-rank-1…7, затем fc-rank-rest; без баллов — fc-rank-none.</summary>
    public static string RankClass(int rank, int points) =>
        !IsRanked(rank, points) ? "fc-rank fc-rank-none"
        : rank <= HighlightedRanks ? $"fc-rank fc-rank-{rank}"
        : "fc-rank fc-rank-rest";

    /// <summary>Класс строки для подсветки первых мест (пусто для остальных).</summary>
    public static string RowClass(StudentResultRowDto row) =>
        IsRanked(row.Rank, row.TotalPoints) && row.Rank <= PodiumRanks ? $"fc-results-podium fc-results-podium-{row.Rank}" : string.Empty;

    /// <summary>
    /// Имя для узкого экрана: «Фамилия И. О.». Первое слово целиком, остальные — инициалами;
    /// полное имя остаётся в подсказке и в профиле.
    /// </summary>
    public static string ShortName(string fullName)
    {
        var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length <= 1)
            return fullName.Trim();
        var short_ = new System.Text.StringBuilder(parts[0]);
        for (var i = 1; i < parts.Length; i++)
            short_.Append(' ').Append(char.ToUpper(parts[i][0], System.Globalization.CultureInfo.CurrentCulture)).Append('.');
        return short_.ToString();
    }

    /// <summary>Сколько студентов делят каждое место (при равных Σ место общее).</summary>
    public static Dictionary<int, int> RankCounts(IEnumerable<StudentResultRowDto> rows) =>
        rows.GroupBy(r => r.Rank).ToDictionary(g => g.Key, g => g.Count());
}
