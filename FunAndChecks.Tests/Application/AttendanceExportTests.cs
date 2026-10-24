using ClosedXML.Excel;
using FluentAssertions;
using FunAndChecks.Application.Attendance;
using FunAndChecks.Domain.Enums;
using FunAndChecks.Export;
using Xunit;

namespace FunAndChecks.Tests.Application;

public class AttendanceExportTests
{
    [Fact]
    public void Xlsx_DistinguishesUnmarkedFromNotEnrolled_AndExportsNumericTotals()
    {
        var journal = new AttendanceJournalDto(1, "Предмет", true,
            [new(10, "Первое", new DateTime(2026, 10, 5, 7, 0, 0, DateTimeKind.Utc)),
             new(11, "Второе", new DateTime(2026, 10, 5, 9, 0, 0, DateTimeKind.Utc))],
            [new(Guid.NewGuid(), "Поздний студент", 2, "Группа", new() { [11] = AttendanceStatus.Unmarked }, 0, 0, 1),
             new(Guid.NewGuid(), "Ранний Студент", 2, "Группа",
                 new() { [10] = AttendanceStatus.Present, [11] = AttendanceStatus.Present }, 2, 0, 0, "Ранний", "Студент")]);
        using var stream = new MemoryStream(AttendanceXlsxExporter.Build(journal));
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheet("Группа");
        sheet.Cell(5, 1).GetString().Should().Be("Поздний");
        sheet.Cell(5, 2).GetString().Should().Be("студент");
        sheet.Cell(5, 3).GetString().Should().Be("н/у");
        sheet.Cell(5, 4).GetString().Should().Be("—");
        sheet.Cell(5, 5).DataType.Should().Be(XLDataType.Number);
        sheet.Cell(5, 6).GetValue<int>().Should().Be(0);
        sheet.Cell(5, 7).GetValue<int>().Should().Be(1);
        sheet.SheetView.SplitRow.Should().Be(4);
        sheet.SheetView.SplitColumn.Should().Be(2);
    }

    [Fact]
    public void Xlsx_CreatesOneSheetPerGroupId_AndKeepsEachGroupsStudentsAndMarks()
    {
        var journal = new AttendanceJournalDto(1, "Предмет", true,
            [new(10, "Практика", new DateTime(2026, 10, 5, 7, 0, 0, DateTimeKind.Utc))],
            [new(Guid.NewGuid(), "Первый", 2, "Группа", new() { [10] = AttendanceStatus.Present }, 1, 0, 0),
             new(Guid.NewGuid(), "Второй", 3, "Группа", new() { [10] = AttendanceStatus.Absent }, 0, 1, 0),
             new(Guid.NewGuid(), "Третий", 2, "Группа", new() { [10] = AttendanceStatus.Unmarked }, 0, 0, 1)]);
        using var stream = new MemoryStream(AttendanceXlsxExporter.Build(journal));
        using var workbook = new XLWorkbook(stream);

        workbook.Worksheets.Should().HaveCount(2);
        var firstGroup = workbook.Worksheet("Группа");
        firstGroup.Cell(5, 1).GetString().Should().Be("Первый");
        firstGroup.Cell(6, 1).GetString().Should().Be("Третий");
        firstGroup.Cell(7, 1).IsEmpty().Should().BeTrue();
        firstGroup.Cell(4, 3).GetString().Should().Contain("05.10.2026 10:00").And.Contain("Практика");
        firstGroup.Cell(5, 3).GetString().Should().Be("✓");
        firstGroup.Cell(6, 3).GetString().Should().Be("—");
        firstGroup.Cell(5, 4).GetValue<int>().Should().Be(1);
        firstGroup.Cell(6, 6).GetValue<int>().Should().Be(1);

        var secondGroup = workbook.Worksheet("Группа (2)");
        secondGroup.Cell(5, 1).GetString().Should().Be("Второй");
        secondGroup.Cell(6, 1).IsEmpty().Should().BeTrue();
        secondGroup.Cell(5, 3).GetString().Should().Be("✕");
        secondGroup.Cell(5, 5).GetValue<int>().Should().Be(1);
    }

