using ClosedXML.Excel;
using FunAndChecks.Application.Attendance;
using FunAndChecks.Domain.Enums;

namespace FunAndChecks.Export;

public static class AttendanceXlsxExporter
{
    public static byte[] Build(AttendanceJournalDto journal)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Посещаемость");
        var lastColumn = journal.Sessions.Count + 5;
        sheet.Cell(1, 1).Value = journal.SubjectName;
        sheet.Range(1, 1, 1, lastColumn).Merge().Style.Font.SetBold().Font.SetFontSize(16);
        sheet.Cell(2, 1).Value = "✓ — присутствовал; ✕ — отсутствовал; — — не отмечен; н/у — не включён в занятие. Даты: Москва (UTC+3).";
        sheet.Range(2, 1, 2, lastColumn).Merge().Style.Alignment.SetWrapText();
        sheet.Row(2).Height = 32;
        sheet.Cell(4, 1).Value = "Студент";
        sheet.Cell(4, 2).Value = "Группа";
        var col = 3;
        foreach (var session in journal.Sessions)
        {
            sheet.Cell(4, col).Value = $"{session.StartsAt.AddHours(3):dd.MM.yyyy HH:mm}\n{session.Name}";
            sheet.Cell(4, col++).Style.Alignment.SetWrapText();
        }
        sheet.Cell(4, col++).Value = "Посещений";
        sheet.Cell(4, col++).Value = "Пропусков";
        sheet.Cell(4, col).Value = "Не отмечено";
        var header = sheet.Range(4, 1, 4, lastColumn);
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#334155");
        header.Style.Font.FontColor = XLColor.White;
        header.Style.Font.Bold = true;
        sheet.Row(4).Height = 42;
        var row = 5;
        foreach (var student in journal.Students)
        {
            sheet.Cell(row, 1).Value = student.FullName;
            sheet.Cell(row, 2).Value = student.GroupName;
            col = 3;
            foreach (var session in journal.Sessions)
            {
                var cell = sheet.Cell(row, col++);
                var status = student.Marks.GetValueOrDefault(session.Id);
                var included = student.Marks.ContainsKey(session.Id);
                cell.Value = !included ? "н/у" : status switch
                {
                    AttendanceStatus.Present => "✓", AttendanceStatus.Absent => "✕", _ => "—",
                };
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml(!included ? "#F1F5F9" : status switch
                {
                    AttendanceStatus.Present => "#DCFCE7", AttendanceStatus.Absent => "#FEE2E2", _ => "#FEF3C7",
                });
            }
            sheet.Cell(row, col++).Value = student.Present;
            sheet.Cell(row, col++).Value = student.Absent;
            sheet.Cell(row, col).Value = student.Unmarked;
            row++;
        }
        sheet.SheetView.FreezeRows(4);
        sheet.SheetView.FreezeColumns(2);
        sheet.Range(4, 1, Math.Max(4, row - 1), lastColumn).SetAutoFilter();
        sheet.Column(1).Width = 30;
        sheet.Column(2).Width = 20;
        sheet.Columns(3, lastColumn).Width = 20;
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
