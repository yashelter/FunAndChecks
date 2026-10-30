using FunAndChecks.Application.Results;
using FunAndChecks.Application.Students;
using FunAndChecks.Export;
using Microsoft.AspNetCore.Mvc;

namespace FunAndChecks.Controllers;

[ApiController]
[Route("api/results")]
public class ResultsController(IResultsService resultsService) : ControllerBase
{
    private const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>
    /// Таблица результатов по предмету. Строится лениво и кэшируется;
    /// кэш сбрасывается при изменениях (сдачи, оценки, состав групп, задачи).
    /// </summary>
    [HttpGet("subjects/{subjectId:int}")]
    [ProducesResponseType(typeof(SubjectResultsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SubjectResultsDto>> GetSubjectResults(int subjectId, CancellationToken cancellationToken) =>
        Ok(await resultsService.GetSubjectResultsAsync(subjectId, cancellationToken));

    /// <summary>
    /// Цвета студентов предмета: живая подписка таблицы сверяет их после подключения к хабу,
    /// не скачивая заново всю таблицу.
    /// </summary>
    [HttpGet("subjects/{subjectId:int}/colors")]
    [ProducesResponseType(typeof(IReadOnlyList<StudentColorUpdateDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<StudentColorUpdateDto>>> GetStudentColors(int subjectId, CancellationToken cancellationToken) =>
        Ok(await resultsService.GetStudentColorsAsync(subjectId, cancellationToken));

    /// <summary>Экспорт таблицы результатов в XLSX с заливкой ячеек цветами.</summary>
    [HttpGet("subjects/{subjectId:int}/export")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ExportSubjectResults(int subjectId, CancellationToken cancellationToken)
    {
        var results = await resultsService.GetSubjectResultsAsync(subjectId, cancellationToken);
        var bytes = ResultsXlsxExporter.Build(results);
        var fileName = $"Results_{results.SubjectName}_{DateTime.Now:yyyy-MM-dd}.xlsx";
        return File(bytes, XlsxContentType, fileName);
    }
}