    [Fact]
    public void Xlsx_GroupSheet_ShowsOnlyThatGroupsSessions_AndSplitsNames()
    {
        var journal = new AttendanceJournalDto(1, "Предмет", true,
            [new(10, "Общее", new DateTime(2026, 10, 5, 7, 0, 0, DateTimeKind.Utc)),
             new(11, "Только вторая", new DateTime(2026, 10, 6, 7, 0, 0, DateTimeKind.Utc)),
             new(12, "Только первая", new DateTime(2026, 10, 7, 7, 0, 0, DateTimeKind.Utc))],
            [new(Guid.NewGuid(), "Иванова Анна Мария", 2, "Первая",
                 new() { [10] = AttendanceStatus.Present, [12] = AttendanceStatus.Absent }, 1, 1, 0, "Иванова", "Анна Мария"),
             new(Guid.NewGuid(), "Петров Пётр", 3, "Вторая",
                 new() { [10] = AttendanceStatus.Absent, [11] = AttendanceStatus.Present }, 1, 1, 0, "Петров", "Пётр")]);
        using var stream = new MemoryStream(AttendanceXlsxExporter.Build(journal));
        using var workbook = new XLWorkbook(stream);

        var first = workbook.Worksheet("Первая");
        first.Cell(1, 1).GetString().Should().Be("Предмет — Первая");
        first.Cell(5, 1).GetString().Should().Be("Иванова");
        first.Cell(5, 2).GetString().Should().Be("Анна Мария");
        first.Cell(4, 3).GetString().Should().Contain("Общее");
        first.Cell(4, 4).GetString().Should().Contain("Только первая");
        first.Cell(5, 4).GetString().Should().Be("✕");
        first.Cell(4, 5).GetString().Should().Be("Посещений");
        first.Cell(4, 8).IsEmpty().Should().BeTrue();

        var second = workbook.Worksheet("Вторая");
        second.Cell(4, 4).GetString().Should().Contain("Только вторая");
        second.Cell(5, 4).GetString().Should().Be("✓");
        second.Cell(4, 5).GetString().Should().Be("Посещений");
        second.Cells().Select(cell => cell.GetString()).Should().NotContain("н/у");
    }

    [Fact]
    public void Xlsx_NormalizesSheetNames_AndResolvesCaseAndTruncationCollisions()
    {
        string[] names =
        [
            "Группа/1", "Группа?1", "группа_1", new string('A', 40), new string('A', 39) + "B",
            "'Группа'", "[]:*?\\/", "  ", "History", "Линия\u0001\u0003",
        ];
        var students = names.Select((name, i) => new AttendanceStudentRowDto(Guid.NewGuid(),
            $"Студент {i + 1}", i + 1, name, new(), 0, 0, 0)).ToList();
        var journal = new AttendanceJournalDto(1, "Предмет", true, [], students);
        using var stream = new MemoryStream(AttendanceXlsxExporter.Build(journal));
        using var workbook = new XLWorkbook(stream);

        workbook.Worksheets.Should().HaveCount(names.Length);
        var sheetNames = workbook.Worksheets.Select(sheet => sheet.Name).ToList();
        sheetNames.Distinct(StringComparer.OrdinalIgnoreCase).Should().HaveCount(names.Length);
        foreach (var sheetName in sheetNames)
        {
            sheetName.Length.Should().BeInRange(1, 31);
            sheetName.Should().NotContainAny(":", "/", "\\", "?", "*", "]", "[", "\u0000", "\u0003");
            sheetName.Should().NotStartWith("'").And.NotEndWith("'");
            sheetName.Should().NotBeEquivalentTo("History");
        }
        for (var i = 0; i < names.Length; i++)
            workbook.Worksheet(i + 1).Cell(5, 2).GetString().Should().Be($"{i + 1}");
    }

    [Fact]
    public void Xlsx_EmptyJournal_StillContainsReadableHeadersAndSessionDates()
    {
        var journal = new AttendanceJournalDto(1, "Предмет", true,
            [new(10, "Практика", new DateTime(2026, 10, 5, 7, 0, 0, DateTimeKind.Utc))], []);
        using var stream = new MemoryStream(AttendanceXlsxExporter.Build(journal));
        using var workbook = new XLWorkbook(stream);

        workbook.Worksheets.Should().ContainSingle();
        var sheet = workbook.Worksheet("Посещаемость");
        sheet.Cell(1, 1).GetString().Should().Be("Предмет");
        sheet.Cell(4, 3).GetString().Should().Contain("05.10.2026 10:00").And.Contain("Практика");
        sheet.Cell(4, 6).GetString().Should().Be("Не отмечено");
        sheet.Cell(5, 1).IsEmpty().Should().BeTrue();
    }
}
