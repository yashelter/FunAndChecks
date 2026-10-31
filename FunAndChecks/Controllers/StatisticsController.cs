using FunAndChecks.Application.Statistics;
using FunAndChecks.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FunAndChecks.Controllers;

/// <summary>Статистика для суперадминов (раздел «Статистика» в «Управлении»).</summary>
[ApiController]
[Route("api/statistics")]
[Authorize(Policy = AuthorizationPolicies.SuperAdmin)]
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
}
