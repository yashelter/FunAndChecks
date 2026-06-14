using System.Globalization;
using ClosedXML.Excel;
using FunAndChecks.Application.Results;
using FunAndChecks.Domain.Enums;

namespace FunAndChecks.Export;

/// <summary>
/// Строит XLSX сводной таблицы результатов с заливкой ячеек:
/// фон ФИО — цвет студента, фон ячейки задачи — цвет проверившего админа.
/// </summary>
public static class ResultsXlsxExporter
{
    public static byte[] Build(SubjectResultsDto results)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Результаты");

        // --- Заголовок ---
        ws.Cell(1, 1).Value = "ФИО";
        ws.Cell(1, 2).Value = "Группа";

        var col = 3;
        foreach (var task in results.TaskHeaders)
            ws.Cell(1, col++).Value = task.TaskName;
        foreach (var grade in results.GradeColumns)
            ws.Cell(1, col++).Value = grade.Name;
        var totalCol = col;
        ws.Cell(1, totalCol).Value = "Σ";

        var headerRange = ws.Range(1, 1, 1, totalCol);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#EEEEEE");
        headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

        // --- Строки студентов (в файле всегда сортировка по группе, затем по ФИО) ---
        var row = 2;
        var ordered = results.UserResults
            .OrderBy(s => s.GroupName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(s => s.FullName, StringComparer.CurrentCultureIgnoreCase);
        foreach (var student in ordered)
        {
            var nameCell = ws.Cell(row, 1);
            nameCell.Value = student.FullName;
            ApplyFill(nameCell, student.StudentColor, withContrastFont: true);

            ws.Cell(row, 2).Value = student.GroupName;

            col = 3;
            foreach (var task in results.TaskHeaders)
            {
                var cell = ws.Cell(row, col++);
                var data = student.Results.GetValueOrDefault(task.TaskId);
                cell.Value = CellText(data);
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                cell.Style.Font.Bold = true;
                if (data is not null && !string.IsNullOrEmpty(data.AdminColor))
                    ApplyFill(cell, data.AdminColor, withContrastFont: true);
            }

            foreach (var grade in results.GradeColumns)
            {
                var cell = ws.Cell(row, col++);
                cell.Value = student.Grades.TryGetValue(grade.ComponentId, out var points)
                    ? $"{points}/{grade.MaxPoints}"
                    : string.Empty;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }

            ws.Cell(row, totalCol).Value = student.TotalPoints;
            ws.Cell(row, totalCol).Style.Font.Bold = true;
            row++;
        }

        ws.SheetView.FreezeRows(1);
        ws.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static string CellText(ResultCellDto? cell) =>
        cell?.Status == SubmissionStatus.Accepted ? "+" : cell?.DisplayValue ?? string.Empty;

    private static void ApplyFill(IXLCell cell, string? hex, bool withContrastFont)
    {
        var normalized = NormalizeHex(hex);
        if (normalized is null)
            return;

        cell.Style.Fill.BackgroundColor = XLColor.FromHtml(normalized);
        if (withContrastFont)
            cell.Style.Font.FontColor = IsLight(normalized) ? XLColor.Black : XLColor.White;
    }

    /// <summary>Приводит #RRGGBB / #RRGGBBAA к #RRGGBB; null — если формат неверный.</summary>
    private static string? NormalizeHex(string? hex)
    {
        if (string.IsNullOrEmpty(hex) || hex[0] != '#')
            return null;

        var digits = hex.Length >= 7 ? hex[1..7] : null;
        return digits is not null && digits.All(Uri.IsHexDigit) ? "#" + digits : null;
    }

    private static bool IsLight(string hex)
    {
        var r = int.Parse(hex.Substring(1, 2), NumberStyles.HexNumber);
        var g = int.Parse(hex.Substring(3, 2), NumberStyles.HexNumber);
        var b = int.Parse(hex.Substring(5, 2), NumberStyles.HexNumber);
        // Воспринимаемая яркость (ITU-R BT.601).
        return 0.299 * r + 0.587 * g + 0.114 * b > 150;
    }
}
