using Frontend.Shared.Models;

namespace Frontend.Shared.UI;

/// <summary>Клик по ячейке задачи: студент и задача, которые открыть.</summary>
public readonly record struct ResultCellClick(StudentResultRowDto Row, TaskHeaderDto Task);

/// <summary>Содержимое ячейки дополнительной колонки: текст, подсказка, ссылка и приглушённая пометка после текста.</summary>
public readonly record struct ResultsExtraCell(string? Text, string? Title = null, string? Href = null, string? Note = null);

/// <summary>
/// Дополнительная колонка таблицы результатов (варианты, посещаемость): таблица общая, а эти колонки есть
/// только у преподавателя. <paramref name="SortKey"/> задан — колонка сортируется кликом по шапке.
/// </summary>
public sealed record ResultsExtraColumn(string Key, string Title, Func<StudentResultRowDto, ResultsExtraCell> Cell,
    Func<StudentResultRowDto, IComparable>? SortKey = null, string? CellClass = null);

/// <summary>
/// Всё, что одинаково для всех строк таблицы: колонки, режим, тексты. Неизменяемый снимок: строка
/// (<c>ResultsRow</c>) перерисовывается, только если сменился он или её данные, поэтому при прокрутке
/// уже показанные строки не рендерятся заново.
/// </summary>
public sealed class ResultsTableLayout
{
    public required IReadOnlyList<TaskHeaderDto> Tasks { get; init; }
    public required IReadOnlyList<GradeColumnDto> Grades { get; init; }
    public IReadOnlyList<ResultsExtraColumn> Leading { get; init; } = [];
    public IReadOnlyList<ResultsExtraColumn> Trailing { get; init; } = [];
    public bool ShowRank { get; init; }
    /// <summary>Таблица преподавателя: ячейки задач — кнопки, клик открывает студента на задаче.</summary>
    public bool Interactive { get; init; }
    public Guid? HighlightStudentId { get; init; }
    /// <summary>«/admin» или «/student»; null — зритель не вошёл, имя без ссылки на профиль.</summary>
    public string? ProfileBase { get; init; }
    /// <summary>Уже закодированный адрес страницы таблицы для «← Назад» из профиля.</summary>
    public string ReturnUrl { get; init; } = "";
    public string YouLabel { get; init; } = "";
    public int TotalStudents { get; init; }
    public IReadOnlyDictionary<int, int> RankCounts { get; init; } = new Dictionary<int, int>();
    /// <summary>Подсказка места: (место, всего, делят ли его).</summary>
    public required Func<int, int, bool, string> RankTitleFormat { get; init; }
    /// <summary>Запомнить состояние таблицы и открыть профиль студента.</summary>
    public required Func<Guid, Task> OpenProfile { get; init; }

    private readonly Dictionary<int, string> _rankTitles = [];

    /// <summary>Подсказка места считается один раз на место, а не в каждой строке при каждом рендере.</summary>
    public string RankTitle(int rank)
    {
        if (!_rankTitles.TryGetValue(rank, out var title))
            _rankTitles[rank] = title = RankTitleFormat(rank, TotalStudents, RankCounts.GetValueOrDefault(rank, 1) > 1);
        return title;
    }

    public string? ProfileUrl(Guid studentId) =>
        ProfileBase is null ? null : $"{ProfileBase}/profile/{studentId}?returnUrl={ReturnUrl}";

    public int ColumnCount => (ShowRank ? 1 : 0) + 3 + Leading.Count + Tasks.Count + Grades.Count + Trailing.Count;
}
