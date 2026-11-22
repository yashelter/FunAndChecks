using FunAndChecks.Application.Statistics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using FunAndChecks.Domain.Constants;

namespace FunAndChecks.Controllers;

/// <summary>Reviewer statistics for administrators with the statistics permission.</summary>
[ApiController]
[Route("api/statistics")]
[Authorize(Policy = AdminPermissions.STATISTICS)]
public class StatisticsController(IReviewerStatisticsService statistics) : ControllerBase
{
    /// <summary>
    /// По каждому проверяющему: принятые и непринятые попытки (все) и сколько разных студентов он завершил в очереди.
    /// Без <paramref name="subjectId"/> — по всем предметам.
    /// </summary>
    [HttpGet("reviewers")]
    [ProducesResponseType(typeof(List<ReviewerStatsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<ReviewerStatsDto>>> GetReviewers([FromQuery] int? subjectId, CancellationToken ct) =>
        Ok(await statistics.GetAsync(subjectId, ct));

    /// <summary>
    /// Расширенная сводка активности администраторов (проверки, очереди, оценки, посещаемость, варианты, ачивки).
    /// </summary>
    [HttpGet("activity")]
    [ProducesResponseType(typeof(List<AdminActivityStatsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<AdminActivityStatsDto>>> GetActivity(
        [FromQuery] int? subjectId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken ct) =>
        Ok(await statistics.GetActivityAsync(subjectId, from, to, ct));
}
