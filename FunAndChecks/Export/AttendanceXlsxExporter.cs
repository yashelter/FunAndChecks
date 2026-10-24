using ClosedXML.Excel;
using FunAndChecks.Application.Attendance;
using FunAndChecks.Domain.Enums;

namespace FunAndChecks.Export;

public static class AttendanceXlsxExporter
{
    public static byte[] Build(AttendanceJournalDto journal)
    {
        using var workbook = new XLWorkbook();
        var sheetNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in journal.Students.GroupBy(student => student.GroupId))
        {
            var sheetName = CreateGroupSheetName(group.Key, group.First().GroupName, sheetNames);
            PopulateSheet(workbook.Worksheets.Add(sheetName), journal, group.ToList());
        }
        if (!workbook.Worksheets.Any())
            PopulateSheet(workbook.Worksheets.Add("Посещаемость"), journal, []);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static string CreateGroupSheetName(int groupId, string groupName, HashSet<string> usedNames)
    {
        var proposedName = string.IsNullOrWhiteSpace(groupName) ? $"Группа {groupId}" : groupName.Trim();
        proposedName = new string(proposedName.Select(c => char.IsControl(c) ? '_' : c).ToArray());
        var baseName = XLHelper.CreateSafeSheetName(proposedName, '_');
        if (baseName.Equals("History", StringComparison.OrdinalIgnoreCase))
            baseName = $"Группа {groupId}";
        var name = baseName;
        for (var duplicate = 2; !usedNames.Add(name); duplicate++)
        {
            var suffix = $" ({duplicate})";
            name = baseName[..Math.Min(baseName.Length, 31 - suffix.Length)] + suffix;
        }
        return name;
    }

    private static void PopulateSheet(IXLWorksheet sheet, AttendanceJournalDto journal,
        IReadOnlyCollection<AttendanceStudentRowDto> students)
    {
        // Лист — одна группа: занятия, в которые она не входила, дали бы столбцы из одних «н/у».
        var groupName = students.FirstOrDefault()?.GroupName;
        var sessions = students.Count == 0 ? journal.Sessions
            : journal.Sessions.Where(session => students.Any(student => student.Marks.ContainsKey(session.Id))).ToList();
        var lastColumn = sessions.Count + 5;
        sheet.Cell(1, 1).Value = string.IsNullOrWhiteSpace(groupName) ? journal.SubjectName : $"{journal.SubjectName} — {groupName}";
        sheet.Range(1, 1, 1, lastColumn).Merge().Style.Font.SetBold().Font.SetFontSize(16);
        sheet.Cell(2, 1).Value = "✓ — присутствовал; ✕ — отсутствовал; — — не отмечен; н/у — не включён в занятие. Даты: Москва (UTC+3).";
        sheet.Range(2, 1, 2, lastColumn).Merge().Style.Alignment.SetWrapText();
        sheet.Row(2).Height = 32;
        sheet.Cell(4, 1).Value = "Фамилия";
        sheet.Cell(4, 2).Value = "Имя";
        var col = 3;
        foreach (var session in sessions)
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
        foreach (var student in students)
        {
            var (lastName, firstName) = PersonName.Split(student.LastName, student.FirstName, student.FullName);
            sheet.Cell(row, 1).Value = lastName;
            sheet.Cell(row, 2).Value = firstName;
            col = 3;
            foreach (var session in sessions)
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
        sheet.Column(1).Width = 22;
        sheet.Column(2).Width = 18;
        sheet.Columns(3, lastColumn).Width = 20;
    }
}
