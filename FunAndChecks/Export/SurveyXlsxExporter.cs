using ClosedXML.Excel;
using FunAndChecks.Application.Surveys;

namespace FunAndChecks.Export;

/// <summary>Ответы опроса: строка — респондент, столбец — вопрос. У анонимного опроса столбцов с именем нет.</summary>
public static class SurveyXlsxExporter
{
    public static byte[] Build(SurveyAdminDto survey, IReadOnlyList<SurveyIndividualResponseDto> responses)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Ответы");
        var col = 1;
        if (!survey.IsAnonymous)
        {
            ws.Cell(1, col++).Value = "Фамилия";
            ws.Cell(1, col++).Value = "Имя";
            ws.Cell(1, col++).Value = "Группа";
        }
        ws.Cell(1, col++).Value = survey.IsAnonymous ? "Дата" : "Время ответа (UTC)";
        var firstQuestion = col;
        var questions = survey.Questions.OrderBy(q => q.Order).ToList();
        foreach (var question in questions) ws.Cell(1, col++).Value = question.Text;

        var row = 2;
        foreach (var response in responses)
        {
            col = 1;
            if (!survey.IsAnonymous)
            {
                var (lastName, firstName) = PersonName.Split(response.LastName, response.FirstName, response.RespondentName);
                ws.Cell(row, col++).Value = lastName;
                ws.Cell(row, col++).Value = firstName;
                ws.Cell(row, col++).Value = response.GroupName ?? "";
            }
            ws.Cell(row, col).Value = response.SubmittedAtUtc;
            ws.Cell(row, col).Style.DateFormat.Format = survey.IsAnonymous ? "yyyy-mm-dd" : "yyyy-mm-dd hh:mm";
            var answers = response.Answers.ToDictionary(a => a.QuestionId, a => a.Value);
            for (var i = 0; i < questions.Count; i++)
                ws.Cell(row, firstQuestion + i).Value = answers.GetValueOrDefault(questions[i].Id, "");
            row++;
        }

        var header = ws.Row(1);
        header.Style.Font.Bold = true;
        header.Style.Alignment.WrapText = true;
        ws.SheetView.FreezeRows(1);
        ws.Columns().AdjustToContents(1, Math.Min(row, 200), 8, 60);
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
