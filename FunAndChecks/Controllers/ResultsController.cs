using FunAndChecks.Application.Results;
using Microsoft.AspNetCore.Mvc;

namespace FunAndChecks.Controllers;

[ApiController]
[Route("api/results")]
public class ResultsController(IResultsService resultsService) : ControllerBase
{
    /// <summary>
    /// Таблица результатов по предмету. Строится лениво и кэшируется;
    /// кэш сбрасывается при изменениях (сдачи, оценки, состав групп, задачи).
    /// </summary>
    [HttpGet("subjects/{subjectId:int}")]
    [ProducesResponseType(typeof(SubjectResultsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SubjectResultsDto>> GetSubjectResults(int subjectId, CancellationToken cancellationToken) =>
        Ok(await resultsService.GetSubjectResultsAsync(subjectId, cancellationToken));
}
