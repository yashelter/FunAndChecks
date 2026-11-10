using Frontend.Shared.Models;

namespace Frontend.Shared.UI;

/// <summary>Чем закончился переход к строке студента в таблице результатов.</summary>
public enum ResultsRowJump
{
    /// <summary>Строка видна в текущем фильтре, таблица прокручена к ней.</summary>
    Shown,
    /// <summary>Студент есть в снимке, но его строку скрывает фильтр таблицы.</summary>
    HiddenByFilter,
    /// <summary>Студента нет в снимке предмета.</summary>
    Missing,
}

/// <summary>
/// Что пользователь настроил в таблице результатов: поиск по ФИО, группа и сортировка по одной колонке.
/// Запоминается на время перехода в профиль (<see cref="Frontend.Shared.Services.ResultsNavigationState"/>).
/// </summary>
public sealed record ResultsViewState(string Search = "", string? Group = null, string? SortKey = null, bool Descending = false)
{
    public const string SortRank = "rank", SortName = "name", SortTotal = "total", SortGroup = "group";

    public static readonly ResultsViewState Default = new();

    public bool HasFilter => !string.IsNullOrWhiteSpace(Search) || !string.IsNullOrEmpty(Group);

    public ResultsViewState WithoutFilter() => this with { Search = "", Group = null };

    /// <summary>Клик по шапке: по возрастанию → по убыванию → порядок сервера.</summary>
    public ResultsViewState ToggleSort(string key) =>
        SortKey != key ? this with { SortKey = key, Descending = false }
        : !Descending ? this with { Descending = true }
        : this with { SortKey = null, Descending = false };
}

/// <summary>
/// Строки таблицы в том порядке, в каком их видит пользователь: после поиска, фильтра по группе и сортировки.
/// Строки виртуализированы — в DOM есть только видимые, поэтому прокрутить к студенту можно лишь по номеру строки.
/// </summary>
public static class ResultsRowLocator
{
    /// <param name="extraSort">Ключ сортировки дополнительной колонки (варианты) по её <c>SortKey</c>.</param>
    /// <returns>Тот же список, если ни фильтра, ни сортировки нет.</returns>
    public static List<StudentResultRowDto> Apply(List<StudentResultRowDto> rows, ResultsViewState state,
        Func<string, Func<StudentResultRowDto, IComparable>?>? extraSort = null)
    {
        IEnumerable<StudentResultRowDto> view = rows;
        var changed = false;
        var search = state.Search.Trim();
        if (search.Length > 0)
        {
            view = view.Where(r => r.FullName.Contains(search, StringComparison.CurrentCultureIgnoreCase));
            changed = true;
        }
        if (!string.IsNullOrEmpty(state.Group))
        {
            view = view.Where(r => r.GroupName == state.Group);
            changed = true;
        }

        // OrderBy устойчив: при равных ключах остаётся порядок сервера.
        IOrderedEnumerable<StudentResultRowDto>? sorted = state.SortKey switch
        {
            null => null,
            ResultsViewState.SortRank => Order(view, r => r.Rank, state.Descending),
            ResultsViewState.SortTotal => Order(view, r => r.TotalPoints, state.Descending),
            ResultsViewState.SortName => state.Descending
                ? view.OrderByDescending(r => r.FullName, StringComparer.CurrentCultureIgnoreCase)
                : view.OrderBy(r => r.FullName, StringComparer.CurrentCultureIgnoreCase),
            ResultsViewState.SortGroup => state.Descending
                ? view.OrderByDescending(r => r.GroupName, StringComparer.CurrentCultureIgnoreCase)
                : view.OrderBy(r => r.GroupName, StringComparer.CurrentCultureIgnoreCase),
            var key => extraSort?.Invoke(key) is { } selector ? Order(view, selector, state.Descending) : null,
        };
        if (sorted is not null)
            return sorted.ToList();
        return changed ? view.ToList() : rows;
    }

    private static IOrderedEnumerable<StudentResultRowDto> Order<TKey>(IEnumerable<StudentResultRowDto> rows,
        Func<StudentResultRowDto, TKey> key, bool descending) =>
        descending ? rows.OrderByDescending(key) : rows.OrderBy(key);

    /// <summary>Индекс строки студента среди видимых строк или -1, если фильтр её скрывает.</summary>
    public static int VisibleIndex(IReadOnlyList<StudentResultRowDto> visible, Guid studentId)
    {
        for (var i = 0; i < visible.Count; i++)
            if (visible[i].StudentId == studentId) return i;
        return -1;
    }
}
