using Frontend.Shared.Models;

namespace Frontend.Shared.UI;

/// <summary>Replace only colors, preserving the table's data and component state.</summary>
public static class StudentColorRows
{
    public static List<StudentResultRowDto> Apply(List<StudentResultRowDto> rows, IReadOnlyList<StudentColorUpdateDto> updates)
    {
        var colors = updates.GroupBy(update => update.StudentId).ToDictionary(group => group.Key, group => group.Last().Color);
        if (!rows.Any(row => colors.TryGetValue(row.StudentId, out var color) && row.StudentColor != color))
            return rows;

        return rows.Select(row => colors.TryGetValue(row.StudentId, out var color)
            ? row with { StudentColor = color }
            : row).ToList();
    }
}
