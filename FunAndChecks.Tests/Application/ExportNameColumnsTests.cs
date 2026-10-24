using ClosedXML.Excel;
using FluentAssertions;
using FunAndChecks.Application.Results;
using FunAndChecks.Application.Surveys;
using FunAndChecks.Domain.Enums;
using FunAndChecks.Export;
using Xunit;

namespace FunAndChecks.Tests.Application;

/// <summary>Фамилия и имя в XLSX — в разных столбцах, а не склеенным ФИО.</summary>
public class ExportNameColumnsTests
{
    [Fact]
    public void ResultsXlsx_PutsLastAndFirstNameIntoSeparateColumns()
    {
        var results = new SubjectResultsDto(1, "Предмет", [new TaskHeaderDto(5, "1.1", 2)], [],
        [
            new StudentResultRowDto(Guid.NewGuid(), "Иванова Анна Мария", "M8O-101", 2,
                new Dictionary<int, ResultCellDto> { [5] = new("+", "#ffffff", SubmissionStatus.Accepted) },
                new Dictionary<int, int>(), "#123456", "Иванова", "Анна Мария"),
        ]);
        using var stream = new MemoryStream(ResultsXlsxExporter.Build(results));
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheet(1);

        sheet.Cell(1, 1).GetString().Should().Be("Фамилия");
        sheet.Cell(1, 2).GetString().Should().Be("Имя");
        sheet.Cell(1, 3).GetString().Should().Be("Группа");
        sheet.Cell(1, 4).GetString().Should().Be("1.1");
        sheet.Cell(2, 1).GetString().Should().Be("Иванова");
        sheet.Cell(2, 2).GetString().Should().Be("Анна Мария");
        sheet.Cell(2, 2).Style.Fill.BackgroundColor.Should().Be(XLColor.FromHtml("#123456"));
        sheet.Cell(2, 3).GetString().Should().Be("M8O-101");
        sheet.Cell(2, 4).GetString().Should().Be("+");
    }

    [Fact]
    public void SurveyXlsx_PutsLastAndFirstNameIntoSeparateColumns_AnonymousHasNone()
    {
        SurveyQuestionDto question = new(7, 1, "Как дела?", null, default, false, false, null, null, null, null, null, []);
        SurveyAdminDto Survey(bool anonymous) => new(1, "Опрос", "", null, null, [], default, anonymous, false, 0, null,
            null, null, default, 0, "Admin", DateTime.UtcNow, DateTime.UtcNow, 1, [question]);
        var response = new SurveyIndividualResponseDto(1, Guid.NewGuid(), "Петров Пётр", "M8O-101", DateTime.UtcNow,
            [new SurveyAnswerViewDto(7, "Хорошо")], "Петров", "Пётр");

        using (var stream = new MemoryStream(SurveyXlsxExporter.Build(Survey(false), [response])))
        using (var workbook = new XLWorkbook(stream))
        {
            var sheet = workbook.Worksheet(1);
            sheet.Cell(1, 1).GetString().Should().Be("Фамилия");
            sheet.Cell(1, 2).GetString().Should().Be("Имя");
            sheet.Cell(2, 1).GetString().Should().Be("Петров");
            sheet.Cell(2, 2).GetString().Should().Be("Пётр");
            sheet.Cell(2, 3).GetString().Should().Be("M8O-101");
            sheet.Cell(2, 5).GetString().Should().Be("Хорошо");
        }

        var anonymous = response with { RespondentId = null, RespondentName = "—", GroupName = null, LastName = null, FirstName = null };
        using (var stream = new MemoryStream(SurveyXlsxExporter.Build(Survey(true), [anonymous])))
        using (var workbook = new XLWorkbook(stream))
        {
            var sheet = workbook.Worksheet(1);
            sheet.Cell(1, 1).GetString().Should().Be("Дата");
            sheet.Cell(2, 2).GetString().Should().Be("Хорошо");
        }
    }
}
