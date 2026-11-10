using Frontend.Shared.Models;
using MudBlazor;

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
/// Позиция строки в том порядке, в каком её показывает MudDataGrid: после фильтров и сортировки.
/// Строки виртуализированы — в DOM есть только видимые, поэтому прокрутить можно лишь по номеру строки.
/// </summary>
public static class ResultsRowLocator
{
    /// <summary>Индекс строки студента среди видимых строк или -1, если фильтр её скрывает.</summary>
    public static int VisibleIndex(MudDataGrid<StudentResultRowDto> grid, Guid studentId)
    {
        IEnumerable<StudentResultRowDto> rows = grid.FilteredItems;
        // Сортировку применяем сами: FilteredItems описан как «после фильтров». Если таблица уже отсортировала
        // строки, повторная устойчивая сортировка по тем же ключам порядок не меняет.
        IOrderedEnumerable<StudentResultRowDto>? sorted = null;
        foreach (var sort in grid.SortDefinitions.Values.OrderBy(s => s.Index))
        {
            var comparer = sort.Comparer ?? Comparer<object>.Default;
            sorted = sorted is null
                ? sort.Descending ? rows.OrderByDescending(sort.SortFunc, comparer) : rows.OrderBy(sort.SortFunc, comparer)
                : sort.Descending ? sorted.ThenByDescending(sort.SortFunc, comparer) : sorted.ThenBy(sort.SortFunc, comparer);
        }
        var index = 0;
        foreach (var row in sorted ?? rows)
        {
            if (row.StudentId == studentId) return index;
            index++;
        }
        return -1;
    }
}
