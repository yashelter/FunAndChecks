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
            [new(Guid.NewGuid(), "Поздний студент", 2, "Группа", new() { [11] = AttendanceStatus.Unmarked }, 0, 0, 1)]);
        using var stream = new MemoryStream(AttendanceXlsxExporter.Build(journal));
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheet("Посещаемость");
        sheet.Cell(5, 3).GetString().Should().Be("н/у");
        sheet.Cell(5, 4).GetString().Should().Be("—");
        sheet.Cell(5, 5).DataType.Should().Be(XLDataType.Number);
        sheet.Cell(5, 6).GetValue<int>().Should().Be(0);
        sheet.Cell(5, 7).GetValue<int>().Should().Be(1);
        sheet.SheetView.SplitRow.Should().Be(4);
        sheet.SheetView.SplitColumn.Should().Be(2);
    }
}
